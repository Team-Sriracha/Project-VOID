using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;

/// <summary>
/// 플레이어 이동, 대시, 회전을 처리
/// Server Authoritative + Client-Side Prediction (CSP) 방식
/// Fish-Net Prediction V2를 활용하여 서버가 모든 이동을 검증하면서도
/// 클라이언트에서 즉각적인 반응을 제공합니다.
/// </summary>
public class PlayerController : TickNetworkBehaviour
{
    #region Constants

    private const float FOOTSTEP_MIN_SPEED_SQR = 0.25f;
    private static readonly Vector3 FOOTSTEP_AUDIO_OFFSET = new(0f, 6f, 0f);

    #endregion

    #region Serialized Fields

    [Header("스탯 참조")]
    [SerializeField] private PlayerStats _playerStats;

    [Header("이동 설정")]
    [SerializeField] private float _moveRotationSpeed = 12f;
    [SerializeField] private float _aimRotationSpeed = 15f;

    [Header("Ground Ring")]
    [SerializeField] private GameObject _groundRingPrefab;

    [Header("사운드")]
    [SerializeField] private AudioCue _dashAudioCue;
    [SerializeField] private AudioCue _footstepAudioCue;
    [SerializeField] [Min(0.05f)] private float _footstepInterval = 0.4f;

    #endregion

    #region Private Fields

    private PlayerAnimationController _animationController;
    private GroundRingRotator _groundRingInstance;
    private CharacterController _characterController;
    private PlayerIdentityRegistrar _identityRegistrar;
    private PlayerCombat _playerCombat;
    
    // 이동 상태 (CSP에서 동기화됨)
    private Vector3 _currentVelocity;
    
    // 대시 상태 (Tick 기반) - 일반 이동과 동일하게 처리
    private uint _dashCooldownEndTick;
    private float _dashRemainingDistance;  // 남은 대시 거리
    private Vector3 _dashDirection;        // 대시 방향
    
    // 입력 수집용 (Update에서 수집, OnTick에서 사용)
    private Vector3 _inputMoveDirection;
    private Vector3 _inputAimDirection;
    private bool _inputDashPressed;
    private bool _inputAimHeld;
    private bool _inputFireHeld;

    // 이동 속도 배율
    private float _moveSpeedMultiplier = 1f;
    
    // 원격 플레이어 외삽(Extrapolation)용
    // 서버에서 받은 속도로 미래 위치를 예측하여 부드럽게 이동
    private Vector3 _remoteVelocity;           // 서버에서 받은 속도
    private Vector3 _remoteServerPosition;     // 마지막 서버 위치
    private Quaternion _remoteTargetRotation;  // 목표 회전
    private bool _hasRemoteData = false;
    
    // 스킬 전진 상태
    private float _skillRemainingDistance;     // 남은 스킬 전진 거리
    private Vector3 _skillDirection;           // 스킬 전진 방향
    private float _skillMoveSpeed;             // 스킬 이동 속도
    private float _footstepTimer;

    #endregion

    #region SyncVars

    // 대시 상태 동기화 (애니메이션/이펙트용 - 다른 클라이언트에서 시각적 표현)
    public readonly SyncVar<bool> SyncIsDashing = new();

    #endregion

    #region Properties

    // CanDash는 메서드로 변경하여 정확한 tick 값을 전달받아 계산
    public bool CanDashAt(uint tick) => tick >= _dashCooldownEndTick && _dashRemainingDistance <= 0;
    public bool CanDash => TimeManager.LocalTick >= _dashCooldownEndTick && _dashRemainingDistance <= 0;
    public bool IsDashing => _dashRemainingDistance > 0;
    public bool IsSkillMoving => _skillRemainingDistance > 0;
    public string PlayerID { get; private set; }
    
    public Vector3 MoveDirection => _currentVelocity.sqrMagnitude > 0.01f 
        ? _currentVelocity.normalized 
        : transform.forward;

    public float GetCurrentMoveSpeed()
    {
        if (_playerStats == null) return 5f;
        
        float baseSpeed = _playerStats.MoveSpeed * _moveSpeedMultiplier;
        
        // 카드 시스템 속도 보너스 적용 (고정값 첨가)
        var cardSystem = GetComponent<PlayerCardSystem>();
        if (cardSystem != null)
        {
            baseSpeed += cardSystem.GetSpeedBonus();
        }
        
        return baseSpeed;
    }

