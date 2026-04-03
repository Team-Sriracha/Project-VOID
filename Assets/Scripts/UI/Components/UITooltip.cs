using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 마우스 호버 시 툴팁을 표시하는 컴포넌트입니다.
/// DOTween을 사용하여 FadeIn/FadeOut 애니메이션을 제공합니다.
/// </summary>
[AddComponentMenu("UI/Interaction/UITooltip")]
public class UITooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    #region Enums

    public enum TooltipPosition
    {
        Top,
        Bottom,
        Left,
        Right
    }

    #endregion

    #region Constants

    private const float FADE_IN_DURATION = 0.2f;
    private const float FADE_OUT_DURATION = 0.2f;

    #endregion

    #region Serialized Fields

    [Header("툴팁 설정")]
    [Tooltip("생성할 툴팁 프리팹입니다.")]
    [SerializeField] private GameObject _tooltipPrefab;

    [Tooltip("트리거 대비 툴팁의 위치입니다.")]
    [SerializeField] private TooltipPosition _position = TooltipPosition.Bottom;

    [Tooltip("버튼 중심/가장자리로부터의 오프셋(픽셀 단위)입니다.")]
    [SerializeField] private float _offset = 20f;

    [TextArea] 
    [Tooltip("툴팁에 표시할 텍스트입니다.")]
    [SerializeField] private string _tooltipText;

    #endregion

    #region Private Fields

    private GameObject _currentTooltip;
    private CanvasGroup _currentCanvasGroup;
    private Tween _fadeTween;

    #endregion

    #region Unity Lifecycle

    private void OnDisable()
    {
        DestroyTooltipImmediately();
    }

    private void OnDestroy()
    {
        KillTween();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 마우스 포인터가 오브젝트 위에 올라갔을 때 툴팁을 표시합니다.
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_tooltipPrefab != null && _currentTooltip == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            // 툴팁 프리팹 생성
            _currentTooltip = Instantiate(_tooltipPrefab, canvas.transform);

            // 위치 계산
            PositionTooltip();

            // Pivot 설정
            SetTooltipPivot();

            // 렌더 순서를 최상위로 설정
            _currentTooltip.transform.SetAsLastSibling();

            // CanvasGroup 가져오기
            _currentCanvasGroup = _currentTooltip.GetComponent<CanvasGroup>();
            if (_currentCanvasGroup == null)
            {
                Debug.LogWarning($"[UITooltip] Tooltip 프리팹에 CanvasGroup이 없습니다! GameObject: {_currentTooltip.name}");
                Destroy(_currentTooltip);
                _currentTooltip = null;
                return;
            }

            // 초기 알파값 0으로 설정
            _currentCanvasGroup.alpha = 0f;

            // 자식에서 TMP_Text 찾아서 텍스트 설정
            TMP_Text textComponent = _currentTooltip.GetComponentInChildren<TMP_Text>();
            if (textComponent != null)
            {
                textComponent.text = _tooltipText;
            }

            // FadeIn 애니메이션
            FadeIn();
        }
    }

    /// <summary>
    /// 마우스 포인터가 오브젝트에서 벗어났을 때 툴팁을 숨깁니다.
    /// </summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        HideTooltip();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 툴팁의 위치를 계산하여 설정합니다.
    /// </summary>
    private void PositionTooltip()
    {
        if (_currentTooltip == null) return;

        RectTransform triggerRect = GetComponent<RectTransform>();
        
        // 트리거 중심에서 시작
        _currentTooltip.transform.position = transform.position;
        Vector3 localPos = _currentTooltip.transform.localPosition;

        // 트리거 크기 기반 오프셋 계산
        float width = triggerRect != null ? triggerRect.rect.width : 0;
        float height = triggerRect != null ? triggerRect.rect.height : 0;

        switch (_position)
        {
            case TooltipPosition.Top:
                localPos.y += (height * 0.5f) + _offset;
                break;
            case TooltipPosition.Bottom:
                localPos.y -= (height * 0.5f) + _offset;
                break;
            case TooltipPosition.Left:
                localPos.x -= (width * 0.5f) + _offset;
                break;
            case TooltipPosition.Right:
                localPos.x += (width * 0.5f) + _offset;
                break;
        }

        _currentTooltip.transform.localPosition = localPos;
    }

    /// <summary>
    /// 툴팁의 Pivot을 설정하여 버튼으로부터 멀어지도록 합니다.
    /// </summary>
    private void SetTooltipPivot()
    {
        if (_currentTooltip == null) return;

        RectTransform tooltipRect = _currentTooltip.GetComponent<RectTransform>();
        if (tooltipRect == null) return;

        switch (_position)
        {
            case TooltipPosition.Top:
                tooltipRect.pivot = new Vector2(0.5f, 0f);
                break;
            case TooltipPosition.Bottom:
                tooltipRect.pivot = new Vector2(0.5f, 1f);
                break;
            case TooltipPosition.Left:
                tooltipRect.pivot = new Vector2(1f, 0.5f);
                break;
            case TooltipPosition.Right:
                tooltipRect.pivot = new Vector2(0f, 0.5f);
                break;
        }
    }

    /// <summary>
    /// FadeOut 애니메이션과 함께 툴팁을 숨깁니다.
    /// </summary>
    private void HideTooltip()
    {
        if (_currentTooltip == null) return;

        FadeOut();
    }

    /// <summary>
    /// 애니메이션 없이 즉시 툴팁을 제거합니다.
    /// </summary>
    private void DestroyTooltipImmediately()
    {
        if (_currentTooltip != null)
        {
            KillTween();
            Destroy(_currentTooltip);
            _currentTooltip = null;
            _currentCanvasGroup = null;
        }
    }

    #endregion

    #region Animation Methods

    /// <summary>
    /// 툴팁을 페이드 인합니다.
    /// </summary>
    private void FadeIn()
    {
        if (_currentCanvasGroup == null) return;

        KillTween();

        _fadeTween = _currentCanvasGroup.DOFade(1f, FADE_IN_DURATION)
            .SetEase(Ease.OutQuad);
    }

    /// <summary>
    /// 툴팁을 페이드 아웃하고 완료 시 GameObject를 삭제합니다.
    /// </summary>
    private void FadeOut()
    {
        if (_currentCanvasGroup == null)
        {
            Destroy(_currentTooltip);
            _currentTooltip = null;
            return;
        }

        KillTween();

        _fadeTween = _currentCanvasGroup.DOFade(0f, FADE_OUT_DURATION)
            .SetEase(Ease.InQuad)
            .OnComplete(() =>
            {
                if (_currentTooltip != null)
                {
                    Destroy(_currentTooltip);
                    _currentTooltip = null;
                    _currentCanvasGroup = null;
                }
            });
    }

    /// <summary>
    /// 실행 중인 Tween을 중지합니다.
    /// </summary>
    private void KillTween()
    {
        _fadeTween?.Kill();
        _fadeTween = null;
    }

    #endregion
}
