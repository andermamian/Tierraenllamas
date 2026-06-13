using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// TIERRA EN LLAMAS - SaveSystem
/// Sistema de guardado completo con:
/// - Guardado automático en checkpoints
/// - Guardado manual (hasta 3 slots)
/// - Cifrado AES de datos locales
/// - Sincronización con Google Play Games
/// - Serialización de todo el estado del juego
/// </summary>
public class SaveSystem : MonoBehaviour
{
    public static SaveSystem Instance { get; private set; }

    [Header("Configuración")]
    [SerializeField] private int maxSaveSlots = 3;
    [SerializeField] private float autoSaveInterval = 300f; // 5 minutos
    [SerializeField] private bool encryptSaves = true;

    [Header("Estado")]
    public bool IsSaving { get; private set; }
    public bool IsLoading { get; private set; }
    public DateTime LastSaveTime { get; private set; }

    // Clave de cifrado (en producción sería más segura)
    private readonly string encryptionKey = "T13rr4EnLl4m4s2025C0l0mb14!";
    private string savePath;
    private float autoSaveTimer;

    // Eventos
    public event Action OnSaveStarted;
    public event Action OnSaveCompleted;
    public event Action OnLoadStarted;
    public event Action OnLoadCompleted;
    public event Action<string> OnSaveError;

    private void Awake()
    {
        Instance = this;
        savePath = Path.Combine(Application.persistentDataPath, "saves");

        if (!Directory.Exists(savePath))
            Directory.CreateDirectory(savePath);
    }

    private void Update()
    {
        // Auto-guardado periódico
        if (GameManager.Instance.CurrentState == GameState.Playing)
        {
            autoSaveTimer += Time.deltaTime;
            if (autoSaveTimer >= autoSaveInterval)
            {
                AutoSave();
                autoSaveTimer = 0;
            }
        }
    }

    #region Guardado

    public void Save(int slot)
    {
        if (IsSaving) return;
        IsSaving = true;
        OnSaveStarted?.Invoke();

        try
        {
            SaveData data = CreateSaveData();
            string json = JsonUtility.ToJson(data, true);

            if (encryptSaves)
                json = Encrypt(json);

            string filePath = GetSavePath(slot);
            File.WriteAllText(filePath, json);

            // Guardar metadata (sin cifrar, para mostrar en menú)
            SaveMetadata meta = new SaveMetadata
            {
                Slot = slot,
                PlayerName = data.PlayerName,
                Chapter = data.Chapter,
                PlayTime = data.PlayTime,
                SaveDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                SceneName = data.CurrentScene,
                Level = data.PlayerLevel
            };
            string metaPath = GetMetadataPath(slot);
            File.WriteAllText(metaPath, JsonUtility.ToJson(meta));

            LastSaveTime = DateTime.Now;
            Debug.Log($"[SaveSystem] Guardado exitoso en slot {slot}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Error al guardar: {e.Message}");
            OnSaveError?.Invoke(e.Message);
        }

        IsSaving = false;
        OnSaveCompleted?.Invoke();
    }

    public void AutoSave()
    {
        Save(0); // Slot 0 = autosave
    }

