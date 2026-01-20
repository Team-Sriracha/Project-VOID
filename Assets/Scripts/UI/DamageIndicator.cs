using UnityEngine;
using TMPro;
using System.Collections;

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
    private const float RANDOM_X_RANGE = 0.3f;  // World X 오프셋
    private const float RANDOM_ROTATION_RANGE = 15f;

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
    private float _randomXOffset;
    private float _randomRotation;
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
        
        // 랜덤 오프셋 설정 (캐주얼 느낌)
        _randomXOffset = Random.Range(-RANDOM_X_RANGE, RANDOM_X_RANGE);
        _randomRotation = Random.Range(-RANDOM_ROTATION_RANGE, RANDOM_ROTATION_RANGE);

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
        _rectTransform.localRotation = Quaternion.Euler(0, 0, _randomRotation);
        _canvasGroup.alpha = 1f;

        gameObject.SetActive(true);
        StartCoroutine(AnimateCoroutine());
    }

    /// <summary>
    /// 인디케이터 리셋 (풀 반환 시)
    /// </summary>
    public void Reset()
    {
        StopAllCoroutines();
        gameObject.SetActive(false);
        _rectTransform.localScale = Vector3.zero;
        _canvasGroup.alpha = 1f;
    }

    #endregion

    #region Constants - Screen Offset

    // 스크린 공간에서의 X 오프셋 (픽셀 단위)
    private const float SCREEN_X_OFFSET_RANGE = 30f;

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

        // 스크린 공간에서 X 오프셋 적용 (픽셀 단위로 좌우 대칭)
        float screenXOffset = _randomXOffset * (SCREEN_X_OFFSET_RANGE / RANDOM_X_RANGE);
        _rectTransform.position = new Vector3(screenPos.x + screenXOffset, screenPos.y, 0);
    }

    #endregion

    #region Animation

    private IEnumerator AnimateCoroutine()
    {
        float elapsed = 0f;

        // Phase 1: Pop (0 ~ 0.15s)
        while (elapsed < POP_DURATION)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / POP_DURATION;
            
            // EaseOutBack 효과
            float scale = EaseOutBack(t) * POP_SCALE;
            _rectTransform.localScale = Vector3.one * scale;
            
            // 살짝 위로 이동
            _currentYOffset = t * 0.1f;
            
            yield return null;
        }

        // Phase 2: Float (0.15 ~ 0.7s)
        while (elapsed < POP_DURATION + FLOAT_DURATION)
        {
            elapsed += Time.deltaTime;
            float t = (elapsed - POP_DURATION) / FLOAT_DURATION;
            
            // 스케일 미세 축소
            float scale = Mathf.Lerp(POP_SCALE, 1f, t);
            _rectTransform.localScale = Vector3.one * scale;
            
            // 위로 떠오르는 효과 (감속)
            _currentYOffset = 0.1f + EaseOutQuad(t) * FLOAT_HEIGHT;
            
            yield return null;
        }

        // Phase 3: Fade (0.7 ~ 1.0s)
        while (elapsed < TOTAL_DURATION)
        {
            elapsed += Time.deltaTime;
            float t = (elapsed - POP_DURATION - FLOAT_DURATION) / FADE_DURATION;
            
            _canvasGroup.alpha = 1f - t;
            
            // 계속 위로 이동
            _currentYOffset = 0.1f + FLOAT_HEIGHT + t * 0.3f;
            
            yield return null;
        }

        // 완료
        _canvasGroup.alpha = 0f;
        gameObject.SetActive(false);
        _onComplete?.Invoke(this);
    }

    #endregion

    #region Easing Functions

    private float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    private float EaseOutQuad(float t)
    {
        return 1f - (1f - t) * (1f - t);
    }

    #endregion
}
