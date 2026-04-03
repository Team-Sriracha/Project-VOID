using System;
using UnityEngine;

/// <summary>
/// 개별 오디오 재생 설정과 클립 묶음을 정의합니다.
/// </summary>
public enum AudioChannel
{
    Bgm,
    Ui,
    Sfx
}

/// <summary>
/// 단일/랜덤 오디오 클립과 기본 재생 파라미터를 정의합니다.
/// </summary>
[CreateAssetMenu(fileName = "AudioCue", menuName = "Project VOID/Audio/Audio Cue")]
public class AudioCue : ScriptableObject
{
    #region Serialized Fields

    [Header("클립")]
    [SerializeField] private AudioClip[] _clips = Array.Empty<AudioClip>();

    [Header("채널")]
    [SerializeField] private AudioChannel _channel = AudioChannel.Sfx;

    [Header("기본 재생 설정")]
    [SerializeField] [Range(0f, 1f)] private float _volume = 1f;
    [SerializeField] private Vector2 _pitchRange = Vector2.one;
    [SerializeField] [Range(0f, 1f)] private float _spatialBlend = 0f;
    [SerializeField] private bool _loop;

    [Header("3D 사운드")]
    [SerializeField] private float _minDistance = 1f;
    [SerializeField] private float _maxDistance = 20f;

    #endregion

    #region Properties

    public AudioChannel Channel => _channel;
    public float Volume => _volume;
    public float SpatialBlend => _spatialBlend;
    public bool Loop => _loop;
    public float MinDistance => _minDistance;
    public float MaxDistance => Mathf.Max(_minDistance, _maxDistance);

    #endregion

    #region Public Methods

    /// <summary>
    /// 재생 가능한 클립을 하나 반환합니다.
    /// </summary>
    public bool TryGetClip(out AudioClip clip)
    {
        clip = null;

        if (_clips == null || _clips.Length == 0)
        {
            return false;
        }

        int clipCount = _clips.Length;
        if (clipCount == 1)
        {
            clip = _clips[0];
            return clip != null;
        }

        int startIndex = UnityEngine.Random.Range(0, clipCount);
        for (int i = 0; i < clipCount; i++)
        {
            int index = (startIndex + i) % clipCount;
            AudioClip candidate = _clips[index];
            if (candidate == null)
            {
                continue;
            }

            clip = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 랜덤 피치를 반환합니다.
    /// </summary>
    public float GetRandomPitch()
    {
        float minPitch = Mathf.Max(0.01f, _pitchRange.x);
        float maxPitch = Mathf.Max(minPitch, _pitchRange.y);
        return UnityEngine.Random.Range(minPitch, maxPitch);
    }

    #endregion
}
