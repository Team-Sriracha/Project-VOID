using Fusion;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몹의 AI 행동을 관리합니다.
/// 상태 머신 기반으로 Idle, Alert, Chase, Attack, Return, Dead 상태를 처리합니다.
/// NavMeshAgent를 사용하여 길찾기를 수행합니다.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MobAI : NetworkBehaviour
{
    #region Constants

    private const float POSITION_THRESHOLD = 0.5f;
    private const float ROTATION_SPEED = 10f;
    private const int MAX_PLAYERS_DETECTED = 10;

    #endregion

    #region Serialized Fields

    [Header("감지 설정")]
    [Tooltip("플레이어 감지 레이어")]
    [SerializeField] private LayerMask _playerLayer;

    [Tooltip("플레이어 감지 주기 (초)")]
    [SerializeField] private float _detectionInterval = 0.2f;

    [Header("디버그")]
    [Tooltip("디버그 로그 출력")]
    [SerializeField] private bool _enableDebugLogs = false;

    #endregion

    #region Networked Properties

    [Networked]
    public MonsterState CurrentState { get; private set; }

    [Networked]
    public PlayerRef TargetPlayer { get; private set; }

    [Networked]
    public Vector3 SpawnPosition { get; private set; }

    [Networked]
    private TickTimer AlertTimer { get; set; }

    [Networked]
    private TickTimer AttackCooldown { get; set; }

    [Networked]
    private TickTimer AttackDamageTimer { get; set; }

    [Networked]
    private TickTimer DetectionTimer { get; set; }

    #endregion

    #region Private Fields

    private NavMeshAgent _navMeshAgent;
    private MobCombat _combat;
    private MobAnimationController _animationController;
    private MobData _data;
    private Collider[] _detectedColliders;
    private Transform _targetTransform;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        _navMeshAgent = GetComponent<NavMeshAgent>();
        _combat = GetComponent<MobCombat>();
        _animationController = GetComponent<MobAnimationController>();

        // Why: MobData 참조 가져오기
        if (_combat != null)
        {
            _data = _combat.GetMobData();
        }

        // Why: NavMeshAgent 초기 설정
        if (_navMeshAgent != null && _data != null)
        {
            _navMeshAgent.speed = _data.MoveSpeed;
            _navMeshAgent.stoppingDistance = _data.AttackRange * 0.9f;
            _navMeshAgent.autoBraking = true;
        }

        // Why: 감지용 배열 초기화
        _detectedColliders = new Collider[MAX_PLAYERS_DETECTED];

        if (HasStateAuthority)
        {
            // Why: 스폰 위치 저장
            SpawnPosition = transform.position;
            CurrentState = MonsterState.Idle;
            TargetPlayer = PlayerRef.None;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Why: Runner가 종료 중이거나 실행 중이 아니면 처리하지 않음
        if (Runner == null || !Runner.IsRunning) return;

        if (!HasStateAuthority) return;

        // Why: Dead 상태에서는 AI 로직 중단
        if (CurrentState == MonsterState.Dead) return;

        // Why: 주기적 플레이어 감지
        if (DetectionTimer.Expired(Runner))
        {
            DetectNearbyPlayers();
            DetectionTimer = TickTimer.CreateFromSeconds(Runner, _detectionInterval);
        }

        // Why: 상태별 행동 처리
        ProcessCurrentState();
    }

    public override void Render()
    {
        // Why: 이동 애니메이션 업데이트
        if (_animationController != null && _navMeshAgent != null)
        {
            bool isMoving = _navMeshAgent.velocity.magnitude > 0.1f;
            float normalizedSpeed = _navMeshAgent.velocity.magnitude / (_data?.MoveSpeed ?? 3.5f);
            _animationController.SetMovement(isMoving, Mathf.Clamp01(normalizedSpeed));
        }
    }

    #endregion

    #region State Machine

    /// <summary>
    /// 현재 상태에 따른 행동을 처리합니다.
    /// </summary>
    private void ProcessCurrentState()
    {
        switch (CurrentState)
        {
            case MonsterState.Idle:
                ProcessIdleState();
                break;
            case MonsterState.Alert:
                ProcessAlertState();
                break;
            case MonsterState.Chase:
                ProcessChaseState();
                break;
            case MonsterState.Attack:
                ProcessAttackState();
                break;
            case MonsterState.Return:
                ProcessReturnState();
                break;
            case MonsterState.Dead:
                // Why: Dead 상태는 MobCombat에서 처리
                break;
        }
    }

    /// <summary>
    /// Idle 상태 처리: 스폰 위치에서 대기
    /// </summary>
    private void ProcessIdleState()
    {
        // Why: NavMeshAgent 정지
        StopNavMeshAgent();

        // Why: 플레이어가 감지되면 Chase로 전환 (Alert 건너뜀)
        if (TargetPlayer != PlayerRef.None)
        {
            SetState(MonsterState.Chase);
        }
    }

    /// <summary>
    /// Alert 상태 처리: 피격 후 경계, 공격자 방향으로 회전
    /// </summary>
    private void ProcessAlertState()
    {
        StopNavMeshAgent();

        // Why: 타겟 방향으로 회전
        if (_targetTransform != null)
        {
            RotateTowardsTarget(_targetTransform.position);
        }

        // Why: Alert 타이머 만료 시 Chase로 전환
        if (AlertTimer.Expired(Runner))
        {
            if (TargetPlayer != PlayerRef.None)
            {
                SetState(MonsterState.Chase);
            }
            else
            {
                SetState(MonsterState.Idle);
            }
        }
    }

    /// <summary>
    /// Chase 상태 처리: NavMesh로 플레이어 추적
    /// </summary>
    private void ProcessChaseState()
    {
        if (TargetPlayer == PlayerRef.None)
        {
            SetState(MonsterState.Return);
            return;
        }

        // Why: 타겟 Transform 업데이트
        UpdateTargetTransform();

        if (_targetTransform == null)
        {
            SetState(MonsterState.Return);
            return;
        }

        float distanceToTarget = Vector3.Distance(transform.position, _targetTransform.position);

        // Why: 추격 범위(ChaseRange) 이탈 시 복귀 (타겟과의 거리 기준)
        if (_data != null && distanceToTarget > _data.ChaseRange)
        {
            LogDebug($"추격 범위 이탈. 복귀 시작.");
            TargetPlayer = PlayerRef.None;
            _targetTransform = null;
            SetState(MonsterState.Return);
            return;
        }

        // Why: 공격 범위 진입 시 Attack으로 전환
        if (_data != null && distanceToTarget <= _data.AttackRange)
        {
            SetState(MonsterState.Attack);
            return;
        }

        // Why: 타겟 추적
        ChaseTarget();
    }

    /// <summary>
    /// Attack 상태 처리: 공격 범위 내에서 멈춰서 공격
    /// </summary>
    private void ProcessAttackState()
    {
        // Why: 공격 범위 내에서는 반드시 멈춤
        StopNavMeshAgent();
        
        // Why: 이동 애니메이션 정지
        if (_animationController != null)
        {
            _animationController.SetMovement(false, 0f);
        }

        if (TargetPlayer == PlayerRef.None)
        {
            SetState(MonsterState.Return);
            return;
        }

        UpdateTargetTransform();

        if (_targetTransform == null)
        {
            SetState(MonsterState.Return);
            return;
        }

        float distanceToTarget = Vector3.Distance(transform.position, _targetTransform.position);

        // Why: 공격 범위 이탈 시 Chase로 전환
        if (_data != null && distanceToTarget > _data.AttackRange * 1.1f)
        {
            SetState(MonsterState.Chase);
            return;
        }

        // Why: 타겟 방향으로 회전
        RotateTowardsTarget(_targetTransform.position);

        // Why: 공격 쿨다운 체크 후 공격
        if (AttackCooldown.ExpiredOrNotRunning(Runner))
        {
            TryAttack();
        }

        // Why: 공격 애니메이션 종료 후 데미지 적용
        if (AttackDamageTimer.IsRunning && AttackDamageTimer.Expired(Runner))
        {
            ApplyAttackDamage();
        }
    }

    /// <summary>
    /// Return 상태 처리: 스폰 위치로 복귀
    /// </summary>
    private void ProcessReturnState()
    {
        float distanceToSpawn = Vector3.Distance(transform.position, SpawnPosition);

        // Why: 스폰 위치 도착 시 Idle로 전환
        if (distanceToSpawn <= POSITION_THRESHOLD)
        {
            StopNavMeshAgent();
            SetState(MonsterState.Idle);
            return;
        }

        // Why: 복귀 중 플레이어 감지 시 Chase로 전환
        if (TargetPlayer != PlayerRef.None)
        {
            SetState(MonsterState.Chase);
            return;
        }

        // Why: 스폰 위치로 이동
        ReturnToSpawn();
    }

    #endregion

    #region State Control

    /// <summary>
    /// 상태를 변경합니다.
    /// </summary>
    /// <param name="newState">새로운 상태</param>
    public void SetState(MonsterState newState)
    {
        if (!HasStateAuthority) return;
        if (CurrentState == newState) return;

        LogDebug($"상태 변경: {CurrentState} → {newState}");
        CurrentState = newState;

        // Why: 상태 진입 시 초기화
        OnStateEnter(newState);
    }

    /// <summary>
    /// 상태 진입 시 초기화를 수행합니다.
    /// </summary>
    /// <param name="state">진입한 상태</param>
    private void OnStateEnter(MonsterState state)
    {
        AlertTimer = TickTimer.None;
        AttackCooldown = TickTimer.None;
        DetectionTimer = TickTimer.None;

        switch (state)
        {
            case MonsterState.Idle:
                TargetPlayer = PlayerRef.None;
                _targetTransform = null;
                break;
            case MonsterState.Alert:
                if (_animationController != null)
                {
                    _animationController.PlayAlert();
                }
                break;
            case MonsterState.Return:
                TargetPlayer = PlayerRef.None;
                _targetTransform = null;
                break;
            case MonsterState.Dead:
                StopNavMeshAgent();
                break;
        }
    }

    /// <summary>
    /// 피격 시 호출됩니다.
    /// 추격 범위(ChaseRange) 내에서 공격받으면 무조건 추격합니다.
    /// </summary>
    /// <param name="attacker">공격자의 PlayerRef</param>
    public void OnDamaged(PlayerRef attacker)
    {
        if (!HasStateAuthority) return;

        // Why: 이미 죽은 상태면 무시
        if (CurrentState == MonsterState.Dead) return;

        // Why: 타겟 설정
        TargetPlayer = attacker;
        UpdateTargetTransform();

        // Why: 추격 범위 내에서 공격받으면 바로 Chase 상태로 전환
        if (_targetTransform != null && _data != null)
        {
            float distanceToAttacker = Vector3.Distance(transform.position, _targetTransform.position);
            
            // Why: 추격 범위 내에서 피격 → 바로 추격
            if (distanceToAttacker <= _data.ChaseRange)
            {
                if (CurrentState == MonsterState.Idle || CurrentState == MonsterState.Return)
                {
                    // Why: Alert 상태를 거치고 Chase로 전환
                    SetState(MonsterState.Alert);
                    if (_data != null)
                    {
                        AlertTimer = TickTimer.CreateFromSeconds(Runner, _data.AlertDuration);
                    }
                }
            }
        }
    }

    /// <summary>
    /// AI를 초기 상태로 리셋합니다.
    /// 리스폰 시 호출됩니다.
    /// </summary>
    public void ResetAI()
    {
        if (!HasStateAuthority) return;

        TargetPlayer = PlayerRef.None;
        _targetTransform = null;
        StopNavMeshAgent();
    }

    #endregion

    #region Navigation

    /// <summary>
    /// 타겟을 추적합니다.
    /// </summary>
    private void ChaseTarget()
    {
        if (_navMeshAgent == null || _targetTransform == null) return;

        if (!_navMeshAgent.enabled)
        {
            _navMeshAgent.enabled = true;
        }

        _navMeshAgent.isStopped = false;
        _navMeshAgent.SetDestination(_targetTransform.position);
    }

    /// <summary>
    /// 스폰 위치로 복귀합니다.
    /// </summary>
    private void ReturnToSpawn()
    {
        if (_navMeshAgent == null) return;

        if (!_navMeshAgent.enabled)
        {
            _navMeshAgent.enabled = true;
        }

        _navMeshAgent.isStopped = false;
        _navMeshAgent.SetDestination(SpawnPosition);
    }

    /// <summary>
    /// NavMeshAgent를 정지합니다.
    /// </summary>
    private void StopNavMeshAgent()
    {
        if (_navMeshAgent == null) return;

        if (_navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
        {
            _navMeshAgent.isStopped = true;
            _navMeshAgent.ResetPath();
        }
    }

    /// <summary>
    /// 타겟 방향으로 회전합니다.
    /// </summary>
    /// <param name="targetPosition">타겟 위치</param>
    private void RotateTowardsTarget(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        direction.y = 0; // Why: Y축 회전만 적용

        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                ROTATION_SPEED * Runner.DeltaTime
            );
        }
    }

    #endregion

    #region Detection

    /// <summary>
    /// 주변 플레이어를 감지합니다.
    /// DetectionRange 내에 플레이어가 있으면 즉시 추격 시작 (피격 없이도)
    /// </summary>
    private void DetectNearbyPlayers()
    {
        if (_data == null)
        {
            LogDebug("DetectNearbyPlayers: _data가 null입니다!");
            return;
        }

        // Why: Multi-Peer 환경에서 올바른 Physics 씬에서 OverlapSphere 수행
        int count;
        if (Runner.SceneManager != null && Runner.SceneManager.TryGetPhysicsScene3D(out var physicsScene) && physicsScene.IsValid())
        {
            // Multi-Peer: 해당 Runner의 PhysicsScene에서 OverlapSphere
            count = physicsScene.OverlapSphere(
                transform.position,
                _data.DetectionRange,
                _detectedColliders,
                _playerLayer,
                QueryTriggerInteraction.Ignore
            );
        }
        else
        {
            // Fallback: 기본 Physics.OverlapSphereNonAlloc (Single-Peer)
            count = Physics.OverlapSphereNonAlloc(
                transform.position,
                _data.DetectionRange,
                _detectedColliders,
                _playerLayer
            );
        }

        LogDebug($"DetectNearbyPlayers: OverlapSphere 결과 = {count}개, Range = {_data.DetectionRange}, LayerMask = {_playerLayer.value}");

        PlayerRef closestPlayer = PlayerRef.None;
        float closestDistance = float.MaxValue;
        Transform closestTransform = null;

        for (int i = 0; i < count; i++)
        {
            Collider col = _detectedColliders[i];
            if (col == null) continue;

            LogDebug($"  - 감지된 콜라이더: {col.gameObject.name}, Layer: {LayerMask.LayerToName(col.gameObject.layer)}");

            // Why: PlayerCombat으로 플레이어 확인
            PlayerCombat playerCombat = col.GetComponentInParent<PlayerCombat>();
            if (playerCombat == null)
            {
                LogDebug($"    → PlayerCombat 없음");
                continue;
            }
            
            if (!playerCombat.IsAlive)
            {
                LogDebug($"    → 플레이어 사망 상태");
                continue;
            }

            NetworkObject netObj = playerCombat.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                LogDebug($"    → NetworkObject 없음");
                continue;
            }

            // Why: Server 모드에서 서버는 InputAuthority가 None이므로 제외
            if (netObj.InputAuthority == PlayerRef.None)
            {
                LogDebug($"    → InputAuthority가 None (서버 자신 또는 유효하지 않은 플레이어)");
                continue;
            }

            float distance = Vector3.Distance(transform.position, col.transform.position);
            LogDebug($"    → 유효한 플레이어! 거리: {distance:F2}");

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPlayer = netObj.InputAuthority;
                closestTransform = playerCombat.transform;
            }
        }

        // Why: 가장 가까운 플레이어를 타겟으로 설정
        if (closestPlayer != PlayerRef.None)
        {
            // Why: 새로운 타겟이거나 더 가까운 타겟 발견 시 업데이트
            TargetPlayer = closestPlayer;
            _targetTransform = closestTransform;

            // Why: Idle 또는 Return 상태에서 플레이어 감지 시 즉시 Chase로 전환
            if (CurrentState == MonsterState.Idle || CurrentState == MonsterState.Return)
            {
                LogDebug($"DetectionRange 내 플레이어 감지! 즉시 Chase 시작.");
                SetState(MonsterState.Chase);
            }
        }
        else
        {
            // Why: 감지 범위 내에 플레이어가 없고, 현재 타겟도 없으면 타겟 해제
            // 추격 중이면 타겟 유지 (ChaseRange로 체크)
            if (CurrentState == MonsterState.Idle || CurrentState == MonsterState.Return)
            {
                TargetPlayer = PlayerRef.None;
                _targetTransform = null;
            }
        }
    }

    /// <summary>
    /// 타겟 Transform을 업데이트합니다.
    /// </summary>
    private void UpdateTargetTransform()
    {
        if (TargetPlayer == PlayerRef.None)
        {
            _targetTransform = null;
            return;
        }

        if (Runner != null && Runner.TryGetPlayerObject(TargetPlayer, out var playerObject))
        {
            _targetTransform = playerObject.transform;

            PlayerCombat combat = playerObject.GetComponent<PlayerCombat>();
            if (combat == null || !combat.IsAlive)
            {
                TargetPlayer = PlayerRef.None;
                _targetTransform = null;
            }
        }
        else
        {
            _targetTransform = null;
        }
    }

    #endregion

    #region Combat

    /// <summary>
    /// 공격을 시도합니다.
    /// </summary>
    private void TryAttack()
    {
        if (_data == null || _targetTransform == null) return;

        // Why: 공격 애니메이션 재생
        if (_animationController != null)
        {
            _animationController.PlayAttack();
        }

        // Why: 애니메이션 종료 후 데미지 적용을 위한 타이머 설정
        AttackDamageTimer = TickTimer.CreateFromSeconds(Runner, _data.AttackAnimationDuration);

        // Why: 공격 쿨다운 설정
        AttackCooldown = TickTimer.CreateFromSeconds(Runner, _data.AttackCooldown);
        
        LogDebug($"공격 시작! {_data.AttackAnimationDuration}초 후 데미지 적용 예정.");
    }

    /// <summary>
    /// 공격 애니메이션 종료 후 데미지를 적용합니다.
    /// </summary>
    private void ApplyAttackDamage()
    {
        // Why: 데미지 타이머 리셋
        AttackDamageTimer = TickTimer.None;

        if (_data == null || _targetTransform == null) return;

        // Why: 공격 시점에 타겟 거리 재확인 (공격 범위 내에 있어야 함)
        float distanceToTarget = Vector3.Distance(transform.position, _targetTransform.position);
        if (distanceToTarget > _data.AttackRange * 1.2f)
        {
            LogDebug("데미지 적용 시점에 타겟이 공격 범위 밖으로 이탈함.");
            return;
        }

        // Why: 타겟에게 데미지 적용
        IDamageable damageable = _targetTransform.GetComponent<IDamageable>();
        if (damageable != null && damageable.IsAlive)
        {
            // Why: 몹은 PlayerRef.None으로 공격 (몹이 공격자)
            damageable.TakeDamage(_data.AttackDamage, PlayerRef.None);
            LogDebug($"데미지 적용! {_data.AttackDamage} 데미지.");
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 스폰 위치를 설정합니다.
    /// MobSpawnManager에서 호출됩니다.
    /// </summary>
    /// <param name="position">스폰 위치</param>
    public void SetSpawnPosition(Vector3 position)
    {
        if (!HasStateAuthority) return;
        SpawnPosition = position;
    }

    #endregion

    #region Debug

    private void LogDebug(string message)
    {
        if (_enableDebugLogs)
        {
            Debug.Log($"[MobAI] {gameObject.name}: {message}");
        }
    }

    #endregion

    #region Gizmos

    [Header("Gizmo 설정")]
    [SerializeField] private bool _alwaysShowGizmos = true;

    private void OnDrawGizmos()
    {
        if (!_alwaysShowGizmos) return;
        DrawGizmos(0.3f); // 반투명
    }

    private void OnDrawGizmosSelected()
    {
        DrawGizmos(1f); // 불투명
    }

    private void DrawGizmos(float alpha)
    {
        if (_data == null) return;

        Vector3 pos = transform.position;

        // Why: 공격 범위 (빨강)
        Gizmos.color = new Color(1f, 0f, 0f, alpha);
        Gizmos.DrawWireSphere(pos, _data.AttackRange);
        
        // Why: 공격 범위 원판 (바닥에)
#if UNITY_EDITOR
        UnityEditor.Handles.color = new Color(1f, 0f, 0f, alpha * 0.2f);
        UnityEditor.Handles.DrawSolidDisc(pos, Vector3.up, _data.AttackRange);
#endif

        // Why: 감지 범위 (초록) - 플레이어가 들어오면 바로 추격
        Gizmos.color = new Color(0f, 1f, 0f, alpha);
        Gizmos.DrawWireSphere(pos, _data.DetectionRange);

        // Why: 추격 범위 (노랑) - 이 범위 밖으로 나가면 귀환
        Gizmos.color = new Color(1f, 1f, 0f, alpha);
        Gizmos.DrawWireSphere(pos, _data.ChaseRange);

        // Why: 스폰 위치 연결선 (파랑)
        if (Application.isPlaying && SpawnPosition != Vector3.zero)
        {
            Gizmos.color = new Color(0f, 0.5f, 1f, alpha);
            Gizmos.DrawLine(pos, SpawnPosition);
            Gizmos.DrawWireSphere(SpawnPosition, 0.5f);
        }

#if UNITY_EDITOR
        // Why: 현재 상태 표시
        if (Application.isPlaying)
        {
            string stateText = CurrentState.ToString();
            UnityEditor.Handles.Label(pos + Vector3.up * 2.5f, $"{_data.MobName}\n[{stateText}]");
        }
#endif
    }

    #endregion
}
