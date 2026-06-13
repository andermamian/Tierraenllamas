using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// TIERRA EN LLAMAS - EnemyAI
/// Inteligencia artificial enemiga con comportamiento realista:
/// - Patrullaje con rutas
/// - Detección por vista, sonido y proximidad
/// - Sistema de cobertura inteligente
/// - Flanqueo coordinado con escuadrón
/// - Retirada cuando está herido
/// - Reacción a entorno (vidrios, pisadas, sombras)
/// - Comunicación entre enemigos
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class EnemyAI : MonoBehaviour, IDamageable
{
    [Header("Identificación")]
    public string EnemyName = "Combatiente";
    public FactionType Faction = FactionType.FrenteAmazonico;
    public EnemyRank Rank = EnemyRank.Soldado;

    [Header("Estadísticas")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;
    [SerializeField] private float damage = 20f;
    [SerializeField] private float accuracy = 0.7f;
    [SerializeField] private float reactionTime = 0.3f;
    [SerializeField] private float fireRate = 3f;

    [Header("Detección")]
    [SerializeField] private float viewDistance = 40f;
    [SerializeField] private float viewAngle = 110f;
    [SerializeField] private float hearingRange = 25f;
    [SerializeField] private float closeDetectionRange = 3f;
    [SerializeField] private float alertDuration = 15f;
    [SerializeField] private LayerMask detectionMask;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Movimiento")]
    [SerializeField] private float patrolSpeed = 2f;
    [SerializeField] private float chaseSpeed = 5f;
    [SerializeField] private float coverSearchRadius = 15f;
    [SerializeField] private float flankDistance = 10f;
    [SerializeField] private float retreatHealthThreshold = 25f;

    [Header("Combate")]
    [SerializeField] private float engagementRange = 30f;
    [SerializeField] private float minEngagementRange = 5f;
    [SerializeField] private float burstDuration = 1.5f;
    [SerializeField] private float burstCooldown = 2f;
    [SerializeField] private float grenadeRange = 15f;
    [SerializeField] private int grenadeCount = 1;

    [Header("Patrulla")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private float patrolWaitTime = 3f;
    private int currentPatrolIndex = 0;

    // Componentes
    private NavMeshAgent agent;
    private Animator animator;
    private EnemySquad squad;

    // Estado
    public AIState CurrentState { get; private set; } = AIState.Patrol;
    public bool IsAlerted { get; private set; }
    public bool IsAlive => currentHealth > 0;
    private bool isInCover;
    private bool isFiring;
    private bool canFire = true;

    // Detección
    private Transform playerTransform;
    private Vector3 lastKnownPlayerPosition;
    private float alertTimer;
    private float searchTimer;
    private bool hasLineOfSight;

    // Cobertura
    private CoverPoint currentCoverPoint;
    private float coverTimer;
    private float timeInCover;

    // Eventos
    public event System.Action<EnemyAI> OnDeath;
    public event System.Action<Vector3> OnAlerted;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        currentHealth = maxHealth;
    }

    private void Start()
    {
        playerTransform = PlayerController.Instance?.transform;
        squad = GetComponentInParent<EnemySquad>();

        // Configurar NavMeshAgent
        agent.speed = patrolSpeed;
        agent.stoppingDistance = 1f;
        agent.angularSpeed = 120f;
        agent.acceleration = 8f;

        // Iniciar patrulla
        if (patrolPoints.Length > 0)
        {
            ChangeState(AIState.Patrol);
        }
        else
        {
            ChangeState(AIState.Idle);
        }
    }

    private void Update()
    {
        if (!IsAlive) return;

        UpdateDetection();
        UpdateState();
        UpdateAnimator();
    }

    #region Máquina de Estados

    private void UpdateState()
    {
        switch (CurrentState)
        {
            case AIState.Idle:
                UpdateIdle();
                break;
            case AIState.Patrol:
                UpdatePatrol();
                break;
            case AIState.Alert:
                UpdateAlert();
                break;
            case AIState.Chase:
                UpdateChase();
                break;
            case AIState.Combat:
                UpdateCombat();
                break;
            case AIState.TakingCover:
                UpdateTakingCover();
                break;
            case AIState.Flanking:
                UpdateFlanking();
                break;
            case AIState.Retreating:
                UpdateRetreating();
                break;
            case AIState.Searching:
                UpdateSearching();
                break;
            case AIState.Suppressing:
                UpdateSuppressing();
                break;
        }
    }

    private void ChangeState(AIState newState)
    {
        if (CurrentState == newState) return;

        // Salir del estado anterior
        ExitState(CurrentState);

        CurrentState = newState;

        // Entrar al nuevo estado
        EnterState(newState);
    }

    private void EnterState(AIState state)
    {
        switch (state)
        {
            case AIState.Patrol:
                agent.speed = patrolSpeed;
                break;
            case AIState.Chase:
                agent.speed = chaseSpeed;
                break;
            case AIState.Combat:
                agent.speed = chaseSpeed * 0.5f;
                break;
            case AIState.Retreating:
                agent.speed = chaseSpeed * 1.2f;
                break;
            case AIState.Alert:
                alertTimer = alertDuration;
                break;
        }
    }

    private void ExitState(AIState state)
    {
        switch (state)
        {
            case AIState.TakingCover:
                if (currentCoverPoint != null)
                    currentCoverPoint.IsOccupied = false;
                break;
        }
    }

    #endregion

    #region Detección

    private void UpdateDetection()
    {
        if (playerTransform == null) return;

        bool detected = false;

        // 1. Detección visual
        if (CheckVisualDetection())
        {
            detected = true;
            lastKnownPlayerPosition = playerTransform.position;
            hasLineOfSight = true;
        }
        else
        {
            hasLineOfSight = false;
        }

        // 2. Detección auditiva
        float playerNoise = PlayerController.Instance?.GetNoiseLevel() ?? 0f;
        float distanceToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        if (distanceToPlayer < hearingRange * playerNoise)
        {
            detected = true;
            lastKnownPlayerPosition = playerTransform.position;
        }

        // 3. Detección por proximidad (siempre detecta si está muy cerca)
        if (distanceToPlayer < closeDetectionRange)
        {
            detected = true;
            lastKnownPlayerPosition = playerTransform.position;
        }

        // Actualizar estado de alerta
        if (detected && !IsAlerted)
        {
            BecomeAlerted(lastKnownPlayerPosition);
        }

        if (IsAlerted)
        {
            alertTimer -= Time.deltaTime;
            if (alertTimer <= 0 && !hasLineOfSight)
            {
                IsAlerted = false;
                ChangeState(AIState.Searching);
            }
        }
    }

    private bool CheckVisualDetection()
    {
        if (playerTransform == null) return false;

        Vector3 dirToPlayer = (playerTransform.position - transform.position);
        float distance = dirToPlayer.magnitude;

        // Fuera de rango
        if (distance > viewDistance) return false;

        // Fuera del ángulo de visión
        float angle = Vector3.Angle(transform.forward, dirToPlayer.normalized);
        if (angle > viewAngle / 2f) return false;

        // Verificar línea de visión (obstáculos)
        RaycastHit hit;
        Vector3 eyePosition = transform.position + Vector3.up * 1.6f;
        Vector3 playerCenter = playerTransform.position + Vector3.up * 1f;

        if (Physics.Raycast(eyePosition, (playerCenter - eyePosition).normalized, out hit, distance, obstacleMask))
        {
            if (hit.transform != playerTransform)
                return false; // Obstáculo entre nosotros
        }

        // Modificar detección por visibilidad del jugador
        float playerVisibility = PlayerController.Instance?.GetVisibilityLevel() ?? 1f;
        float effectiveRange = viewDistance * playerVisibility;

        return distance < effectiveRange;
    }

    public void AlertToPosition(Vector3 position)
    {
        if (!IsAlerted)
        {
            BecomeAlerted(position);
        }
        lastKnownPlayerPosition = position;
    }

    private void BecomeAlerted(Vector3 position)
    {
        IsAlerted = true;
        alertTimer = alertDuration;
        lastKnownPlayerPosition = position;

        // Notificar al escuadrón
        if (squad != null)
        {
            squad.AlertSquad(position, this);
        }

        OnAlerted?.Invoke(position);

        // Transición a combate o persecución
        float distance = Vector3.Distance(transform.position, position);
        if (distance < engagementRange && hasLineOfSight)
        {
            ChangeState(AIState.Combat);
        }
        else
        {
            ChangeState(AIState.Chase);
        }
    }

    #endregion

    #region Comportamientos de Estado

    private void UpdateIdle()
    {
        // Mirar alrededor ocasionalmente
        if (Random.value < 0.01f)
        {
            transform.Rotate(0, Random.Range(-45f, 45f), 0);
        }
    }

    private void UpdatePatrol()
    {
        if (patrolPoints.Length == 0) return;

        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            StartCoroutine(PatrolWait());
        }
    }

    private IEnumerator PatrolWait()
    {
        yield return new WaitForSeconds(patrolWaitTime);
        currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
        agent.SetDestination(patrolPoints[currentPatrolIndex].position);
    }

    private void UpdateAlert()
    {
        // Mirar hacia la última posición conocida
        Vector3 lookDir = (lastKnownPlayerPosition - transform.position).normalized;
        lookDir.y = 0;
        if (lookDir != Vector3.zero)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(lookDir), 5f * Time.deltaTime);
        }
    }

    private void UpdateChase()
    {
        agent.SetDestination(lastKnownPlayerPosition);

        float distanceToTarget = Vector3.Distance(transform.position, lastKnownPlayerPosition);

        if (hasLineOfSight && distanceToTarget < engagementRange)
        {
            // Decidir táctica
            DecideCombatTactic();
        }
        else if (distanceToTarget < 2f && !hasLineOfSight)
        {
            // Llegamos pero no vemos al jugador
            ChangeState(AIState.Searching);
        }
    }

    private void UpdateCombat()
    {
        if (!hasLineOfSight)
        {
            ChangeState(AIState.Chase);
            return;
        }

        // Verificar si necesita retirarse
        if (currentHealth < retreatHealthThreshold)
        {
            ChangeState(AIState.Retreating);
            return;
        }

        // Mirar al jugador
        LookAtPlayer();

        // Disparar
        if (canFire)
        {
            StartCoroutine(FireBurst());
        }

        // Moverse tácticamente
        float distToPlayer = Vector3.Distance(transform.position, playerTransform.position);
        if (distToPlayer < minEngagementRange)
        {
            // Muy cerca, retroceder
            Vector3 retreatDir = (transform.position - playerTransform.position).normalized;
            agent.SetDestination(transform.position + retreatDir * 5f);
        }
        else if (distToPlayer > engagementRange)
        {
            // Muy lejos, acercarse
            agent.SetDestination(lastKnownPlayerPosition);
        }
    }

    private void UpdateTakingCover()
    {
        if (currentCoverPoint == null)
        {
            FindCover();
            return;
        }

        // Ir a la cobertura
        float distToCover = Vector3.Distance(transform.position, currentCoverPoint.Position);
        if (distToCover > 1f)
        {
            agent.SetDestination(currentCoverPoint.Position);
        }
        else
        {
            // En cobertura - disparar periódicamente
            isInCover = true;
            timeInCover += Time.deltaTime;

            if (timeInCover > 3f && hasLineOfSight && canFire)
            {
                StartCoroutine(PeekAndShoot());
            }

            // Cambiar de cobertura si lleva mucho tiempo
            if (timeInCover > 8f)
            {
                timeInCover = 0;
                FindCover();
            }
        }
    }

    private void UpdateFlanking()
    {
        if (!agent.pathPending && agent.remainingDistance < 2f)
        {
            // Llegamos a posición de flanqueo
            if (hasLineOfSight)
            {
                ChangeState(AIState.Combat);
            }
            else
            {
                ChangeState(AIState.Chase);
            }
        }
    }

    private void UpdateRetreating()
    {
        // Buscar cobertura lejana al jugador
        Vector3 retreatDir = (transform.position - lastKnownPlayerPosition).normalized;
        Vector3 retreatTarget = transform.position + retreatDir * 15f;

        NavMeshHit navHit;
        if (NavMesh.SamplePosition(retreatTarget, out navHit, 5f, NavMesh.AllAreas))
        {
            agent.SetDestination(navHit.position);
        }

        // Disparar mientras se retira
        if (hasLineOfSight && canFire)
        {
            StartCoroutine(FireBurst());
        }
    }

    private void UpdateSearching()
    {
        searchTimer += Time.deltaTime;

        if (searchTimer > 10f)
        {
            // Dejar de buscar, volver a patrullar
            searchTimer = 0;
            IsAlerted = false;
            ChangeState(AIState.Patrol);
            return;
        }

        // Buscar en posiciones aleatorias cerca de la última posición conocida
        if (!agent.pathPending && agent.remainingDistance < 1f)
        {
            Vector3 searchPoint = lastKnownPlayerPosition +
                Random.insideUnitSphere * 10f;
            searchPoint.y = lastKnownPlayerPosition.y;

            NavMeshHit navHit;
            if (NavMesh.SamplePosition(searchPoint, out navHit, 5f, NavMesh.AllAreas))
            {
                agent.SetDestination(navHit.position);
            }
        }
    }

    private void UpdateSuppressing()
    {
        // Fuego de supresión hacia última posición conocida
        LookAtPosition(lastKnownPlayerPosition);
        if (canFire)
        {
            StartCoroutine(SuppressiveFire());
        }
    }

    #endregion

    #region Tácticas

    private void DecideCombatTactic()
    {
        float healthPercent = currentHealth / maxHealth;
        float distToPlayer = Vector3.Distance(transform.position, playerTransform.position);

        // IA basada en rango y estado
        if (healthPercent < 0.3f)
        {
            ChangeState(AIState.Retreating);
        }
        else if (Rank == EnemyRank.Veterano && Random.value < 0.4f)
        {
            // Veteranos intentan flanquear
            AttemptFlank();
        }
        else if (Random.value < 0.5f)
        {
            ChangeState(AIState.TakingCover);
        }
        else
        {
            ChangeState(AIState.Combat);
        }
    }

    private void AttemptFlank()
    {
        Vector3 playerPos = lastKnownPlayerPosition;
        Vector3 dirToPlayer = (playerPos - transform.position).normalized;

        // Calcular posición de flanqueo (perpendicular)
        Vector3 flankDir = Vector3.Cross(dirToPlayer, Vector3.up).normalized;
        if (Random.value > 0.5f) flankDir = -flankDir;

        Vector3 flankTarget = playerPos + flankDir * flankDistance;

        NavMeshHit navHit;
        if (NavMesh.SamplePosition(flankTarget, out navHit, 5f, NavMesh.AllAreas))
        {
            agent.SetDestination(navHit.position);
            ChangeState(AIState.Flanking);
        }
        else
        {
            ChangeState(AIState.Combat);
        }
    }

    private void FindCover()
    {
        // Buscar puntos de cobertura cercanos
        Collider[] covers = Physics.OverlapSphere(transform.position, coverSearchRadius,
            LayerMask.GetMask("Cover"));

        CoverPoint bestCover = null;
        float bestScore = float.MinValue;

        foreach (var cover in covers)
        {
            CoverPoint cp = cover.GetComponent<CoverPoint>();
            if (cp == null || cp.IsOccupied) continue;

            // Evaluar cobertura
            float score = EvaluateCover(cp);
            if (score > bestScore)
            {
                bestScore = score;
                bestCover = cp;
            }
        }

        if (bestCover != null)
        {
            currentCoverPoint = bestCover;
            bestCover.IsOccupied = true;
            agent.SetDestination(bestCover.Position);
            ChangeState(AIState.TakingCover);
        }
        else
        {
            ChangeState(AIState.Combat);
        }
    }

    private float EvaluateCover(CoverPoint cover)
    {
        float score = 0;

        // Preferir coberturas que bloqueen línea de visión del jugador
        Vector3 coverToPlayer = (lastKnownPlayerPosition - cover.Position).normalized;
        float dot = Vector3.Dot(cover.Normal, coverToPlayer);
        if (dot > 0) score += 50f; // La cobertura mira hacia el jugador

        // Preferir coberturas cercanas
        float distance = Vector3.Distance(transform.position, cover.Position);
        score -= distance * 2f;

        // Preferir coberturas con ángulo de disparo
        if (cover.AllowsPeeking) score += 20f;

        return score;
    }

    #endregion

    #region Combate

    private IEnumerator FireBurst()
    {
        canFire = false;

        // Tiempo de reacción
        yield return new WaitForSeconds(reactionTime);

        float burstTime = 0;
        while (burstTime < burstDuration && hasLineOfSight && IsAlive)
        {
            FireAtPlayer();
            yield return new WaitForSeconds(1f / fireRate);
            burstTime += 1f / fireRate;
        }

        yield return new WaitForSeconds(burstCooldown);
        canFire = true;
    }

    private IEnumerator PeekAndShoot()
    {
        canFire = false;
        animator.SetTrigger("Peek");

        yield return new WaitForSeconds(0.5f);

        // Disparar 2-3 tiros
        int shots = Random.Range(2, 4);
        for (int i = 0; i < shots; i++)
        {
            if (hasLineOfSight)
                FireAtPlayer();
            yield return new WaitForSeconds(1f / fireRate);
        }

        animator.SetTrigger("ReturnToCover");
        yield return new WaitForSeconds(Random.Range(2f, 4f));
        canFire = true;
    }

    private IEnumerator SuppressiveFire()
    {
        canFire = false;

        float suppressTime = 0;
        while (suppressTime < 3f && IsAlive)
        {
            FireAtPosition(lastKnownPlayerPosition + Random.insideUnitSphere * 2f);
            yield return new WaitForSeconds(1f / (fireRate * 1.5f));
            suppressTime += 1f / (fireRate * 1.5f);
        }

        yield return new WaitForSeconds(burstCooldown * 1.5f);
        canFire = true;
    }

    private void FireAtPlayer()
    {
        if (playerTransform == null) return;

        Vector3 targetPos = playerTransform.position + Vector3.up * 1f;
        FireAtPosition(targetPos);
    }

    private void FireAtPosition(Vector3 target)
    {
        Vector3 direction = (target - transform.position).normalized;

        // Aplicar imprecisión
        float inaccuracy = (1f - accuracy) * 0.1f;
        direction += Random.insideUnitSphere * inaccuracy;

        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up * 1.5f, direction, out hit, engagementRange))
        {
            if (hit.transform == playerTransform)
            {
                // Impacto en jugador
                BodyPart hitPart = GetRandomBodyPart();
                PlayerController.Instance?.Health.TakeDamage(damage, hitPart);
                ThirdPersonCamera.Instance?.ImpactEffect(direction, damage * 0.01f);
            }
        }

        // Efecto de disparo
        animator.SetTrigger("Fire");
    }

    private BodyPart GetRandomBodyPart()
    {
        float roll = Random.value;
        if (roll < 0.05f) return BodyPart.Head;
        if (roll < 0.5f) return BodyPart.Torso;
        if (roll < 0.7f) return BodyPart.LeftArm;
        if (roll < 0.85f) return BodyPart.RightArm;
        if (roll < 0.92f) return BodyPart.LeftLeg;
        return BodyPart.RightLeg;
    }

    #endregion

    #region Daño

    public void TakeDamage(float damage, DamageType type, BodyPart part)
    {
        if (!IsAlive) return;

        float finalDamage = damage;

        // Multiplicadores por parte del cuerpo
        switch (part)
        {
            case BodyPart.Head: finalDamage *= 3f; break;
            case BodyPart.Torso: finalDamage *= 1f; break;
            case BodyPart.LeftArm:
            case BodyPart.RightArm: finalDamage *= 0.7f; break;
            case BodyPart.LeftLeg:
            case BodyPart.RightLeg:
                finalDamage *= 0.8f;
                agent.speed *= 0.6f; // Herida en pierna
                break;
        }

        currentHealth -= finalDamage;

        // Reaccionar al daño
        if (!IsAlerted)
        {
            BecomeAlerted(PlayerController.Instance.transform.position);
        }

        animator.SetTrigger("Hit");

        if (currentHealth <= 0)
        {
            Die();
        }
        else if (currentHealth < retreatHealthThreshold)
        {
            ChangeState(AIState.Retreating);
        }
    }

    private void Die()
    {
        currentHealth = 0;
        ChangeState(AIState.Dead);
        agent.enabled = false;
        animator.SetTrigger("Die");

        // Soltar arma/items
        // DropLoot();

        // Notificar escuadrón
        if (squad != null)
            squad.MemberKilled(this);

        OnDeath?.Invoke(this);

        // Dar experiencia al jugador
        PlayerController.Instance?.Stats.AddExperience(GetXPValue());

        // Desactivar después de animación
        StartCoroutine(DeathCleanup());
    }

    private IEnumerator DeathCleanup()
    {
        yield return new WaitForSeconds(10f);
        // Mantener el cuerpo visible pero desactivar componentes costosos
        GetComponent<Collider>().enabled = false;
    }

    private int GetXPValue()
    {
        switch (Rank)
        {
            case EnemyRank.Recluta: return 15;
            case EnemyRank.Soldado: return 25;
            case EnemyRank.Veterano: return 40;
            case EnemyRank.Comandante: return 75;
            default: return 20;
        }
    }

    #endregion

    #region Utilidades

    private void LookAtPlayer()
    {
        if (playerTransform == null) return;
        LookAtPosition(playerTransform.position);
    }

    private void LookAtPosition(Vector3 position)
    {
        Vector3 dir = (position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 8f * Time.deltaTime);
        }
    }

    private void UpdateAnimator()
    {
        animator.SetFloat("Speed", agent.velocity.magnitude / chaseSpeed);
        animator.SetBool("IsAlerted", IsAlerted);
        animator.SetBool("IsInCover", isInCover);
        animator.SetBool("IsFiring", isFiring);
        animator.SetInteger("State", (int)CurrentState);
    }

    #endregion
}

