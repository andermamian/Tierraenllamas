using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// TIERRA EN LLAMAS - CombatManager
/// Gestiona encuentros de combate, zonas de conflicto, oleadas de enemigos,
/// sistema de alerta regional y transiciones entre exploración y combate.
/// </summary>
public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance { get; private set; }

    [Header("Estado de Combate")]
    public CombatState CurrentCombatState = CombatState.Peace;
    public float CombatIntensity { get; private set; } // 0-1
    public int ActiveEnemies { get; private set; }
    public int TotalKills { get; private set; }

    [Header("Configuración")]
    [SerializeField] private float combatMusicFadeTime = 2f;
    [SerializeField] private float peaceCooldown = 8f;
    [SerializeField] private float alertEscalationTime = 5f;

    [Header("Sistema de Alerta Regional")]
    [SerializeField] private float alertLevel = 0f; // 0-5
    [SerializeField] private float alertDecayRate = 0.1f;
    [SerializeField] private float alertEscalationRate = 0.5f;

    // Encuentros activos
    private List<CombatEncounter> activeEncounters = new List<CombatEncounter>();
    private List<EnemyAI> trackedEnemies = new List<EnemyAI>();
    private float lastCombatTime;
    private float combatStartTime;

    // Eventos
    public event Action<CombatState> OnCombatStateChanged;
    public event Action<float> OnAlertLevelChanged;
    public event Action<int> OnEnemyKilled;
    public event Action OnCombatStart;
    public event Action OnCombatEnd;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        UpdateCombatState();
        UpdateAlertLevel();
        UpdateIntensity();
    }

    #region Estado de Combate

    private void UpdateCombatState()
    {
        // Contar enemigos activos alertados
        ActiveEnemies = 0;
        foreach (var enemy in trackedEnemies)
        {
            if (enemy != null && enemy.IsAlive && enemy.IsAlerted)
                ActiveEnemies++;
        }

        CombatState newState;

        if (ActiveEnemies > 0)
        {
            newState = CombatState.Active;
            lastCombatTime = Time.time;
        }
        else if (Time.time - lastCombatTime < peaceCooldown)
        {
            newState = CombatState.Cooldown;
        }
        else
        {
            newState = CombatState.Peace;
        }

        if (newState != CurrentCombatState)
        {
            ChangeCombatState(newState);
        }
    }

    private void ChangeCombatState(CombatState newState)
    {
        CombatState previousState = CurrentCombatState;
        CurrentCombatState = newState;

        switch (newState)
        {
            case CombatState.Active:
                if (previousState == CombatState.Peace)
                {
                    combatStartTime = Time.time;
                    OnCombatStart?.Invoke();
                    GameManager.Instance.ChangeState(GameState.InCombat);
                }
                break;

            case CombatState.Peace:
                OnCombatEnd?.Invoke();
                if (GameManager.Instance.CurrentState == GameState.InCombat)
                    GameManager.Instance.ChangeState(GameState.Playing);
                break;
        }

        OnCombatStateChanged?.Invoke(newState);
    }

    #endregion

    #region Sistema de Alerta Regional

    private void UpdateAlertLevel()
    {
        if (CurrentCombatState == CombatState.Active)
        {
            // Escalar alerta durante combate
            alertLevel += alertEscalationRate * Time.deltaTime;
        }
        else
        {
            // Decaer alerta en paz
            alertLevel -= alertDecayRate * Time.deltaTime;
        }

        alertLevel = Mathf.Clamp(alertLevel, 0f, 5f);
        OnAlertLevelChanged?.Invoke(alertLevel);
    }

    /// <summary>
    /// Nivel de alerta determina refuerzos y patrullas
    /// 0: Paz total - patrullas normales
    /// 1: Sospecha - patrullas más frecuentes
    /// 2: Alerta baja - enemigos buscan activamente
    /// 3: Alerta media - refuerzos llegan
    /// 4: Alerta alta - helicópteros/vehículos
    /// 5: Máxima - oleadas continuas
    /// </summary>
    public int GetAlertTier()
    {
        return Mathf.FloorToInt(alertLevel);
    }

    public void IncreaseAlert(float amount)
    {
        alertLevel += amount;
        alertLevel = Mathf.Clamp(alertLevel, 0f, 5f);
    }

    #endregion

    #region Gestión de Encuentros

    public void RegisterEnemy(EnemyAI enemy)
    {
        if (!trackedEnemies.Contains(enemy))
        {
            trackedEnemies.Add(enemy);
            enemy.OnDeath += HandleEnemyDeath;
        }
    }

    public void UnregisterEnemy(EnemyAI enemy)
    {
        trackedEnemies.Remove(enemy);
        enemy.OnDeath -= HandleEnemyDeath;
    }

    private void HandleEnemyDeath(EnemyAI enemy)
    {
        TotalKills++;
        OnEnemyKilled?.Invoke(TotalKills);
        trackedEnemies.Remove(enemy);

        // Incrementar alerta por matar enemigos
        IncreaseAlert(0.3f);

        // Karma: matar afecta humanidad
        if (enemy.Faction == FactionType.Civiles)
        {
            GameManager.Instance.Karma.ModifyHumanidad(-15f);
        }
    }

    public void StartEncounter(CombatEncounter encounter)
    {
        activeEncounters.Add(encounter);
        encounter.Begin();
    }

    public void EndEncounter(CombatEncounter encounter)
    {
        activeEncounters.Remove(encounter);
    }

    #endregion

    #region Intensidad

    private void UpdateIntensity()
    {
        if (CurrentCombatState != CombatState.Active)
        {
            CombatIntensity = Mathf.Lerp(CombatIntensity, 0f, Time.deltaTime);
            return;
        }

        // Calcular intensidad basada en múltiples factores
        float enemyFactor = Mathf.Clamp01(ActiveEnemies / 8f);
        float healthFactor = 1f - (PlayerController.Instance?.Health.GetHealthPercent() ?? 1f);
        float durationFactor = Mathf.Clamp01((Time.time - combatStartTime) / 30f);

        CombatIntensity = Mathf.Lerp(CombatIntensity,
            (enemyFactor * 0.4f + healthFactor * 0.4f + durationFactor * 0.2f),
            Time.deltaTime * 2f);
    }

    #endregion

    #region Spawning de Refuerzos

    public void SpawnReinforcements(Vector3 position, FactionType faction, int count)
    {
        StartCoroutine(SpawnReinforcementsCoroutine(position, faction, count));
    }

    private IEnumerator SpawnReinforcementsCoroutine(Vector3 position, FactionType faction, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 spawnPos = position + UnityEngine.Random.insideUnitSphere * 5f;
            spawnPos.y = position.y;

            // Instanciar enemigo según facción
            // GameObject enemy = Instantiate(GetEnemyPrefab(faction), spawnPos, Quaternion.identity);
            // RegisterEnemy(enemy.GetComponent<EnemyAI>());

            yield return new WaitForSeconds(0.5f);
        }
    }

    #endregion
}

