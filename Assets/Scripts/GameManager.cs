using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;

/// <summary>
/// TIERRA EN LLAMAS - GameManager Principal
/// Controla el ciclo de vida del juego, estados globales y transiciones.
/// Singleton persistente entre escenas.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Estado del Juego")]
    public GameState CurrentState = GameState.MainMenu;
    public float GameTime { get; private set; }
    public int CurrentChapter { get; private set; } = 1;
    public string CurrentScene { get; private set; }

    [Header("Configuración Global")]
    public GameSettings Settings;
    public DifficultyLevel Difficulty = DifficultyLevel.Normal;

    [Header("Referencias de Sistemas")]
    public KarmaSystem Karma;
    public FactionSystem Factions;
    public NarrativeManager Narrative;
    public CombatManager Combat;
    public SaveSystem SaveManager;
    public WeatherSystem Weather;
    public AudioManager AudioMgr;

    // Eventos globales
    public event Action<GameState> OnGameStateChanged;
    public event Action<int> OnChapterChanged;
    public event Action<string> OnSceneTransition;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeSystems();
    }

    private void InitializeSystems()
    {
        Settings = new GameSettings();
        Karma = GetComponentInChildren<KarmaSystem>() ?? gameObject.AddComponent<KarmaSystem>();
        Factions = GetComponentInChildren<FactionSystem>() ?? gameObject.AddComponent<FactionSystem>();
        SaveManager = GetComponentInChildren<SaveSystem>() ?? gameObject.AddComponent<SaveSystem>();

        Application.targetFrameRate = Settings.TargetFPS;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        QualitySettings.vSyncCount = 0;

        Debug.Log("[GameManager] Tierra en Llamas inicializado - Colombia, 1993");
    }

    private void Update()
    {
        if (CurrentState == GameState.Playing)
        {
            GameTime += Time.deltaTime;
        }
    }

    public void ChangeState(GameState newState)
    {
        if (CurrentState == newState) return;

        GameState previousState = CurrentState;
        CurrentState = newState;

        switch (newState)
        {
            case GameState.Playing:
                Time.timeScale = 1f;
                break;
            case GameState.Paused:
                Time.timeScale = 0f;
                break;
            case GameState.InDialogue:
                Time.timeScale = 0.1f; // Slow-mo durante diálogos tensos
                break;
            case GameState.InCombat:
                Time.timeScale = 1f;
                break;
            case GameState.Cutscene:
                Time.timeScale = 1f;
                break;
            case GameState.GameOver:
                Time.timeScale = 0f;
                break;
        }

        OnGameStateChanged?.Invoke(newState);
        Debug.Log($"[GameManager] Estado: {previousState} -> {newState}");
    }

    public void StartNewGame(CharacterBackground background)
    {
        CurrentChapter = 1;
        GameTime = 0f;
        Karma.ResetKarma();
        Factions.ResetRelations();
        ChangeState(GameState.Playing);
        LoadScene("Putumayo_Selva");
    }

    public void AdvanceChapter()
    {
        CurrentChapter++;
        OnChapterChanged?.Invoke(CurrentChapter);

        // Determinar escena según capítulo
        if (CurrentChapter <= 4)
            LoadScene("Putumayo_Selva");
        else if (CurrentChapter <= 9)
            LoadScene("Medellin_Ciudad");
        else
            LoadScene("Bogota_Capital");
    }

    public void LoadScene(string sceneName)
    {
        CurrentScene = sceneName;
        OnSceneTransition?.Invoke(sceneName);
        StartCoroutine(LoadSceneAsync(sceneName));
    }

    private IEnumerator LoadSceneAsync(string sceneName)
    {
        ChangeState(GameState.Loading);

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        asyncLoad.allowSceneActivation = false;

        while (asyncLoad.progress < 0.9f)
        {
            yield return null;
        }

        // Guardar automáticamente antes de transición
        SaveManager.AutoSave();

        asyncLoad.allowSceneActivation = true;
        yield return new WaitForSeconds(0.5f);

        ChangeState(GameState.Playing);
    }

    public void TriggerGameOver(GameOverReason reason)
    {
        ChangeState(GameState.GameOver);
        Debug.Log($"[GameManager] Game Over: {reason}");
    }

    public EndingType DetermineEnding()
    {
        float honor = Karma.Honor;
        float humanidad = Karma.Humanidad;
        float lealtadEstado = Factions.GetRelation(FactionType.FuerzaNacional);
        float lealtadGuerrilla = Factions.GetRelation(FactionType.FrenteAmazonico);
        float lealtadCartel = Factions.GetRelation(FactionType.CartelDelNorte);

        if (humanidad > 70f && honor > 60f)
            return EndingType.LaPazImperfecta;
        if (lealtadEstado > 80f && honor > 50f)
            return EndingType.ElHombreDelEstado;
        if (lealtadGuerrilla > 80f && humanidad < 40f)
            return EndingType.LaSelvaLoReclamo;
        if (lealtadCartel > 80f && honor < 30f)
            return EndingType.ElSenorDelNorte;

        return EndingType.LaCeniza;
    }
}

// === ENUMERACIONES GLOBALES ===

public enum GameState
{
    MainMenu,
    Loading,
    Playing,
    Paused,
    InDialogue,
    InCombat,
    InCover,
    Cutscene,
    GameOver,
    Ending
}

public enum DifficultyLevel
{
    Facil,      // Más vida, enemigos menos agresivos
    Normal,     // Balanceado
    Realista,   // Daño realista, IA agresiva, sin regeneración
    Veterano    // Un disparo puede matar, recursos escasos
}

public enum CharacterBackground
{
    Campesino,   // +Supervivencia, +Resistencia
    Soldado,     // +Combate, +Resistencia
    Periodista,  // +Persuasión, +Sigilo
    Estudiante   // +Persuasión, +Supervivencia
}

public enum EndingType
{
    LaPazImperfecta,
    ElHombreDelEstado,
    LaSelvaLoReclamo,
    ElSenorDelNorte,
    LaCeniza
}

public enum GameOverReason
{
    PlayerDeath,
    Captured,
    Betrayed,
    Abandoned
}

[System.Serializable]
public class GameSettings
{
    public int TargetFPS = 60;
    public float MasterVolume = 1f;
    public float MusicVolume = 0.7f;
    public float SFXVolume = 1f;
    public float AmbientVolume = 0.8f;
    public bool Subtitles = true;
    public bool HighContrast = false;
    public float TextSize = 1f;
    public bool Vibration = true;
    public GraphicsQuality Quality = GraphicsQuality.High;
    public bool PostProcessing = true;
    public bool DynamicShadows = true;
    public bool VolumetricFog = true;
    public bool MotionBlur = true;
}

public enum GraphicsQuality
{
    Low,
    Medium,
    High,
    Ultra
}
