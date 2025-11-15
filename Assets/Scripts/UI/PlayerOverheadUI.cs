using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 오버헤드 UI를 관리합니다 (HP바, 레벨, ID, 탄약, 재장전바).
/// 개선점: UI가 자체적으로 컴포넌트를 참조하여 업데이트 (Controller와 분리)
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

    #endregion

    #region Private Fields

    private PlayerCombat _combat;
    private PlayerController _controller;
    private NetworkedWeapon _weapon;
    private NetworkRunner _runner;
    private NetworkObject _networkObject;
    private bool _isInitialized;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        InitializeReferences();
    }

    private void Update()
    {
        if (!_isInitialized) return;

        UpdateAllUI();
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 부모 플레이어 오브젝트에서 컴포넌트 참조를 찾습니다.
    /// </summary>
    private void InitializeReferences()
    {
        // Why: 오버헤드 UI는 플레이어의 자식 오브젝트
        Transform playerRoot = transform.parent;
        if (playerRoot == null)
        {
            Debug.LogError("[PlayerOverheadUI] 부모 플레이어 오브젝트를 찾을 수 없습니다!");
            return;
        }

        _combat = playerRoot.GetComponent<PlayerCombat>();
        _controller = playerRoot.GetComponent<PlayerController>();
        _weapon = playerRoot.GetComponent<NetworkedWeapon>();

        _networkObject = playerRoot.GetComponent<NetworkObject>();
        if (_networkObject != null)
        {
            _runner = _networkObject.Runner;
        }

        _isInitialized = _combat != null && _controller != null;

        if (_isInitialized)
        {
            // Why: 초기 ID 설정
            UpdatePlayerID();

            // Why: 재장전 바 초기 상태 숨김
            HideReload();
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
