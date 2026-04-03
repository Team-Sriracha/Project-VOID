using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 프로젝트 전체의 BGM/UI/SFX 재생을 담당하는 지속형 오디오 매니저입니다.
/// </summary>
public class AudioManager : MonoBehaviour
{
    #region Types

    private sealed class AttachedPlayback
    {
        public AudioSource Source;
        public Transform Target;
        public Vector3 LocalOffset;
    }

    #endregion

    #region Constants

    private const int INITIAL_WORLD_SOURCE_COUNT = 8;
    private const float DEFAULT_BGM_FADE_DURATION = 0.35f;

    #endregion

    #region Singleton

    private static AudioManager _instance;

    /// <summary>
    /// 현재 오디오 매니저 인스턴스를 반환합니다.
    /// </summary>
    public static AudioManager Instance
    {
        get
        {
            if (_instance == null && !Application.isBatchMode)
            {
                _instance = FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);
                _instance?.EnsureBootstrapped();
            }

            return _instance;
        }
    }

    #endregion

    #region Private Fields

    private readonly List<AudioSource> _worldSources = new();
    private readonly List<AttachedPlayback> _attachedPlaybacks = new();

    private AudioSource _bgmPrimarySource;
    private AudioSource _bgmSecondarySource;
    private AudioSource _uiSource;
    private AudioSource _activeBgmSource;
    private AudioSource _inactiveBgmSource;
    private AudioCue _currentBgmCue;
    private Coroutine _bgmFadeCoroutine;

    private float _masterVolume = 1f;
    private float _bgmVolume = 1f;
    private float _uiVolume = 1f;
    private float _sfxVolume = 1f;
    private bool _isBootstrapped;

    #endregion

    #region Properties

    public float MasterVolume => _masterVolume;
    public float BgmVolume => _bgmVolume;
    public float UiVolume => _uiVolume;
    public float SfxVolume => _sfxVolume;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        EnsureBootstrapped();
    }

    private void Update()
    {
        UpdateAttachedPlaybacks();
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    #endregion

    #region Initialization

    private void EnsureBootstrapped()
    {
        if (_isBootstrapped)
        {
            return;
        }

        DontDestroyOnLoad(gameObject);
        InitializeSources();
        LoadSavedVolumes();
        _isBootstrapped = true;
    }

    private void InitializeSources()
    {
        _bgmPrimarySource = CreateChildSource("BgmPrimary");
        _bgmSecondarySource = CreateChildSource("BgmSecondary");
        _uiSource = CreateChildSource("Ui");

        _bgmPrimarySource.loop = true;
        _bgmSecondarySource.loop = true;
        _uiSource.spatialBlend = 0f;
        _uiSource.playOnAwake = false;

        _activeBgmSource = _bgmPrimarySource;
        _inactiveBgmSource = _bgmSecondarySource;

        for (int i = 0; i < INITIAL_WORLD_SOURCE_COUNT; i++)
        {
            _worldSources.Add(CreateChildSource($"World_{i + 1}"));
        }
    }

    private void LoadSavedVolumes()
    {
        AudioSettingsStore.AudioVolumeSettings settings = AudioSettingsStore.Load();
        _masterVolume = settings.MasterVolume;
        _bgmVolume = settings.BgmVolume;
        _uiVolume = settings.UiVolume;
        _sfxVolume = settings.SfxVolume;
    }

    private AudioSource CreateChildSource(string objectName)
    {
        GameObject child = new GameObject(objectName);
        child.transform.SetParent(transform, false);

        AudioSource source = child.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;
        return source;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// UI 사운드를 재생합니다.
    /// </summary>
    public void PlayUi(AudioCue cue)
    {
        if (!EnsureRuntimeReady())
        {
            return;
        }

        if (!TryResolveCue(cue, out AudioClip clip))
        {
            return;
        }

        _uiSource.pitch = cue.GetRandomPitch();
        _uiSource.PlayOneShot(clip, ResolvePlaybackVolume(cue));
    }

    /// <summary>
    /// 월드 좌표 기준 원샷 사운드를 재생합니다.
    /// </summary>
    public void PlayWorldOneShot(AudioCue cue, Vector3 worldPosition)
    {
        if (!EnsureRuntimeReady())
        {
            return;
        }

        if (!TryResolveCue(cue, out AudioClip clip))
        {
            return;
        }

        AudioSource source = GetAvailableWorldSource();
        ConfigureSourceFromCue(source, cue, clip);
        source.transform.position = worldPosition;
        source.Play();
    }

    /// <summary>
    /// 대상 위치를 추적하는 원샷 사운드를 재생합니다.
    /// </summary>
    public void PlayAttachedOneShot(AudioCue cue, Transform target, Vector3 localOffset = default)
    {
        if (!EnsureRuntimeReady())
        {
            return;
        }

        if (target == null)
        {
            return;
        }

        if (!TryResolveCue(cue, out AudioClip clip))
        {
            return;
        }

        AudioSource source = GetAvailableWorldSource();
        ConfigureSourceFromCue(source, cue, clip);
        source.transform.position = target.position + localOffset;
        source.Play();

        _attachedPlaybacks.Add(new AttachedPlayback
        {
            Source = source,
            Target = target,
            LocalOffset = localOffset
        });
    }

    /// <summary>
    /// BGM을 전환합니다.
    /// </summary>
    public void PlayBgm(AudioCue cue, float fadeDuration = DEFAULT_BGM_FADE_DURATION)
    {
        if (!EnsureRuntimeReady())
        {
            return;
        }

        if (cue == null)
        {
            StopBgm(fadeDuration);
            return;
        }

        if (_currentBgmCue == cue && _activeBgmSource != null && _activeBgmSource.isPlaying)
        {
            return;
        }

        if (!TryResolveCue(cue, out AudioClip clip))
        {
            return;
        }

        if (_bgmFadeCoroutine != null)
        {
            StopCoroutine(_bgmFadeCoroutine);
            _bgmFadeCoroutine = null;
        }

        ConfigureSourceFromCue(_inactiveBgmSource, cue, clip);
        _inactiveBgmSource.loop = true;
        _inactiveBgmSource.volume = 0f;
        _inactiveBgmSource.Play();

        _currentBgmCue = cue;
        _bgmFadeCoroutine = StartCoroutine(FadeBgmRoutine(_inactiveBgmSource, _activeBgmSource, fadeDuration));
    }

    /// <summary>
    /// 현재 BGM을 정지합니다.
    /// </summary>
    public void StopBgm(float fadeDuration = DEFAULT_BGM_FADE_DURATION)
    {
        if (!EnsureRuntimeReady())
        {
            return;
        }

        if (_activeBgmSource == null || !_activeBgmSource.isPlaying)
        {
            _currentBgmCue = null;
            return;
        }

        if (_bgmFadeCoroutine != null)
        {
            StopCoroutine(_bgmFadeCoroutine);
            _bgmFadeCoroutine = null;
        }

        _currentBgmCue = null;
        _bgmFadeCoroutine = StartCoroutine(FadeOutCurrentBgmRoutine(fadeDuration));
    }

    /// <summary>
    /// 볼륨 설정을 적용하고 저장합니다.
    /// </summary>
    public void ApplyVolumes(float masterVolume, float bgmVolume, float uiVolume, float sfxVolume, bool save = true)
    {
        if (!EnsureRuntimeReady())
        {
            return;
        }

        _masterVolume = Mathf.Clamp01(masterVolume);
        _bgmVolume = Mathf.Clamp01(bgmVolume);
        _uiVolume = Mathf.Clamp01(uiVolume);
        _sfxVolume = Mathf.Clamp01(sfxVolume);

        if (_activeBgmSource != null && _activeBgmSource.isPlaying)
        {
            _activeBgmSource.volume = ResolvePlaybackVolume(_currentBgmCue);
        }

        if (_inactiveBgmSource != null && _inactiveBgmSource.isPlaying && _inactiveBgmSource.clip != null)
        {
            _inactiveBgmSource.volume = ResolvePlaybackVolume(_currentBgmCue);
        }

        if (save)
        {
            AudioSettingsStore.Save(_masterVolume, _bgmVolume, _uiVolume, _sfxVolume);
        }
    }

    #endregion

    #region Bgm

    private IEnumerator FadeBgmRoutine(AudioSource nextSource, AudioSource previousSource, float fadeDuration)
    {
        float duration = Mathf.Max(0f, fadeDuration);
        float targetVolume = ResolvePlaybackVolume(_currentBgmCue);

        if (duration <= 0f)
        {
            nextSource.volume = targetVolume;

            if (previousSource != null)
            {
                previousSource.Stop();
                previousSource.volume = 0f;
            }

            SwapBgmSources();
            _bgmFadeCoroutine = null;
            yield break;
        }

        float previousStartVolume = previousSource != null ? previousSource.volume : 0f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            nextSource.volume = Mathf.Lerp(0f, targetVolume, t);

            if (previousSource != null)
            {
                previousSource.volume = Mathf.Lerp(previousStartVolume, 0f, t);
            }

            yield return null;
        }

        nextSource.volume = targetVolume;

        if (previousSource != null)
        {
            previousSource.Stop();
            previousSource.volume = 0f;
        }

        SwapBgmSources();
        _bgmFadeCoroutine = null;
    }

    private IEnumerator FadeOutCurrentBgmRoutine(float fadeDuration)
    {
        AudioSource source = _activeBgmSource;
        if (source == null)
        {
            _bgmFadeCoroutine = null;
            yield break;
        }

        float duration = Mathf.Max(0f, fadeDuration);
        if (duration <= 0f)
        {
            source.Stop();
            source.volume = 0f;
            _bgmFadeCoroutine = null;
            yield break;
        }

        float startVolume = source.volume;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            source.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
        }

        source.Stop();
        source.volume = 0f;
        _bgmFadeCoroutine = null;
    }

    private void SwapBgmSources()
    {
        AudioSource previousActive = _activeBgmSource;
        _activeBgmSource = _inactiveBgmSource;
        _inactiveBgmSource = previousActive;
    }

    #endregion

    #region Helpers

    private bool EnsureRuntimeReady()
    {
        if (_instance == null)
        {
            _instance = FindFirstObjectByType<AudioManager>(FindObjectsInactive.Include);
        }

        if (_instance == null)
        {
            return false;
        }

        if (!_instance._isBootstrapped)
        {
            _instance.EnsureBootstrapped();
        }

        return true;
    }

    private bool TryResolveCue(AudioCue cue, out AudioClip clip)
    {
        clip = null;

        if (cue == null)
        {
            return false;
        }

        return cue.TryGetClip(out clip);
    }

    private AudioSource GetAvailableWorldSource()
    {
        for (int i = 0; i < _worldSources.Count; i++)
        {
            AudioSource source = _worldSources[i];
            if (!source.isPlaying)
            {
                return source;
            }
        }

        AudioSource createdSource = CreateChildSource($"World_{_worldSources.Count + 1}");
        _worldSources.Add(createdSource);
        return createdSource;
    }

    private void ConfigureSourceFromCue(AudioSource source, AudioCue cue, AudioClip clip)
    {
        source.Stop();
        source.clip = clip;
        source.loop = cue.Loop;
        source.pitch = cue.GetRandomPitch();
        source.spatialBlend = cue.SpatialBlend;
        source.minDistance = cue.MinDistance;
        source.maxDistance = cue.MaxDistance;
        source.volume = ResolvePlaybackVolume(cue);
    }

    private float ResolvePlaybackVolume(AudioCue cue)
    {
        if (cue == null)
        {
            return 0f;
        }

        float channelVolume = cue.Channel switch
        {
            AudioChannel.Bgm => _bgmVolume,
            AudioChannel.Ui => _uiVolume,
            _ => _sfxVolume
        };

        return _masterVolume * channelVolume * cue.Volume;
    }

    private void UpdateAttachedPlaybacks()
    {
        for (int i = _attachedPlaybacks.Count - 1; i >= 0; i--)
        {
            AttachedPlayback playback = _attachedPlaybacks[i];
            if (playback == null || playback.Source == null || !playback.Source.isPlaying)
            {
                _attachedPlaybacks.RemoveAt(i);
                continue;
            }

            if (playback.Target == null)
            {
                playback.Source.Stop();
                _attachedPlaybacks.RemoveAt(i);
                continue;
            }

            playback.Source.transform.position = playback.Target.position + playback.LocalOffset;
        }
    }

    #endregion
}
