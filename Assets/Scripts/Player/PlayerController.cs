using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// TIERRA EN LLAMAS - PlayerController
/// Control completo del jugador en tercera persona con movimiento realista,
/// sistema de cobertura, sigilo y daño localizado.
/// Optimizado para controles táctiles en Android.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    public static PlayerController Instance { get; private set; }

    [Header("Movimiento")]
    [SerializeField] private float walkSpeed = 2.5f;
    [SerializeField] private float runSpeed = 5.5f;
    [SerializeField] private float crouchSpeed = 1.5f;
    [SerializeField] private float sprintSpeed = 7.5f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float gravity = -19.62f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float groundCheckDistance = 0.3f;

    [Header("Estadísticas del Personaje")]
    public PlayerStats Stats;
    public PlayerHealth Health;

    [Header("Estados")]
    public PlayerState CurrentState = PlayerState.Idle;
    public MovementMode MoveMode = MovementMode.Walk;
    public bool IsGrounded { get; private set; }
    public bool IsInCover { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsAiming { get; private set; }
    public bool IsSprinting { get; private set; }
    public bool IsInStealth { get; private set; }

    [Header("Sistema de Cobertura")]
    [SerializeField] private float coverDetectionRange = 1.5f;
    [SerializeField] private LayerMask coverLayer;
    [SerializeField] private float coverTransitionSpeed = 8f;
    private CoverPoint currentCover;
    private Vector3 coverNormal;

    [Header("Sistema de Sigilo")]
    [SerializeField] private float noiseLevel = 0f;
    [SerializeField] private float visibilityLevel = 1f;
    [SerializeField] private float stealthKillRange = 2f;

    [Header("Física")]
    [SerializeField] private LayerMask groundLayer;
    private CharacterController controller;
    private Animator animator;
    private Vector3 velocity;
    private Vector3 moveDirection;
    private float currentSpeed;

    [Header("Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float currentStamina;
    [SerializeField] private float staminaDrainRate = 15f;
    [SerializeField] private float staminaRegenRate = 8f;

    // Eventos
    public event Action<PlayerState> OnStateChanged;
    public event Action<float> OnNoiseGenerated;
    public event Action OnCoverEntered;
    public event Action OnCoverExited;

    // Input (desde TouchInputManager)
    private Vector2 inputMovement;
    private bool inputSprint;
    private bool inputCrouch;
    private bool inputCover;
    private bool inputJump;

    private void Awake()
    {
        Instance = this;
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        currentStamina = maxStamina;

        Stats = new PlayerStats();
        Health = new PlayerHealth();
    }

    private void Update()
    {
        if (GameManager.Instance.CurrentState != GameState.Playing &&
            GameManager.Instance.CurrentState != GameState.InCombat)
            return;

        UpdateGroundCheck();
        UpdateMovement();
        UpdateStamina();
        UpdateStealth();
        UpdateAnimator();
        ApplyGravity();
    }

    #region Movimiento

    public void SetMovementInput(Vector2 input)
    {
        inputMovement = input;
    }

    public void SetSprintInput(bool sprint)
    {
        inputSprint = sprint;
    }

    private void UpdateMovement()
    {
        if (IsInCover)
        {
            UpdateCoverMovement();
            return;
        }

        // Calcular dirección de movimiento relativa a la cámara
        Vector3 forward = Camera.main.transform.forward;
        Vector3 right = Camera.main.transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        moveDirection = (forward * inputMovement.y + right * inputMovement.x).normalized;

        // Determinar velocidad según estado
        if (IsCrouching)
            currentSpeed = crouchSpeed;
        else if (IsSprinting && currentStamina > 0)
            currentSpeed = sprintSpeed;
        else if (inputSprint && currentStamina > 0)
            currentSpeed = runSpeed;
        else
            currentSpeed = walkSpeed;

        // Aplicar modificadores de daño localizado
        currentSpeed *= Health.GetSpeedModifier();

        // Mover personaje
        if (moveDirection.magnitude > 0.1f)
        {
            // Rotación suave hacia dirección de movimiento
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

            controller.Move(moveDirection * currentSpeed * Time.deltaTime);

            // Generar ruido según velocidad
            GenerateNoise(currentSpeed / sprintSpeed);

            ChangeState(IsSprinting ? PlayerState.Sprinting :
                       IsCrouching ? PlayerState.Crouching :
                       PlayerState.Moving);
        }
        else
        {
            ChangeState(IsCrouching ? PlayerState.Crouching : PlayerState.Idle);
        }
    }

    private void ApplyGravity()
    {
        if (IsGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    private void UpdateGroundCheck()
    {
        IsGrounded = Physics.CheckSphere(
            transform.position + Vector3.down * (controller.height / 2f),
            groundCheckDistance,
            groundLayer
        );
    }

    public void Jump()
    {
        if (IsGrounded && !IsInCover && !IsCrouching)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            GenerateNoise(0.8f);
        }
    }

    #endregion

    #region Sistema de Cobertura

    public void ToggleCover()
    {
        if (IsInCover)
        {
            ExitCover();
        }
        else
        {
            TryEnterCover();
        }
    }

    private void TryEnterCover()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up, transform.forward, out hit, coverDetectionRange, coverLayer))
        {
            IsInCover = true;
            coverNormal = hit.normal;
            currentCover = hit.collider.GetComponent<CoverPoint>();

            // Posicionar contra la cobertura
            Vector3 coverPosition = hit.point + hit.normal * 0.3f;
            coverPosition.y = transform.position.y;
            StartCoroutine(MoveToPosition(coverPosition, coverTransitionSpeed));

            // Rotar para mirar en dirección opuesta a la cobertura
            transform.rotation = Quaternion.LookRotation(-coverNormal);

            ChangeState(PlayerState.InCover);
            OnCoverEntered?.Invoke();
            GameManager.Instance.ChangeState(GameState.InCover);
        }
    }

    private void ExitCover()
    {
        IsInCover = false;
        currentCover = null;
        ChangeState(PlayerState.Idle);
        OnCoverExited?.Invoke();

        if (GameManager.Instance.CurrentState == GameState.InCover)
            GameManager.Instance.ChangeState(GameState.Playing);
    }

    private void UpdateCoverMovement()
    {
        // Movimiento lateral a lo largo de la cobertura
        Vector3 coverRight = Vector3.Cross(coverNormal, Vector3.up);
        float lateralInput = inputMovement.x;

        if (Mathf.Abs(lateralInput) > 0.1f)
        {
            Vector3 lateralMove = coverRight * lateralInput * crouchSpeed * Time.deltaTime;
            controller.Move(lateralMove);
        }
    }

    public void PeekFromCover(bool peek)
    {
        if (!IsInCover) return;
        IsAiming = peek;
        animator.SetBool("Peeking", peek);
    }

    private IEnumerator MoveToPosition(Vector3 target, float speed)
    {
        while (Vector3.Distance(transform.position, target) > 0.05f)
        {
            transform.position = Vector3.Lerp(transform.position, target, speed * Time.deltaTime);
            yield return null;
        }
    }

    #endregion

    #region Sistema de Sigilo

    private void UpdateStealth()
    {
        // Calcular nivel de visibilidad
        float lightExposure = CalculateLightExposure();
        float movementFactor = currentSpeed / sprintSpeed;
        float crouchFactor = IsCrouching ? 0.5f : 1f;

        visibilityLevel = Mathf.Clamp01(lightExposure * movementFactor * crouchFactor);
        IsInStealth = visibilityLevel < 0.3f && noiseLevel < 0.2f;

        // Reducir ruido gradualmente
        noiseLevel = Mathf.Lerp(noiseLevel, 0f, Time.deltaTime * 3f);
    }

    private float CalculateLightExposure()
    {
        // Raycast hacia arriba para detectar sombras/cobertura
        if (Physics.Raycast(transform.position, Vector3.up, 5f))
            return 0.3f; // Bajo techo/vegetación

        // Verificar ciclo día/noche
        float timeOfDay = GameManager.Instance.Weather != null ?
            GameManager.Instance.Weather.GetTimeOfDay() : 0.5f;

        return timeOfDay > 0.7f || timeOfDay < 0.3f ? 0.4f : 0.9f; // Noche vs día
    }

    public void GenerateNoise(float intensity)
    {
        // Modificar por terreno
        float terrainModifier = GetTerrainNoiseModifier();
        noiseLevel = Mathf.Clamp01(intensity * terrainModifier);

        if (IsCrouching) noiseLevel *= 0.3f;
        if (IsInStealth) noiseLevel *= 0.1f;

        OnNoiseGenerated?.Invoke(noiseLevel);
    }

    private float GetTerrainNoiseModifier()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, Vector3.down, out hit, 2f, groundLayer))
        {
            // Diferentes superficies generan diferente ruido
            string tag = hit.collider.tag;
            switch (tag)
            {
                case "Grass": return 0.3f;      // Hierba/selva - silencioso
                case "Dirt": return 0.4f;        // Tierra
                case "Wood": return 0.7f;        // Madera - cruje
                case "Metal": return 0.9f;       // Metal - muy ruidoso
                case "Water": return 0.6f;       // Agua - chapoteo
                case "Concrete": return 0.5f;    // Concreto urbano
                case "Glass": return 1.0f;       // Vidrio - alerta máxima
                default: return 0.5f;
            }
        }
        return 0.5f;
    }

    public void AttemptStealthKill()
    {
        if (!IsInStealth) return;

        Collider[] enemies = Physics.OverlapSphere(transform.position, stealthKillRange,
            LayerMask.GetMask("Enemy"));

        foreach (var enemy in enemies)
        {
            EnemyAI ai = enemy.GetComponent<EnemyAI>();
            if (ai != null && !ai.IsAlerted)
            {
                // Verificar que estamos detrás del enemigo
                Vector3 toEnemy = (enemy.transform.position - transform.position).normalized;
                float dot = Vector3.Dot(enemy.transform.forward, toEnemy);

                if (dot > 0.5f) // Estamos detrás
                {
                    ai.TakeDamage(9999f, DamageType.Stealth, BodyPart.Head);
                    animator.SetTrigger("StealthKill");
                    GenerateNoise(0.1f); // Mínimo ruido
                    break;
                }
            }
        }
    }

    #endregion

    #region Crouch / Sprint

    public void ToggleCrouch()
    {
        IsCrouching = !IsCrouching;
        controller.height = IsCrouching ? 1.0f : 1.8f;
        controller.center = IsCrouching ? new Vector3(0, 0.5f, 0) : new Vector3(0, 0.9f, 0);
    }

    public void SetSprint(bool sprint)
    {
        IsSprinting = sprint && currentStamina > 10f && !IsCrouching && !IsInCover;
    }

    private void UpdateStamina()
    {
        if (IsSprinting && moveDirection.magnitude > 0.1f)
        {
            currentStamina -= staminaDrainRate * Time.deltaTime;
            if (currentStamina <= 0)
            {
                currentStamina = 0;
                IsSprinting = false;
            }
        }
        else
        {
            currentStamina += staminaRegenRate * Time.deltaTime;
            currentStamina = Mathf.Min(currentStamina, maxStamina);
        }
    }

    public float GetStaminaPercent() => currentStamina / maxStamina;

    #endregion

    #region Animator

    private void UpdateAnimator()
    {
        animator.SetFloat("Speed", currentSpeed / sprintSpeed);
        animator.SetBool("IsGrounded", IsGrounded);
        animator.SetBool("IsCrouching", IsCrouching);
        animator.SetBool("IsInCover", IsInCover);
        animator.SetBool("IsAiming", IsAiming);
        animator.SetBool("IsSprinting", IsSprinting);
        animator.SetFloat("VelocityY", velocity.y);
    }

    #endregion

    #region Estado

    private void ChangeState(PlayerState newState)
    {
        if (CurrentState == newState) return;
        CurrentState = newState;
        OnStateChanged?.Invoke(newState);
    }

    #endregion

    #region Getters públicos

    public float GetNoiseLevel() => noiseLevel;
    public float GetVisibilityLevel() => visibilityLevel;
    public Vector3 GetPosition() => transform.position;
    public Vector3 GetForward() => transform.forward;

    #endregion
}

