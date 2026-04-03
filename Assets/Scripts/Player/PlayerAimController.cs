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
    #region Constants

    private const float INPUT_REJECT_FEEDBACK_COOLDOWN = 0.15f;

    #endregion

    #region Serialized Fields

    [Header("컴포넌트 참조")]
    [SerializeField] private AimVisualizer _aimVisualizer;
    [SerializeField] private NetworkedWeapon _weapon;
    [SerializeField] private PlayerInventory _inventory;

    [Header("사운드")]
    [SerializeField] private AudioCue _inputRejectedAudioCue;

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
    
    // 스킬 조준 상태
    private bool _serverSkillHeld;
    private bool _serverSkillReleased;
    private Vector3 _skillAimDirection;
    private float _nextRejectedFeedbackTime;

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
                    
                    // 스킬 입력 처리 (Reload와 별도)
                    bool skillHeld = inputHandler.IsSkillHeld();
                    bool skillReleased = inputHandler.WasSkillReleased();
                    
                    // 스킬 방향: 모바일 외부 입력이 있으면 우선, 없으면 마우스 조준 방향
                    Vector3 externalSkillDir = inputHandler.GetMobileSkillAimDirection();
                    Vector3 skillDir = externalSkillDir.sqrMagnitude > 0.01f ? externalSkillDir : aimDir;
                    
                    SendAimInputToServer(aimDir, aimPressed, attackHeld, firePressed, reloadPressed, dropWeaponPressed);
                    SendSkillInputToServer(skillHeld, skillReleased, skillDir);
                }
            }
            
            // 조준선 표시 (로컬 클라이언트 전용)
            // 서버 상태(SyncVar)를 기다리지 않고 로컬 입력 기반으로 즉시 그려야 부드럽습니다.
            RenderLocalAimVisualizer();
        }
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

    [ServerRpc]
    private void SendSkillInputToServer(bool skillHeld, bool skillReleased, Vector3 aimDirection)
    {
        _serverSkillHeld = skillHeld;
        if (skillReleased) _serverSkillReleased = true;
        if (skillHeld && aimDirection.sqrMagnitude > 0.01f)
        {
            _skillAimDirection = aimDirection.normalized;
        }
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
        
        // 스킬 사용 중에는 조준/공격 차단
        var playerController = GetComponent<PlayerController>();
        if (playerController != null && playerController.IsSkillMoving)
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

        // 재장전 입력 처리 (원거리 무기만)
        if (_serverReloadPressed && _weapon != null)
        {
            // 원거리 무기면 재장전 (근접무기는 스킬로 처리)
            if (_weapon.CurrentWeaponData is not MeleeWeaponData)
            {
                if (_weapon.CanReload())
                {
                    _weapon.Reload();
                }
                else
                {
                    PlayRejectedInputFeedback();
                }
            }
            _serverReloadPressed = false;
        }
        
        // 스킬 발동 (홈드 후 릴리즈 시)
        if (_serverSkillReleased && _weapon != null)
        {
            if (_weapon.CurrentWeaponData is MeleeWeaponData meleeData && meleeData.HasSkill)
            {
                if (_weapon.CanUseSkill())
                {
                    var controller = GetComponent<PlayerController>();
                    Vector3 skillDir = GetSkillAimDirection(
                        _skillAimDirection,
                        _serverAimDirection,
                        controller?.MoveDirection ?? Vector3.zero,
                        transform.forward
                    );
                    _weapon.UseSkill(skillDir);
                }
                else
                {
                    PlayRejectedInputFeedback();
                }
            }
            else
            {
                PlayRejectedInputFeedback();
            }
            _serverSkillReleased = false;
            _skillAimDirection = Vector3.zero;
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

    #region Helper Methods

    /// <summary>
    /// 스킬 조준 방향을 결정합니다.
    /// 우선순위: 외부 스킬 방향 (모바일 드래그) > 마우스 조준 > 이동 방향 > 정면
    /// </summary>
    private Vector3 GetSkillAimDirection(Vector3 externalSkillDir, Vector3 mouseAimDir, Vector3 moveDir, Vector3 fallbackForward)
    {
        if (externalSkillDir.sqrMagnitude > 0.01f) return externalSkillDir.normalized;
        if (mouseAimDir.sqrMagnitude > 0.01f) return mouseAimDir.normalized;
        if (moveDir.sqrMagnitude > 0.01f) return moveDir.normalized;
        return fallbackForward.normalized;
    }

    private void PlayRejectedInputFeedback()
    {
        if (Owner == null || Time.time < _nextRejectedFeedbackTime)
        {
            return;
        }

        _nextRejectedFeedbackTime = Time.time + INPUT_REJECT_FEEDBACK_COOLDOWN;
        TargetRpc_PlayRejectedInputAudio(Owner);
    }

    [TargetRpc]
    private void TargetRpc_PlayRejectedInputAudio(NetworkConnection conn)
    {
        AudioManager.Instance?.PlayUi(_inputRejectedAudioCue);
    }

    #endregion

    #region Rendering

    /// <summary>
    /// 로컬 클라이언트에서 직접 입력을 받아 조준선을 그립니다.
    /// SyncVar를 사용하지 않아 네트워크 지연 없이 부드럽습니다.
    /// </summary>
    private void RenderLocalAimVisualizer()
    {
        if (_aimVisualizer == null) return;

        var inputHandler = GetComponent<PlayerInputHandler>();
        if (inputHandler == null) return;
        
        var playerController = GetComponent<PlayerController>();

        // 1. 로컬 입력 직접 조회
        Vector3 inputAimDir = inputHandler.GetAimDirection();
        bool isSkillHeld = inputHandler.IsSkillHeld();
        bool isSkillMoving = playerController != null && playerController.IsSkillMoving;
        
        // 스킬 이동 중에는 조준선/인디케이터 모두 숨김
        if (isSkillMoving)
        {
            _aimVisualizer.HideAimIndicator();
            _aimVisualizer.HideSkillCapsuleIndicator();
            return;
        }
        
        // 2. 스킬 홈드 시 스킬 인디케이터 표시 (쿨다운 중이 아닐 때만)
        if (isSkillHeld && _weapon != null && _weapon.CurrentWeaponData is MeleeWeaponData meleeData && meleeData.HasSkill)
        {
            // 쿨다운 중이면 인디케이터 표시 안함
            if (_weapon.SkillCooldownRemainingTime > 0f)
            {
                _aimVisualizer.HideSkillCapsuleIndicator();
            }
            else
            {
                var skillData = meleeData.SkillData;
                
                Vector3 externalSkillDir = inputHandler?.GetMobileSkillAimDirection() ?? Vector3.zero;
                Vector3 skillDir = GetSkillAimDirection(
                    externalSkillDir,
                    inputAimDir,
                    playerController?.MoveDirection ?? Vector3.zero,
                    transform.forward
                );
                
                _aimVisualizer.ShowSkillCapsuleIndicator(skillData.ForwardDistance, skillData.AttackRadius, skillDir);
            }
            _aimVisualizer.HideAimIndicator();
            return;
        }
        else
        {
            _aimVisualizer.HideSkillCapsuleIndicator();
        }

        // 3. 조준(우클릭)할 때만 조준선 표시 (공격 시에는 표시 안함)
        bool isAimingOnly = inputHandler.IsAimPressed();
        if (isAimingOnly && inputAimDir.sqrMagnitude > 0.01f && _weapon != null && _weapon.CurrentWeaponData != null)
        {
            _aimVisualizer.ShowAimIndicator(_weapon.CurrentWeaponData, inputAimDir);
        }
        else
        {
            _aimVisualizer.HideAimIndicator();
        }
    }

    #endregion
}