// === ENUMERACIONES DE IA ===

public enum AIState
{
    Idle,
    Patrol,
    Alert,
    Chase,
    Combat,
    TakingCover,
    Flanking,
    Retreating,
    Searching,
    Suppressing,
    Dead
}

public enum EnemyRank
{
    Recluta,     // Baja precisión, se asusta fácil
    Soldado,     // Estándar
    Veterano,    // Alta precisión, flanquea
    Comandante   // Coordina, usa granadas, muy preciso
}

/// <summary>
/// Punto de cobertura en el escenario
/// </summary>
public class CoverPoint : MonoBehaviour
{
    public Vector3 Position => transform.position;
    public Vector3 Normal => transform.forward;
    public bool IsOccupied;
    public bool AllowsPeeking = true;
    public CoverHeight Height = CoverHeight.Full;

    public enum CoverHeight
    {
        Low,    // Agachado
        Full    // De pie
    }
}

/// <summary>
/// Escuadrón de enemigos que se coordinan
/// </summary>
public class EnemySquad : MonoBehaviour
{
    public List<EnemyAI> Members = new List<EnemyAI>();
    public FactionType Faction;
    private int maxFlankers = 2;
    private int currentFlankers = 0;

    public void AlertSquad(Vector3 position, EnemyAI alerter)
    {
        foreach (var member in Members)
        {
            if (member != alerter && member.IsAlive)
            {
                member.AlertToPosition(position);
            }
        }
    }

    public void MemberKilled(EnemyAI member)
    {
        Members.Remove(member);

        // Si quedan pocos, los demás se retiran
        if (Members.Count <= 1)
        {
            foreach (var m in Members)
            {
                if (m.IsAlive)
                {
                    // Forzar retirada
                    m.AlertToPosition(m.transform.position + m.transform.forward * -20f);
                }
            }
        }
    }

    public bool CanFlank()
    {
        return currentFlankers < maxFlankers;
    }

    public void RegisterFlanker()
    {
        currentFlankers++;
    }
}
