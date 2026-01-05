using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 몹 오버헤드 UI를 관리합니다 (이름, HP 바).
/// PlayerOverheadUI와 동일한 Screen Space Overlay 방식 사용.
/// </summary>
public class MobOverheadUI : MonoBehaviour
{
    #region Serialized Fields

    [Header("HP")]
    [Tooltip("HP 바 Slider")]
    [SerializeField] private Slider _hpBar;

    [Header("이름")]
    [Tooltip("몹 이름 텍스트")]
    [SerializeField] private TextMeshProUGUI _nameText;

    [Header("Screen Space 설정")]
    [Tooltip("몹 머리 위 월드 오프셋 (Y축)")]
    [SerializeField] private float _headOffset = 2.5f;

    [Header("성능 최적화")]
    [Tooltip("UI 정보 업데이트 간격 (초)")]
    [SerializeField] private float _uiUpdateInterval = 0.033f;

    #endregion

    #region Private Fields

    private Transform _targetMob;
    private MobCombat _combat;
    private MobData _mobData;
    private Camera _mainCamera;
    private RectTransform _rectTransform;
    private CanvasGroup _canvasGroup;
    private Collider _targetCollider;

    private float _lastUIUpdateTime;
    private bool _isInitialized;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        _mainCamera = Camera.main;
        _rectTransform = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();
        
        // Why: CanvasGroup이 없으면 추가
        if (_canvasGroup == null)
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    private void Update()
    {
        if (!_isInitialized) return;

        // Why: 타겟이 없거나 파괴되었으면 UI 제거
        if (_targetMob == null)
        {
            Destroy(gameObject);
            return;
        }

        float currentTime = Time.time;

        // Why: 성능 최적화: 0.033초(~30fps) 간격으로만 업데이트
        if (currentTime - _lastUIUpdateTime >= _uiUpdateInterval)
        {
            UpdateHealthBar();
            UpdateVisibility();
            _lastUIUpdateTime = currentTime;
        }
    }

    private void LateUpdate()
    {
        if (!_isInitialized || _targetMob == null || _mainCamera == null)
            return;

        // Why: 죽은 몹은 위치 업데이트 안 함
        if (_combat != null && !_combat.IsAlive)
            return;

        UpdateScreenPosition();
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 타겟 몹을 설정하고 초기화합니다.
    /// MobSpawnManager가 스폰 시 호출.
    /// </summary>
    /// <param name="mobTransform">몹 Transform</param>
    public void SetTargetMob(Transform mobTransform)
    {
        _targetMob = mobTransform;
        _mainCamera = Camera.main;
        _rectTransform = GetComponent<RectTransform>();

        InitializeReferences();
    }

    /// <summary>
    /// 컴포넌트 참조를 초기화합니다.
    /// </summary>
    private void InitializeReferences()
    {
        if (_targetMob == null) return;

        _combat = _targetMob.GetComponent<MobCombat>();

        // Why: FOV 가시성 체크를 위한 콜라이더 캐싱
        _targetCollider = _targetMob.GetComponent<Collider>();

        if (_combat != null)
        {
            _mobData = _combat.GetMobData();

            // Why: 이름 설정 (한 번만)
            if (_nameText != null && _mobData != null)
            {
                _nameText.text = _mobData.MobName;
            }
        }

        _isInitialized = _combat != null && _mobData != null;

        if (!_isInitialized)
        {
            Debug.LogWarning($"[MobOverheadUI] 초기화 실패: {_targetMob.name}, MobCombat={_combat != null}, MobData={_mobData != null}");
        }
        else
        {
            Debug.Log($"[MobOverheadUI] 초기화 성공: {_mobData.MobName}");
        }
    }

    #endregion

    #region UI Updates

    /// <summary>
    /// HP 바를 업데이트합니다.
    /// MobCombat의 [Networked] HP를 직접 읽음.
    /// </summary>
    private void UpdateHealthBar()
    {
        if (_hpBar == null || _combat == null || _mobData == null)
            return;

        float hpRatio = _mobData.MaxHP > 0
            ? _combat.HP / _mobData.MaxHP
            : 0f;

        _hpBar.value = hpRatio;
    }

    /// <summary>
    /// 몹의 월드 좌표를 스크린 좌표로 변환하여 UI 위치를 업데이트합니다.
    /// PlayerOverheadUI와 동일한 방식.
    /// </summary>
    private void UpdateScreenPosition()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
        }

