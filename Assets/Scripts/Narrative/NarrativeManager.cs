using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// TIERRA EN LLAMAS - NarrativeManager
/// Sistema narrativo completo con:
/// - Diálogos ramificados (2-4 opciones)
/// - Decisiones con temporizador (30s máximo)
/// - Impacto en karma y facciones
/// - Flashbacks desbloqueables
/// - Diario del jugador automático
/// - 5 finales según variables acumuladas
/// </summary>
public class NarrativeManager : MonoBehaviour
{
    public static NarrativeManager Instance { get; private set; }

    [Header("Estado Narrativo")]
    public int CurrentChapter = 1;
    public int CurrentMission = 0;
    public string CurrentArc = "La Selva No Perdona";
    public bool IsInDialogue { get; private set; }

    [Header("Diálogo Actual")]
    public DialogueNode CurrentNode;
    public NPC CurrentSpeaker;
    public float DecisionTimer { get; private set; }
    public bool HasActiveTimer { get; private set; }

    [Header("Configuración")]
    [SerializeField] private float defaultDecisionTime = 30f;
    [SerializeField] private float textSpeed = 0.03f; // Segundos por carácter

    [Header("Datos")]
    public List<Mission> ActiveMissions = new List<Mission>();
    public List<Mission> CompletedMissions = new List<Mission>();
    public List<JournalEntry> Journal = new List<JournalEntry>();
    public List<string> UnlockedFlashbacks = new List<string>();
    public Dictionary<string, bool> StoryFlags = new Dictionary<string, bool>();

    // Diálogos cargados
    private Dictionary<string, DialogueTree> loadedDialogues = new Dictionary<string, DialogueTree>();

    // Eventos
    public event Action<DialogueNode> OnDialogueStart;
    public event Action OnDialogueEnd;
    public event Action<DialogueChoice[]> OnChoicesPresented;
    public event Action<DialogueChoice> OnChoiceMade;
    public event Action<float> OnTimerUpdate;
    public event Action<Mission> OnMissionStarted;
    public event Action<Mission> OnMissionCompleted;
    public event Action<JournalEntry> OnJournalUpdated;
    public event Action<string> OnFlashbackUnlocked;