    private SaveData CreateSaveData()
    {
        var gm = GameManager.Instance;
        var player = PlayerController.Instance;
        var karma = gm.Karma;
        var factions = gm.Factions;
        var narrative = NarrativeManager.Instance;

        SaveData data = new SaveData();

        // Info general
        data.SaveVersion = "2.0";
        data.SaveDate = DateTime.Now.ToString("O");
        data.PlayTime = gm.GameTime;
        data.Chapter = gm.CurrentChapter;
        data.CurrentScene = gm.CurrentScene;
        data.Difficulty = (int)gm.Difficulty;

        // Jugador
        if (player != null)
        {
            data.PlayerName = "Protagonista"; // Se personaliza
            data.PlayerLevel = player.Stats.Level;
            data.PlayerExperience = player.Stats.Experience;
            data.PlayerHealth = player.Health.CurrentHealth;
            data.PlayerMaxHealth = player.Health.MaxHealth;
            data.PlayerPosition = player.transform.position;
            data.PlayerRotation = player.transform.eulerAngles;

            // Estadísticas
            data.StatCombate = player.Stats.Combate;
            data.StatSigilo = player.Stats.Sigilo;
            data.StatPersuasion = player.Stats.Persuasion;
            data.StatResistencia = player.Stats.Resistencia;
            data.StatSupervivencia = player.Stats.Supervivencia;
            data.SkillPoints = player.Stats.SkillPoints;

            // Heridas
            data.LeftArmWounded = player.Health.LeftArmWounded;
            data.RightArmWounded = player.Health.RightArmWounded;
            data.LeftLegWounded = player.Health.LeftLegWounded;
            data.RightLegWounded = player.Health.RightLegWounded;
        }

        // Karma
        if (karma != null)
        {
            data.KarmaHonor = karma.Honor;
            data.KarmaHumanidad = karma.Humanidad;
        }

        // Facciones
        if (factions != null)
        {
            data.FactionFrenteAmazonico = factions.GetRelation(FactionType.FrenteAmazonico);
            data.FactionFuerzaNacional = factions.GetRelation(FactionType.FuerzaNacional);
            data.FactionLosHalcones = factions.GetRelation(FactionType.LosHalcones);
            data.FactionCartelDelNorte = factions.GetRelation(FactionType.CartelDelNorte);
            data.FactionCiviles = factions.GetRelation(FactionType.Civiles);
        }

        // Narrativa
        if (narrative != null)
        {
            data.CurrentMission = narrative.CurrentMission;
            data.StoryFlags = new List<string>(narrative.StoryFlags.Keys);
            data.CompletedMissionIds = new List<string>();
            foreach (var m in narrative.CompletedMissions)
                data.CompletedMissionIds.Add(m.Id);
            data.ActiveMissionIds = new List<string>();
            foreach (var m in narrative.ActiveMissions)
                data.ActiveMissionIds.Add(m.Id);
            data.UnlockedFlashbacks = new List<string>(narrative.UnlockedFlashbacks);
        }

        // Armas
        var weapon = WeaponSystem.Instance;
        if (weapon != null)
        {
            data.CurrentWeaponIndex = weapon.CurrentWeaponIndex;
            data.CurrentAmmo = weapon.CurrentAmmo;
            data.ReserveAmmo = weapon.ReserveAmmo;
            data.GrenadeCount = weapon.GrenadeCount;
            data.WeaponTypes = new List<int>();
            foreach (var w in weapon.WeaponInventory)
                data.WeaponTypes.Add((int)w.Type);
        }

        // Clima y tiempo
        var weather = WeatherSystem.Instance;
        if (weather != null)
        {
            data.TimeOfDay = weather.GetTimeOfDay();
            data.CurrentWeather = (int)weather.CurrentWeather;
        }

        // Estadísticas de juego
        data.TotalKills = CombatManager.Instance?.TotalKills ?? 0;

        return data;
    }

    #endregion

    #region Carga

    public bool Load(int slot)
    {
        if (IsLoading) return false;

        string filePath = GetSavePath(slot);
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[SaveSystem] No existe guardado en slot {slot}");
            return false;
        }

        IsLoading = true;
        OnLoadStarted?.Invoke();

