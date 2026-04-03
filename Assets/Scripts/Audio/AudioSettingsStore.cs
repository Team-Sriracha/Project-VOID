using UnityEngine;

/// <summary>
/// 오디오 볼륨 설정 저장/로드를 담당합니다.
/// </summary>
public static class AudioSettingsStore
{
    #region Constants

    private const string KEY_MASTER_VOLUME = "audio.master";
    private const string KEY_BGM_VOLUME = "audio.bgm";
    private const string KEY_UI_VOLUME = "audio.ui";
    private const string KEY_SFX_VOLUME = "audio.sfx";

    #endregion

    #region Types

    public readonly struct AudioVolumeSettings
    {
        public float MasterVolume { get; }
        public float BgmVolume { get; }
        public float UiVolume { get; }
        public float SfxVolume { get; }

        public AudioVolumeSettings(float masterVolume, float bgmVolume, float uiVolume, float sfxVolume)
        {
            MasterVolume = Mathf.Clamp01(masterVolume);
            BgmVolume = Mathf.Clamp01(bgmVolume);
            UiVolume = Mathf.Clamp01(uiVolume);
            SfxVolume = Mathf.Clamp01(sfxVolume);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 저장된 볼륨 설정을 불러옵니다.
    /// </summary>
    public static AudioVolumeSettings Load()
    {
        return new AudioVolumeSettings(
            PlayerPrefs.GetFloat(KEY_MASTER_VOLUME, 1f),
            PlayerPrefs.GetFloat(KEY_BGM_VOLUME, 1f),
            PlayerPrefs.GetFloat(KEY_UI_VOLUME, 1f),
            PlayerPrefs.GetFloat(KEY_SFX_VOLUME, 1f));
    }

    /// <summary>
    /// 볼륨 설정을 저장합니다.
    /// </summary>
    public static void Save(float masterVolume, float bgmVolume, float uiVolume, float sfxVolume)
    {
        PlayerPrefs.SetFloat(KEY_MASTER_VOLUME, Mathf.Clamp01(masterVolume));
        PlayerPrefs.SetFloat(KEY_BGM_VOLUME, Mathf.Clamp01(bgmVolume));
        PlayerPrefs.SetFloat(KEY_UI_VOLUME, Mathf.Clamp01(uiVolume));
        PlayerPrefs.SetFloat(KEY_SFX_VOLUME, Mathf.Clamp01(sfxVolume));
        PlayerPrefs.Save();
    }

    #endregion
}
