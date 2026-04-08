using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몹 AI 행동 관리 (FSM 기반)
/// Idle, Alert, Chase, Attack, Return, Dead 상태 처리
/// NavMeshAgent 기반 길찾기
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

    [Header("Gizmo 설정")]
    [SerializeField] private bool _alwaysShowGizmos = true;

    #endregion

    #region SyncVars

    // 서버 전용 로직 변수 (동기화 불필요)
    public MonsterState CurrentState;
    public NetworkConnection TargetConnection;
    public Vector3 SpawnPosition;

    // Timer replacements
    private float _alertEndTime;
    private float _attackCooldownEndTime;

    private float _nextDetectionTime;

    #endregion

    #region Private Fields

    private NavMeshAgent _navMeshAgent;
    private MobCombat _combat;
    private MobAnimationController _animationController;
    private MobData _data;
    private Collider[] _detectedColliders;
    private Transform _targetTransform;
    private bool _isAttackActive;
    private int _attackSequenceStep;
    private MobAttackPatternData _activeAttackPattern;
    #endregion

    #region Fishnet Lifecycle

    public override void OnStartServer()
    {
        base.OnStartServer();
        
        _navMeshAgent = GetComponent<NavMeshAgent>();
        _combat = GetComponent<MobCombat>();
        _animationController = GetComponent<MobAnimationController>();

        if (_combat != null)
        {
            _data = _combat.GetMobData();
        }

        if (_navMeshAgent != null && _data != null)
        {
            _navMeshAgent.speed = _data.MoveSpeed;
            _navMeshAgent.stoppingDistance = _data.AttackRange * 0.9f;
            _navMeshAgent.autoBraking = true;
        }

        _detectedColliders = new Collider[MAX_PLAYERS_DETECTED];

        SpawnPosition = transform.position;
        CurrentState = MonsterState.Idle;
        TargetConnection = null;
        ResetAttackCycle(resetSequence: true);

        TimeManager.OnTick += OnTick;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        _navMeshAgent = GetComponent<NavMeshAgent>();
        _combat = GetComponent<MobCombat>();
        _animationController = GetComponent<MobAnimationController>();

        if (_combat != null)
        {
            _data = _combat.GetMobData();
        }

        // 클라이언트에서는 NavMeshAgent가 Transform을 제어하지 않도록 비활성화
        // (NetworkTransform이 동기화 담당)
        if (!IsServerInitialized && _navMeshAgent != null)
        {
            _navMeshAgent.enabled = false;
        }

        FOVRevealAgent.Ensure(gameObject, FOVRevealMode.StencilOnly);
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        
        if (TimeManager != null)
        {
            TimeManager.OnTick -= OnTick;
        }
    }

    private void OnTick()
    {
        if (!IsServerInitialized) return;

        if (CurrentState == MonsterState.Dead) return;

        // 주기적 플레이어 감지
        if (Time.time >= _nextDetectionTime)
        {
            DetectNearbyPlayers();
            _nextDetectionTime = Time.time + _detectionInterval;
        }

        ProcessCurrentState();
    }

    private void Update()
    {
        if (!IsServerInitialized) return;

        // Render() 역할 - 이동 애니메이션 업데이트
        if (_animationController != null && _navMeshAgent != null)
        {
            bool isMoving = _navMeshAgent.velocity.magnitude > 0.1f;
            _animationController.SetMovement(isMoving);
        }
    }

    #endregion

    #region State Machine

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
                break;
        }
    }

    private void ProcessIdleState()
    {
        StopNavMeshAgent();

        if (TargetConnection != null)
        {
            SetState(MonsterState.Chase);
        }
    }

    private void ProcessAlertState()
    {
        StopNavMeshAgent();

        if (_targetTransform != null)
        {
            RotateTowardsTarget(_targetTransform.position);
        }

        if (Time.time >= _alertEndTime)
        {
            if (TargetConnection != null)
            {
                SetState(MonsterState.Chase);
            }
            else
            {
                SetState(MonsterState.Idle);
            }
        }
    }

    private void ProcessChaseState()
    {
        if (TargetConnection == null)
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

        // ChaseRange는 스폰 위치 기준 체크 (너무 멀어지면 복귀)
        // 높이(Y) 무시, 수평 거리만 계산
        Vector3 spawnPosFlat = new Vector3(SpawnPosition.x, transform.position.y, SpawnPosition.z);
        float distanceFromSpawn = Vector3.Distance(transform.position, spawnPosFlat);
        if (_data != null && distanceFromSpawn > _data.ChaseRange)
        {
            LogDebug($"스폰 위치에서 ChaseRange({_data.ChaseRange}) 초과. 복귀 시작.");
            TargetConnection = null;
            _targetTransform = null;
            SetState(MonsterState.Return);
            return;
        }

        float attackRange = GetCurrentAttackRange();
        if (_data != null && distanceToTarget <= attackRange)
        {
            SetState(MonsterState.Attack);
            return;
        }

        ChaseTarget();
    }

    private void ProcessAttackState()
    {
        StopNavMeshAgent();
        
        if (_animationController != null)
        {
            _animationController.SetMovement(false);
        }

        if (TargetConnection == null)
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

        float attackRange = GetCurrentAttackRange();
        if (_data != null && distanceToTarget > attackRange * 1.1f)
        {
            SetState(MonsterState.Chase);
            return;
        }

        RotateTowardsTarget(_targetTransform.position);

        if (Time.time >= _attackCooldownEndTime)
        {
            TryAttack();
        }


    }

    private void ProcessReturnState()
    {
        float distanceToSpawn = Vector3.Distance(transform.position, SpawnPosition);

        if (distanceToSpawn <= POSITION_THRESHOLD)
        {
            StopNavMeshAgent();
            SetState(MonsterState.Idle);
            return;
        }

        if (TargetConnection != null)
        {
            SetState(MonsterState.Chase);
            return;
        }

        ReturnToSpawn();
    }

    #endregion

    #region State Control

    public void SetState(MonsterState newState)
    {
        if (!IsServerInitialized) return;
        if (CurrentState == newState) return;

        LogDebug($"상태 변경: {CurrentState} → {newState}");
        CurrentState = newState;

        OnStateEnter(newState);
    }

    private void OnStateEnter(MonsterState state)
    {
        _alertEndTime = 0;
        _attackCooldownEndTime = 0;
        _nextDetectionTime = 0;

        switch (state)
        {
            case MonsterState.Idle:
                TargetConnection = null;
                _targetTransform = null;
                ResetAttackCycle(resetSequence: true);
                break;
            case MonsterState.Alert:
                // Alert 상태 진입 시 종료 시간 설정 (AlertDuration 후 Chase 전환)
                _alertEndTime = Time.time + (_data?.AlertDuration ?? 1f);
                if (_animationController != null)
                {
                    _animationController.PlayAlert();
                }
                break;
            case MonsterState.Return:
                TargetConnection = null;
                _targetTransform = null;
                ResetAttackCycle(resetSequence: true);
                break;
            case MonsterState.Dead:
                ResetAttackCycle(resetSequence: true);
                StopNavMeshAgent();
                break;
        }
    }

    public void OnDamaged(NetworkConnection attacker)
    {
        if (!IsServerInitialized) return;

        if (CurrentState == MonsterState.Dead) return;

        TargetConnection = attacker;
        UpdateTargetTransform();

        if (_targetTransform != null && _data != null)
        {
            float distanceToAttacker = Vector3.Distance(transform.position, _targetTransform.position);
            
            if (distanceToAttacker <= _data.ChaseRange)
            {
                if (CurrentState == MonsterState.Idle || CurrentState == MonsterState.Return)
                {
                    SetState(MonsterState.Alert);
                    if (_data != null)
                    {
                        _alertEndTime = Time.time + _data.AlertDuration;
                    }
                }
            }
        }
    }

    public void ResetAI()
    {
        if (!IsServerInitialized) return;

        TargetConnection = null;
        _targetTransform = null;
        StopNavMeshAgent();
    }

    #endregion

    #region Navigation

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

    private void StopNavMeshAgent()
    {
        if (_navMeshAgent == null) return;

        if (_navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
        {
            _navMeshAgent.isStopped = true;
            _navMeshAgent.ResetPath();
        }
    }

    private void RotateTowardsTarget(Vector3 targetPosition)
    {
        Vector3 direction = (targetPosition - transform.position).normalized;
        direction.y = 0;

        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                ROTATION_SPEED * (float)TimeManager.TickDelta
            );
        }
    }

    #endregion

    #region Detection

    private void DetectNearbyPlayers()
    {
        if (_data == null)
        {
            LogDebug("DetectNearbyPlayers: _data가 null입니다!");
            return;
        }

        // Fishnet: 기본 Physics.OverlapSphereNonAlloc 사용
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            _data.DetectionRange,
            _detectedColliders,
            _playerLayer
        );

        // [Fix] 스폰 위치에서 너무 멀어지면(복귀 중) 새로운 타겟 감지 금지
        // 복귀가 우선이므로 플레이어가 근처에 있어도 무시함
        float distFromSpawn = Vector3.Distance(transform.position, SpawnPosition);
        if (_data != null && distFromSpawn > _data.ChaseRange)
        {
            // 이미 타겟이 있다면 ChaseState에서 거리 체크하여 해제할 것이므로 여기선 신규 감지만 막음
            return;
        }



        NetworkConnection closestConnection = null;
        float closestDistance = float.MaxValue;
        Transform closestTransform = null;

        for (int i = 0; i < count; i++)
        {
            Collider col = _detectedColliders[i];
            if (col == null) continue;

            PlayerCombat playerCombat = col.GetComponentInParent<PlayerCombat>();
            if (playerCombat == null) continue;
            
            if (!playerCombat.IsAlive) continue;

            NetworkObject netObj = playerCombat.GetComponent<NetworkObject>();
            if (netObj == null) continue;

            if (netObj.Owner == null) continue;

            float distance = Vector3.Distance(transform.position, col.transform.position);
            LogDebug($"    → 유효한 플레이어! 거리: {distance:F2}");

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestConnection = netObj.Owner;
                closestTransform = playerCombat.transform;
            }
        }

        if (closestConnection != null)
        {
            TargetConnection = closestConnection;
            _targetTransform = closestTransform;

            if (CurrentState == MonsterState.Idle || CurrentState == MonsterState.Return)
            {
                // Detection Range 진입 시 Alert 상태 전환
                // Alert 종료 후 Chase 전환
                LogDebug($"DetectionRange 내 플레이어 감지! Alert 상태로 전환.");
                SetState(MonsterState.Alert);
            }
        }
        else
        {
            if (CurrentState == MonsterState.Idle || CurrentState == MonsterState.Return)
            {
                TargetConnection = null;
                _targetTransform = null;
            }
        }
    }

    private void UpdateTargetTransform()
    {
        if (TargetConnection == null)
        {
            _targetTransform = null;
            return;
        }

        if (TargetConnection.FirstObject != null)
        {
            _targetTransform = TargetConnection.FirstObject.transform;

            PlayerCombat combat = TargetConnection.FirstObject.GetComponent<PlayerCombat>();
            if (combat == null || !combat.IsAlive)
            {
                TargetConnection = null;
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

    private void TryAttack()
    {
        if (_data == null || _targetTransform == null) return;

        MobAttackPatternData attackPattern = GetPlannedAttackPattern();
        int attackAnimationIndex = attackPattern != null ? attackPattern.AnimationIndex : 0;
        float attackCooldown = attackPattern != null ? attackPattern.AttackCooldown : _data.AttackCooldown;

        _activeAttackPattern = attackPattern;

        if (_animationController != null)
        {
            _animationController.PlayAttack(attackAnimationIndex);
        }

        _attackCooldownEndTime = Time.time + attackCooldown;
        _attackSequenceStep++;
        _isAttackActive = true;
        
        LogDebug($"공격 시작! 패턴: {attackPattern?.PatternName ?? "Default"}, 애니메이션 인덱스: {attackAnimationIndex}");
    }

    // Animation Controller에서 호출됨
    public void OnAnimationEvent_AttackHit()
    {
        // _attackDamageTime = 0;

        if (!_isAttackActive) return;

        if (_data == null || _targetTransform == null)
        {
            return;
        }

        MobAttackPatternData attackPattern = _activeAttackPattern;
        float attackRange = attackPattern != null ? attackPattern.AttackRange : _data.AttackRange;
        float attackDamage = attackPattern != null ? attackPattern.AttackDamage : _data.AttackDamage;

        float distanceToTarget = Vector3.Distance(transform.position, _targetTransform.position);
        if (distanceToTarget > attackRange * 1.2f)
        {
            LogDebug("데미지 적용 시점에 타겟이 공격 범위 밖으로 이탈함.");
            return;
        }

        IDamageable damageable = _targetTransform.GetComponent<IDamageable>();
        if (damageable != null && damageable.IsAlive)
        {
            // 몹 → 타겟 방향에서 타겟 표면 위치 계산
            Vector3 directionToMob = (transform.position - _targetTransform.position).normalized;
            Vector3 hitPosition = _targetTransform.position + directionToMob * 0.3f + Vector3.up * 1f;
            
            damageable.TakeDamage(attackDamage, null, hitPosition);
            LogDebug($"데미지 적용! {attackDamage} 데미지.");
        }
    }

    private MobAttackPatternData GetPlannedAttackPattern()
    {
        if (_data == null || _data.AttackPatternCount == 0)
        {
            return null;
        }

        return _data.GetAttackPatternForSequenceStep(_attackSequenceStep);
    }

    private float GetCurrentAttackRange()
    {
        if (_data == null)
        {
            return 0f;
        }

        if (_isAttackActive && _activeAttackPattern != null)
        {
            return _activeAttackPattern.AttackRange;
        }

        MobAttackPatternData plannedAttackPattern = GetPlannedAttackPattern();
        return plannedAttackPattern != null ? plannedAttackPattern.AttackRange : _data.AttackRange;
    }

    private void ResetAttackCycle(bool resetSequence)
    {
        _isAttackActive = false;
        _activeAttackPattern = null;

        if (resetSequence)
        {
            _attackSequenceStep = 0;
        }
    }

    #endregion

    #region Public Methods

    public void SetSpawnPosition(Vector3 position)
    {
        if (!IsServerInitialized) return;
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

    private void OnDrawGizmos()
    {
        if (!_alwaysShowGizmos) return;
        DrawGizmos(0.3f);
    }

    private void OnDrawGizmosSelected()
    {
        DrawGizmos(1f);
    }

    private void DrawGizmos(float alpha)
    {
        if (_data == null) return;

        Vector3 pos = transform.position;

        Gizmos.color = new Color(1f, 0f, 0f, alpha);
        Gizmos.DrawWireSphere(pos, _data.AttackRange);

        Gizmos.color = new Color(0f, 1f, 0f, alpha);
        Gizmos.DrawWireSphere(pos, _data.DetectionRange);

        Gizmos.color = new Color(1f, 1f, 0f, alpha);
        Gizmos.DrawWireSphere(pos, _data.ChaseRange);

        if (Application.isPlaying && SpawnPosition != Vector3.zero)
        {
            Gizmos.color = new Color(0f, 0.5f, 1f, alpha);
            Gizmos.DrawLine(pos, SpawnPosition);
            Gizmos.DrawWireSphere(SpawnPosition, 0.5f);
        }

#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            string stateText = CurrentState.ToString();
            UnityEditor.Handles.Label(pos + Vector3.up * 2.5f, $"{_data.MobName}\n[{stateText}]");
        }
#endif
    }

    #endregion
}
