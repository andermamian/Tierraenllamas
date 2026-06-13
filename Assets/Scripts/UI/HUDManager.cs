using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using TMPro;

/// <summary>
/// TIERRA EN LLAMAS - HUDManager
/// Interfaz de usuario completa para Android:
/// - Barra de vida con indicador de sangrado
/// - Munición y arma actual
/// - Mini-mapa con radar
/// - Indicador de objetivo/misión
/// - Controles táctiles (joystick, botones)
/// - Indicadores de daño direccional
/// - Sistema de karma visual
/// - Reloj del juego (hora del día)
/// Sigue Material Design 3 para Android
/// </summary>
public class HUDManager : MonoBehaviour
{
    public static HUDManager Instance { get; private set; }

    [Header("Barra de Vida")]
    [SerializeField] private Slider healthBar;
    [SerializeField] private Image healthFill;
    [SerializeField] private Image healthBackground;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private Image bleedingIndicator;
    [SerializeField] private Image damageVignette;

    [Header("Stamina")]
    [SerializeField] private Slider staminaBar;
    [SerializeField] private Image staminaFill;

    [Header("Munición")]
    [SerializeField] private TextMeshProUGUI ammoCurrentText;
    [SerializeField] private TextMeshProUGUI ammoReserveText;
    [SerializeField] private TextMeshProUGUI weaponNameText;
    [SerializeField] private Image weaponIcon;
    [SerializeField] private Image reloadIndicator;

    [Header("Mini-Mapa / Radar")]
    [SerializeField] private RawImage minimapImage;
    [SerializeField] private RectTransform minimapPlayerIcon;
    [SerializeField] private RectTransform minimapContainer;
    [SerializeField] private GameObject enemyBlipPrefab;
    [SerializeField] private GameObject objectiveBlipPrefab;

    [Header("Objetivo de Misión")]
    [SerializeField] private TextMeshProUGUI objectiveText;
    [SerializeField] private TextMeshProUGUI distanceText;
    [SerializeField] private Image objectiveArrow;
    [SerializeField] private CanvasGroup objectiveGroup;

    [Header("Indicadores de Daño")]
    [SerializeField] private Image damageIndicatorTop;
    [SerializeField] private Image damageIndicatorBottom;
    [SerializeField] private Image damageIndicatorLeft;
    [SerializeField] private Image damageIndicatorRight;

    [Header("Reloj del Juego")]
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private Image timeIcon; // Sol o luna

    [Header("Controles Táctiles")]
    [SerializeField] private RectTransform joystickArea;
    [SerializeField] private Button fireButton;
    [SerializeField] private Button aimButton;
    [SerializeField] private Button reloadButton;
    [SerializeField] private Button coverButton;
    [SerializeField] private Button crouchButton;
    [SerializeField] private Button interactButton;
    [SerializeField] private Button grenadeButton;
    [SerializeField] private Button switchWeaponButton;
    [SerializeField] private Button pauseButton;

    [Header("Diálogo")]
    [SerializeField] private CanvasGroup dialoguePanel;
    [SerializeField] private TextMeshProUGUI speakerNameText;
    [SerializeField] private TextMeshProUGUI dialogueText;
    [SerializeField] private RectTransform choicesContainer;
    [SerializeField] private GameObject choiceButtonPrefab;
    [SerializeField] private Slider timerBar;

    [Header("Notificaciones")]
    [SerializeField] private CanvasGroup notificationPanel;
    [SerializeField] private TextMeshProUGUI notificationText;
    [SerializeField] private Image notificationIcon;

    [Header("Karma Feedback")]
    [SerializeField] private CanvasGroup karmaFeedback;
    [SerializeField] private TextMeshProUGUI karmaChangeText;
    [SerializeField] private Image karmaIcon;

    [Header("Crosshair")]
    [SerializeField] private RectTransform crosshair;
    [SerializeField] private Image[] crosshairParts;
    [SerializeField] private float crosshairSpreadMultiplier = 100f;

    [Header("Colores del HUD")]
    [SerializeField] private Color healthColorFull = new Color(0.1f, 0.8f, 0.1f);
    [SerializeField] private Color healthColorLow = new Color(0.9f, 0.1f, 0.1f);
    [SerializeField] private Color staminaColor = new Color(0.2f, 0.6f, 1f);
    [SerializeField] private Color ammoColor = Color.white;
    [SerializeField] private Color ammoLowColor = new Color(1f, 0.5f, 0f);

