using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 마우스 호버 시 Fill Amount와 색상을 애니메이션하는 컴포넌트입니다.
/// DOTween을 사용하여 부드러운 전환 효과를 제공합니다.
/// </summary>
[RequireComponent(typeof(Image))]
[AddComponentMenu("UI/Effects/UIHoverFillEffect")]
public class UIHoverFillEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    #region Constants

    private const float DEFAULT_DURATION = 0.3f;
    private const float HOVER_FILL_AMOUNT = 1f;
    private const float NORMAL_FILL_AMOUNT = 0f;

    #endregion

    #region Serialized Fields

    [Header("호버 효과 설정")]
    [Tooltip("Fill Amount 애니메이션 지속 시간입니다.")]
    [SerializeField] private float _duration = DEFAULT_DURATION;

    [Tooltip("호버 시 이미지 색상입니다.")]
    [SerializeField] private Color _hoverColor = Color.white;

    [Tooltip("기본 이미지 색상입니다.")]
    [SerializeField] private Color _normalColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    [Tooltip("호버 시 텍스트 색상입니다.")]
    [SerializeField] private Color _hoverTextColor = Color.black;

    [Tooltip("기본 텍스트 색상입니다.")]
    [SerializeField] private Color _normalTextColor = Color.white;

    [Header("아이콘 효과 설정")]
    [Tooltip("호버 시 알파를 변경할 아이콘 이미지 목록입니다.")]
    [SerializeField] private Image[] _hoverIcons;

    [Tooltip("기본 아이콘 알파값입니다.")]
    [Range(0f, 1f)]
    [SerializeField] private float _normalIconAlpha = 0.08f;

    [Tooltip("호버 시 아이콘 알파값입니다.")]
    [Range(0f, 1f)]
    [SerializeField] private float _hoverIconAlpha = 0.3f;

    [Tooltip("애니메이션 이징 타입입니다.")]
    [SerializeField] private Ease _easeType = Ease.OutQuad;

    #endregion

    #region Private Fields

    private Image _targetImage;
    private TMP_Text[] _targetTexts;
    private Image[] _sidebarImages;
    private Sequence _currentSequence;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        InitializeComponents();
    }

    private void OnDestroy()
    {
        KillTween();
    }

    #endregion

    #region Initialization

    private void InitializeComponents()
    {
        _targetImage = GetComponent<Image>();

        // Image를 Filled 타입으로 설정
        if (_targetImage != null && _targetImage.type != Image.Type.Filled)
        {
            _targetImage.type = Image.Type.Filled;
            _targetImage.fillMethod = Image.FillMethod.Horizontal;
            _targetImage.fillOrigin = (int)Image.OriginHorizontal.Right;
        }

        // 자식 오브젝트에서 텍스트 찾기
        _targetTexts = GetComponentsInChildren<TMP_Text>();
        
        // 직접 지정한 Hover 아이콘은 별도 알파 제어를 위해 일반 이미지 처리에서 제외
        var allImages = GetComponentsInChildren<Image>();
        var filteredImages = new System.Collections.Generic.List<Image>();
        foreach (var img in allImages)
        {
            if (img != _targetImage && !IsHoverIcon(img))
            {
                filteredImages.Add(img);
            }
        }
        _sidebarImages = filteredImages.ToArray();

        // 초기 상태 설정
        ResetToNormal();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 마우스 포인터가 오브젝트 위에 올라갔을 때 호버 효과를 시작합니다.
    /// </summary>
    public void OnPointerEnter(PointerEventData eventData)
    {
        AnimateToHover();
    }

    /// <summary>
    /// 마우스 포인터가 오브젝트에서 벗어났을 때 호버 효과를 해제합니다.
    /// </summary>
    public void OnPointerExit(PointerEventData eventData)
    {
        AnimateToNormal();
    }

    #endregion

    #region Animation Methods

    /// <summary>
    /// 호버 상태로 애니메이션합니다.
    /// </summary>
    private void AnimateToHover()
    {
        if (_targetImage == null) return;

        KillTween();

        _currentSequence = DOTween.Sequence();
        _currentSequence.Append(_targetImage.DOFillAmount(HOVER_FILL_AMOUNT, _duration).SetEase(_easeType));
        _currentSequence.Join(_targetImage.DOColor(_hoverColor, _duration).SetEase(_easeType));
        
        // 텍스트 색상도 함께 전환
        if (_targetTexts != null)
        {
            foreach (var text in _targetTexts)
            {
                if (text != null)
                {
                    _currentSequence.Join(text.DOColor(_hoverTextColor, _duration).SetEase(_easeType));
                }
            }
        }

        // Sidebar 이미지 색상도 함께 전환 (자기 자신 제외)
        if (_sidebarImages != null)
        {
            foreach (var img in _sidebarImages)
            {
                if (img != null && img != _targetImage)
                {
                    _currentSequence.Join(img.DOColor(_hoverTextColor, _duration).SetEase(_easeType));
                }
            }
        }

        JoinIconAlphaTween(_hoverIconAlpha);
    }

    /// <summary>
    /// 일반 상태로 애니메이션합니다.
    /// </summary>
    private void AnimateToNormal()
    {
        if (_targetImage == null) return;

        KillTween();

        _currentSequence = DOTween.Sequence();
        _currentSequence.Append(_targetImage.DOFillAmount(NORMAL_FILL_AMOUNT, _duration).SetEase(_easeType));
        _currentSequence.Join(_targetImage.DOColor(_normalColor, _duration).SetEase(_easeType));
        
        // 텍스트 색상도 함께 전환
        if (_targetTexts != null)
        {
            foreach (var text in _targetTexts)
            {
                if (text != null)
                {
                    _currentSequence.Join(text.DOColor(_normalTextColor, _duration).SetEase(_easeType));
                }
            }
        }

        // Sidebar 이미지 색상도 함께 전환 (자기 자신 제외)
        if (_sidebarImages != null)
        {
            foreach (var img in _sidebarImages)
            {
                if (img != null && img != _targetImage)
                {
                    _currentSequence.Join(img.DOColor(_normalTextColor, _duration).SetEase(_easeType));
                }
            }
        }

        JoinIconAlphaTween(_normalIconAlpha);
    }

    /// <summary>
    /// 애니메이션 없이 즉시 일반 상태로 되돌립니다.
    /// </summary>
    private void ResetToNormal()
    {
        if (_targetImage == null) return;

        _targetImage.fillAmount = NORMAL_FILL_AMOUNT;
        _targetImage.color = _normalColor;

        if (_targetTexts != null)
        {
            foreach (var text in _targetTexts)
            {
                if (text != null)
                {
                    text.color = _normalTextColor;
                }
            }
        }

        if (_sidebarImages != null)
        {
            foreach (var img in _sidebarImages)
            {
                if (img != null && img != _targetImage)
                {
                    img.color = _normalTextColor;
                }
            }
        }

        SetHoverIconAlpha(_normalIconAlpha);
    }

    /// <summary>
    /// 실행 중인 Tween을 중지합니다.
    /// </summary>
    private void KillTween()
    {
        _currentSequence?.Kill();
        _currentSequence = null;
    }

    /// <summary>
    /// 지정한 이미지가 Hover 아이콘 목록에 포함되어 있는지 반환합니다.
    /// </summary>
    private bool IsHoverIcon(Image image)
    {
        if (image == null || _hoverIcons == null)
        {
            return false;
        }

        foreach (var hoverIcon in _hoverIcons)
        {
            if (hoverIcon == image)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Hover 아이콘들의 알파 애니메이션을 시퀀스에 추가합니다.
    /// </summary>
    /// <param name="targetAlpha">목표 알파값입니다.</param>
    private void JoinIconAlphaTween(float targetAlpha)
    {
        if (_hoverIcons == null || _currentSequence == null)
        {
            return;
        }

        foreach (var hoverIcon in _hoverIcons)
        {
            if (hoverIcon == null)
            {
                continue;
            }

            _currentSequence.Join(hoverIcon.DOFade(targetAlpha, _duration).SetEase(_easeType));
        }
    }

    /// <summary>
    /// Hover 아이콘 알파를 즉시 설정합니다.
    /// </summary>
    /// <param name="alpha">적용할 알파값입니다.</param>
    private void SetHoverIconAlpha(float alpha)
    {
        if (_hoverIcons == null)
        {
            return;
        }

        foreach (var hoverIcon in _hoverIcons)
        {
            if (hoverIcon == null)
            {
                continue;
            }

            Color iconColor = hoverIcon.color;
            iconColor.a = alpha;
            hoverIcon.color = iconColor;
        }
    }

    #endregion
}