// === ENUMERACIONES DEL JUGADOR ===

public enum PlayerState
{
    Idle,
    Moving,
    Sprinting,
    Crouching,
    InCover,
    Aiming,
    Shooting,
    Reloading,
    MeleeAttack,
    TakingDamage,
    Dead,
    Swimming,
    Climbing,
    InVehicle,
    InDialogue
}

public enum MovementMode
{
    Walk,
    Run,
    Crouch,
    Sprint,
    Swim
}

/// <summary>
/// Estadísticas del personaje que se modifican por trasfondo y progresión
/// </summary>
[System.Serializable]
public class PlayerStats
{
    public int Level = 1;
    public int Experience = 0;
    public int MaxLevel = 30;

    // Atributos base (0-100)
    public float Combate = 20f;
    public float Sigilo = 20f;
    public float Persuasion = 20f;
    public float Resistencia = 20f;
    public float Supervivencia = 20f;

    // Puntos de habilidad disponibles
    public int SkillPoints = 0;

    public void ApplyBackground(CharacterBackground bg)
    {
        switch (bg)
        {
            case CharacterBackground.Campesino:
                Supervivencia += 15f;
                Resistencia += 10f;
                break;
            case CharacterBackground.Soldado:
                Combate += 15f;
                Resistencia += 10f;
                break;
            case CharacterBackground.Periodista:
                Persuasion += 15f;
                Sigilo += 10f;
                break;
            case CharacterBackground.Estudiante:
                Persuasion += 10f;
                Supervivencia += 15f;
                break;
        }
    }

