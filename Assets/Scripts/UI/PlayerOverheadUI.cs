using FishNet.Object;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 오버헤드 UI 관리 (HP/탄약 등)
/// Screen Space Overlay 방식, 항상 최상단 표시
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
    [SerializeField] private Transform _targetPlayer;
    [SerializeField] private float _headOffset = 4f;

    [Header("성능 최적화")]
    [SerializeField] private float _uiUpdateInterval = 0.033f;

    #endregion

    #region Private Fields

    private PlayerCombat _combat;
    private PlayerController _controller;
    private NetworkedWeapon _weapon;
    private NetworkObject _networkObject;
    private bool _isInitialized;
    private float _lastUIUpdateTime;
    private Camera _mainCamera;
    private RectTransform _rectTransform;
    private Vector3 _smoothScreenPosition;
    private Vector3 _smoothVelocity;
    private CanvasGroup _canvasGroup;
    private Collider _targetCollider;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        _mainCamera = Camera.main;
        _rectTransform = GetComponent<RectTransform>();
        _smoothScreenPosition = _rectTransform.position;
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
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

    public void SetTargetPlayer(Transform playerTransform)
    {
        _targetPlayer = playerTransform;
        InitializeReferences();
    }

    private void InitializeReferences()
    {
        if (_targetPlayer == null) return;

        _combat = _targetPlayer.GetComponent<PlayerCombat>();
        _controller = _targetPlayer.GetComponent<PlayerController>();
        _weapon = _targetPlayer.GetComponent<NetworkedWeapon>();

        _networkObject = _targetPlayer.GetComponent<NetworkObject>();

        _targetCollider = _targetPlayer.GetComponent<Collider>();

        _isInitialized = _combat != null && _controller != null;

        if (_isInitialized)
        {
            UpdatePlayerID();
            HideReload();
        }
    }

    #endregion

    #region Screen Position Update

    private void UpdateScreenPosition()
    {
        Vector3 characterScreenPos = _mainCamera.WorldToScreenPoint(_targetPlayer.position);

        if (characterScreenPos.z < 0)
        {
            _rectTransform.position = new Vector3(-1000, -1000, 0);
            return;
        }

        const float margin = 50f;
        if (characterScreenPos.x < -margin || characterScreenPos.x > Screen.width + margin ||
            characterScreenPos.y < -margin || characterScreenPos.y > Screen.height + margin)
        {
            _rectTransform.position = new Vector3(-1000, -1000, 0);
            return;
        }

        float screenHeightRatio = characterScreenPos.y / Screen.height;
        float screenWidthRatio = characterScreenPos.x / Screen.width;

        float adjustedYOffset = _headOffset * (1f + (0.5f - screenHeightRatio) * 0.5f);
        
        float xOffsetAmount = (0.5f - screenWidthRatio) * _headOffset * 0.5f;
        Vector3 cameraRight = _mainCamera.transform.right;

        Vector3 worldPosition = _targetPlayer.position + Vector3.up * adjustedYOffset + cameraRight * xOffsetAmount;
        Vector3 targetScreenPosition = _mainCamera.WorldToScreenPoint(worldPosition);

        bool isLocalPlayer = _networkObject != null && _networkObject.IsOwner;

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

    private void UpdateAllUI()
    {
        UpdateHealthBar();
        UpdateLevel();
        UpdatePlayerID();
        UpdateAmmoAndReload();
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_canvasGroup == null || _targetPlayer == null) return;

        if (_networkObject != null && _networkObject.IsOwner)
        {
            _canvasGroup.alpha = 1f;
            return;
        }

        if (_combat != null && !_combat.IsAlive)
        {
            _canvasGroup.alpha = 0f;
            return;
        }

        bool isVisibleInFOV = true;
        if (FOVController.LocalInstance != null)
        {
            if (_targetCollider != null)
            {
                isVisibleInFOV = FOVController.LocalInstance.IsColliderInsideFOV(_targetCollider);
            }
            else
            {
                isVisibleInFOV = FOVController.LocalInstance.IsInsideFOV(_targetPlayer.position);
            }
        }

        _canvasGroup.alpha = isVisibleInFOV ? 1f : 0f;
    }

    #endregion

    #region HP Bar

    private void UpdateHealthBar()
    {
        if (_hpBar == null || _combat == null) return;

        float hpRatio = _combat.MaxHP > 0 ? _combat.HP.Value / _combat.MaxHP : 0f;
        _hpBar.value = hpRatio;
    }

    #endregion

    #region Level

    private void UpdateLevel()
    {
        if (_levelText == null || _combat == null) return;

        _levelText.text = $"{_combat.Level.Value}";
    }

    #endregion

    #region Player ID

    private void UpdatePlayerID()
    {
        if (_playerIDText == null || _controller == null) return;

        _playerIDText.text = _controller.PlayerID;
    }

    #endregion

    #region Ammo & Reload

    private void UpdateAmmoAndReload()
    {
        if (_networkObject == null || !_networkObject.IsOwner)
        {
            HideAmmo();
            HideReload();
            return;
        }

        if (_weapon == null) return;

        if (_weapon.CurrentWeaponData is GunData gunData)
        {
            if (_weapon.IsReloading.Value)
            {
                HideAmmo();
                UpdateReloadProgress(gunData);
            }
            else
            {
                UpdateAmmo(_weapon.CurrentAmmo.Value, _weapon.TotalAmmo.Value);
                HideReload();
            }
        }
        else if (_weapon.CurrentWeaponData is MeleeWeaponData meleeData)
        {
            HideAmmo();
            UpdateMeleeAttackCooldown(meleeData);
        }
    }

    private void UpdateAmmo(int currentAmmo, int maxAmmo)
    {
        if (_ammoText == null) return;

        _ammoText.text = $"{currentAmmo} / {maxAmmo}";
        ShowAmmo(true);
    }

    private void UpdateReloadProgress(GunData gunData)
    {
        if (_reloadBar == null || _weapon == null) return;

        float totalTime = gunData.ReloadTime;
        float remainingTime = _weapon.ReloadRemainingTime;
        float progress = 1f - (remainingTime / totalTime);

        _reloadBar.value = Mathf.Clamp01(progress);
        ShowReload(true);
    }

    private void UpdateMeleeAttackCooldown(MeleeWeaponData meleeData)
    {
        if (_reloadBar == null || _weapon == null) return;

        float totalTime = meleeData.AttackDelay;
        float remainingTime = _weapon.AttackCooldownRemainingTime;

        if (remainingTime > 0f)
        {
            float progress = 1f - (remainingTime / totalTime);
            _reloadBar.value = Mathf.Clamp01(progress);
        }
        else
        {
            _reloadBar.value = 1f;
        }

        ShowReload(true);
    }

    private void ShowAmmo(bool show)
    {
        if (_ammoPanel != null)
            _ammoPanel.SetActive(show);
    }

    private void HideAmmo()
    {
        ShowAmmo(false);
    }

    private void ShowReload(bool show)
    {
        if (_reloadPanel != null)
            _reloadPanel.SetActive(show);
    }

    private void HideReload()
    {
        ShowReload(false);
    }

    #endregion
}
