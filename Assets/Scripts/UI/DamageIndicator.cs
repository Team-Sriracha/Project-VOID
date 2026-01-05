using UnityEngine;
using TMPro;
using System.Collections;

/// <summary>
/// 개별 데미지 인디케이터 UI 컴포넌트.
/// PlayerOverheadUI와 동일한 Screen Space Overlay 방식 사용.
/// 월드 좌표를 스크린 좌표로 변환하여 표시합니다.
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
    /// 인디케이터를 초기화하고 애니메이션을 시작합니다.
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
            // Why: 본인이 맞은 경우 피격 색상, 다른 대상은 기본 색상
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
    /// 인디케이터를 리셋합니다 (풀 반환 시).
    /// </summary>
    public void Reset()
    {
        StopAllCoroutines();
        gameObject.SetActive(false);
        _rectTransform.localScale = Vector3.zero;
        _canvasGroup.alpha = 1f;
    }

    #endregion

    #region Screen Position

    /// <summary>
    /// 월드 좌표를 스크린 좌표로 변환하여 UI 위치를 업데이트합니다.
    /// PlayerOverheadUI와 동일한 방식.
    /// </summary>
    private void UpdateScreenPosition()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
        }

        // 카메라 right 벡터를 사용하여 X 오프셋 적용 (카메라 시점 기준 좌우)
        Vector3 cameraRight = _mainCamera.transform.right;
        Vector3 worldPos = _baseWorldPosition 
            + cameraRight * _randomXOffset  // 카메라 기준 좌우
            + Vector3.up * _currentYOffset;  // 위로 떠오름
        
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