    public void AddExperience(int xp)
    {
        Experience += xp;
        int xpNeeded = GetXPForNextLevel();

        while (Experience >= xpNeeded && Level < MaxLevel)
        {
            Experience -= xpNeeded;
            Level++;
            SkillPoints += 2;
            xpNeeded = GetXPForNextLevel();
        }
    }

    public int GetXPForNextLevel()
    {
        return 100 + (Level * 50); // Escalado progresivo
    }
}

/// <summary>
/// Sistema de salud con daño localizado
/// </summary>
[System.Serializable]
public class PlayerHealth
{
    public float MaxHealth = 100f;
    public float CurrentHealth = 100f;
    public float HealthRegenRate = 2f; // Solo en zonas seguras
    public bool CanRegenerate = false;

    // Daño localizado
    public float HeadDamageMultiplier = 3.0f;
    public float TorsoDamageMultiplier = 1.0f;
    public float ArmDamageMultiplier = 0.7f;
    public float LegDamageMultiplier = 0.8f;

    // Estado de heridas
    public bool LeftArmWounded = false;
    public bool RightArmWounded = false;
    public bool LeftLegWounded = false;
    public bool RightLegWounded = false;
    public bool IsBleeding = false;
    public float BleedRate = 0f;

    public event Action<float> OnHealthChanged;
    public event Action<BodyPart> OnWounded;
    public event Action OnDeath;

