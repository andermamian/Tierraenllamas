using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// TIERRA EN LLAMAS - WeatherSystem
/// Sistema de clima dinámico y ciclo día/noche para Colombia:
/// - Selva: lluvia tropical intensa, niebla densa, humedad
/// - Medellín: lluvia urbana, noches claras, atardeceres
/// - Bogotá: niebla andina, frío, cielos grises
/// Afecta visibilidad, sonido ambiental y gameplay.
/// </summary>
public class WeatherSystem : MonoBehaviour
{
    public static WeatherSystem Instance { get; private set; }

    [Header("Ciclo Día/Noche")]
    [SerializeField] private float dayDuration = 600f; // 10 minutos reales = 1 día en juego
    [SerializeField] private float currentTime = 0.25f; // 0-1 (0=medianoche, 0.25=6AM, 0.5=mediodía, 0.75=6PM)
    [SerializeField] private Light sunLight;
    [SerializeField] private Light moonLight;

    [Header("Clima Actual")]
    public WeatherType CurrentWeather = WeatherType.Clear;
    public float WeatherIntensity = 0f; // 0-1
    public float Temperature = 28f; // Celsius
    public float Humidity = 0.7f;
    public float WindSpeed = 0f;
    public Vector3 WindDirection = Vector3.right;

    [Header("Configuración por Escenario")]
    public ScenarioClimate CurrentClimate;

    [Header("Efectos Visuales")]
    [SerializeField] private ParticleSystem rainParticles;
    [SerializeField] private ParticleSystem heavyRainParticles;
    [SerializeField] private ParticleSystem fogParticles;
    [SerializeField] private ParticleSystem fireflyParticles;
    [SerializeField] private Material skyboxMaterial;

    [Header("Colores del Cielo")]
    [SerializeField] private Gradient skyColorGradient;
    [SerializeField] private Gradient ambientColorGradient;
    [SerializeField] private Gradient fogColorGradient;
    [SerializeField] private AnimationCurve sunIntensityCurve;

    [Header("Niebla")]
    [SerializeField] private float baseFogDensity = 0.01f;
    [SerializeField] private float rainFogDensity = 0.03f;
    [SerializeField] private float heavyFogDensity = 0.08f;

    // Estado interno
    private float weatherTransitionTimer;
    private WeatherType targetWeather;
    private float targetIntensity;
    private float nextWeatherChangeTime;

