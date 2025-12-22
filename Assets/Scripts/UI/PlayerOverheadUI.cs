using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 오버헤드 UI를 관리합니다 (HP바, 레벨, ID, 탄약, 재장전바).
/// Screen Space Overlay 방식으로 항상 가장 앞에 표시됩니다.
/// </summary>
public class PlayerOverheadUI : MonoBehaviour
{
    #region Serialized Fields

    [Header("HP")]
    [SerializeField] private Slider _hpBar;

    [Header("Level")]
    [SerializeField] private TextMeshProUGUI _levelText;

    [Header("Player ID")]
    [SerializeField] private TextMeshProUGUI _playerIDText;

    [Header("Ammo")]
    [SerializeField] private TextMeshProUGUI _ammoText;
    [SerializeField] private GameObject _ammoPanel;

    [Header("Reload")]
    [SerializeField] private Slider _reloadBar;
    [SerializeField] private GameObject _reloadPanel;

    [Header("Screen Space 설정")]
    [Tooltip("추적할 플레이어 Transform")]
    [SerializeField] private Transform _targetPlayer;

    [Tooltip("플레이어 머리 위 월드 오프셋 (Y축)")]
    [SerializeField] private float _headOffset = 4f;

    [Header("성능 최적화")]
    [Tooltip("UI 정보 업데이트 간격 (초) - HP, 탄약 등")]
    [SerializeField] private float _uiUpdateInterval = 0.033f;

    #endregion

    #region Private Fields