    private Coroutine dialogueCoroutine;
    private Coroutine timerCoroutine;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        LoadDialogueData();
    }

    #region Sistema de Diálogos

    public void StartDialogue(string dialogueId, NPC speaker)
    {
        if (IsInDialogue) return;

        DialogueTree tree;
        if (!loadedDialogues.TryGetValue(dialogueId, out tree))
        {
            Debug.LogError($"[Narrative] Diálogo no encontrado: {dialogueId}");
            return;
        }

        CurrentSpeaker = speaker;
        IsInDialogue = true;
        GameManager.Instance.ChangeState(GameState.InDialogue);

        // Iniciar desde el primer nodo
        dialogueCoroutine = StartCoroutine(PlayDialogue(tree));
    }

    private IEnumerator PlayDialogue(DialogueTree tree)
    {
        CurrentNode = tree.RootNode;

        while (CurrentNode != null)
        {
            // Verificar condiciones del nodo
            if (!EvaluateConditions(CurrentNode.Conditions))
            {
                CurrentNode = GetNextValidNode(CurrentNode);
                continue;
            }

            // Mostrar texto del nodo
            OnDialogueStart?.Invoke(CurrentNode);

            // Esperar a que el texto se muestre completamente
            yield return new WaitForSeconds(CurrentNode.Text.Length * textSpeed + 1f);

            // Si tiene opciones de respuesta
            if (CurrentNode.Choices != null && CurrentNode.Choices.Length > 0)
            {
                // Filtrar opciones disponibles según karma/facciones
                List<DialogueChoice> availableChoices = FilterChoices(CurrentNode.Choices);
                OnChoicesPresented?.Invoke(availableChoices.ToArray());

                // Iniciar temporizador si es decisión tensa
                if (CurrentNode.HasTimer)
                {
                    HasActiveTimer = true;
                    DecisionTimer = CurrentNode.TimerDuration > 0 ?
                        CurrentNode.TimerDuration : defaultDecisionTime;
                    timerCoroutine = StartCoroutine(DecisionTimerCoroutine(availableChoices));
                }

                // Esperar decisión del jugador
                yield return new WaitUntil(() => !IsInDialogue || CurrentNode.ChoiceMade);

                if (!IsInDialogue) yield break;
            }
            else
            {
                // Nodo sin opciones - esperar tap para continuar
                yield return new WaitUntil(() => !IsInDialogue || Input.GetMouseButtonDown(0));

                if (!IsInDialogue) yield break;

                // Avanzar al siguiente nodo
                CurrentNode = GetNextNode(CurrentNode);
            }
        }

        EndDialogue();
    }

    private IEnumerator DecisionTimerCoroutine(List<DialogueChoice> choices)
    {
        while (DecisionTimer > 0 && HasActiveTimer)
        {
            DecisionTimer -= Time.unscaledDeltaTime;
            OnTimerUpdate?.Invoke(DecisionTimer / defaultDecisionTime);
            yield return null;
        }

        if (HasActiveTimer)
        {
            // Tiempo agotado - elegir opción por defecto (la última, generalmente la peor)
            MakeChoice(choices[choices.Count - 1]);
        }
    }

    public void MakeChoice(DialogueChoice choice)
    {
        HasActiveTimer = false;
        if (timerCoroutine != null)
            StopCoroutine(timerCoroutine);

        // Aplicar consecuencias
        ApplyChoiceConsequences(choice);

        OnChoiceMade?.Invoke(choice);
        CurrentNode.ChoiceMade = true;

        // Navegar al siguiente nodo según la elección
        CurrentNode = GetNodeById(choice.NextNodeId);

        // Registrar en diario
        AddJournalEntry(new JournalEntry
        {
            Title = $"Decisión: {choice.Text}",
            Description = choice.JournalNote,
            Chapter = CurrentChapter,
            Timestamp = GameManager.Instance.GameTime,
            Type = JournalEntryType.Decision
        });
    }

    public void EndDialogue()
    {
        IsInDialogue = false;
        HasActiveTimer = false;
        CurrentNode = null;
        CurrentSpeaker = null;

        if (timerCoroutine != null)
            StopCoroutine(timerCoroutine);
        if (dialogueCoroutine != null)
            StopCoroutine(dialogueCoroutine);

        OnDialogueEnd?.Invoke();

        if (GameManager.Instance.CurrentState == GameState.InDialogue)
            GameManager.Instance.ChangeState(GameState.Playing);
    }

    #endregion

    #region Consecuencias de Decisiones

    private void ApplyChoiceConsequences(DialogueChoice choice)
    {
        var karma = GameManager.Instance.Karma;
        var factions = GameManager.Instance.Factions;

        // Modificar karma
        if (choice.HonorChange != 0)
            karma.ModifyHonor(choice.HonorChange);
        if (choice.HumanidadChange != 0)
            karma.ModifyHumanidad(choice.HumanidadChange);

        // Modificar relaciones con facciones
        if (choice.FactionChanges != null)
        {
            foreach (var fc in choice.FactionChanges)
            {
                factions.ModifyRelation(fc.Faction, fc.Change);
            }
        }

        // Activar flags de historia
        if (choice.SetFlags != null)
        {
            foreach (var flag in choice.SetFlags)
            {
                StoryFlags[flag] = true;
            }
        }

        // Desbloquear misiones
        if (!string.IsNullOrEmpty(choice.UnlockMission))
        {
            StartMission(choice.UnlockMission);
        }
    }

    #endregion

    #region Sistema de Misiones

    public void StartMission(string missionId)
    {
        Mission mission = LoadMission(missionId);
        if (mission != null)
        {
            ActiveMissions.Add(mission);
            OnMissionStarted?.Invoke(mission);

            AddJournalEntry(new JournalEntry
            {
                Title = $"Nueva misión: {mission.Title}",
                Description = mission.Description,
                Chapter = CurrentChapter,
                Timestamp = GameManager.Instance.GameTime,
                Type = JournalEntryType.Mission
            });
        }
    }

    public void CompleteMission(string missionId, bool success)
    {
        Mission mission = ActiveMissions.Find(m => m.Id == missionId);
        if (mission == null) return;

        mission.IsCompleted = true;
        mission.WasSuccessful = success;
        ActiveMissions.Remove(mission);
        CompletedMissions.Add(mission);

        // Recompensas
        if (success)
        {
            PlayerController.Instance?.Stats.AddExperience(mission.XPReward);
            // Aplicar recompensas de karma/facción
        }

        OnMissionCompleted?.Invoke(mission);
    }

    public void UpdateMissionObjective(string missionId, string objectiveId)
    {
        Mission mission = ActiveMissions.Find(m => m.Id == missionId);
        if (mission == null) return;

        var objective = mission.Objectives.Find(o => o.Id == objectiveId);
        if (objective != null)
        {
            objective.IsCompleted = true;

            // Verificar si todas las obligatorias están completas
            bool allRequired = true;
            foreach (var obj in mission.Objectives)
            {
                if (obj.IsRequired && !obj.IsCompleted)
                {
                    allRequired = false;
                    break;
                }
            }

            if (allRequired)
            {
                CompleteMission(missionId, true);
            }
        }
    }

    private Mission LoadMission(string missionId)
    {
        // Cargar desde JSON en StreamingAssets
        string path = $"Missions/{missionId}";
        TextAsset json = Resources.Load<TextAsset>(path);
        if (json != null)
        {
            return JsonUtility.FromJson<Mission>(json.text);
        }
        return null;
    }

    #endregion

    #region Diario del Jugador

    public void AddJournalEntry(JournalEntry entry)
    {
        entry.Id = Journal.Count;
        Journal.Add(entry);
        OnJournalUpdated?.Invoke(entry);
    }

    public void RegisterCharacterMet(string characterName, string description)
    {
        AddJournalEntry(new JournalEntry
        {
            Title = $"Personaje: {characterName}",
            Description = description,
            Chapter = CurrentChapter,
            Timestamp = GameManager.Instance.GameTime,
            Type = JournalEntryType.Character
        });
    }

    public void RegisterLocationDiscovered(string locationName, string description)
    {
        AddJournalEntry(new JournalEntry
        {
            Title = $"Lugar: {locationName}",
            Description = description,
            Chapter = CurrentChapter,
            Timestamp = GameManager.Instance.GameTime,
            Type = JournalEntryType.Location
        });
    }

    #endregion

    #region Flashbacks

    public void TriggerFlashback(string flashbackId)
    {
        if (UnlockedFlashbacks.Contains(flashbackId)) return;

        UnlockedFlashbacks.Add(flashbackId);
        OnFlashbackUnlocked?.Invoke(flashbackId);

        // Iniciar secuencia de flashback
        StartCoroutine(PlayFlashback(flashbackId));
    }

    private IEnumerator PlayFlashback(string flashbackId)
    {
        GameManager.Instance.ChangeState(GameState.Cutscene);

        // Efecto visual de transición (pantalla se vuelve sepia/borrosa)
        // PostProcessManager.Instance.ApplyFlashbackEffect();

        yield return new WaitForSeconds(1f);

        // Cargar diálogo del flashback
        StartDialogue($"flashback_{flashbackId}", null);

        yield return new WaitUntil(() => !IsInDialogue);

        // Restaurar efectos
        // PostProcessManager.Instance.RemoveFlashbackEffect();

        GameManager.Instance.ChangeState(GameState.Playing);
    }

    #endregion

    #region Utilidades

    private List<DialogueChoice> FilterChoices(DialogueChoice[] choices)
    {
        List<DialogueChoice> available = new List<DialogueChoice>();
        var karma = GameManager.Instance.Karma;
        var factions = GameManager.Instance.Factions;

        foreach (var choice in choices)
        {
            bool meetsRequirements = true;

            // Verificar requisitos de karma
            if (choice.RequiredHonor > 0 && karma.Honor < choice.RequiredHonor)
                meetsRequirements = false;
            if (choice.RequiredHumanidad > 0 && karma.Humanidad < choice.RequiredHumanidad)
                meetsRequirements = false;

            // Verificar requisitos de facción
            if (choice.RequiredFaction != FactionType.None)
            {
                float relation = factions.GetRelation(choice.RequiredFaction);
                if (relation < choice.RequiredFactionLevel)
                    meetsRequirements = false;
            }

            // Verificar flags de historia
            if (choice.RequiredFlags != null)
            {
                foreach (var flag in choice.RequiredFlags)
                {
                    if (!StoryFlags.ContainsKey(flag) || !StoryFlags[flag])
                    {
                        meetsRequirements = false;
                        break;
                    }
                }
            }

            // Verificar habilidades del personaje
            if (choice.RequiredSkill != SkillType.None)
            {
                float skillLevel = GetPlayerSkill(choice.RequiredSkill);
                if (skillLevel < choice.RequiredSkillLevel)
                    meetsRequirements = false;
            }

            if (meetsRequirements)
                available.Add(choice);
        }

        // Siempre debe haber al menos 2 opciones
        if (available.Count < 2)
        {
            // Agregar opciones genéricas
            available.Add(new DialogueChoice
            {
                Text = "...",
                NextNodeId = choices[0].NextNodeId
            });
        }

        return available;
    }

    private bool EvaluateConditions(DialogueCondition[] conditions)
    {
        if (conditions == null || conditions.Length == 0) return true;

        foreach (var condition in conditions)
        {
            if (!EvaluateCondition(condition))
                return false;
        }
        return true;
    }

    private bool EvaluateCondition(DialogueCondition condition)
    {
        switch (condition.Type)
        {
            case ConditionType.KarmaAbove:
                return GetKarmaValue(condition.Parameter) >= condition.Value;
            case ConditionType.KarmaBelow:
                return GetKarmaValue(condition.Parameter) < condition.Value;
            case ConditionType.FactionAbove:
                return GameManager.Instance.Factions.GetRelation(
                    (FactionType)condition.IntParameter) >= condition.Value;
            case ConditionType.HasFlag:
                return StoryFlags.ContainsKey(condition.Parameter) && StoryFlags[condition.Parameter];
            case ConditionType.ChapterIs:
                return CurrentChapter == condition.IntParameter;
            default:
                return true;
        }
    }

    private float GetKarmaValue(string karmaType)
    {
        var karma = GameManager.Instance.Karma;
        switch (karmaType.ToLower())
        {
            case "honor": return karma.Honor;
            case "humanidad": return karma.Humanidad;
            default: return 0;
        }
    }

    private float GetPlayerSkill(SkillType skill)
    {
        var stats = PlayerController.Instance?.Stats;
        if (stats == null) return 0;

        switch (skill)
        {
            case SkillType.Combate: return stats.Combate;
            case SkillType.Sigilo: return stats.Sigilo;
            case SkillType.Persuasion: return stats.Persuasion;
            case SkillType.Resistencia: return stats.Resistencia;
            case SkillType.Supervivencia: return stats.Supervivencia;
            default: return 0;
        }
    }

    private DialogueNode GetNextNode(DialogueNode node)
    {
        if (string.IsNullOrEmpty(node.NextNodeId)) return null;
        return GetNodeById(node.NextNodeId);
    }

    private DialogueNode GetNextValidNode(DialogueNode node)
    {
        // Buscar siguiente nodo que cumpla condiciones
        return GetNextNode(node);
    }

    private DialogueNode GetNodeById(string id)
    {
        // Buscar en el árbol de diálogo actual
        foreach (var tree in loadedDialogues.Values)
        {
            DialogueNode found = tree.FindNode(id);
            if (found != null) return found;
        }
        return null;
    }

    private void LoadDialogueData()
    {
        // Cargar todos los JSON de diálogos desde StreamingAssets
        Debug.Log("[NarrativeManager] Cargando datos narrativos...");
    }

    #endregion
}