    // Eventos
    public event Action<WeatherType> OnWeatherChanged;
    public event Action<float> OnTimeChanged; // 0-24 hours

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        SetClimateForScene(GameManager.Instance?.CurrentScene ?? "Putumayo_Selva");
        ScheduleNextWeatherChange();
    }

    private void Update()
    {
        UpdateDayNightCycle();
        UpdateWeather();
        UpdateVisualEffects();
        UpdateGameplayEffects();
    }

    #region Ciclo Día/Noche

    private void UpdateDayNightCycle()
    {
        // Avanzar tiempo
        currentTime += (Time.deltaTime / dayDuration);
        if (currentTime >= 1f) currentTime -= 1f;

        float hours = currentTime * 24f;
        OnTimeChanged?.Invoke(hours);

        // Actualizar sol
        if (sunLight != null)
        {
            float sunAngle = (currentTime - 0.25f) * 360f; // Sol sale a las 6AM
            sunLight.transform.rotation = Quaternion.Euler(sunAngle, -30f, 0);

            float intensity = sunIntensityCurve.Evaluate(currentTime);
            sunLight.intensity = intensity;
            sunLight.color = skyColorGradient.Evaluate(currentTime);

            // Desactivar sol de noche
            sunLight.enabled = intensity > 0.01f;
        }

        // Actualizar luna
        if (moonLight != null)
        {
            float moonAngle = ((currentTime + 0.5f) % 1f - 0.25f) * 360f;
            moonLight.transform.rotation = Quaternion.Euler(moonAngle, 150f, 0);

            bool isNight = currentTime < 0.25f || currentTime > 0.75f;
            moonLight.enabled = isNight;
            moonLight.intensity = isNight ? 0.3f : 0f;
        }

        // Actualizar ambiente
        RenderSettings.ambientLight = ambientColorGradient.Evaluate(currentTime);

        // Actualizar skybox
        if (skyboxMaterial != null)
        {
            skyboxMaterial.SetFloat("_Exposure", Mathf.Lerp(0.2f, 1.5f,
                sunIntensityCurve.Evaluate(currentTime)));
        }
    }

    public float GetTimeOfDay() => currentTime;

    public float GetHours() => currentTime * 24f;

    public bool IsNight() => currentTime < 0.25f || currentTime > 0.75f;

    public bool IsDawn() => currentTime > 0.2f && currentTime < 0.3f;

    public bool IsDusk() => currentTime > 0.7f && currentTime < 0.8f;

    public void SetTime(float hours)
    {
        currentTime = hours / 24f;
    }

    #endregion

    #region Sistema de Clima

    private void UpdateWeather()
    {
        // Verificar si es hora de cambiar el clima
        if (Time.time > nextWeatherChangeTime)
        {
            ChangeWeatherRandom();
            ScheduleNextWeatherChange();
        }

        // Transición suave entre climas
        if (weatherTransitionTimer > 0)
        {
            weatherTransitionTimer -= Time.deltaTime;
            float t = 1f - (weatherTransitionTimer / 10f); // 10 segundos de transición
            WeatherIntensity = Mathf.Lerp(WeatherIntensity, targetIntensity, t);
        }
    }

    private void ChangeWeatherRandom()
    {
        if (CurrentClimate == null) return;

        // Probabilidades basadas en el escenario
        float roll = UnityEngine.Random.value;
        float cumulative = 0f;

        foreach (var wp in CurrentClimate.WeatherProbabilities)
        {
            cumulative += wp.Probability;
            if (roll <= cumulative)
            {
                SetWeather(wp.Weather, UnityEngine.Random.Range(wp.MinIntensity, wp.MaxIntensity));
                break;
            }
        }
    }

    public void SetWeather(WeatherType weather, float intensity)
    {
        if (weather == CurrentWeather && Mathf.Abs(intensity - WeatherIntensity) < 0.1f)
            return;

        targetWeather = weather;
        targetIntensity = intensity;
        weatherTransitionTimer = 10f;

        CurrentWeather = weather;
        OnWeatherChanged?.Invoke(weather);

        Debug.Log($"[Weather] Clima: {weather}, Intensidad: {intensity:F2}");
    }

    private void ScheduleNextWeatherChange()
    {
        float minInterval = CurrentClimate != null ? CurrentClimate.MinWeatherDuration : 60f;
        float maxInterval = CurrentClimate != null ? CurrentClimate.MaxWeatherDuration : 180f;
        nextWeatherChangeTime = Time.time + UnityEngine.Random.Range(minInterval, maxInterval);
    }

    #endregion

    #region Efectos Visuales

    private void UpdateVisualEffects()
    {
        // Lluvia
        UpdateRain();

        // Niebla
        UpdateFog();

        // Luciérnagas (solo de noche en selva)
        UpdateFireflies();

        // Viento
        UpdateWind();
    }

    private void UpdateRain()
    {
        bool isRaining = CurrentWeather == WeatherType.Rain ||
                         CurrentWeather == WeatherType.HeavyRain ||
                         CurrentWeather == WeatherType.Thunderstorm;

        if (rainParticles != null)
        {
            var emission = rainParticles.emission;
            if (isRaining && WeatherIntensity > 0.3f)
            {
                if (!rainParticles.isPlaying) rainParticles.Play();
                emission.rateOverTime = WeatherIntensity * 500f;
            }
            else
            {
                if (rainParticles.isPlaying) rainParticles.Stop();
            }
        }

        if (heavyRainParticles != null)
        {
            var emission = heavyRainParticles.emission;
            if (CurrentWeather == WeatherType.HeavyRain || CurrentWeather == WeatherType.Thunderstorm)
            {
                if (!heavyRainParticles.isPlaying) heavyRainParticles.Play();
                emission.rateOverTime = WeatherIntensity * 1000f;
            }
            else
            {
                if (heavyRainParticles.isPlaying) heavyRainParticles.Stop();
            }
        }
    }

    private void UpdateFog()
    {
        float targetDensity = baseFogDensity;

        switch (CurrentWeather)
        {
            case WeatherType.Fog:
                targetDensity = heavyFogDensity * WeatherIntensity;
                break;
            case WeatherType.Rain:
            case WeatherType.HeavyRain:
                targetDensity = rainFogDensity * WeatherIntensity;
                break;
            case WeatherType.Clear:
                targetDensity = baseFogDensity;
                break;
        }

        // Más niebla de noche
        if (IsNight()) targetDensity *= 1.5f;
        if (IsDawn()) targetDensity *= 2f; // Niebla matutina

        RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, targetDensity, Time.deltaTime);
        RenderSettings.fogColor = fogColorGradient.Evaluate(currentTime);
        RenderSettings.fog = true;

        if (fogParticles != null)
        {
            var emission = fogParticles.emission;
            emission.rateOverTime = targetDensity * 100f;
        }
    }

    private void UpdateFireflies()
    {
        if (fireflyParticles == null) return;

        bool showFireflies = IsNight() &&
            CurrentClimate != null &&
            CurrentClimate.ScenarioType == ScenarioType.Selva &&
            CurrentWeather != WeatherType.HeavyRain;

        if (showFireflies && !fireflyParticles.isPlaying)
            fireflyParticles.Play();
        else if (!showFireflies && fireflyParticles.isPlaying)
            fireflyParticles.Stop();
    }

    private void UpdateWind()
    {
        // Viento afecta vegetación y partículas
        WindSpeed = CurrentWeather == WeatherType.Thunderstorm ? 15f :
                    CurrentWeather == WeatherType.HeavyRain ? 8f :
                    CurrentWeather == WeatherType.Rain ? 3f : 1f;

        WindDirection = Quaternion.Euler(0, Time.time * 5f, 0) * Vector3.right;

        // Shader global para vegetación
        Shader.SetGlobalVector("_WindDirection", WindDirection * WindSpeed);
        Shader.SetGlobalFloat("_WindStrength", WindSpeed / 15f);
    }

    #endregion

    #region Efectos en Gameplay

    private void UpdateGameplayEffects()
    {
        // La lluvia reduce la visibilidad de los enemigos
        float visibilityModifier = 1f;
        float noiseModifier = 1f;

        switch (CurrentWeather)
        {
            case WeatherType.Rain:
                visibilityModifier = 0.7f;
                noiseModifier = 0.8f; // Lluvia enmascara ruido
                break;
            case WeatherType.HeavyRain:
                visibilityModifier = 0.4f;
                noiseModifier = 0.5f;
                break;
            case WeatherType.Thunderstorm:
                visibilityModifier = 0.3f;
                noiseModifier = 0.3f;
                break;
            case WeatherType.Fog:
                visibilityModifier = 0.5f;
                noiseModifier = 1f;
                break;
        }

        // Noche reduce visibilidad
        if (IsNight()) visibilityModifier *= 0.5f;

        // Aplicar a variables globales de gameplay
        Shader.SetGlobalFloat("_VisibilityModifier", visibilityModifier);
        Shader.SetGlobalFloat("_NoiseModifier", noiseModifier);
    }

    /// <summary>
    /// Obtiene el modificador de visibilidad para la IA enemiga
    /// </summary>
    public float GetVisibilityModifier()
    {
        float mod = 1f;
        if (CurrentWeather == WeatherType.Rain) mod *= 0.7f;
        if (CurrentWeather == WeatherType.HeavyRain) mod *= 0.4f;
        if (CurrentWeather == WeatherType.Fog) mod *= 0.5f;
        if (IsNight()) mod *= 0.5f;
        return mod;
    }

    /// <summary>
    /// Obtiene el modificador de ruido ambiental
    /// </summary>
    public float GetNoiseModifier()
    {
        if (CurrentWeather == WeatherType.HeavyRain) return 0.5f;
        if (CurrentWeather == WeatherType.Thunderstorm) return 0.3f;
        if (CurrentWeather == WeatherType.Rain) return 0.8f;
        return 1f;
    }

    #endregion

    #region Configuración por Escenario

    public void SetClimateForScene(string sceneName)
    {
        if (sceneName.Contains("Putumayo") || sceneName.Contains("Selva"))
        {
            CurrentClimate = ScenarioClimate.CreateSelvaClimate();
        }
        else if (sceneName.Contains("Medellin"))
        {
            CurrentClimate = ScenarioClimate.CreateMedellinClimate();
        }
        else if (sceneName.Contains("Bogota"))
        {
            CurrentClimate = ScenarioClimate.CreateBogotaClimate();
        }

        Temperature = CurrentClimate.BaseTemperature;
        Humidity = CurrentClimate.BaseHumidity;
    }

    #endregion
}

