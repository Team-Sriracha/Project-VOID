using UnityEngine;

/// <summary>
/// 씬 진입 시 해당 씬의 BGM을 요청합니다.
/// </summary>
public class SceneAudioController : MonoBehaviour
{
    #region Serialized Fields

    [Header("BGM")]
    [SerializeField] private AudioCue _bgmCue;
    [SerializeField] private float _fadeDuration = 0.35f;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        if (_bgmCue == null)
        {
            return;
        }

        AudioManager.Instance?.PlayBgm(_bgmCue, _fadeDuration);
    }

    #endregion
}