// === ESTRUCTURAS DE DATOS NARRATIVOS ===

[System.Serializable]
public class DialogueTree
{
    public string Id;
    public string Title;
    public DialogueNode RootNode;
    public List<DialogueNode> AllNodes;

    public DialogueNode FindNode(string nodeId)
    {
        if (AllNodes == null) return null;
        return AllNodes.Find(n => n.Id == nodeId);
    }
}

[System.Serializable]
public class DialogueNode
{
    public string Id;
    public string SpeakerName;
    public string Text;
    public string Emotion; // neutral, angry, sad, scared, happy
    public string NextNodeId;
    public DialogueChoice[] Choices;
    public DialogueCondition[] Conditions;
    public bool HasTimer;
    public float TimerDuration;
    public bool ChoiceMade;

    // Efectos durante el diálogo
    public string AnimationTrigger;
    public string SoundEffect;
    public string CameraAngle;
}

[System.Serializable]
public class DialogueChoice
{
    public string Text;
    public string NextNodeId;
    public string JournalNote;

    // Consecuencias
    public float HonorChange;
    public float HumanidadChange;
    public FactionChange[] FactionChanges;
    public string[] SetFlags;
    public string UnlockMission;

    // Requisitos para mostrar la opción
    public float RequiredHonor;
    public float RequiredHumanidad;
    public FactionType RequiredFaction;
    public float RequiredFactionLevel;
    public string[] RequiredFlags;
    public SkillType RequiredSkill;
    public float RequiredSkillLevel;