// === ENUMERACIONES Y DATOS ===

public enum WeatherType
{
    Clear,
    Cloudy,
    Rain,
    HeavyRain,
    Thunderstorm,
    Fog,
    Overcast
}

public enum ScenarioType
{
    Selva,
    CiudadMedellin,
    CiudadBogota
}

[System.Serializable]
public class ScenarioClimate
{
    public ScenarioType ScenarioType;
    public float BaseTemperature;
    public float BaseHumidity;
    public float MinWeatherDuration;
    public float MaxWeatherDuration;
    public WeatherProbability[] WeatherProbabilities;

    public static ScenarioClimate CreateSelvaClimate()
    {
        return new ScenarioClimate
        {
            ScenarioType = ScenarioType.Selva,
            BaseTemperature = 32f,
            BaseHumidity = 0.9f,
            MinWeatherDuration = 30f,
            MaxWeatherDuration = 120f,
            WeatherProbabilities = new WeatherProbability[]
            {
                new WeatherProbability { Weather = WeatherType.Rain, Probability = 0.35f, MinIntensity = 0.3f, MaxIntensity = 0.8f },
                new WeatherProbability { Weather = WeatherType.HeavyRain, Probability = 0.2f, MinIntensity = 0.7f, MaxIntensity = 1f },
                new WeatherProbability { Weather = WeatherType.Thunderstorm, Probability = 0.1f, MinIntensity = 0.8f, MaxIntensity = 1f },
                new WeatherProbability { Weather = WeatherType.Fog, Probability = 0.15f, MinIntensity = 0.4f, MaxIntensity = 0.9f },
                new WeatherProbability { Weather = WeatherType.Clear, Probability = 0.1f, MinIntensity = 0f, MaxIntensity = 0.2f },
                new WeatherProbability { Weather = WeatherType.Cloudy, Probability = 0.1f, MinIntensity = 0.3f, MaxIntensity = 0.6f }
            }
        };
    }