        try
        {
            string json = File.ReadAllText(filePath);

            if (encryptSaves)
                json = Decrypt(json);

            SaveData data = JsonUtility.FromJson<SaveData>(json);
            ApplySaveData(data);

            Debug.Log($"[SaveSystem] Carga exitosa desde slot {slot}");
            IsLoading = false;
            OnLoadCompleted?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveSystem] Error al cargar: {e.Message}");
            OnSaveError?.Invoke(e.Message);
            IsLoading = false;
            return false;
        }
    }

    private void ApplySaveData(SaveData data)
    {
        var gm = GameManager.Instance;

        // Cargar escena
        gm.LoadScene(data.CurrentScene);

        // Restaurar estado del juego
        gm.Difficulty = (DifficultyLevel)data.Difficulty;

        // Karma
        if (gm.Karma != null)
        {
            gm.Karma.ModifyHonor(data.KarmaHonor - gm.Karma.Honor);
            gm.Karma.ModifyHumanidad(data.KarmaHumanidad - gm.Karma.Humanidad);
        }

        // Facciones
        if (gm.Factions != null)
        {
            gm.Factions.ModifyRelation(FactionType.FrenteAmazonico,
                data.FactionFrenteAmazonico - gm.Factions.GetRelation(FactionType.FrenteAmazonico));
            gm.Factions.ModifyRelation(FactionType.FuerzaNacional,
                data.FactionFuerzaNacional - gm.Factions.GetRelation(FactionType.FuerzaNacional));
            gm.Factions.ModifyRelation(FactionType.LosHalcones,
                data.FactionLosHalcones - gm.Factions.GetRelation(FactionType.LosHalcones));
            gm.Factions.ModifyRelation(FactionType.CartelDelNorte,
                data.FactionCartelDelNorte - gm.Factions.GetRelation(FactionType.CartelDelNorte));
            gm.Factions.ModifyRelation(FactionType.Civiles,
                data.FactionCiviles - gm.Factions.GetRelation(FactionType.Civiles));
        }

        // Clima
        if (WeatherSystem.Instance != null)
        {
            WeatherSystem.Instance.SetTime(data.TimeOfDay * 24f);
            WeatherSystem.Instance.SetWeather((WeatherType)data.CurrentWeather, 0.5f);
        }

        // Narrativa - restaurar flags
        if (NarrativeManager.Instance != null && data.StoryFlags != null)
        {
            foreach (var flag in data.StoryFlags)
            {
                NarrativeManager.Instance.StoryFlags[flag] = true;
            }
        }
    }

    #endregion

    #region Metadata

    public SaveMetadata GetSaveMetadata(int slot)
    {
        string metaPath = GetMetadataPath(slot);
        if (!File.Exists(metaPath)) return null;

        string json = File.ReadAllText(metaPath);
        return JsonUtility.FromJson<SaveMetadata>(json);
    }

    public bool SaveExists(int slot)
    {
        return File.Exists(GetSavePath(slot));
    }

    public void DeleteSave(int slot)
    {
        string savePath = GetSavePath(slot);
        string metaPath = GetMetadataPath(slot);

        if (File.Exists(savePath)) File.Delete(savePath);
        if (File.Exists(metaPath)) File.Delete(metaPath);
    }

    #endregion

    #region Cifrado AES

    private string Encrypt(string plainText)
    {
        byte[] key = Encoding.UTF8.GetBytes(encryptionKey.PadRight(32).Substring(0, 32));
        byte[] iv = Encoding.UTF8.GetBytes(encryptionKey.PadRight(16).Substring(0, 16));

        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;

            ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return Convert.ToBase64String(encryptedBytes);
        }
    }

    private string Decrypt(string cipherText)
    {
        byte[] key = Encoding.UTF8.GetBytes(encryptionKey.PadRight(32).Substring(0, 32));
        byte[] iv = Encoding.UTF8.GetBytes(encryptionKey.PadRight(16).Substring(0, 16));

        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;

            ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            byte[] cipherBytes = Convert.FromBase64String(cipherText);
            byte[] decryptedBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
    }

    #endregion

    #region Utilidades

    private string GetSavePath(int slot)
    {
        return Path.Combine(savePath, $"save_slot_{slot}.dat");
    }

    private string GetMetadataPath(int slot)
    {
        return Path.Combine(savePath, $"save_meta_{slot}.json");
    }

    #endregion
}

// === DATOS DE GUARDADO ===

[System.Serializable]
public class SaveData
{
    // Meta
    public string SaveVersion;
    public string SaveDate;
    public float PlayTime;
    public int Chapter;
    public string CurrentScene;
    public int Difficulty;

    // Jugador
    public string PlayerName;
    public int PlayerLevel;
    public int PlayerExperience;
    public float PlayerHealth;
    public float PlayerMaxHealth;
    public Vector3 PlayerPosition;
    public Vector3 PlayerRotation;

    // Estadísticas
    public float StatCombate;
    public float StatSigilo;
    public float StatPersuasion;
    public float StatResistencia;
    public float StatSupervivencia;
    public int SkillPoints;

    // Heridas
    public bool LeftArmWounded;
    public bool RightArmWounded;
    public bool LeftLegWounded;
    public bool RightLegWounded;

    // Karma
    public float KarmaHonor;
    public float KarmaHumanidad;

    // Facciones
    public float FactionFrenteAmazonico;
    public float FactionFuerzaNacional;
    public float FactionLosHalcones;
    public float FactionCartelDelNorte;
    public float FactionCiviles;

    // Narrativa
    public int CurrentMission;
    public List<string> StoryFlags;
    public List<string> CompletedMissionIds;
    public List<string> ActiveMissionIds;
    public List<string> UnlockedFlashbacks;

    // Armas
    public int CurrentWeaponIndex;
    public int CurrentAmmo;
    public int ReserveAmmo;
    public int GrenadeCount;
    public List<int> WeaponTypes;

    // Mundo
    public float TimeOfDay;
    public int CurrentWeather;

    // Estadísticas
    public int TotalKills;
}

[System.Serializable]
public class SaveMetadata
{
    public int Slot;
    public string PlayerName;
    public int Chapter;
    public float PlayTime;
    public string SaveDate;
    public string SceneName;
    public int Level;
}