public enum CombatState
{
    Peace,
    Cooldown,
    Active,
    Boss
}

/// <summary>
/// Definición de un encuentro de combate scriptado
/// </summary>
[System.Serializable]
public class CombatEncounter
{
    public string EncounterName;
    public Vector3 Center;
    public float Radius;
    public List<EnemyWave> Waves;
    public bool IsCompleted;
    public int CurrentWave;

    public event Action OnEncounterComplete;

    public void Begin()
    {
        CurrentWave = 0;
        SpawnWave(CurrentWave);
    }

    public void SpawnWave(int waveIndex)
    {
        if (waveIndex >= Waves.Count)
        {
            Complete();
            return;
        }

        // Spawn enemies from wave data
        Debug.Log($"[CombatEncounter] Oleada {waveIndex + 1}/{Waves.Count} - {EncounterName}");
    }

    public void WaveCleared()
    {
        CurrentWave++;
        if (CurrentWave >= Waves.Count)
            Complete();
        else
            SpawnWave(CurrentWave);
    }

    private void Complete()
    {
        IsCompleted = true;
        OnEncounterComplete?.Invoke();
    }
}

[System.Serializable]
public class EnemyWave
{
    public int EnemyCount;
    public EnemyRank MinRank;
    public EnemyRank MaxRank;
    public FactionType Faction;
    public float SpawnDelay;
    public Vector3[] SpawnPoints;
}
