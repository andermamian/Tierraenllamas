using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// TIERRA EN LLAMAS - KarmaSystem
/// Sistema de karma tricategórico: Honor, Lealtad, Humanidad
/// Cada eje es independiente y afecta diálogos, misiones y finales.
/// </summary>
public class KarmaSystem : MonoBehaviour
{
    public static KarmaSystem Instance { get; private set; }

    [Header("Ejes de Karma (0-100)")]
    [SerializeField] private float honor = 50f;
    [SerializeField] private float humanidad = 50f;

    public float Honor
    {
        get => honor;
        private set => honor = Mathf.Clamp(value, 0f, 100f);
    }

    public float Humanidad
    {
        get => humanidad;
        private set => humanidad = Mathf.Clamp(value, 0f, 100f);
    }

    // Eventos
    public event Action<float, float> OnHonorChanged;      // newValue, delta
    public event Action<float, float> OnHumanidadChanged;
    public event Action<KarmaThreshold> OnThresholdReached;

    // Historial de cambios
    public List<KarmaChange> History = new List<KarmaChange>();

    private void Awake()
    {
        Instance = this;
    }

    #region Modificadores

    public void ModifyHonor(float amount)
    {
        float previous = honor;
        Honor += amount;

        RecordChange("Honor", amount, honor);
        OnHonorChanged?.Invoke(honor, amount);
        CheckThresholds("Honor", previous, honor);

        Debug.Log($"[Karma] Honor: {previous:F0} -> {honor:F0} ({(amount > 0 ? "+" : "")}{amount:F0})");
    }

    public void ModifyHumanidad(float amount)
    {
        float previous = humanidad;
        Humanidad += amount;

        RecordChange("Humanidad", amount, humanidad);
        OnHumanidadChanged?.Invoke(humanidad, amount);
        CheckThresholds("Humanidad", previous, humanidad);

        Debug.Log($"[Karma] Humanidad: {previous:F0} -> {humanidad:F0} ({(amount > 0 ? "+" : "")}{amount:F0})");
    }

    #endregion

    #region Evaluación

    /// <summary>
    /// Obtiene el título moral del jugador según su karma actual
    /// </summary>
    public string GetMoralTitle()
    {
        if (honor > 70 && humanidad > 70)
            return "Héroe del Pueblo";
        if (honor > 70 && humanidad < 30)
            return "Soldado Implacable";
        if (honor < 30 && humanidad > 70)
            return "Idealista Ingenuo";
        if (honor < 30 && humanidad < 30)
            return "Mercenario Sin Alma";
        if (honor > 50 && humanidad > 50)
            return "Hombre de Principios";

        return "Sobreviviente";
    }

    /// <summary>
    /// Evalúa si el jugador cumple condiciones para un final específico
    /// </summary>
    public bool MeetsEndingCondition(EndingType ending)
    {
        var factions = GameManager.Instance.Factions;

        switch (ending)
        {
            case EndingType.LaPazImperfecta:
                return humanidad > 70 && honor > 60;
            case EndingType.ElHombreDelEstado:
                return factions.GetRelation(FactionType.FuerzaNacional) > 80 && honor > 50;
            case EndingType.LaSelvaLoReclamo:
                return factions.GetRelation(FactionType.FrenteAmazonico) > 80 && humanidad < 40;
            case EndingType.ElSenorDelNorte:
                return factions.GetRelation(FactionType.CartelDelNorte) > 80 && honor < 30;
            case EndingType.LaCeniza:
                return honor < 40 && humanidad < 40;
            default:
                return false;
        }
    }

    /// <summary>
    /// Obtiene los finales disponibles según el karma actual
    /// </summary>
    public List<EndingType> GetAvailableEndings()
    {
        List<EndingType> endings = new List<EndingType>();
        foreach (EndingType ending in Enum.GetValues(typeof(EndingType)))
        {
            if (MeetsEndingCondition(ending))
                endings.Add(ending);
        }
        if (endings.Count == 0)
            endings.Add(EndingType.LaCeniza);
        return endings;
    }

