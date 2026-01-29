using UnityEngine;
using TMPro;
using System.Collections;
using DG.Tweening;

/// <summary>
/// 데미지 표시 UI
/// 월드 좌표를 스크린 좌표로 변환하여 표시
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class DamageIndicator : MonoBehaviour
{
    #region Constants

    private const float TOTAL_DURATION = 1.0f;
    private const float POP_DURATION = 0.15f;
    private const float FLOAT_DURATION = 0.55f;
    private const float FADE_DURATION = 0.3f;

    private const float POP_SCALE = 1.3f;
    private const float FLOAT_HEIGHT = 0.8f;  // World Y 오프셋

    #endregion

    #region Serialized Fields

    [SerializeField] private TextMeshProUGUI _damageText;
    [SerializeField] private CanvasGroup _canvasGroup;

    [Header("색상 설정")]
    [SerializeField] private Color _normalColor = Color.white;
    [SerializeField] private Color _hitColor = new Color(1f, 0.3f, 0.3f); // Red - 본인 피격 시
    [SerializeField] private Color _healColor = new Color(0.2f, 1f, 0.2f); // Green

    #endregion

    #region Private Fields

    private RectTransform _rectTransform;
    private Vector3 _baseWorldPosition;  // 기준 월드 좌표 (피격 위치)
    private float _currentYOffset;
    private System.Action<DamageIndicator> _onComplete;
    private Camera _mainCamera;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        
        if (_canvasGroup == null)
            _canvasGroup = GetComponent<CanvasGroup>();
    }

    private void LateUpdate()
    {
        // 월드 좌표 → 스크린 좌표 변환 (매 프레임)
        UpdateScreenPosition();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 인디케이터 초기화 및 애니메이션 시작
    /// </summary>
    /// <param name="worldPosition">피격 월드 좌표</param>
    /// <param name="damage">표시할 데미지 값</param>
    /// <param name="isHeal">회복 여부</param>
    /// <param name="isLocalHit">본인 피격 여부</param>
    /// <param name="onComplete">완료 시 콜백 (풀 반환용)</param>
    public void Initialize(Vector3 worldPosition, float damage, bool isHeal, bool isLocalHit, System.Action<DamageIndicator> onComplete)
    {
        _onComplete = onComplete;
        _baseWorldPosition = worldPosition;
        _mainCamera = Camera.main;
        _currentYOffset = 0f;

        // 텍스트 설정
        if (isHeal)
        {
            _damageText.text = $"+{Mathf.RoundToInt(damage)}";
            _damageText.color = _healColor;
        }
        else
        {
            _damageText.text = Mathf.RoundToInt(damage).ToString();
            // 본인 피격 시 피격 색상, 다른 대상은 기본 색상
            _damageText.color = isLocalHit ? _hitColor : _normalColor;
        }

        // 기본 폰트 크기
        _damageText.fontSize = 28;

        // 초기 상태 설정
        _rectTransform.localScale = Vector3.zero;
        _rectTransform.localRotation = Quaternion.identity;
        _canvasGroup.alpha = 1f;

        gameObject.SetActive(true);
        PlayAnimation();
    }

    /// <summary>
    /// 인디케이터 리셋 (풀 반환 시)
    /// </summary>
    public void Reset()
    {
        // 진행 중인 트윈 제거
        transform.DOKill();
        _canvasGroup.DOKill();
        
        gameObject.SetActive(false);
        _rectTransform.localScale = Vector3.zero;
        _canvasGroup.alpha = 1f;
    }

    #endregion

    #region Screen Position

    /// <summary>
    /// 월드 좌표를 스크린 좌표로 변환, UI 위치 업데이트
    /// 스크린 좌표 변환 후 X 오프셋 적용, 카메라 각도 무관 좌우 대칭 유지
    /// </summary>
    private void UpdateScreenPosition()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
        }

        // 월드 좌표 (Y 오프셋만 월드에서 적용)
        Vector3 worldPos = _baseWorldPosition + Vector3.up * _currentYOffset;
        
        // 월드 → 스크린 좌표 변환
        Vector3 screenPos = _mainCamera.WorldToScreenPoint(worldPos);

        // 카메라 뒤에 있으면 화면 밖으로 이동
        if (screenPos.z < 0)
        {
            _rectTransform.position = new Vector3(-1000, -1000, 0);
            return;
        }

        _rectTransform.position = new Vector3(screenPos.x, screenPos.y, 0);
    }

    #endregion

    #region Animation

    private void PlayAnimation()
    {
        // 1. 초기화
        _rectTransform.localScale = Vector3.zero;
        _canvasGroup.alpha = 1f;

        // 2. DOTween Sequence 생성
        Sequence seq = DOTween.Sequence();

        // 2.1 Pop (커지면서 위로 살짝)
        seq.Append(_rectTransform.DOScale(POP_SCALE, POP_DURATION).SetEase(Ease.OutBack));
        seq.Join(DOVirtual.Float(0, 0.1f, POP_DURATION, v => _currentYOffset = v));

        // 2.2 Float (천천히 위로 + 원래 크기로)
        seq.Append(_rectTransform.DOScale(1f, FLOAT_DURATION).SetEase(Ease.OutQuad));
        seq.Join(DOVirtual.Float(0.1f, 0.1f + FLOAT_HEIGHT, FLOAT_DURATION, v => _currentYOffset = v).SetEase(Ease.OutQuad));

        // 2.3 Fade (사라지면서 위로 계속 이동)
        seq.Append(_canvasGroup.DOFade(0f, FADE_DURATION));
        seq.Join(DOVirtual.Float(0.1f + FLOAT_HEIGHT, 0.1f + FLOAT_HEIGHT + 0.3f, FADE_DURATION, v => _currentYOffset = v));

        // 3. 완료 시 콜백
        seq.OnComplete(() =>
        {
            gameObject.SetActive(false);
            _onComplete?.Invoke(this);
        });
    }

    #endregion
}
