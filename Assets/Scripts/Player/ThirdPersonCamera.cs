using UnityEngine;

/// <summary>
/// TIERRA EN LLAMAS - Sistema de Cámara en Tercera Persona
/// Cámara cinematográfica con transiciones suaves entre modos:
/// exploración, combate, cobertura, diálogo y vehículos.
/// Incluye detección de colisiones y efectos de cámara.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    public static ThirdPersonCamera Instance { get; private set; }

    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 targetOffset = new Vector3(0, 1.6f, 0);

    [Header("Configuración de Exploración")]
    [SerializeField] private float explorationDistance = 4f;
    [SerializeField] private float explorationHeight = 2f;
    [SerializeField] private float explorationSensitivity = 2f;

    [Header("Configuración de Combate")]
    [SerializeField] private float combatDistance = 2.5f;
    [SerializeField] private float combatHeight = 1.5f;
    [SerializeField] private float combatShoulderOffset = 0.8f;
    [SerializeField] private float aimDistance = 1.5f;

    [Header("Configuración de Cobertura")]
    [SerializeField] private float coverDistance = 3f;
    [SerializeField] private float coverHeight = 2.5f;

    [Header("Suavizado")]
    [SerializeField] private float positionSmoothTime = 0.15f;
    [SerializeField] private float rotationSmoothTime = 0.1f;
    [SerializeField] private float transitionSpeed = 5f;

    [Header("Límites de Rotación")]
    [SerializeField] private float minVerticalAngle = -30f;
    [SerializeField] private float maxVerticalAngle = 60f;

    [Header("Colisión")]
    [SerializeField] private LayerMask collisionLayers;
    [SerializeField] private float collisionRadius = 0.3f;
    [SerializeField] private float minDistance = 0.5f;

    [Header("Efectos de Cámara")]
    [SerializeField] private float shakeIntensity = 0f;
    [SerializeField] private float shakeDuration = 0f;
    [SerializeField] private float fovDefault = 60f;
    [SerializeField] private float fovSprint = 70f;
    [SerializeField] private float fovAim = 45f;

    // Estado interno
    private CameraMode currentMode = CameraMode.Exploration;
    private float currentDistance;
    private float currentHeight;
    private float yaw;
    private float pitch;
    private Vector3 currentVelocity;
    private float currentFOV;
    private Camera cam;

    // Input táctil
    private Vector2 lookInput;
    private float pinchZoom;

    private void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        currentFOV = fovDefault;
    }

    private void Start()
    {
        if (target == null && PlayerController.Instance != null)
            target = PlayerController.Instance.transform;

        currentDistance = explorationDistance;
        currentHeight = explorationHeight;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        UpdateCameraMode();
        UpdateRotation();
        UpdatePosition();
        UpdateFOV();
        ApplyShake();
    }

    public void SetLookInput(Vector2 input)
    {
        lookInput = input;
    }

    private void UpdateCameraMode()
    {
        var player = PlayerController.Instance;
        if (player == null) return;

        CameraMode targetMode;

        if (player.IsAiming)
            targetMode = CameraMode.Aim;
        else if (player.IsInCover)
            targetMode = CameraMode.Cover;
        else if (GameManager.Instance.CurrentState == GameState.InCombat)
            targetMode = CameraMode.Combat;
        else if (GameManager.Instance.CurrentState == GameState.InDialogue)
            targetMode = CameraMode.Dialogue;
        else
            targetMode = CameraMode.Exploration;

        if (currentMode != targetMode)
        {
            currentMode = targetMode;
        }

        // Interpolar hacia configuración del modo actual
        float targetDistance, targetHeight;
        GetModeSettings(out targetDistance, out targetHeight);

        currentDistance = Mathf.Lerp(currentDistance, targetDistance, transitionSpeed * Time.deltaTime);
        currentHeight = Mathf.Lerp(currentHeight, targetHeight, transitionSpeed * Time.deltaTime);
    }

    private void GetModeSettings(out float distance, out float height)
    {
        switch (currentMode)
        {
            case CameraMode.Combat:
                distance = combatDistance;
                height = combatHeight;
                break;
            case CameraMode.Aim:
                distance = aimDistance;
                height = combatHeight;
                break;
            case CameraMode.Cover:
                distance = coverDistance;
                height = coverHeight;
                break;
            case CameraMode.Dialogue:
                distance = explorationDistance * 0.7f;
                height = explorationHeight * 0.8f;
                break;
            default:
                distance = explorationDistance;
                height = explorationHeight;
                break;
        }
    }

    private void UpdateRotation()
    {
        float sensitivity = currentMode == CameraMode.Aim ?
            explorationSensitivity * 0.5f : explorationSensitivity;

        yaw += lookInput.x * sensitivity;
        pitch -= lookInput.y * sensitivity;
        pitch = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);
    }

    private void UpdatePosition()
    {
        Vector3 targetPos = target.position + targetOffset;

        // Calcular posición deseada
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
        Vector3 offset = rotation * new Vector3(0, 0, -currentDistance);
        offset.y += currentHeight;

        // Offset de hombro en combate/aim
        if (currentMode == CameraMode.Combat || currentMode == CameraMode.Aim)
        {
            Vector3 shoulderOffset = rotation * new Vector3(combatShoulderOffset, 0, 0);
            offset += shoulderOffset;
        }

        Vector3 desiredPosition = targetPos + offset;

        // Detección de colisiones
        desiredPosition = HandleCollision(targetPos, desiredPosition);

        // Suavizar movimiento
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition,
            ref currentVelocity, positionSmoothTime);

        // Mirar al target
        Vector3 lookTarget = targetPos;
        if (currentMode == CameraMode.Aim)
        {
            // En aim, mirar más adelante
            lookTarget += target.forward * 10f;
        }

        Quaternion lookRotation = Quaternion.LookRotation(lookTarget - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation,
            (1f / rotationSmoothTime) * Time.deltaTime);
    }

    private Vector3 HandleCollision(Vector3 from, Vector3 to)
    {
        Vector3 direction = to - from;
        float distance = direction.magnitude;

        RaycastHit hit;
        if (Physics.SphereCast(from, collisionRadius, direction.normalized, out hit,
            distance, collisionLayers))
        {
            float newDistance = Mathf.Max(hit.distance - collisionRadius, minDistance);
            return from + direction.normalized * newDistance;
        }

        return to;
    }

    private void UpdateFOV()
    {
        float targetFOV;
        var player = PlayerController.Instance;

        if (player != null && player.IsAiming)
            targetFOV = fovAim;
        else if (player != null && player.IsSprinting)
            targetFOV = fovSprint;
        else
            targetFOV = fovDefault;

        currentFOV = Mathf.Lerp(currentFOV, targetFOV, 5f * Time.deltaTime);
        cam.fieldOfView = currentFOV;
    }

    #region Efectos de Cámara

    public void Shake(float intensity, float duration)
    {
        shakeIntensity = intensity;
        shakeDuration = duration;
    }

    private void ApplyShake()
    {
        if (shakeDuration > 0)
        {
            Vector3 shakeOffset = Random.insideUnitSphere * shakeIntensity;
            transform.position += shakeOffset;
            shakeDuration -= Time.deltaTime;
            shakeIntensity = Mathf.Lerp(shakeIntensity, 0, Time.deltaTime * 5f);
        }
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    /// <summary>
    /// Efecto de impacto cuando el jugador recibe daño
    /// </summary>
    public void ImpactEffect(Vector3 direction, float force)
    {
        Shake(force * 0.1f, 0.3f);
    }

    /// <summary>
    /// Efecto de explosión cercana
    /// </summary>
    public void ExplosionEffect(float distance)
    {
        float intensity = Mathf.Clamp01(1f - (distance / 20f));
        Shake(intensity * 0.5f, 1f);
    }

    #endregion
}

public enum CameraMode
{
    Exploration,
    Combat,
    Aim,
    Cover,
    Dialogue,
    Vehicle,
    Cinematic
}
