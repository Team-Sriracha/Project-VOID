using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// 플레이어의 조준, 회전, 발사를 처리합니다.
/// 좌클릭 발사, 우클릭 조준
/// </summary>
public class PlayerAimController : NetworkBehaviour
{
    #region Serialized Fields

    [Header("컴포넌트 참조")]
    [SerializeField] private AimVisualizer _aimVisualizer;
    [SerializeField] private NetworkedWeapon _weapon;
    [SerializeField] private PlayerInventory _inventory;

    [Header("디버그")]
    [SerializeField] private bool _enableFireDebugLog = false;

    #endregion

    #region SyncVars

    public readonly SyncVar<bool> IsAiming = new();
    public readonly SyncVar<Vector3> AimDirection = new();

    // 발사 시점의 방향이 흔들리지 않도록 마지막 유효한 조준 방향을 저장하여 사용
    private readonly SyncVar<Vector3> LastValidAimDirection = new();

    #endregion

    #region Private Fields

    private Rigidbody _rb;
    
    // 입력 캐시 (서버에서 사용)
    private Vector3 _serverAimDirection;
    private bool _serverAimPressed;
    private bool _serverAttackHeld;
    private bool _serverFirePressed;
    private bool _serverReloadPressed;
    private bool _serverDropWeaponPressed;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_aimVisualizer == null) _aimVisualizer = GetComponent<AimVisualizer>();
        if (_weapon == null) _weapon = GetComponent<NetworkedWeapon>();
        if (_inventory == null) _inventory = GetComponent<PlayerInventory>();
    }

    #endregion

    #region Fishnet Lifecycle

    private void Update()
    {
        // 클라이언트: 입력 수집 및 서버로 전송
        if (IsOwner)
        {
            // 플레이 상태에서만 입력을 전송하며, 카운트다운이나 사망 상태 등에서는 입력 차단
            var combat = GetComponent<PlayerCombat>();
            bool isDead = combat != null && !combat.IsAlive;
            bool isNotPlaying = GameStateManager.Instance == null || 
                                GameStateManager.Instance.CurrentGameState.Value != GameState.Playing;
            bool isDraggingItem = ItemPickupEntry.IsAnyItemDragging; // 인벤토리 드래그 중 입력 차단
            
            if (isNotPlaying || isDead || isDraggingItem)
            {
                // 입력 차단 시 빈 데이터를 전송하여 서버에서도 동작 중지
                SendAimInputToServer(Vector3.zero, false, false, false, false, false);
            }
            else
            {
                var inputHandler = GetComponent<PlayerInputHandler>();
                if (inputHandler != null)
                {
                    Vector3 aimDir = inputHandler.GetAimDirection();
                    bool aimPressed = inputHandler.IsAimPressed();
                    bool attackHeld = inputHandler.IsAttackHeld();
                    bool firePressed = inputHandler.IsFirePressed();
                    bool reloadPressed = inputHandler.IsReloadPressed();
                    bool dropWeaponPressed = inputHandler.IsDropWeaponPressed();
                    
                    SendAimInputToServer(aimDir, aimPressed, attackHeld, firePressed, reloadPressed, dropWeaponPressed);
                }
            }
        }
        
        // 조준선 표시 (로컬 클라이언트)
        RenderAimVisualizer();
    }

    private void FixedUpdate()
    {
        // 서버: 입력 처리 및 발사
        if (IsServerInitialized)
        {
            ProcessAimAndFire();
        }
        // 회전은 PlayerController의 CSP에서 처리
    }

    #endregion

    #region Input Handling

    [ServerRpc]
    private void SendAimInputToServer(Vector3 aimDirection, bool aimPressed, bool attackHeld, bool firePressed, bool reloadPressed, bool dropWeaponPressed)
    {
        _serverAimDirection = aimDirection;
        _serverAimPressed = aimPressed;
        _serverAttackHeld = attackHeld;
        
        // Trigger inputs should be accumulated, not overwritten by false
        if (firePressed) _serverFirePressed = true;
        if (reloadPressed) _serverReloadPressed = true;
        if (dropWeaponPressed) _serverDropWeaponPressed = true;
    }

    private void ProcessAimAndFire()
    {
        // 서버에서도 상태를 재확인하여 유효하지 않은 상태에서의 발사 방지
        var combat = GetComponent<PlayerCombat>();
        bool isDead = combat != null && !combat.IsAlive;
        bool isNotPlaying = GameStateManager.Instance == null || 
                            GameStateManager.Instance.CurrentGameState.Value != GameState.Playing;
        
        if (isNotPlaying || isDead)
        {
            IsAiming.Value = false;
            AimDirection.Value = Vector3.zero;
            return;
        }
        
        bool isAttacking = false;

        // 우클릭 홀드로 조준 모드 진입 여부 결정
        IsAiming.Value = _serverAimPressed;

        // 조준 모드일 때만 조준선 렌더링용 방향 벡터 갱신
        if (IsAiming.Value)
        {
            AimDirection.Value = _serverAimDirection;
        }
        else
        {
            AimDirection.Value = Vector3.zero;
        }

        // 발사 또는 조준 중일 때 유효한 방향 벡터를 계속 갱신하여 저장
        if ((_serverAimPressed || _serverAttackHeld || _serverFirePressed) && _serverAimDirection.sqrMagnitude > 0.01f)
        {
            LastValidAimDirection.Value = _serverAimDirection;
        }

        // 재장전 입력 처리
        if (_serverReloadPressed && _weapon != null)
        {
            _weapon.Reload();
            _serverReloadPressed = false; // 단발성 입력 리셋
        }

        // 무기 드랍 입력 처리 (G키)
        if (_serverDropWeaponPressed && _inventory != null)
        {
            // 인벤토리가 완전히 로드되었고 무기를 장착 중일 때만 드랍
            if (_inventory.NetworkObject != null && _inventory.NetworkObject.IsSpawned && _inventory.HasWeaponEquipped())
            {
                _inventory.DropItem(0); // SLOT_WEAPON = 0
            }
            _serverDropWeaponPressed = false; // 단발성 입력 리셋
        }

        // 무기 데이터의 발사 모드(단발/연사)에 따라 발사 타이밍 결정
        if (_weapon != null && LastValidAimDirection.Value.sqrMagnitude > 0.01f)
        {
            FireMode fireMode = _weapon.CurrentWeaponData?.FireMode ?? FireMode.SemiAuto;

            bool shouldFire = fireMode switch
            {
                FireMode.SemiAuto => _serverFirePressed,   // 단발: 클릭할 때마다
                FireMode.FullAuto => _serverAttackHeld,    // 연사: 홀드 중 계속
                _ => _serverFirePressed
            };

            if (shouldFire)
            {
                if (_enableFireDebugLog)
                {
                    Debug.Log($"[PlayerAimController] 발사 시도 - FireMode: {fireMode}, Direction: {LastValidAimDirection.Value}");
                }
                _weapon.Fire(LastValidAimDirection.Value);
                isAttacking = true;
                _serverFirePressed = false; // 단발성 입력 리셋
            }
        }

        // 공격 홀드 중인지도 체크
        if (!isAttacking)
        {
            isAttacking = _serverAttackHeld;
        }

        // 아무런 공격/조준 입력이 없으면 저장된 방향 초기화
        if (!IsAiming.Value && !isAttacking)
        {
            LastValidAimDirection.Value = Vector3.zero;
        }

        // 회전은 PlayerController의 CSP에서 처리
    }

    #endregion

    #region Rendering

    private void RenderAimVisualizer()
    {
        // 자신의 클라이언트에서만 조준선 표시
        if (!IsOwner || _aimVisualizer == null) return;

        // 조준 중이고 방향이 유효하며 무기가 있을 때만 표시
        if (IsAiming.Value && AimDirection.Value.sqrMagnitude > 0.01f && _weapon != null && _weapon.CurrentWeaponData != null)
        {
            _aimVisualizer.ShowAimIndicator(_weapon.CurrentWeaponData, AimDirection.Value);
        }
        else
        {
            _aimVisualizer.HideAimIndicator();
        }
    }

    #endregion
}
