using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// TIERRA EN LLAMAS - AudioManager
/// Sistema de audio inmersivo con:
/// - Música adaptativa (vallenato, cumbia, tensión, combate)
/// - Fauna colombiana (aves, insectos, ranas, monos)
/// - Efectos ambientales (lluvia, truenos, ríos, ciudad)
/// - Audio posicional 3D para combate
/// - Radio de época con transmisiones históricas
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Fuentes de Audio")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource ambientSource;
    [SerializeField] private AudioSource weatherSource;
    [SerializeField] private AudioSource radioSource;
    [SerializeField] private AudioSource uiSource;

    [Header("Pool de SFX")]
    [SerializeField] private int sfxPoolSize = 20;
    private List<AudioSource> sfxPool = new List<AudioSource>();
    private int currentSfxIndex = 0;

    [Header("Configuración de Música")]
    [SerializeField] private float musicFadeTime = 3f;
    [SerializeField] private float combatMusicDelay = 1f;
    public MusicState CurrentMusicState = MusicState.Exploration;

    [Header("Configuración Ambiental")]
    [SerializeField] private float ambientCrossfadeTime = 5f;
    public AmbientPreset CurrentAmbient;

    [Header("Volúmenes")]
    private float masterVolume = 1f;
    private float musicVolume = 0.7f;
    private float sfxVolume = 1f;
    private float ambientVolume = 0.8f;

    // Clips de audio organizados
    private Dictionary<string, AudioClip> loadedClips = new Dictionary<string, AudioClip>();

    // Estado
    private bool isTransitioning;
    private Coroutine musicTransition;
    private Coroutine ambientTransition;

    private void Awake()
    {
        Instance = this;
        InitializeSFXPool();
    }

    private void Start()
    {
        // Suscribirse a eventos del juego
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStateChanged += HandleGameStateChange;
            GameManager.Instance.OnSceneTransition += HandleSceneTransition;
        }

        if (CombatManager.Instance != null)
        {
            CombatManager.Instance.OnCombatStart += () => TransitionMusic(MusicState.Combat);
            CombatManager.Instance.OnCombatEnd += () => TransitionMusic(MusicState.Exploration);
        }

        if (WeatherSystem.Instance != null)
        {
            WeatherSystem.Instance.OnWeatherChanged += HandleWeatherChange;
        }
    }

    private void InitializeSFXPool()
    {
        for (int i = 0; i < sfxPoolSize; i++)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f; // 3D por defecto
            source.maxDistance = 50f;
            source.rolloffMode = AudioRolloffMode.Custom;
            sfxPool.Add(source);
        }
    }

    #region Música Adaptativa

    public void TransitionMusic(MusicState newState)
    {
        if (CurrentMusicState == newState) return;
        CurrentMusicState = newState;

        if (musicTransition != null)
            StopCoroutine(musicTransition);

        musicTransition = StartCoroutine(CrossfadeMusic(GetMusicForState(newState)));
    }

    private IEnumerator CrossfadeMusic(AudioClip newClip)
    {
        if (newClip == null) yield break;

        isTransitioning = true;
        float startVolume = musicSource.volume;

        // Fade out
        float elapsed = 0;
        while (elapsed < musicFadeTime)
        {
            musicSource.volume = Mathf.Lerp(startVolume, 0, elapsed / musicFadeTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Cambiar clip
        musicSource.clip = newClip;
        musicSource.Play();

        // Fade in
        elapsed = 0;
        while (elapsed < musicFadeTime)
        {
            musicSource.volume = Mathf.Lerp(0, musicVolume * masterVolume, elapsed / musicFadeTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        musicSource.volume = musicVolume * masterVolume;
        isTransitioning = false;
    }

    private AudioClip GetMusicForState(MusicState state)
    {
        string sceneName = GameManager.Instance?.CurrentScene ?? "";
        string clipName = "";

        switch (state)
        {
            case MusicState.Exploration:
                if (sceneName.Contains("Putumayo"))
                    clipName = "music_selva_exploration";
                else if (sceneName.Contains("Medellin"))
                    clipName = "music_medellin_night"; // Cumbia/salsa ambiental
                else
                    clipName = "music_bogota_tension";
                break;

            case MusicState.Combat:
                clipName = "music_combat_intense";
                break;

            case MusicState.Stealth:
                clipName = "music_stealth_tension";
                break;

            case MusicState.Dialogue:
                clipName = "music_dialogue_ambient";
                break;

            case MusicState.Boss:
                clipName = "music_boss_fight";
                break;

            case MusicState.Emotional:
                clipName = "music_emotional_strings";
                break;
        }

        return GetClip(clipName);
    }

    #endregion

    #region Audio Ambiental

    public void SetAmbientPreset(AmbientPreset preset)
    {
        CurrentAmbient = preset;

        if (ambientTransition != null)
            StopCoroutine(ambientTransition);

        ambientTransition = StartCoroutine(CrossfadeAmbient(preset));
    }

    private IEnumerator CrossfadeAmbient(AmbientPreset preset)
    {
        float startVolume = ambientSource.volume;

        // Fade out
        float elapsed = 0;
        while (elapsed < ambientCrossfadeTime)
        {
            ambientSource.volume = Mathf.Lerp(startVolume, 0, elapsed / ambientCrossfadeTime);
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Cambiar clip ambiental
        ambientSource.clip = GetClip(preset.AmbientClipName);
        ambientSource.loop = true;
        ambientSource.Play();

        // Fade in
        elapsed = 0;
        float targetVol = ambientVolume * masterVolume * preset.VolumeMultiplier;
        while (elapsed < ambientCrossfadeTime)
        {
            ambientSource.volume = Mathf.Lerp(0, targetVol, elapsed / ambientCrossfadeTime);
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// Presets ambientales para cada escenario colombiano
    /// </summary>
    public static AmbientPreset GetPresetForScene(string scene)
    {
        if (scene.Contains("Putumayo") || scene.Contains("Selva"))
        {
            return new AmbientPreset
            {
                Name = "Selva del Putumayo",
                AmbientClipName = "ambient_selva_tropical",
                VolumeMultiplier = 1f,
                // Sonidos: tucanes, guacamayas, monos aulladores, insectos,
                // ranas dendrobates, río, hojas, viento entre árboles
                RandomSounds = new string[]
                {
                    "fauna_tucan", "fauna_guacamaya", "fauna_mono_aullador",
                    "fauna_rana", "fauna_grillo", "fauna_cigarra",
                    "nature_rio", "nature_cascada_lejana", "nature_hojas_viento"
                },
                RandomSoundInterval = 8f,
                RandomSoundVariance = 5f
            };
        }
        else if (scene.Contains("Medellin"))
        {
            return new AmbientPreset
            {
                Name = "Medellín Nocturno",
                AmbientClipName = "ambient_ciudad_noche",
                VolumeMultiplier = 0.8f,
                // Sonidos: tráfico lejano, música de cantina, perros,
                // motos, sirenas, gente hablando
                RandomSounds = new string[]
                {
                    "city_traffic", "city_moto_lejana", "city_perro",
                    "city_sirena_lejana", "city_musica_cantina",
                    "city_voces_lejanas", "city_bus"
                },
                RandomSoundInterval = 12f,
                RandomSoundVariance = 8f
            };
        }
        else // Bogotá
        {
            return new AmbientPreset
            {
                Name = "Bogotá Andina",
                AmbientClipName = "ambient_bogota_frio",
                VolumeMultiplier = 0.7f,
                // Sonidos: viento frío, tráfico, TransMilenio,
                // palomas, vendedores ambulantes
                RandomSounds = new string[]
                {
                    "city_viento_frio", "city_transmilenio",
                    "city_palomas", "city_vendedor_ambulante",
                    "city_traffic_bogota", "city_campana_iglesia"
                },
                RandomSoundInterval = 15f,
                RandomSoundVariance = 10f
            };
        }
    }

    #endregion

    #region Efectos de Sonido

    public void PlaySFX(string clipName, Vector3 position, float volume = 1f)
    {
        AudioClip clip = GetClip(clipName);
        if (clip == null) return;

        AudioSource source = GetAvailableSFXSource();
        source.transform.position = position;
        source.clip = clip;
        source.volume = volume * sfxVolume * masterVolume;
        source.spatialBlend = 1f;
        source.Play();
    }

    public void PlaySFX2D(string clipName, float volume = 1f)
    {
        AudioClip clip = GetClip(clipName);
        if (clip == null) return;

        AudioSource source = GetAvailableSFXSource();
        source.clip = clip;
        source.volume = volume * sfxVolume * masterVolume;
        source.spatialBlend = 0f; // 2D
        source.Play();
    }

    public void PlayUISound(string clipName)
    {
        AudioClip clip = GetClip(clipName);
        if (clip == null) return;

        uiSource.PlayOneShot(clip, sfxVolume * masterVolume);
    }

    private AudioSource GetAvailableSFXSource()
    {
        AudioSource source = sfxPool[currentSfxIndex];
        currentSfxIndex = (currentSfxIndex + 1) % sfxPool.Count;
        return source;
    }

    #endregion

    #region Radio de Época

    public void PlayRadioTransmission(string transmissionId)
    {
        AudioClip clip = GetClip($"radio_{transmissionId}");
        if (clip == null) return;

        radioSource.clip = clip;
        radioSource.volume = sfxVolume * masterVolume * 0.6f;
        radioSource.Play();

        // Efecto de radio (filtro pasa-banda)
        // Aplicar AudioLowPassFilter y AudioHighPassFilter
    }

    public void StopRadio()
    {
        radioSource.Stop();
    }

    #endregion

    #region Handlers de Eventos

    private void HandleGameStateChange(GameState state)
    {
        switch (state)
        {
            case GameState.InDialogue:
                TransitionMusic(MusicState.Dialogue);
                break;
            case GameState.Paused:
                musicSource.Pause();
                ambientSource.Pause();
                break;
            case GameState.Playing:
                if (!musicSource.isPlaying) musicSource.UnPause();
                if (!ambientSource.isPlaying) ambientSource.UnPause();
                break;
        }
    }

    private void HandleSceneTransition(string sceneName)
    {
        SetAmbientPreset(GetPresetForScene(sceneName));
        TransitionMusic(MusicState.Exploration);
    }

    private void HandleWeatherChange(WeatherType weather)
    {
        switch (weather)
        {
            case WeatherType.Rain:
                PlayWeatherLoop("weather_rain_light");
                break;
            case WeatherType.HeavyRain:
                PlayWeatherLoop("weather_rain_heavy");
                break;
            case WeatherType.Thunderstorm:
                PlayWeatherLoop("weather_rain_heavy");
                StartCoroutine(RandomThunder());
                break;
            case WeatherType.Clear:
            case WeatherType.Cloudy:
                StopWeatherLoop();
                break;
        }
    }

    private void PlayWeatherLoop(string clipName)
    {
        AudioClip clip = GetClip(clipName);
        if (clip == null) return;

        weatherSource.clip = clip;
        weatherSource.loop = true;
        weatherSource.volume = ambientVolume * masterVolume;
        weatherSource.Play();
    }

    private void StopWeatherLoop()
    {
        StartCoroutine(FadeOut(weatherSource, 3f));
    }

    private IEnumerator RandomThunder()
    {
        while (WeatherSystem.Instance?.CurrentWeather == WeatherType.Thunderstorm)
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(5f, 20f));
            PlaySFX2D("weather_thunder", UnityEngine.Random.Range(0.5f, 1f));

            // Flash de relámpago
            // LightningFlash();
        }
    }

    private IEnumerator FadeOut(AudioSource source, float duration)
    {
        float startVol = source.volume;
        float elapsed = 0;
        while (elapsed < duration)
        {
            source.volume = Mathf.Lerp(startVol, 0, elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        source.Stop();
    }

    #endregion

    #region Utilidades

    private AudioClip GetClip(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        if (loadedClips.ContainsKey(name))
            return loadedClips[name];

        AudioClip clip = Resources.Load<AudioClip>($"Audio/{name}");
        if (clip != null)
            loadedClips[name] = clip;

        return clip;
    }

    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        UpdateAllVolumes();
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        musicSource.volume = musicVolume * masterVolume;
    }

    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
    }

    public void SetAmbientVolume(float volume)
    {
        ambientVolume = Mathf.Clamp01(volume);
        ambientSource.volume = ambientVolume * masterVolume;
    }

    private void UpdateAllVolumes()
    {
        musicSource.volume = musicVolume * masterVolume;
        ambientSource.volume = ambientVolume * masterVolume;
    }

    #endregion
}

// === DATOS DE AUDIO ===

public enum MusicState
{
    Exploration,
    Combat,
    Stealth,
    Dialogue,
    Boss,
    Emotional,
    Victory,
    GameOver
}

[System.Serializable]
public class AmbientPreset
{
    public string Name;
    public string AmbientClipName;
    public float VolumeMultiplier = 1f;
    public string[] RandomSounds;
    public float RandomSoundInterval = 10f;
    public float RandomSoundVariance = 5f;
}