    // Visual
    public ChoiceType Type;
    public string SkillCheckLabel; // "[Persuasión 40]"
}

[System.Serializable]
public class DialogueCondition
{
    public ConditionType Type;
    public string Parameter;
    public int IntParameter;
    public float Value;
}

[System.Serializable]
public class FactionChange
{
    public FactionType Faction;
    public float Change;
}

public enum ConditionType
{
    KarmaAbove,
    KarmaBelow,
    FactionAbove,
    FactionBelow,
    HasFlag,
    NotHasFlag,
    ChapterIs,
    SkillAbove
}

public enum ChoiceType
{
    Normal,
    Aggressive,
    Peaceful,
    Neutral,
    SkillCheck
}

public enum SkillType
{
    None,
    Combate,
    Sigilo,
    Persuasion,
    Resistencia,
    Supervivencia
}

// === MISIONES ===

[System.Serializable]
public class Mission
{
    public string Id;
    public string Title;
    public string Description;
    public int Chapter;
    public MissionType Type;
    public FactionType GivenBy;
    public List<MissionObjective> Objectives;
    public int XPReward;
    public bool IsCompleted;
    public bool WasSuccessful;
    public bool IsOptional;

    // Requisitos
    public int RequiredChapter;
    public string[] RequiredFlags;
}