    private Coroutine notificationCoroutine;
    private Coroutine karmaCoroutine;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        SubscribeToEvents();
        InitializeHUD();
    }

    private void Update()
    {
        UpdateHealthDisplay();
        UpdateStaminaDisplay();
        UpdateAmmoDisplay();
        UpdateMinimap();
        UpdateObjectiveIndicator();
        UpdateTimeDisplay();
        UpdateCrosshair();
        UpdateDamageIndicators();
    }

    #region Inicialización

    private void InitializeHUD()
    {
        // Ocultar elementos opcionales
        if (dialoguePanel != null) dialoguePanel.alpha = 0;
        if (notificationPanel != null) notificationPanel.alpha = 0;
        if (karmaFeedback != null) karmaFeedback.alpha = 0;
        if (reloadIndicator != null) reloadIndicator.gameObject.SetActive(false);

        // Configurar botones
        SetupButtons();
    }

    private void SetupButtons()
    {
        var input = TouchInputManager.Instance;
        if (input == null) return;

        // Los botones llaman al TouchInputManager
        if (fireButton != null)
        {
            var trigger = fireButton.gameObject.AddComponent<HoldButton>();
            trigger.OnHoldStart += input.OnFireButtonDown;
            trigger.OnHoldEnd += input.OnFireButtonUp;
        }

        if (reloadButton != null)
            reloadButton.onClick.AddListener(input.OnReloadButton);

        if (coverButton != null)
            coverButton.onClick.AddListener(input.OnCoverButton);

        if (crouchButton != null)
            crouchButton.onClick.AddListener(input.OnCrouchButton);

        if (grenadeButton != null)
            grenadeButton.onClick.AddListener(input.OnGrenadeButton);

        if (pauseButton != null)
            pauseButton.onClick.AddListener(PauseGame);
    }

    private void SubscribeToEvents()
    {
        // Karma
        var karma = GameManager.Instance?.Karma;
        if (karma != null)
        {
            karma.OnHonorChanged += (val, delta) => ShowKarmaChange("Honor", delta);
            karma.OnHumanidadChanged += (val, delta) => ShowKarmaChange("Humanidad", delta);
        }

        // Armas
        var weapon = WeaponSystem.Instance;
        if (weapon != null)
        {
            weapon.OnAmmoChanged += UpdateAmmoText;
            weapon.OnWeaponChanged += UpdateWeaponDisplay;
            weapon.OnReloadStart += ShowReloadIndicator;
            weapon.OnReloadEnd += HideReloadIndicator;
        }

        // Narrativa
        var narrative = NarrativeManager.Instance;
        if (narrative != null)
        {
            narrative.OnDialogueStart += ShowDialogue;
            narrative.OnDialogueEnd += HideDialogue;
            narrative.OnChoicesPresented += ShowChoices;
            narrative.OnTimerUpdate += UpdateTimer;
        }

        // Salud
        var player = PlayerController.Instance;
        if (player != null)
        {
            player.Health.OnHealthChanged += OnHealthChanged;
            player.Health.OnWounded += ShowWoundIndicator;
        }
    }

    #endregion

    #region Salud y Stamina

    private void UpdateHealthDisplay()
    {
        var health = PlayerController.Instance?.Health;
        if (health == null) return;

        float percent = health.GetHealthPercent();
        if (healthBar != null)
            healthBar.value = percent;

        // Color según nivel de vida
        if (healthFill != null)
            healthFill.color = Color.Lerp(healthColorLow, healthColorFull, percent);

        // Texto
        if (healthText != null)
            healthText.text = $"{Mathf.CeilToInt(health.CurrentHealth)}";

        // Indicador de sangrado
        if (bleedingIndicator != null)
        {
            bleedingIndicator.gameObject.SetActive(health.IsBleeding);
            if (health.IsBleeding)
            {
                float pulse = Mathf.PingPong(Time.time * 3f, 1f);
                bleedingIndicator.color = new Color(1, 0, 0, pulse * 0.5f);
            }
        }

        // Viñeta de daño bajo
        if (damageVignette != null)
        {
            float vignetteAlpha = percent < 0.3f ? (1f - percent / 0.3f) * 0.5f : 0f;
            damageVignette.color = new Color(0.5f, 0, 0, vignetteAlpha);
        }
    }

    private void UpdateStaminaDisplay()
    {
        var player = PlayerController.Instance;
        if (player == null || staminaBar == null) return;

        float percent = player.GetStaminaPercent();
        staminaBar.value = percent;

        // Solo mostrar cuando no está llena
        staminaBar.gameObject.SetActive(percent < 0.99f);
    }

    private void OnHealthChanged(float percent)
    {
        // Flash rojo al recibir daño
        StartCoroutine(DamageFlash());
    }

    private IEnumerator DamageFlash()
    {
        if (damageVignette == null) yield break;

        damageVignette.color = new Color(0.8f, 0, 0, 0.4f);
        yield return new WaitForSeconds(0.1f);

        float elapsed = 0;
        while (elapsed < 0.5f)
        {
            float alpha = Mathf.Lerp(0.4f, 0, elapsed / 0.5f);
            damageVignette.color = new Color(0.8f, 0, 0, alpha);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private void ShowWoundIndicator(BodyPart part)
    {
        ShowNotification($"Herida en {GetBodyPartName(part)}", NotificationType.Warning);
    }

    #endregion

    #region Munición

    private void UpdateAmmoDisplay()
    {
        var weapon = WeaponSystem.Instance;
        if (weapon == null || weapon.CurrentWeapon == null) return;

        // Color de munición baja
        bool lowAmmo = weapon.CurrentAmmo <= weapon.CurrentWeapon.MagazineSize * 0.25f;
        if (ammoCurrentText != null)
            ammoCurrentText.color = lowAmmo ? ammoLowColor : ammoColor;
    }

    private void UpdateAmmoText(int current, int reserve)
    {
        if (ammoCurrentText != null)
            ammoCurrentText.text = current.ToString();
        if (ammoReserveText != null)
            ammoReserveText.text = reserve.ToString();
    }

    private void UpdateWeaponDisplay(WeaponData weapon)
    {
        if (weaponNameText != null)
            weaponNameText.text = weapon.Name;
    }

    private void ShowReloadIndicator()
    {
        if (reloadIndicator != null)
            reloadIndicator.gameObject.SetActive(true);
    }

    private void HideReloadIndicator()
    {
        if (reloadIndicator != null)
            reloadIndicator.gameObject.SetActive(false);
    }

    #endregion

    #region Mini-Mapa

    private void UpdateMinimap()
    {
        // Rotar icono del jugador según orientación
        if (minimapPlayerIcon != null && PlayerController.Instance != null)
        {
            float playerYaw = PlayerController.Instance.transform.eulerAngles.y;
            minimapPlayerIcon.rotation = Quaternion.Euler(0, 0, -playerYaw);
        }
    }

    #endregion

    #region Objetivo de Misión

    private void UpdateObjectiveIndicator()
    {
        var narrative = NarrativeManager.Instance;
        if (narrative == null || narrative.ActiveMissions.Count == 0) return;

        Mission currentMission = narrative.ActiveMissions[0];
        if (objectiveText != null)
        {
            var activeObj = currentMission.Objectives.Find(o => !o.IsCompleted);
            if (activeObj != null)
            {
                objectiveText.text = activeObj.Description;
            }
        }
    }

    #endregion

    #region Reloj

    private void UpdateTimeDisplay()
    {
        var weather = WeatherSystem.Instance;
        if (weather == null || timeText == null) return;

        float hours = weather.GetHours();
        int h = Mathf.FloorToInt(hours);
        int m = Mathf.FloorToInt((hours - h) * 60f);

        timeText.text = $"{h:D2}:{m:D2} {(h < 12 ? "AM" : "PM")}";
    }

    #endregion

    #region Crosshair

    private void UpdateCrosshair()
    {
        if (crosshair == null) return;

        var player = PlayerController.Instance;
        bool showCrosshair = player != null && player.IsAiming;
        crosshair.gameObject.SetActive(showCrosshair);

        if (showCrosshair && crosshairParts != null)
        {
            // Expandir crosshair según spread del arma
            float spread = 10f; // Base
            if (player.IsSprinting) spread *= 2f;
            if (player.IsCrouching) spread *= 0.7f;

            float offset = spread * crosshairSpreadMultiplier;
            // Mover partes del crosshair
        }
    }

    #endregion

    #region Indicadores de Daño Direccional

    private void UpdateDamageIndicators()
    {
        // Fade out gradual
        FadeDamageIndicator(damageIndicatorTop);
        FadeDamageIndicator(damageIndicatorBottom);
        FadeDamageIndicator(damageIndicatorLeft);
        FadeDamageIndicator(damageIndicatorRight);
    }

    public void ShowDamageDirection(Vector3 damageSource)
    {
        var player = PlayerController.Instance;
        if (player == null) return;

        Vector3 dir = (damageSource - player.transform.position).normalized;
        Vector3 forward = player.transform.forward;
        Vector3 right = player.transform.right;

        float dotForward = Vector3.Dot(forward, dir);
        float dotRight = Vector3.Dot(right, dir);

        if (dotForward > 0.5f) ShowIndicator(damageIndicatorTop);
        else if (dotForward < -0.5f) ShowIndicator(damageIndicatorBottom);

        if (dotRight > 0.5f) ShowIndicator(damageIndicatorRight);
        else if (dotRight < -0.5f) ShowIndicator(damageIndicatorLeft);
    }

    private void ShowIndicator(Image indicator)
    {
        if (indicator == null) return;
        indicator.color = new Color(1, 0, 0, 0.8f);
    }

    private void FadeDamageIndicator(Image indicator)
    {
        if (indicator == null) return;
        Color c = indicator.color;
        c.a = Mathf.Lerp(c.a, 0, Time.deltaTime * 3f);
        indicator.color = c;
    }

    #endregion

    #region Diálogos

    private void ShowDialogue(DialogueNode node)
    {
        if (dialoguePanel == null) return;

        dialoguePanel.alpha = 1f;
        dialoguePanel.interactable = true;
        dialoguePanel.blocksRaycasts = true;

        if (speakerNameText != null)
            speakerNameText.text = node.SpeakerName;

        if (dialogueText != null)
            StartCoroutine(TypewriterEffect(node.Text));

        // Ocultar controles de combate durante diálogo
        SetCombatControlsVisible(false);
    }

    private void HideDialogue()
    {
        if (dialoguePanel == null) return;

        dialoguePanel.alpha = 0f;
        dialoguePanel.interactable = false;
        dialoguePanel.blocksRaycasts = false;

        // Limpiar opciones
        if (choicesContainer != null)
        {
            foreach (Transform child in choicesContainer)
                Destroy(child.gameObject);
        }

        SetCombatControlsVisible(true);
    }

    private void ShowChoices(DialogueChoice[] choices)
    {
        if (choicesContainer == null || choiceButtonPrefab == null) return;

        // Limpiar anteriores
        foreach (Transform child in choicesContainer)
            Destroy(child.gameObject);

        // Crear botones de opción
        foreach (var choice in choices)
        {
            GameObject btnObj = Instantiate(choiceButtonPrefab, choicesContainer);
            TextMeshProUGUI btnText = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            Button btn = btnObj.GetComponent<Button>();

            if (btnText != null)
            {
                string label = choice.Text;
                if (!string.IsNullOrEmpty(choice.SkillCheckLabel))
                    label = $"{choice.SkillCheckLabel} {label}";
                btnText.text = label;
            }

            // Color según tipo de elección
            Image btnImage = btnObj.GetComponent<Image>();
            if (btnImage != null)
            {
                btnImage.color = GetChoiceColor(choice.Type);
            }

            DialogueChoice capturedChoice = choice;
            btn.onClick.AddListener(() => NarrativeManager.Instance.MakeChoice(capturedChoice));
        }
    }

    private void UpdateTimer(float normalizedTime)
    {
        if (timerBar != null)
        {
            timerBar.gameObject.SetActive(true);
            timerBar.value = normalizedTime;

            // Cambiar color cuando queda poco tiempo
            Image fill = timerBar.fillRect.GetComponent<Image>();
            if (fill != null)
            {
                fill.color = normalizedTime < 0.3f ? Color.red : Color.yellow;
            }
        }
    }

    private IEnumerator TypewriterEffect(string text)
    {
        dialogueText.text = "";
        foreach (char c in text)
        {
            dialogueText.text += c;
            yield return new WaitForSecondsRealtime(0.03f);
        }
    }

    #endregion

    #region Notificaciones

    public void ShowNotification(string message, NotificationType type = NotificationType.Info)
    {
        if (notificationCoroutine != null)
            StopCoroutine(notificationCoroutine);

        notificationCoroutine = StartCoroutine(NotificationRoutine(message, type));
    }

    private IEnumerator NotificationRoutine(string message, NotificationType type)
    {
        if (notificationPanel == null) yield break;

        notificationText.text = message;
        notificationPanel.alpha = 1f;

        // Color según tipo
        if (notificationIcon != null)
        {
            switch (type)
            {
                case NotificationType.Info: notificationIcon.color = Color.white; break;
                case NotificationType.Warning: notificationIcon.color = Color.yellow; break;
                case NotificationType.Danger: notificationIcon.color = Color.red; break;
                case NotificationType.Success: notificationIcon.color = Color.green; break;
            }
        }

        yield return new WaitForSeconds(3f);

        // Fade out
        float elapsed = 0;
        while (elapsed < 1f)
        {
            notificationPanel.alpha = 1f - elapsed;
            elapsed += Time.deltaTime;
            yield return null;
        }
        notificationPanel.alpha = 0;
    }

    #endregion

    #region Karma Feedback

    private void ShowKarmaChange(string axis, float delta)
    {
        if (karmaCoroutine != null)
            StopCoroutine(karmaCoroutine);

        karmaCoroutine = StartCoroutine(KarmaFeedbackRoutine(axis, delta));
    }

    private IEnumerator KarmaFeedbackRoutine(string axis, float delta)
    {
        if (karmaFeedback == null) yield break;

        string sign = delta > 0 ? "+" : "";
        karmaChangeText.text = $"{axis}: {sign}{delta:F0}";
        karmaChangeText.color = delta > 0 ? Color.green : Color.red;
        karmaFeedback.alpha = 1f;

        yield return new WaitForSeconds(2f);

        float elapsed = 0;
        while (elapsed < 1f)
        {
            karmaFeedback.alpha = 1f - elapsed;
            elapsed += Time.deltaTime;
            yield return null;
        }
        karmaFeedback.alpha = 0;
    }

    #endregion

    #region Utilidades

    private void SetCombatControlsVisible(bool visible)
    {
        if (fireButton != null) fireButton.gameObject.SetActive(visible);
        if (aimButton != null) aimButton.gameObject.SetActive(visible);
        if (reloadButton != null) reloadButton.gameObject.SetActive(visible);
        if (coverButton != null) coverButton.gameObject.SetActive(visible);
        if (grenadeButton != null) grenadeButton.gameObject.SetActive(visible);
    }

    private Color GetChoiceColor(ChoiceType type)
    {
        switch (type)
        {
            case ChoiceType.Aggressive: return new Color(0.8f, 0.2f, 0.2f, 0.8f);
            case ChoiceType.Peaceful: return new Color(0.2f, 0.6f, 0.8f, 0.8f);
            case ChoiceType.SkillCheck: return new Color(0.8f, 0.7f, 0.1f, 0.8f);
            default: return new Color(0.3f, 0.3f, 0.3f, 0.8f);
        }
    }

    private string GetBodyPartName(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.Head: return "cabeza";
            case BodyPart.Torso: return "torso";
            case BodyPart.LeftArm: return "brazo izquierdo";
            case BodyPart.RightArm: return "brazo derecho";
            case BodyPart.LeftLeg: return "pierna izquierda";
            case BodyPart.RightLeg: return "pierna derecha";
            default: return "cuerpo";
        }
    }

    private void PauseGame()
    {
        GameManager.Instance.ChangeState(GameState.Paused);
    }

    #endregion
}

public enum NotificationType
{
    Info,
    Warning,
    Danger,
    Success,
    Mission
}

/// <summary>
/// Componente auxiliar para detectar hold en botones
/// </summary>
public class HoldButton : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
{
    public event Action OnHoldStart;
    public event Action OnHoldEnd;

    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData eventData)
    {
        OnHoldStart?.Invoke();
    }

    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData eventData)
    {
        OnHoldEnd?.Invoke();
    }
}
