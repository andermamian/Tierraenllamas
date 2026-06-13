using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections;

/// <summary>
/// TIERRA EN LLAMAS - PostProcessManager
/// Efectos de post-procesado cinematográficos:
/// - Bloom para luces nocturnas de Medellín
/// - Color grading por escenario
/// - Viñeta dinámica (combate/heridas)
/// - Motion blur en persecuciones
/// - Depth of Field en diálogos
/// - Efecto de flashback (sepia + grain)
/// - Aberración cromática al recibir daño
/// - Film grain para atmósfera
/// </summary>
public class PostProcessManager : MonoBehaviour
{
    public static PostProcessManager Instance { get; private set; }

    [Header("Referencias")]
    [SerializeField] private Volume globalVolume;
    [SerializeField] private Volume combatVolume;
    [SerializeField] private Volume dialogueVolume;

    // Componentes del volumen global
    private Bloom bloom;
    private ColorAdjustments colorAdjustments;
    private Vignette vignette;
    private MotionBlur motionBlur;
    private DepthOfField depthOfField;
    private ChromaticAberration chromaticAberration;
    private FilmGrain filmGrain;
    private LiftGammaGain liftGammaGain;
    private ColorCurves colorCurves;
    private Tonemapping tonemapping;

    [Header("Presets por Escenario")]
    public PostProcessPreset CurrentPreset;

    [Header("Estado")]
    private float targetVignetteIntensity = 0.3f;
    private float currentChromaticAberration = 0f;
    private bool isFlashbackActive = false;

    private void Awake()
    {
        Instance = this;
        InitializeComponents();
    }

    private void Start()
    {
        ApplyScenePreset(GameManager.Instance?.CurrentScene ?? "Putumayo_Selva");
    }

    private void Update()
    {
        UpdateDynamicEffects();
    }

    private void InitializeComponents()
    {
        if (globalVolume == null) return;

        VolumeProfile profile = globalVolume.profile;
        profile.TryGet(out bloom);
        profile.TryGet(out colorAdjustments);
        profile.TryGet(out vignette);
        profile.TryGet(out motionBlur);
        profile.TryGet(out depthOfField);
        profile.TryGet(out chromaticAberration);
        profile.TryGet(out filmGrain);
        profile.TryGet(out liftGammaGain);
        profile.TryGet(out colorCurves);
        profile.TryGet(out tonemapping);
    }

    #region Presets por Escenario

    public void ApplyScenePreset(string sceneName)
    {
        if (sceneName.Contains("Putumayo") || sceneName.Contains("Selva"))
        {
            ApplySelvaPreset();
        }
        else if (sceneName.Contains("Medellin"))
        {
            ApplyMedellinPreset();
        }
        else if (sceneName.Contains("Bogota"))
        {
            ApplyBogotaPreset();
        }
    }

    private void ApplySelvaPreset()
    {
        // Selva: Verde intenso, humedad, contraste alto
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.value = 0.3f;
            colorAdjustments.contrast.value = 15f;
            colorAdjustments.saturation.value = 20f;
            colorAdjustments.colorFilter.value = new Color(0.9f, 1f, 0.85f); // Tinte verde
        }

        if (bloom != null)
        {
            bloom.intensity.value = 0.8f;
            bloom.threshold.value = 1.2f;
            bloom.scatter.value = 0.6f;
        }

        if (vignette != null)
        {
            vignette.intensity.value = 0.35f;
            vignette.color.value = new Color(0.05f, 0.1f, 0.02f);
        }

        if (filmGrain != null)
        {
            filmGrain.intensity.value = 0.15f;
            filmGrain.response.value = 0.5f;
        }