    #endregion

    #region Acciones que afectan Karma

    /// <summary>
    /// Matar a un civil reduce drásticamente la humanidad
    /// </summary>
    public void OnCivilianKilled()
    {
        ModifyHumanidad(-20f);
        ModifyHonor(-10f);
    }

    /// <summary>
    /// Proteger a un civil aumenta humanidad
    /// </summary>
    public void OnCivilianProtected()
    {
        ModifyHumanidad(+10f);
        ModifyHonor(+5f);
    }

    /// <summary>
    /// Cumplir una promesa aumenta honor
    /// </summary>
    public void OnPromiseKept()
    {
        ModifyHonor(+15f);
    }

    /// <summary>
    /// Romper una promesa reduce honor
    /// </summary>
    public void OnPromiseBroken()
    {
        ModifyHonor(-15f);
    }

    /// <summary>
    /// Rendirse en vez de matar aumenta humanidad
    /// </summary>
    public void OnMercyShown()
    {
        ModifyHumanidad(+8f);
    }

    /// <summary>
    /// Ejecutar a un enemigo rendido reduce humanidad
    /// </summary>
    public void OnExecutionPerformed()
    {
        ModifyHumanidad(-12f);
        ModifyHonor(-5f);
    }

    #endregion

    #region Utilidades

    public void ResetKarma()
    {
        honor = 50f;
        humanidad = 50f;
        History.Clear();
    }

    private void RecordChange(string axis, float amount, float newValue)
    {
        History.Add(new KarmaChange
        {
            Axis = axis,
            Amount = amount,
            NewValue = newValue,
            Timestamp = GameManager.Instance.GameTime,
            Chapter = GameManager.Instance.CurrentChapter
        });
    }

    private void CheckThresholds(string axis, float previous, float current)
    {
        // Notificar cuando se cruzan umbrales importantes
        float[] thresholds = { 20f, 40f, 60f, 80f };

        foreach (float threshold in thresholds)
        {
            if ((previous < threshold && current >= threshold) ||
                (previous >= threshold && current < threshold))
            {
                OnThresholdReached?.Invoke(new KarmaThreshold
                {
                    Axis = axis,
                    Value = threshold,
                    CrossedUp = current >= threshold
                });
            }
        }
    }

    #endregion
}

[System.Serializable]
public class KarmaChange
{
    public string Axis;
    public float Amount;
    public float NewValue;
    public float Timestamp;
    public int Chapter;
}

[System.Serializable]
public class KarmaThreshold
{
    public string Axis;
    public float Value;
    public bool CrossedUp;
}

// === SISTEMA DE FACCIONES ===

public enum FactionType
{
    None,
    FrenteAmazonico,    // Guerrilla
    FuerzaNacional,     // Estado
    LosHalcones,        // Paramilitares
    CartelDelNorte,     // Narcotráfico
    Civiles             // Civiles y ONG
}

/// <summary>
/// Sistema de relaciones con facciones
/// </summary>
public class FactionSystem : MonoBehaviour
{
    public static FactionSystem Instance { get; private set; }

    [Header("Relaciones (0-100)")]
    private Dictionary<FactionType, float> relations = new Dictionary<FactionType, float>();

    // Eventos
    public event Action<FactionType, float, float> OnRelationChanged;
    public event Action<FactionType> OnFactionHostile;
    public event Action<FactionType> OnFactionAllied;

    private void Awake()
    {
        Instance = this;
        InitializeRelations();
    }

    private void InitializeRelations()
    {
        relations[FactionType.FrenteAmazonico] = 30f;
        relations[FactionType.FuerzaNacional] = 40f;
        relations[FactionType.LosHalcones] = 20f;
        relations[FactionType.CartelDelNorte] = 25f;
        relations[FactionType.Civiles] = 50f;
    }

