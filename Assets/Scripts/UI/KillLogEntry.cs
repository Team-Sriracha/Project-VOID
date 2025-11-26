using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// 단일 킬로그 UI 엔트리
/// </summary>
public class KillLogEntry : MonoBehaviour
{
    #region Serialized Fields

    [SerializeField] private TextMeshProUGUI _logText;
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private float _fadeOutTime = 5f;
    [SerializeField] private float _fadeDuration = 1f;

    #endregion

    #region Public Methods

    /// <summary>
    /// 킬로그를 초기화하고 페이드아웃을 시작합니다.
    /// </summary>
    public void Initialize(string killerName, string victimName)
    {
        if (_logText != null)
        {
            _logText.text = $"{killerName} killed {victimName}";
        }

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 1f;
        }

        StartCoroutine(FadeOutAndReturn());
    }

    #endregion

    #region Coroutines

    private IEnumerator FadeOutAndReturn()
    {
        // 일정 시간 대기
        yield return new WaitForSeconds(_fadeOutTime);

        // 페이드아웃
        if (_canvasGroup != null)
        {
            float elapsed = 0f;
            while (elapsed < _fadeDuration)
            {
                elapsed += Time.deltaTime;
                _canvasGroup.alpha = 1f - (elapsed / _fadeDuration);
                yield return null;
            }
        }

        // UIManager에 반환
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ReturnKillLogEntry(this);
        }
    }

    #endregion
}