[System.Serializable]
public class MissionObjective
{
    public string Id;
    public string Description;
    public bool IsRequired;
    public bool IsCompleted;
    public ObjectiveType Type;
    public string TargetId;
    public int TargetCount;
    public int CurrentCount;
}

public enum MissionType
{
    Main,
    Side,
    Faction,
    Exploration
}

public enum ObjectiveType
{
    GoToLocation,
    TalkToNPC,
    KillTarget,
    CollectItem,
    EscortNPC,
    Survive,
    AvoidDetection,
    MakeDecision
}

// === DIARIO ===

[System.Serializable]
public class JournalEntry
{
    public int Id;
    public string Title;
    public string Description;
    public int Chapter;
    public float Timestamp;
    public JournalEntryType Type;
}

public enum JournalEntryType
{
    Mission,
    Decision,
    Character,
    Location,
    Lore,
    Flashback
}

// === NPC ===

[System.Serializable]
public class NPC : MonoBehaviour
{
    public string NPCName;
    public string Description;
    public FactionType Faction;
    public string DialogueId;
    public bool IsImportant;
    public NPCRoutine Routine;

    [Header("Interacción")]
    public float InteractionRange = 3f;
    public bool CanTalk = true;
    public string[] AvailableDialogues;

    public void Interact()
    {
        if (!CanTalk) return;

        string dialogueToUse = GetContextualDialogue();
        NarrativeManager.Instance.StartDialogue(dialogueToUse, this);
    }

    private string GetContextualDialogue()
    {
        // Seleccionar diálogo según contexto (capítulo, karma, flags)
        if (AvailableDialogues != null && AvailableDialogues.Length > 0)
        {
            return AvailableDialogues[0]; // Simplificado
        }
        return DialogueId;
    }
}

[System.Serializable]
public class NPCRoutine
{
    public RoutinePoint[] Points;
    public float Speed = 2f;
    public bool IsActive = true;
}

[System.Serializable]
public class RoutinePoint
{
    public Vector3 Position;
    public float WaitTime;
    public string Animation;
    public float TimeOfDay; // 0-24
}
