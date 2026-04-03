using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UISwitchButtonExpander의 확장/축소 이벤트에 반응하여 색상을 전환하는 컴포넌트입니다.
/// DOTween을 사용하여 부드러운 색상 전환 효과를 제공합니다.
/// </summary>
[RequireComponent(typeof(Graphic))]
[AddComponentMenu("UI/Effects/UIExpanderColorReactor")]
public class UIExpanderColorReactor : MonoBehaviour
{
    #region Constants

    private const float DEFAULT_DURATION = 0.3f;

    #endregion

    #region Serialized Fields

    [Header("색상 설정")]
    [Tooltip("축소 상태일 때 색상입니다.")]
    [SerializeField] private Color _collapsedColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    [Tooltip("확장 상태일 때 색상입니다.")]
    [SerializeField] private Color _expandedColor = Color.white;

    [Header("애니메이션 설정")]
    [Tooltip("색상 전환 지속 시간입니다.")]
    [SerializeField] private float _duration = DEFAULT_DURATION;

    [Tooltip("애니메이션 이징 타입입니다.")]
    [SerializeField] private Ease _easeType = Ease.OutQuad;

    [Header("연결 설정")]
    [Tooltip("확장/축소 이벤트를 받을 UISwitchButtonExpander입니다. (비워두면 부모에서 자동 검색)")]
    [SerializeField] private UISwitchButtonExpander _targetExpander;

    #endregion

    #region Private Fields

    private Graphic _targetGraphic;
    private Tween _colorTween;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        InitializeComponents();
    }

    private void Start()
    {
        RegisterEventHandlers();
    }

    private void OnDestroy()
    {
        UnregisterEventHandlers();
        KillTween();
    }

    #endregion

    #region Initialization

    private void InitializeComponents()
    {
        _targetGraphic = GetComponent<Graphic>();

        // Target Expander가 지정되지 않았으면 부모에서 찾기
        if (_targetExpander == null)
        {
            _targetExpander = GetComponentInParent<UISwitchButtonExpander>();
        }

        // 초기 색상 설정
        if (_targetGraphic != null)
        {
            _targetGraphic.color = _collapsedColor;
        }
    }

    private void RegisterEventHandlers()
    {
        if (_targetExpander != null)
        {
            _targetExpander.OnExpandEvent.AddListener(OnExpand);
            _targetExpander.OnCollapseEvent.AddListener(OnCollapse);
        }
    }

    private void UnregisterEventHandlers()
    {
        if (_targetExpander != null)
        {
            _targetExpander.OnExpandEvent.RemoveListener(OnExpand);
            _targetExpander.OnCollapseEvent.RemoveListener(OnCollapse);
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// 확장 이벤트 발생 시 확장 색상으로 전환합니다.
    /// </summary>
    private void OnExpand()
    {
        AnimateToColor(_expandedColor);
    }

    /// <summary>
    /// 축소 이벤트 발생 시 축소 색상으로 전환합니다.
    /// </summary>
    private void OnCollapse()
    {
        AnimateToColor(_collapsedColor);
    }

    #endregion

    #region Animation Methods

    /// <summary>
    /// 지정된 색상으로 애니메이션합니다.
    /// </summary>
    /// <param name="targetColor">목표 색상</param>
    private void AnimateToColor(Color targetColor)
    {
        if (_targetGraphic == null) return;

        KillTween();

        _colorTween = _targetGraphic.DOColor(targetColor, _duration)
            .SetEase(_easeType);
    }

    /// <summary>
    /// 실행 중인 Tween을 중지합니다.
    /// </summary>
    private void KillTween()
    {
        _colorTween?.Kill();
        _colorTween = null;
    }

    #endregion
}
