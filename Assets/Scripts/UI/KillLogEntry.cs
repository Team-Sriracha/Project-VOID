using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 단일 킬로그 UI 엔트리
/// 킬러 이름 - 무기 아이콘 - 피해자 이름 형식으로 표시
/// </summary>
public class KillLogEntry : MonoBehaviour
{
    #region Serialized Fields

    [Header("UI 요소")]
    [Tooltip("공격자 이름 텍스트")]
    [SerializeField] private TextMeshProUGUI _killerNameText;

    [Tooltip("무기 아이콘 이미지")]
    [SerializeField] private Image _weaponIcon;

    [Tooltip("피해자 이름 텍스트")]
    [SerializeField] private TextMeshProUGUI _victimNameText;

    [Header("페이드 설정")]
    [SerializeField] private CanvasGroup _canvasGroup;
    [SerializeField] private float _fadeOutTime = 5f;
    [SerializeField] private float _fadeDuration = 1f;

    [Header("기본 아이콘")]
    [Tooltip("무기 아이콘이 없을 때 사용할 기본 아이콘")]
    [SerializeField] private Sprite _defaultWeaponIcon;

    #endregion

    #region Public Methods

    /// <summary>
    /// 킬로그를 초기화하고 페이드아웃을 시작합니다.
    /// </summary>
    /// <param name="killerName">공격자 이름</param>
    /// <param name="victimName">피해자 이름</param>
    /// <param name="weaponIcon">무기 아이콘 (null이면 기본 아이콘 사용)</param>
    public void Initialize(string killerName, string victimName, Sprite weaponIcon = null)
    {
        // 공격자 이름 설정
        if (_killerNameText != null)
        {
            _killerNameText.text = killerName;
        }

        // 피해자 이름 설정
        if (_victimNameText != null)
        {
            _victimNameText.text = victimName;
        }

        // 무기 아이콘 설정
        if (_weaponIcon != null)
        {
            // Why: weaponIcon이 있으면 사용, 없으면 기본 아이콘 사용
            Sprite iconToUse = weaponIcon != null ? weaponIcon : _defaultWeaponIcon;
            
            if (iconToUse != null)
            {
                _weaponIcon.sprite = iconToUse;
                _weaponIcon.gameObject.SetActive(true);
            }
            else
            {
                // Why: 아이콘이 전혀 없으면 숨김
                _weaponIcon.gameObject.SetActive(false);
            }
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