    public static ScenarioClimate CreateMedellinClimate()
    {
        return new ScenarioClimate
        {
            ScenarioType = ScenarioType.CiudadMedellin,
            BaseTemperature = 22f,
            BaseHumidity = 0.6f,
            MinWeatherDuration = 60f,
            MaxWeatherDuration = 240f,
            WeatherProbabilities = new WeatherProbability[]
            {
                new WeatherProbability { Weather = WeatherType.Clear, Probability = 0.35f, MinIntensity = 0f, MaxIntensity = 0.1f },
                new WeatherProbability { Weather = WeatherType.Cloudy, Probability = 0.25f, MinIntensity = 0.2f, MaxIntensity = 0.5f },
                new WeatherProbability { Weather = WeatherType.Rain, Probability = 0.25f, MinIntensity = 0.3f, MaxIntensity = 0.7f },
                new WeatherProbability { Weather = WeatherType.HeavyRain, Probability = 0.1f, MinIntensity = 0.6f, MaxIntensity = 0.9f },
                new WeatherProbability { Weather = WeatherType.Fog, Probability = 0.05f, MinIntensity = 0.2f, MaxIntensity = 0.5f }
            }
        };
    }

    public static ScenarioClimate CreateBogotaClimate()
    {
        return new ScenarioClimate
        {
            ScenarioType = ScenarioType.CiudadBogota,
            BaseTemperature = 14f,
            BaseHumidity = 0.75f,
            MinWeatherDuration = 45f,
            MaxWeatherDuration = 180f,
            WeatherProbabilities = new WeatherProbability[]
            {
                new WeatherProbability { Weather = WeatherType.Overcast, Probability = 0.3f, MinIntensity = 0.3f, MaxIntensity = 0.7f },
                new WeatherProbability { Weather = WeatherType.Rain, Probability = 0.25f, MinIntensity = 0.2f, MaxIntensity = 0.6f },
                new WeatherProbability { Weather = WeatherType.Fog, Probability = 0.2f, MinIntensity = 0.5f, MaxIntensity = 0.9f },
                new WeatherProbability { Weather = WeatherType.Cloudy, Probability = 0.15f, MinIntensity = 0.3f, MaxIntensity = 0.6f },
                new WeatherProbability { Weather = WeatherType.Clear, Probability = 0.1f, MinIntensity = 0f, MaxIntensity = 0.2f }
            }
        };
    }
}

[System.Serializable]
public class WeatherProbability
{
    public WeatherType Weather;
    public float Probability;
    public float MinIntensity;
    public float MaxIntensity;
}