    public float GetRelation(FactionType faction)
    {
        if (relations.ContainsKey(faction))
            return relations[faction];
        return 0f;
    }

    public void ModifyRelation(FactionType faction, float amount)
    {
        if (!relations.ContainsKey(faction)) return;

        float previous = relations[faction];
        relations[faction] = Mathf.Clamp(relations[faction] + amount, 0f, 100f);
        float current = relations[faction];

        OnRelationChanged?.Invoke(faction, current, amount);

        // Verificar cambios de estado
        if (previous >= 20f && current < 20f)
            OnFactionHostile?.Invoke(faction);
        if (previous < 70f && current >= 70f)
            OnFactionAllied?.Invoke(faction);

        // Facciones opuestas se ven afectadas
        ApplyRivalryEffects(faction, amount);

        Debug.Log($"[Facciones] {faction}: {previous:F0} -> {current:F0} ({(amount > 0 ? "+" : "")}{amount:F0})");
    }

    private void ApplyRivalryEffects(FactionType faction, float amount)
    {
        // Ayudar a una facción perjudica a sus rivales
        float rivalryFactor = -0.3f;

        switch (faction)
        {
            case FactionType.FrenteAmazonico:
                ModifyRelationSilent(FactionType.FuerzaNacional, amount * rivalryFactor);
                ModifyRelationSilent(FactionType.LosHalcones, amount * rivalryFactor * 1.5f);
                break;
            case FactionType.FuerzaNacional:
                ModifyRelationSilent(FactionType.FrenteAmazonico, amount * rivalryFactor);
                ModifyRelationSilent(FactionType.CartelDelNorte, amount * rivalryFactor);
                break;
            case FactionType.LosHalcones:
                ModifyRelationSilent(FactionType.FrenteAmazonico, amount * rivalryFactor * 1.5f);
                break;
            case FactionType.CartelDelNorte:
                ModifyRelationSilent(FactionType.FuerzaNacional, amount * rivalryFactor);
                break;
        }
    }

    private void ModifyRelationSilent(FactionType faction, float amount)
    {
        if (!relations.ContainsKey(faction)) return;
        relations[faction] = Mathf.Clamp(relations[faction] + amount, 0f, 100f);
    }

    public FactionStanding GetStanding(FactionType faction)
    {
        float relation = GetRelation(faction);
        if (relation >= 80f) return FactionStanding.Allied;
        if (relation >= 60f) return FactionStanding.Friendly;
        if (relation >= 40f) return FactionStanding.Neutral;
        if (relation >= 20f) return FactionStanding.Unfriendly;
        return FactionStanding.Hostile;
    }

    public string GetFactionName(FactionType faction)
    {
        switch (faction)
        {
            case FactionType.FrenteAmazonico: return "Frente Amazónico";
            case FactionType.FuerzaNacional: return "Fuerza Nacional";
            case FactionType.LosHalcones: return "Los Halcones";
            case FactionType.CartelDelNorte: return "Cartel del Norte";
            case FactionType.Civiles: return "Civiles y ONG";
            default: return "Desconocido";
        }
    }

    public Color GetFactionColor(FactionType faction)
    {
        switch (faction)
        {
            case FactionType.FrenteAmazonico: return new Color(0.2f, 0.6f, 0.1f); // Verde selva
            case FactionType.FuerzaNacional: return new Color(0.1f, 0.3f, 0.6f);  // Azul militar
            case FactionType.LosHalcones: return new Color(0.6f, 0.6f, 0.6f);     // Gris
            case FactionType.CartelDelNorte: return new Color(0.8f, 0.1f, 0.1f);  // Rojo
            case FactionType.Civiles: return new Color(0.9f, 0.8f, 0.2f);         // Amarillo
            default: return Color.white;
        }
    }

    public void ResetRelations()
    {
        InitializeRelations();
    }
}

public enum FactionStanding
{
    Hostile,
    Unfriendly,
    Neutral,
    Friendly,
    Allied
}