    public float GetDashSpeed()
    {
        return _playerStats != null ? _playerStats.DashSpeed : 0f;
    }

    public float GetDashCooldown()
    {
        if (_playerStats == null) return 0f;
        
        var cardSystem = GetComponent<PlayerCardSystem>();
        float cdMultiplier = cardSystem?.GetDashCooldownMultiplier() ?? 1f;
        return _playerStats.DashCooldown * cdMultiplier;
    }

    // UI용 getter 추가
    public uint DashCooldownEndTick => _dashCooldownEndTick;
    public float DashRemainingDistance => _dashRemainingDistance;
    public float TickRate => (float)TimeManager.TickRate;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _animationController = GetComponent<PlayerAnimationController>();
        _identityRegistrar = GetComponent<PlayerIdentityRegistrar>();
        _playerCombat = GetComponent<PlayerCombat>();
        
        // CharacterController를 사용하여 물리 충돌 처리
        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
        {
            Debug.LogError($"[PlayerController] CharacterController가 없습니다! Player Prefab에 추가해주세요.");
        }
        
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
        }

        // CSP용 Tick 콜백 설정 (비물리 기반이므로 OnTick만 사용)
        SetTickCallbacks(TickCallback.Tick);
    }

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();

        InitializeDefaultPlayerId();

        // SyncVar 이벤트 등록 (다른 클라이언트에서 대시 애니메이션 표시용)
        SyncIsDashing.OnChange += OnSyncIsDashingChanged;
        BindIdentityDisplayNameSync();

        if (UIManager.Instance != null)
        {
            UIManager.Instance.RegisterPlayerOverheadUI(transform);
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        FOVRevealAgent.Ensure(gameObject, FOVRevealMode.StencilOnly);

        if (IsOwner && _groundRingPrefab != null)
        {
            SpawnGroundRing();
        }
        
        // 원격 플레이어의 초기 외삽 데이터를 설정하여 갑작스러운 이동 방지
        if (!IsOwner)
        {
            _remoteServerPosition = transform.position;
            _remoteTargetRotation = transform.rotation;
            _remoteVelocity = Vector3.zero;
            _hasRemoteData = true;
        }
    }

    public override void OnStopNetwork()
    {
        try
        {
            base.OnStopNetwork();
            
            // SyncVar 이벤트 해제
            SyncIsDashing.OnChange -= OnSyncIsDashingChanged;
            UnbindIdentityDisplayNameSync();

            if (_groundRingInstance != null)
            {
                Destroy(_groundRingInstance.gameObject);
                _groundRingInstance = null;
            }

            if (UIManager.Instance != null)
            {
                UIManager.Instance.UnregisterPlayerOverheadUI(transform);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[PlayerController] Error in OnStopNetwork: {ex}");
        }
    }

    private void Update()
    {
        // 원격 플레이어 외삽 (서버 속도로 예측 이동 + 위치 보정)
        if (!IsOwner && _hasRemoteData)
        {
            ExtrapolateRemotePlayer();
        }

        UpdateFootstepAudio();
        
        // Owner만 입력 수집 (실제 이동은 OnTick에서 처리)
        if (!IsOwner) return;
        
        CollectInput();
    }
    
    /// <summary>
    /// 원격 플레이어를 속도 기반으로 외삽(예측)합니다.
    /// 서버 데이터가 오면 부드럽게 보정합니다.
    /// </summary>
    private void ExtrapolateRemotePlayer()
    {
        // 1. 속도 기반 외삽: 매 프레임 속도만큼 직접 이동
        // Lerp 대신 직접 이동을 사용하여 실시간성 확보
        if (_remoteVelocity.sqrMagnitude > 0.01f)
        {
            transform.position += _remoteVelocity * Time.deltaTime;
        }
        
        // 2. 서버 위치로 강한 보정: 오차가 있으면 빠르게 서버 위치로 당김
        // 외삽만으로는 오차가 누적되므로, 서버 위치로 지속적인 보정 필요
        Vector3 toServer = _remoteServerPosition - transform.position;
        float distance = toServer.magnitude;
        
        if (distance > 0.01f)
        {
            // 오차의 일정 비율을 매 프레임 보정 (80% = 더 빠른 보정)
            float correctionRatio = 0.8f;
            transform.position += toServer * correctionRatio;
        }
        
        // 3. 회전 보간
        transform.rotation = Quaternion.Slerp(
            transform.rotation, 
            _remoteTargetRotation, 
            15f * Time.deltaTime
        );
    }

    private void LateUpdate()
    {
        // Replicate 외부에서 처리하여 Replay 중 중복 실행 방지 및 부드러운 재생 보장
        UpdateAnimation();
    }

    #endregion

    #region Input Collection

    /// <summary>
    /// 입력을 수집합니다 (Update에서 호출).
    /// 실제 이동은 OnTick에서 처리
    /// </summary>
    private void CollectInput()
    {
        // Playing 상태체크
        bool isNotPlaying = GameStateManager.Instance == null || 
                            GameStateManager.Instance.CurrentGameState.Value != GameState.Playing;
        
        var combat = GetComponent<PlayerCombat>();
        bool isDead = combat != null && !combat.IsAlive;
        
        // Why: 인벤토리 드래그 중에는 조준/발사 입력만 차단 (이동은 허용)
        bool isDraggingItem = ItemPickupEntry.IsAnyItemDragging;
        
        if (isNotPlaying || isDead)
        {
            _inputMoveDirection = Vector3.zero;
            _inputAimDirection = Vector3.zero;
            _inputDashPressed = false;
            _inputAimHeld = false;
            _inputFireHeld = false;
            return;
        }
        
        var inputHandler = GetComponent<PlayerInputHandler>();
        if (inputHandler != null)
        {
            _inputMoveDirection = inputHandler.GetMoveDirection();
            _inputAimHeld = inputHandler.IsAimPressed();
            // Why: 공격 버튼을 누르고 있는 동안에도 회전이 가능해야 하므로 Held 상태를 체크합니다.
            _inputFireHeld = inputHandler.IsAttackHeld();
            
            // Why: 인벤토리 드래그 중에는 조준 방향을 무시 (회전 방지)
            // 단, 조준 버튼을 누르면 드래그가 취소되므로 그때는 조준 방향 사용
            if (isDraggingItem && !_inputAimHeld)
            {
                _inputAimDirection = Vector3.zero;
            }
            else
            {
                _inputAimDirection = inputHandler.GetAimDirection();
            }
            
            // 대시 입력은 단발성이므로 처리될 때까지 상태 유지(누적)
            if (inputHandler.IsDashPressed())
            {
                _inputDashPressed = true;
            }
        }
    }

    #endregion

    #region CSP - Tick Callbacks

    /// <summary>
    /// 매 Tick마다 호출됨 (Mobile: 30 Tick/sec)
    /// </summary>
    protected override void TimeManager_OnTick()
    {
        PerformReplicate(BuildMoveData());
        CreateReconcile();
    }

    /// <summary>
    /// 입력 데이터를 ReplicateData로 변환합니다.
    /// Owner만 실제 입력을 반환하고, 그 외에는 default를 반환합니다.
    /// </summary>
    private MoveReplicateData BuildMoveData()
    {
        if (!IsOwner)
            return default;

        MoveReplicateData data = new(
            _inputMoveDirection,
            _inputAimDirection,
            _inputDashPressed,
            _inputAimHeld,
            _inputFireHeld
        );

        // 대시 입력 소비 후 리셋
        _inputDashPressed = false;

        return data;
    }

    /// <summary>
    /// 서버가 검증한 상태를 Reconcile 데이터로 생성합니다.
    /// </summary>
    public override void CreateReconcile()
    {
        MoveReconcileData rd = new(
            transform.position,
            transform.rotation,
            _currentVelocity,
            _dashCooldownEndTick,
            _dashRemainingDistance,
            _dashDirection
        );
        PerformReconcile(rd);
    }

    /// <summary>
    /// 이동 로직을 실행합니다.
    /// 서버와 클라이언트 모두에서 동일하게 실행되어 예측을 가능하게 합니다.
    /// </summary>
    [Replicate]
    private void PerformReplicate(MoveReplicateData rd, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        // Why: Tick 기반 델타 사용 (Time.deltaTime 대신)
        float delta = (float)TimeManager.TickDelta;
        
        // Why: Replicate의 Tick을 사용해야 Replay 시에도 올바른 tick 값 사용
        uint replicateTick = rd.GetTick();
        
        // Why: Replay 중에도 대시 시작을 허용해야 함!
        // CanDashAt이 _dashRemainingDistance > 0이면 false 반환하므로 중복 시작 방지됨
        if (rd.DashPressed && CanDashAt(replicateTick) && _playerStats != null)
        {
            StartDashMovement(rd.MoveDirection, replicateTick);
        }

        // 이동 처리 - 스킬 > 대시 > 일반 이동 우선순위
        if (_playerStats != null && _characterController != null)
        {
            Vector3 moveVector;
            
            if (_skillRemainingDistance > 0)
            {
                // 스킬 전진: 입력 무시, 스킬 방향으로 자동 이동
                float moveThisTick = _skillMoveSpeed * delta;
                
                if (moveThisTick > _skillRemainingDistance)
                {
                    moveThisTick = _skillRemainingDistance;
                }
                
                moveVector = _skillDirection * moveThisTick;
                _skillRemainingDistance -= moveThisTick;
                _currentVelocity = _skillDirection * _skillMoveSpeed;
            }
            else if (_dashRemainingDistance > 0)
            {
                // 대시 이동: 대시 방향으로 대시 속도로 이동
                float moveThisTick = _playerStats.DashSpeed * delta;
                
                // 남은 거리보다 많이 이동하면 안 됨
                if (moveThisTick > _dashRemainingDistance)
                {
                    moveThisTick = _dashRemainingDistance;
                }
                
                moveVector = _dashDirection * moveThisTick;
                _dashRemainingDistance -= moveThisTick;
                _currentVelocity = _dashDirection * _playerStats.DashSpeed;
            }
            else
            {
                // 일반 이동
                Vector3 targetVelocity = rd.MoveDirection.normalized * GetCurrentMoveSpeed();
                _currentVelocity = targetVelocity;
                moveVector = _currentVelocity * delta;
            }
            
            if (IsOwner || IsServerInitialized)
            {
                // Owner와 서버는 CharacterController.Move()를 사용하여 충돌을 포함한 이동 처리
                if (_characterController != null && _characterController.enabled)
                {
                    _characterController.Move(moveVector);
                }
            }
            else
            {
                // 원격 플레이어는 속도만 저장하고, 실제 이동은 Update의 외삽 로직에서 수행
                // Reconcile에서 위치 보정을 받으므로 여기서는 위치를 직접 수정하지 않음
                _remoteVelocity = _currentVelocity;
                _hasRemoteData = true;
            }
        }

        // 회전 처리: Owner와 서버만 직접 처리
        if (IsOwner || IsServerInitialized)
        {
            ProcessRotation(rd, delta);
        }
        else
        {
            // 원격 플레이어는 목표 회전값만 계산하고, 실제 회전은 Update에서 부드럽게 보간
            Quaternion targetRotation = transform.rotation;
            
            if (rd.AimHeld && rd.AimDirection.sqrMagnitude > 0.01f)
            {
                targetRotation = Quaternion.LookRotation(rd.AimDirection.normalized);
            }
            else if (rd.FireHeld && rd.AimDirection.sqrMagnitude > 0.01f)
            {
                targetRotation = Quaternion.LookRotation(rd.AimDirection.normalized);
            }
            else if (rd.MoveDirection.sqrMagnitude > 0.01f)
            {
                targetRotation = Quaternion.LookRotation(rd.MoveDirection.normalized);
            }
            
            _remoteTargetRotation = targetRotation;
        }
        
        // Why: 애니메이션은 LateUpdate에서 처리 (Replicate 내부에서 하면 Replay 중 중복 호출됨)
    }

    /// <summary>
    /// 서버로부터 받은 상태로 클라이언트 상태를 복원합니다.
    /// 예측 실패 시 보정 처리
    /// </summary>
    [Reconcile]
    private void PerformReconcile(MoveReconcileData rd, Channel channel = Channel.Unreliable)
    {
        if (IsOwner)
        {
            // Owner: 서버에서 받은 상태로 Transform 직접 복원
            transform.position = rd.Position;
            transform.rotation = rd.Rotation;
        }
        else
        {
            // 원격 플레이어는 외삽 데이터만 업데이트 (Update에서 보간)
            // Transform을 직접 수정하면 화면 떨림(Jitter) 발생 가능
            _remoteServerPosition = rd.Position;
            _remoteTargetRotation = rd.Rotation;
            _remoteVelocity = rd.CurrentVelocity;
            _hasRemoteData = true;
        }
        
        _currentVelocity = rd.CurrentVelocity;
        _dashCooldownEndTick = rd.DashCooldownEndTick;
        _dashRemainingDistance = rd.DashRemainingDistance;
        _dashDirection = rd.DashDirection;
    }

    #endregion

    #region Movement Logic

    private void ProcessRotation(MoveReplicateData rd, float delta)
    {
        if (rd.AimDirection.sqrMagnitude < 0.01f && rd.MoveDirection.sqrMagnitude < 0.01f)
            return;
            
        // Case 1: 조준 중 - 부드럽게 조준 방향 따라감
        if (rd.AimHeld && rd.AimDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(rd.AimDirection.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _aimRotationSpeed * 60f * delta);
        }
        // Case 2: 비조준 + 발사 - 조준 시와 동일한 속도로 회전
        else if (rd.FireHeld && rd.AimDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(rd.AimDirection.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _aimRotationSpeed * 60f * delta);
        }
        // Case 3: 일반 이동 - 이동 방향으로 회전
        else if (rd.MoveDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(rd.MoveDirection.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _moveRotationSpeed * 60f * delta);
        }
    }
    
    private void UpdateAnimation()
    {
        if (_animationController != null)
        {
            bool isMoving = _currentVelocity.sqrMagnitude > 0.1f;
            Vector3 localDirection = transform.InverseTransformDirection(_currentVelocity.normalized);
            _animationController.SetMovement(isMoving, localDirection.x, localDirection.z);
        }
    }

    private void UpdateFootstepAudio()
    {
        if (!IsClientInitialized || _footstepAudioCue == null)
        {
            return;
        }

        if (!ShouldPlayFootstep())
        {
            _footstepTimer = 0f;
            return;
        }

        _footstepTimer -= Time.deltaTime;
        if (_footstepTimer > 0f)
        {
            return;
        }

        AudioManager.Instance?.PlayAttachedOneShot(_footstepAudioCue, transform, FOOTSTEP_AUDIO_OFFSET);
        _footstepTimer = Mathf.Max(0.05f, _footstepInterval);
    }

    private bool ShouldPlayFootstep()
    {
        if (_playerCombat != null && !_playerCombat.IsAlive)
        {
            return false;
        }

        if (_dashRemainingDistance > 0f || _skillRemainingDistance > 0f)
        {
            return false;
        }

        return _currentVelocity.sqrMagnitude >= FOOTSTEP_MIN_SPEED_SQR;
    }

    #endregion

    #region Dash Logic

    /// <summary>
    /// 대시 이동을 시작합니다. 대시 거리만큼 자동으로 이동합니다.
    /// 일반 이동과 동일한 tick 기반 이동 시스템을 사용합니다.
    /// </summary>
    private void StartDashMovement(Vector3 moveDirection, uint currentTick)
    {
        if (_playerStats == null) return;

        _dashDirection = moveDirection.sqrMagnitude > 0.01f 
            ? moveDirection.normalized 
            : transform.forward;

        // DashSpeed * DashDuration = 총 대시 거리
        _dashRemainingDistance = _playerStats.DashSpeed * _playerStats.DashDuration;
        
        // 대시 쿨다운 보너스 적용 (감소할수록 쿨다운 빠름)
        var cardSystem = GetComponent<PlayerCardSystem>();
        float cdMultiplier = cardSystem?.GetDashCooldownMultiplier() ?? 1f;
        float finalCooldown = _playerStats.DashCooldown * cdMultiplier;
        uint dashCooldownTicks = (uint)Mathf.CeilToInt(finalCooldown * TimeManager.TickRate);
        _dashCooldownEndTick = currentTick + dashCooldownTicks;
        
        // 서버에서 다른 클라이언트에 대시 상태 알림 (이펙트/애니메이션 트리거용)
        if (IsServerInitialized)
        {
            SyncIsDashing.Value = true;
            SyncIsDashing.Value = false;
            RPC_PlayDashAudio();
        }
    }

    /// <summary>
    /// 타 클라이언트 대시 상태 변경 시 호출
    /// </summary>
    private void OnSyncIsDashingChanged(bool prev, bool next, bool asServer)
    {
        // 서버에서는 상태를 직접 관리하므로 무시
        if (asServer) return;
        
        // Owner가 아닌 클라이언트에서 대시 애니메이션이나 이펙트 재생
        if (!IsOwner && _animationController != null)
        {
            // TODO: 대시 애니메이션이나 이펙트가 있다면 여기서 재생
            // _animationController.SetDashing(next);
        }
    }

    [ObserversRpc]
    private void RPC_PlayDashAudio()
    {
        AudioManager.Instance?.PlayAttachedOneShot(_dashAudioCue, transform, Vector3.up);
    }

    #endregion

    #region Skill Movement

    /// <summary>
    /// 스킬 전진을 시작합니다. NetworkedWeapon에서 호출됩니다.
    /// </summary>
    public void StartSkillMovement(Vector3 direction, float distance, float duration)
    {
        if (IsSkillMoving) return; // 이미 스킬 이동 중이면 무시
        
        _skillDirection = direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
        _skillRemainingDistance = distance;
        _skillMoveSpeed = distance / Mathf.Max(duration, 0.1f);
    }

    /// <summary>
    /// 스킬 전진을 강제 종료합니다.
    /// </summary>
    public void StopSkillMovement()
    {
        _skillRemainingDistance = 0f;
    }

    #endregion

    #region Helpers

    private void InitializeDefaultPlayerId()
    {
        if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentGameMode == GameMode.PracticeRange)
        {
            PlayerID = "Player";
            return;
        }

        PlayerID = $"Player_{OwnerId}";
    }

    private void BindIdentityDisplayNameSync()
    {
        if (_identityRegistrar == null)
        {
            _identityRegistrar = GetComponent<PlayerIdentityRegistrar>();
        }

        if (_identityRegistrar == null)
        {
            return;
        }

        _identityRegistrar.PlayerDisplayName.OnChange -= OnPlayerDisplayNameChanged;
        _identityRegistrar.PlayerDisplayName.OnChange += OnPlayerDisplayNameChanged;
        _identityRegistrar.PlayerGuestId.OnChange -= OnPlayerGuestIdChanged;
        _identityRegistrar.PlayerGuestId.OnChange += OnPlayerGuestIdChanged;
        ApplyIdentityDisplayName(_identityRegistrar.PlayerDisplayName.Value);
    }

    private void UnbindIdentityDisplayNameSync()
    {
        if (_identityRegistrar == null)
        {
            return;
        }

        _identityRegistrar.PlayerDisplayName.OnChange -= OnPlayerDisplayNameChanged;
        _identityRegistrar.PlayerGuestId.OnChange -= OnPlayerGuestIdChanged;
    }

    private void OnPlayerDisplayNameChanged(string prev, string next, bool asServer)
    {
        _ = prev;
        _ = asServer;
        ApplyIdentityDisplayName(next);
    }

    private void OnPlayerGuestIdChanged(string prev, string next, bool asServer)
    {
        _ = prev;
        _ = next;
        _ = asServer;
        ApplyIdentityDisplayName(_identityRegistrar != null ? _identityRegistrar.PlayerDisplayName.Value : string.Empty);
    }

    private void ApplyIdentityDisplayName(string displayName)
    {
        if (ShouldUsePracticeGuestFallbackName())
        {
            PlayerID = "Player";
            return;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        PlayerID = displayName.Trim();
    }

    private bool ShouldUsePracticeGuestFallbackName()
    {
        if (_identityRegistrar == null || string.IsNullOrWhiteSpace(_identityRegistrar.PlayerGuestId.Value))
        {
            return false;
        }

        if (GameStateManager.Instance == null)
        {
            return false;
        }

        return GameModeCatalog.IsPracticeMode(GameStateManager.Instance.CurrentGameMode);
    }

    private void SpawnGroundRing()
    {
        if (_groundRingInstance != null) return;
        
        // 프리팹의 로컬 위치를 유지하기 위해 먼저 생성 후 부모 설정
        GameObject ringObj = Instantiate(_groundRingPrefab);
        ringObj.transform.SetParent(transform, false); // false = 로컬 위치 유지
        _groundRingInstance = ringObj.GetComponent<GroundRingRotator>();
        
        if (_groundRingInstance != null)
        {
            _groundRingInstance.Initialize(this, true);
        }
    }

    public void SetMoveSpeedMultiplier(float multiplier)
    {
        _moveSpeedMultiplier = multiplier;
    }

    #endregion
}