    private PlayerCombat _combat;
    private PlayerController _controller;
    private NetworkedWeapon _weapon;
    private NetworkRunner _runner;
    private NetworkObject _networkObject;
    private bool _isInitialized;
    private float _lastUIUpdateTime;
    private Camera _mainCamera;
    private RectTransform _rectTransform;
    private Vector3 _smoothScreenPosition;
    private Vector3 _smoothVelocity;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        _mainCamera = Camera.main;
        _rectTransform = GetComponent<RectTransform>();
        _smoothScreenPosition = _rectTransform.position;
    }

    private void Update()
    {
        if (!_isInitialized) return;

        float currentTime = Time.time;

        if (currentTime - _lastUIUpdateTime >= _uiUpdateInterval)
        {
            UpdateAllUI();
            _lastUIUpdateTime = currentTime;
        }
    }

    private void LateUpdate()
    {
        if (!_isInitialized || _targetPlayer == null || _mainCamera == null) return;

        UpdateScreenPosition();
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 타겟 플레이어를 설정합니다.
    /// </summary>
    public void SetTargetPlayer(Transform playerTransform)
    {
        _targetPlayer = playerTransform;
        InitializeReferences();
    }

    /// <summary>
    /// 타겟 플레이어 오브젝트에서 컴포넌트 참조를 찾습니다.
    /// </summary>
    private void InitializeReferences()
    {
        if (_targetPlayer == null) return;

        _combat = _targetPlayer.GetComponent<PlayerCombat>();
        _controller = _targetPlayer.GetComponent<PlayerController>();
        _weapon = _targetPlayer.GetComponent<NetworkedWeapon>();

        _networkObject = _targetPlayer.GetComponent<NetworkObject>();
        if (_networkObject != null)
        {
            _runner = _networkObject.Runner;
        }

        _isInitialized = _combat != null && _controller != null;

        if (_isInitialized)
        {
            UpdatePlayerID();
            HideReload();
        }
    }

    #endregion

    #region Screen Position Update

    /// <summary>
    /// 플레이어의 월드 좌표를 스크린 좌표로 변환하여 UI 위치를 업데이트합니다.
    /// </summary>
    private void UpdateScreenPosition()
    {
        // Why: 먼저 캐릭터 위치만 스크린 좌표로 변환
        Vector3 characterScreenPos = _mainCamera.WorldToScreenPoint(_targetPlayer.position);

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

        Vector3 worldPosition = _targetPlayer.position + Vector3.up * adjustedYOffset + cameraRight * xOffsetAmount;
        Vector3 targetScreenPosition = _mainCamera.WorldToScreenPoint(worldPosition);

        // Why: 로컬 플레이어는 짧은 스무딩으로 떨림 제거
        // 원격 플레이어는 즉시 반영 (PlayerVisualInterpolation이 이미 보간)
        bool isLocalPlayer = _networkObject != null && _networkObject.HasInputAuthority;

        if (isLocalPlayer)
        {
            _smoothScreenPosition = Vector3.SmoothDamp(
                _smoothScreenPosition,
                targetScreenPosition,
                ref _smoothVelocity,
                0.03f
            );
            _rectTransform.position = _smoothScreenPosition;
        }
        else
        {
            _rectTransform.position = targetScreenPosition;
        }
    }

    #endregion

    #region Update All UI

    /// <summary>
    /// 모든 UI 요소를 업데이트합니다.
    /// </summary>
    private void UpdateAllUI()
    {
        UpdateHealthBar();
        UpdateLevel();
        UpdatePlayerID();
        UpdateAmmoAndReload();
    }

    #endregion

    #region HP Bar

    /// <summary>
    /// HP 바를 업데이트합니다.
    /// </summary>
    private void UpdateHealthBar()
    {
        if (_hpBar == null || _combat == null) return;

        float hpRatio = _combat.MaxHP > 0 ? _combat.HP / _combat.MaxHP : 0f;
        _hpBar.value = hpRatio;
    }

    #endregion

    #region Level

    /// <summary>
    /// 레벨 텍스트를 업데이트합니다.
    /// </summary>
    private void UpdateLevel()
    {
        if (_levelText == null || _combat == null) return;

        _levelText.text = $"{_combat.Level}";
    }

    #endregion

    #region Player ID

    /// <summary>
    /// 플레이어 ID를 업데이트합니다.
    /// </summary>
    private void UpdatePlayerID()
    {
        if (_playerIDText == null || _controller == null) return;

        _playerIDText.text = _controller.PlayerID.ToString();
    }

    #endregion

    #region Ammo & Reload

    /// <summary>
    /// 탄약 및 재장전 UI를 업데이트합니다.
    /// </summary>
    private void UpdateAmmoAndReload()
    {
        // Why: 로컬 플레이어가 아니면(적이면) 탄약/재장전 UI 숨김
        if (_networkObject == null || !_networkObject.HasInputAuthority)
        {
            HideAmmo();
            HideReload();
            return;
        }

        // Why: GunData인 경우 탄약/재장전 표시
        if (_weapon.CurrentWeaponData is GunData gunData)
        {
            // Why: 재장전 중일 때만 재장전 바 표시, 아니면 탄약 표시
            if (_weapon.IsReloading)
            {
                HideAmmo();
                UpdateReloadProgress();
            }
            else
            {
                UpdateAmmo(_weapon.CurrentAmmo, _weapon.TotalAmmo);
                HideReload();
            }
        }
        // Why: MeleeWeaponData인 경우 공격 쿨다운 표시
        else if (_weapon.CurrentWeaponData is MeleeWeaponData meleeData)
        {
            // Why: 근접 무기는 탄약 숨기고 재장전 바를 공격 쿨다운 표시로 사용
            HideAmmo();
            UpdateMeleeAttackCooldown();
        }
    }

    /// <summary>
    /// 탄약 정보를 업데이트합니다.
    /// </summary>
    private void UpdateAmmo(int currentAmmo, int maxAmmo)
    {
        if (_ammoText == null) return;

        _ammoText.text = $"{currentAmmo} / {maxAmmo}";
        ShowAmmo(true);
    }

    /// <summary>
    /// 재장전 진행률을 업데이트합니다.
    /// </summary>
    private void UpdateReloadProgress()
    {
        if (_reloadBar == null || _weapon == null || _runner == null) return;
        if (_weapon.CurrentWeaponData is not GunData gunData) return;

        // Why: TickTimer의 남은 시간으로 진행률 계산
        float totalTime = gunData.ReloadTime;
        float remainingTime = _weapon.ReloadTimer.RemainingTime(_runner) ?? 0f;
        float progress = 1f - (remainingTime / totalTime);

        _reloadBar.value = Mathf.Clamp01(progress);
        ShowReload(true);
    }

    /// <summary>
    /// 근접 무기 공격 쿨다운을 업데이트합니다.
    /// </summary>
    private void UpdateMeleeAttackCooldown()
    {
        if (_reloadBar == null || _weapon == null || _runner == null) return;
        if (_weapon.CurrentWeaponData is not MeleeWeaponData meleeData) return;

        // Why: TickTimer의 남은 시간으로 쿨다운 진행률 계산
        float totalTime = meleeData.AttackDelay;
        float remainingTime = _weapon.AttackCooldownTimer.RemainingTime(_runner) ?? 0f;

        // Why: 쿨다운 중이면 0 → 1로 채워짐, 완료되면 1.0 (꽉 찬 상태) 유지
        if (remainingTime > 0f)
        {
            float progress = 1f - (remainingTime / totalTime);
            _reloadBar.value = Mathf.Clamp01(progress);
        }
        else
        {
            // Why: 공격 준비 완료 시 바를 꽉 찬 상태(1.0)로 유지
            _reloadBar.value = 1f;
        }

        ShowReload(true);
    }

    /// <summary>
    /// 탄약 UI 표시/숨김을 전환합니다.
    /// </summary>
    private void ShowAmmo(bool show)
    {
        if (_ammoPanel != null)
            _ammoPanel.SetActive(show);
    }

    /// <summary>
    /// 탄약 UI를 숨깁니다.
    /// </summary>
    private void HideAmmo()
    {
        ShowAmmo(false);
    }

    /// <summary>
    /// 재장전 바 표시/숨김을 전환합니다.
    /// </summary>
    private void ShowReload(bool show)
    {
        if (_reloadPanel != null)
            _reloadPanel.SetActive(show);
    }

    /// <summary>
    /// 재장전 바를 숨깁니다.
    /// </summary>
    private void HideReload()
    {
        ShowReload(false);
    }

    #endregion
}
