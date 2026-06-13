using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// TIERRA EN LLAMAS - TouchInputManager
/// Sistema de entrada táctil completo para Android.
/// Incluye: joystick virtual, botones de acción, gestos de cámara,
/// doble tap para sprint, swipe para cobertura.
/// </summary>
public class TouchInputManager : MonoBehaviour
{
    public static TouchInputManager Instance { get; private set; }

    [Header("Configuración de Joystick")]
    [SerializeField] private float joystickRadius = 120f;
    [SerializeField] private float joystickDeadzone = 0.15f;
    [SerializeField] private RectTransform joystickBase;
    [SerializeField] private RectTransform joystickHandle;

    [Header("Configuración de Cámara")]
    [SerializeField] private float cameraSensitivity = 0.15f;
    [SerializeField] private float cameraSmoothing = 5f;
    [SerializeField] private bool invertY = false;

    [Header("Configuración de Gestos")]
    [SerializeField] private float doubleTapTime = 0.3f;
    [SerializeField] private float swipeThreshold = 50f;
    [SerializeField] private float longPressTime = 0.5f;

    [Header("Zonas de Pantalla")]
    [SerializeField] private float leftZoneWidth = 0.4f;  // 40% izquierdo para joystick
    [SerializeField] private float rightZoneWidth = 0.6f;  // 60% derecho para cámara

    // Outputs
    public Vector2 MovementInput { get; private set; }
    public Vector2 CameraInput { get; private set; }
    public bool SprintInput { get; private set; }
    public bool CrouchInput { get; private set; }
    public bool FireInput { get; private set; }
    public bool AimInput { get; private set; }
    public bool ReloadInput { get; private set; }
    public bool CoverInput { get; private set; }
    public bool InteractInput { get; private set; }
    public bool JumpInput { get; private set; }

    // Estado interno del joystick
    private int joystickTouchId = -1;
    private Vector2 joystickOrigin;
    private bool joystickActive;

    // Estado interno de cámara
    private int cameraTouchId = -1;
    private Vector2 lastCameraPosition;
    private Vector2 smoothedCameraInput;

    // Detección de gestos
    private float lastTapTime;
    private Vector2 touchStartPosition;
    private float touchStartTime;
    private bool isLongPress;