        targetVignetteIntensity = 0.35f;
    }

    private void ApplyMedellinPreset()
    {
        // Medellín nocturno: Neón, alto contraste, bloom fuerte
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.value = -0.2f;
            colorAdjustments.contrast.value = 25f;
            colorAdjustments.saturation.value = 30f;
            colorAdjustments.colorFilter.value = new Color(1f, 0.9f, 0.8f); // Cálido
        }

        if (bloom != null)
        {
            bloom.intensity.value = 1.5f; // Bloom fuerte para neones
            bloom.threshold.value = 0.8f;
            bloom.scatter.value = 0.7f;
            bloom.tint.value = new Color(1f, 0.8f, 0.6f);
        }

        if (vignette != null)
        {
            vignette.intensity.value = 0.4f;
            vignette.color.value = new Color(0.1f, 0.02f, 0.02f);
        }

        if (filmGrain != null)
        {
            filmGrain.intensity.value = 0.2f;
            filmGrain.response.value = 0.6f;
        }

        targetVignetteIntensity = 0.4f;
    }

    private void ApplyBogotaPreset()
    {
        // Bogotá: Frío, desaturado, niebla, gris
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.value = -0.1f;
            colorAdjustments.contrast.value = 10f;
            colorAdjustments.saturation.value = -15f; // Desaturado
            colorAdjustments.colorFilter.value = new Color(0.85f, 0.9f, 1f); // Tinte azul frío
        }

        if (bloom != null)
        {
            bloom.intensity.value = 0.5f;
            bloom.threshold.value = 1.5f;
            bloom.scatter.value = 0.5f;
        }

        if (vignette != null)
        {
            vignette.intensity.value = 0.3f;
            vignette.color.value = new Color(0.05f, 0.05f, 0.1f);
        }

        if (filmGrain != null)
        {
            filmGrain.intensity.value = 0.25f;
            filmGrain.response.value = 0.4f;
        }

        targetVignetteIntensity = 0.3f;
    }

    #endregion

    #region Efectos Dinámicos

    private void UpdateDynamicEffects()
    {
        // Viñeta suave
        if (vignette != null)
        {
            float current = vignette.intensity.value;
            vignette.intensity.value = Mathf.Lerp(current, targetVignetteIntensity, Time.deltaTime * 2f);
        }

        // Aberración cromática (fade out)
        if (chromaticAberration != null && currentChromaticAberration > 0)
        {
            currentChromaticAberration = Mathf.Lerp(currentChromaticAberration, 0, Time.deltaTime * 3f);
            chromaticAberration.intensity.value = currentChromaticAberration;
        }
    }

    /// <summary>
    /// Efecto de recibir daño: viñeta roja + aberración cromática
    /// </summary>
    public void OnPlayerDamaged(float damagePercent)
    {
        // Viñeta roja temporal
        if (vignette != null)
        {
            vignette.color.value = Color.red;
            vignette.intensity.value = 0.5f + damagePercent * 0.3f;
        }

        // Aberración cromática
        currentChromaticAberration = 0.5f + damagePercent;

        // Restaurar después
        StartCoroutine(RestoreAfterDamage());
    }

    private IEnumerator RestoreAfterDamage()
    {
        yield return new WaitForSeconds(0.3f);

        // Restaurar color de viñeta
        if (vignette != null)
        {
            ApplyScenePreset(GameManager.Instance?.CurrentScene ?? "");
        }
    }

    /// <summary>
    /// Efecto de vida baja: pulsación de viñeta roja
    /// </summary>
    public void SetLowHealthEffect(bool active, float healthPercent)
    {
        if (!active)
        {
            targetVignetteIntensity = 0.3f;
            return;
        }

        // Pulsación
        float pulse = Mathf.Sin(Time.time * 3f) * 0.5f + 0.5f;
        targetVignetteIntensity = Mathf.Lerp(0.4f, 0.7f, pulse * (1f - healthPercent));

        if (vignette != null)
            vignette.color.value = Color.Lerp(Color.red, new Color(0.5f, 0, 0), pulse);
    }

    #endregion

    #region Efectos Especiales

    /// <summary>
    /// Activar efecto de flashback (sepia, grain alto, viñeta fuerte)
    /// </summary>
    public void ApplyFlashbackEffect()
    {
        isFlashbackActive = true;

        if (colorAdjustments != null)
        {
            colorAdjustments.saturation.value = -60f;
            colorAdjustments.colorFilter.value = new Color(1f, 0.9f, 0.7f); // Sepia
            colorAdjustments.contrast.value = 30f;
        }

        if (filmGrain != null)
        {
            filmGrain.intensity.value = 0.6f;
        }

        if (vignette != null)
        {
            vignette.intensity.value = 0.6f;
            vignette.color.value = Color.black;
        }
    }

    public void RemoveFlashbackEffect()
    {
        isFlashbackActive = false;
        ApplyScenePreset(GameManager.Instance?.CurrentScene ?? "");
    }

    /// <summary>
    /// Depth of Field para diálogos (fondo borroso)
    /// </summary>
    public void EnableDialogueDOF(float focusDistance)
    {
        if (depthOfField == null) return;

        depthOfField.active = true;
        depthOfField.mode.value = DepthOfFieldMode.Bokeh;
        depthOfField.focusDistance.value = focusDistance;
        depthOfField.focalLength.value = 50f;
        depthOfField.aperture.value = 2.8f;
    }

    public void DisableDialogueDOF()
    {
        if (depthOfField == null) return;
        depthOfField.active = false;
    }

    /// <summary>
    /// Motion blur para persecuciones en vehículo
    /// </summary>
    public void SetMotionBlur(bool active, float intensity = 0.5f)
    {
        if (motionBlur == null) return;
        motionBlur.active = active;
        motionBlur.intensity.value = intensity;
    }

    /// <summary>
    /// Efecto de explosión cercana (blanco + shake)
    /// </summary>
    public void ExplosionEffect()
    {
        StartCoroutine(ExplosionRoutine());
    }

    private IEnumerator ExplosionRoutine()
    {
        // Flash blanco
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.value = 3f;
        }

        currentChromaticAberration = 1f;

        yield return new WaitForSeconds(0.1f);

        // Fade back
        float elapsed = 0;
        while (elapsed < 1f)
        {
            if (colorAdjustments != null)
                colorAdjustments.postExposure.value = Mathf.Lerp(3f, 0.3f, elapsed);
            elapsed += Time.deltaTime * 2f;
            yield return null;
        }

        ApplyScenePreset(GameManager.Instance?.CurrentScene ?? "");
    }

    /// <summary>
    /// Efecto de noche: oscurece y ajusta tonos
    /// </summary>
    public void SetNightMode(float nightIntensity)
    {
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.value = Mathf.Lerp(0.3f, -0.5f, nightIntensity);
        }

        if (bloom != null)
        {
            bloom.intensity.value = Mathf.Lerp(0.8f, 2f, nightIntensity);
            bloom.threshold.value = Mathf.Lerp(1.2f, 0.5f, nightIntensity);
        }
    }

    #endregion

    #region Calidad para Android

    public void SetQualityLevel(int level)
    {
        switch (level)
        {
            case 0: // Bajo
                if (bloom != null) bloom.active = false;
                if (motionBlur != null) motionBlur.active = false;
                if (depthOfField != null) depthOfField.active = false;
                if (filmGrain != null) filmGrain.active = false;
                break;

            case 1: // Medio
                if (bloom != null) { bloom.active = true; bloom.highQualityFiltering.value = false; }
                if (motionBlur != null) motionBlur.active = false;
                if (filmGrain != null) filmGrain.active = true;
                break;

            case 2: // Alto
                if (bloom != null) { bloom.active = true; bloom.highQualityFiltering.value = true; }
                if (motionBlur != null) motionBlur.active = true;
                if (filmGrain != null) filmGrain.active = true;
                if (depthOfField != null) depthOfField.active = true;
                break;
        }
    }

    #endregion
}

[System.Serializable]
public class PostProcessPreset
{
    public string Name;
    public float Exposure;
    public float Contrast;
    public float Saturation;
    public Color ColorFilter;
    public float BloomIntensity;
    public float VignetteIntensity;
    public Color VignetteColor;
    public float FilmGrainIntensity;
}