    public void TakeDamage(float damage, BodyPart part)
    {
        float finalDamage = damage;

        switch (part)
        {
            case BodyPart.Head:
                finalDamage *= HeadDamageMultiplier;
                break;
            case BodyPart.Torso:
                finalDamage *= TorsoDamageMultiplier;
                break;
            case BodyPart.LeftArm:
            case BodyPart.RightArm:
                finalDamage *= ArmDamageMultiplier;
                if (finalDamage > 20f) ApplyWound(part);
                break;
            case BodyPart.LeftLeg:
            case BodyPart.RightLeg:
                finalDamage *= LegDamageMultiplier;
                if (finalDamage > 20f) ApplyWound(part);
                break;
        }

        CurrentHealth -= finalDamage;
        CurrentHealth = Mathf.Max(0, CurrentHealth);
        OnHealthChanged?.Invoke(CurrentHealth / MaxHealth);

        // Sangrado por heridas graves
        if (finalDamage > 30f)
        {
            IsBleeding = true;
            BleedRate += 1f;
        }

        if (CurrentHealth <= 0)
        {
            OnDeath?.Invoke();
        }
    }

    private void ApplyWound(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.LeftArm: LeftArmWounded = true; break;
            case BodyPart.RightArm: RightArmWounded = true; break;
            case BodyPart.LeftLeg: LeftLegWounded = true; break;
            case BodyPart.RightLeg: RightLegWounded = true; break;
        }
        OnWounded?.Invoke(part);
    }

    public float GetSpeedModifier()
    {
        float modifier = 1f;
        if (LeftLegWounded) modifier *= 0.7f;
        if (RightLegWounded) modifier *= 0.7f;
        return modifier;
    }

    public float GetAccuracyModifier()
    {
        float modifier = 1f;
        if (LeftArmWounded) modifier *= 0.75f;
        if (RightArmWounded) modifier *= 0.75f;
        return modifier;
    }

    public void Heal(float amount)
    {
        CurrentHealth = Mathf.Min(CurrentHealth + amount, MaxHealth);
        OnHealthChanged?.Invoke(CurrentHealth / MaxHealth);
    }

    public void HealWound(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.LeftArm: LeftArmWounded = false; break;
            case BodyPart.RightArm: RightArmWounded = false; break;
            case BodyPart.LeftLeg: LeftLegWounded = false; break;
            case BodyPart.RightLeg: RightLegWounded = false; break;
        }
    }

    public float GetHealthPercent() => CurrentHealth / MaxHealth;
}

public enum BodyPart
{
    Head,
    Torso,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg
}