    // Botones virtuales activos
    private Dictionary<string, bool> buttonStates = new Dictionary<string, bool>();

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        ResetFrameInputs();
        ProcessTouches();
        ApplyInputToPlayer();
    }

    private void ResetFrameInputs()
    {
        CameraInput = Vector2.zero;
        JumpInput = false;
        InteractInput = false;
    }

    private void ProcessTouches()
    {
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);

            // Ignorar toques sobre UI
            if (IsOverUI(touch.fingerId)) continue;

            float screenX = touch.position.x / Screen.width;

            switch (touch.phase)
            {
                case TouchPhase.Began:
                    HandleTouchBegan(touch, screenX);
                    break;

                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    HandleTouchMoved(touch, screenX);
                    break;

                case TouchPhase.Ended:
                case TouchPhase.Canceled:
                    HandleTouchEnded(touch, screenX);
                    break;
            }
        }

        // Suavizar input de cámara
        smoothedCameraInput = Vector2.Lerp(smoothedCameraInput, CameraInput,
            cameraSmoothing * Time.deltaTime);
    }

    private void HandleTouchBegan(Touch touch, float screenX)
    {
        if (screenX < leftZoneWidth && joystickTouchId == -1)
        {
            // Iniciar joystick
            joystickTouchId = touch.fingerId;
            joystickOrigin = touch.position;
            joystickActive = true;

            if (joystickBase != null)
            {
                joystickBase.position = touch.position;
                joystickBase.gameObject.SetActive(true);
            }

            // Detectar doble tap para sprint
            if (Time.time - lastTapTime < doubleTapTime)
            {
                SprintInput = true;
            }
            lastTapTime = Time.time;
        }
        else if (screenX >= leftZoneWidth)
        {
            // Iniciar control de cámara
            if (cameraTouchId == -1)
            {
                cameraTouchId = touch.fingerId;
                lastCameraPosition = touch.position;
            }
        }

        // Registrar para detección de gestos
        touchStartPosition = touch.position;
        touchStartTime = Time.time;
        isLongPress = false;
    }

    private void HandleTouchMoved(Touch touch, float screenX)
    {
        if (touch.fingerId == joystickTouchId)
        {
            // Actualizar joystick
            Vector2 delta = touch.position - joystickOrigin;
            float magnitude = delta.magnitude;

            if (magnitude > joystickRadius)
            {
                delta = delta.normalized * joystickRadius;
            }

            Vector2 normalizedInput = delta / joystickRadius;

            if (normalizedInput.magnitude < joystickDeadzone)
                normalizedInput = Vector2.zero;

            MovementInput = normalizedInput;

            // Actualizar visual del joystick
            if (joystickHandle != null)
            {
                joystickHandle.position = joystickOrigin + delta;
            }
        }
        else if (touch.fingerId == cameraTouchId)
        {
            // Actualizar cámara
            Vector2 delta = touch.position - lastCameraPosition;
            CameraInput = new Vector2(
                delta.x * cameraSensitivity,
                (invertY ? -1 : 1) * delta.y * cameraSensitivity
            );
            lastCameraPosition = touch.position;
        }

        // Detectar long press
        if (Time.time - touchStartTime > longPressTime && !isLongPress)
        {
            isLongPress = true;
            // Long press en zona derecha = aim
            if (screenX >= leftZoneWidth)
            {
                AimInput = true;
            }
        }
    }

    private void HandleTouchEnded(Touch touch, float screenX)
    {
        if (touch.fingerId == joystickTouchId)
        {
            // Soltar joystick
            joystickTouchId = -1;
            joystickActive = false;
            MovementInput = Vector2.zero;
            SprintInput = false;

            if (joystickBase != null)
                joystickBase.gameObject.SetActive(false);
            if (joystickHandle != null)
                joystickHandle.localPosition = Vector3.zero;
        }
        else if (touch.fingerId == cameraTouchId)
        {
            cameraTouchId = -1;
            CameraInput = Vector2.zero;

            // Detectar swipe
            Vector2 swipeDelta = touch.position - touchStartPosition;
            float swipeTime = Time.time - touchStartTime;

            if (swipeDelta.magnitude > swipeThreshold && swipeTime < 0.5f)
            {
                HandleSwipe(swipeDelta);
            }
        }

        // Soltar aim si era long press
        if (isLongPress && screenX >= leftZoneWidth)
        {
            AimInput = false;
        }
    }

    private void HandleSwipe(Vector2 delta)
    {
        float absX = Mathf.Abs(delta.x);
        float absY = Mathf.Abs(delta.y);

        if (absY > absX)
        {
            if (delta.y > 0)
            {
                // Swipe arriba = saltar
                JumpInput = true;
            }
            else
            {
                // Swipe abajo = agacharse
                CrouchInput = !CrouchInput;
            }
        }
        else
        {
            // Swipe lateral = buscar cobertura
            CoverInput = true;
        }
    }

    private void ApplyInputToPlayer()
    {
        var player = PlayerController.Instance;
        if (player == null) return;

        player.SetMovementInput(MovementInput);
        player.SetSprintInput(SprintInput);

        var camera = ThirdPersonCamera.Instance;
        if (camera != null)
        {
            camera.SetLookInput(smoothedCameraInput);
        }
    }

    #region Botones de UI (llamados desde botones del HUD)

    public void OnFireButtonDown()
    {
        FireInput = true;
        var weapon = WeaponSystem.Instance;
        if (weapon != null) weapon.StartFiring();
    }

    public void OnFireButtonUp()
    {
        FireInput = false;
        var weapon = WeaponSystem.Instance;
        if (weapon != null) weapon.StopFiring();
    }

    public void OnReloadButton()
    {
        ReloadInput = true;
        var weapon = WeaponSystem.Instance;
        if (weapon != null) weapon.Reload();
    }

    public void OnCoverButton()
    {
        var player = PlayerController.Instance;
        if (player != null) player.ToggleCover();
    }

    public void OnCrouchButton()
    {
        var player = PlayerController.Instance;
        if (player != null) player.ToggleCrouch();
    }

    public void OnInteractButton()
    {
        InteractInput = true;
    }

    public void OnAimButtonDown()
    {
        AimInput = true;
    }

    public void OnAimButtonUp()
    {
        AimInput = false;
    }

    public void OnGrenadeButton()
    {
        var weapon = WeaponSystem.Instance;
        if (weapon != null) weapon.ThrowGrenade();
    }

    public void OnMeleeButton()
    {
        var weapon = WeaponSystem.Instance;
        if (weapon != null) weapon.MeleeAttack();
    }

    public void OnStealthKillButton()
    {
        var player = PlayerController.Instance;
        if (player != null) player.AttemptStealthKill();
    }

    #endregion

    #region Utilidades

    private bool IsOverUI(int fingerId)
    {
        return EventSystem.current != null &&
               EventSystem.current.IsPointerOverGameObject(fingerId);
    }

    public void SetSensitivity(float sensitivity)
    {
        cameraSensitivity = sensitivity;
    }

    public void SetInvertY(bool invert)
    {
        invertY = invert;
    }

    #endregion
}