        // Why: 먼저 캐릭터 위치만 스크린 좌표로 변환
        Vector3 characterScreenPos = _mainCamera.WorldToScreenPoint(_targetMob.position);

        // Why: 카메라 뒤에 있으면 화면 밖으로 이동
        if (characterScreenPos.z < 0)
        {
            _rectTransform.position = new Vector3(-1000, -1000, 0);
            return;
        }

        // Why: 화면 경계 밖에 있으면 UI를 숨김 (갑자기 나타나는 현상 방지)
        // 약간의 마진(-50 ~ Screen.width+50)을 두어 경계에서 부드럽게 처리
        const float margin = 50f;
        if (characterScreenPos.x < -margin || characterScreenPos.x > Screen.width + margin ||
            characterScreenPos.y < -margin || characterScreenPos.y > Screen.height + margin)
        {
            _rectTransform.position = new Vector3(-1000, -1000, 0);
            return;
        }

        // Why: 화면 중앙(0.5)을 기준으로 얼마나 떨어져 있는지 계산
        float screenHeightRatio = characterScreenPos.y / Screen.height;
        float screenWidthRatio = characterScreenPos.x / Screen.width;

        // Why: 화면 아래쪽에 있을수록 Y 오프셋 증가, 위쪽에 있을수록 감소
        float adjustedYOffset = _headOffset * (1f + (0.5f - screenHeightRatio) * 0.5f);
        
        // Why: 화면 좌측에 있으면 오른쪽으로 보정 (양의 X), 우측이면 왼쪽으로 보정 (음의 X)
        // 카메라의 오른쪽 방향을 기준으로 월드 오프셋 적용
        float xOffsetAmount = (0.5f - screenWidthRatio) * _headOffset * 0.5f;
        Vector3 cameraRight = _mainCamera.transform.right;

        Vector3 worldPosition = _targetMob.position + Vector3.up * adjustedYOffset + cameraRight * xOffsetAmount;
        Vector3 screenPosition = _mainCamera.WorldToScreenPoint(worldPosition);

        _rectTransform.position = screenPosition;
    }

    /// <summary>
    /// 가시성을 업데이트합니다.
    /// 죽은 몹은 UI를 숨깁니다.
    /// </summary>
    private void UpdateVisibility()
    {
        if (_canvasGroup == null || _combat == null) return;

        // 1. 생존 여부 체크
        bool isAlive = _combat.IsAlive;
        if (!isAlive)
        {
            _canvasGroup.alpha = 0f;
            return;
        }

        // 2. FOV 가시성 체크 (콜라이더의 일부라도 시야에 들어오면 표시)
        bool isVisibleInFOV = true;
        if (FOVController.LocalInstance != null)
        {
            if (_targetCollider != null)
            {
                isVisibleInFOV = FOVController.LocalInstance.IsColliderInsideFOV(_targetCollider);
            }
            else
            {
                isVisibleInFOV = FOVController.LocalInstance.IsInsideFOV(_targetMob.position);
            }
        }

        // 3. 최종 가시성 적용
        // 시야에 들어오면 Alpha 1 (전체 표시), 아니면 0 (숨김)
        _canvasGroup.alpha = isVisibleInFOV ? 1f : 0f;
    }

    #endregion

    #region Cleanup

    private void OnDestroy()
    {
        _targetMob = null;
        _combat = null;
        _mobData = null;
    }

    #endregion
}
