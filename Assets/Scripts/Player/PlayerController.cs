using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어 이동 및 대시를 처리
/// </summary>
public class PlayerController : NetworkBehaviour
{
    #region Serialized Fields

    [Header("스탯 참조")]
    [SerializeField] private PlayerStats _playerStats;

    [Header("현재 속도 (읽기 전용)")]
    [SerializeField, Tooltip("현재 이동 속도 (기본값 × 배율)")]
    private float _currentMoveSpeed;

    [SerializeField, Tooltip("이동 속도 배율 (1.0 = 100%)")]
    private float _currentMoveSpeedMultiplier;

    #endregion

    #region Networked Properties

    [Networked]
    public Vector3 MoveDirection { get; set; }

    [Networked]
    public NetworkBool IsDashing { get; set; }

    [Networked]
    public TickTimer DashTimer { get; set; }

    [Networked]
    public TickTimer DashCooldownTimer { get; set; }

    [Networked]
    public NetworkString<_16> PlayerID { get; set; }

    /// <summary>
    /// 이동 속도 강화 배율 (1.0 = 기본 속도, 1.2 = 20% 증가)
    /// </summary>
    [Networked]
    public float MoveSpeedMultiplier { get; set; }

    #endregion

    #region Properties

    /// <summary>
    /// 대시 가능 여부를 반환
    /// </summary>
    public bool CanDash => DashCooldownTimer.ExpiredOrNotRunning(Runner) && !IsDashing;

    /// <summary>
    /// 현재 실제 이동 속도를 계산하여 반환합니다 (기본값 × 배율).
    /// </summary>
    public float GetCurrentMoveSpeed()
    {
        if (_playerStats == null) return 0f;
        return _playerStats.MoveSpeed * MoveSpeedMultiplier;
    }

    #endregion

    #region Private Fields

    private Rigidbody _rb;
    private Vector3 _dashVelocity;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            // Why: 플레이어 ID 초기화
            PlayerID = $"Player_{Object.InputAuthority.PlayerId}";

            // Why: 이동 속도 배율 기본값 설정
            MoveSpeedMultiplier = 1f;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Why: State Authority(서버)만 물리 처리
        if (!HasStateAuthority)
            return;

        // Why: 인스펙터에 현재 속도 표시 (디버깅/밸런싱용)
        UpdateInspectorStats();

        // Why: GetInput으로 현재 틱의 입력 가져오기
        if (GetInput(out NetworkInputData input))
        {
            MoveDirection = input.MoveDirection.normalized;

            if (input.DashPressed && CanDash)
            {
                StartDash();
            }
        }

        ProcessMovement();
        ProcessDash();
    }

    /// <summary>
    /// 인스펙터에 현재 속도를 업데이트합니다 (디버깅/밸런싱용).
    /// </summary>
    private void UpdateInspectorStats()
    {
        _currentMoveSpeed = GetCurrentMoveSpeed();
        _currentMoveSpeedMultiplier = MoveSpeedMultiplier;
    }

    #endregion

    #region Movement

    private void ProcessMovement()
    {
        if (IsDashing || _playerStats == null)
            return;

        Vector3 currentVelocity = _rb.linearVelocity;
        // Why: 기본 속도 × 배율로 최종 속도 계산
        Vector3 targetVelocity = MoveDirection * GetCurrentMoveSpeed();
        targetVelocity.y = currentVelocity.y; // Why: Y축(중력)은 유지

        // Why: 가속/감속을 부드럽게 적용
        float accelerationRate = MoveDirection.magnitude > 0.1f
            ? _playerStats.Acceleration
            : _playerStats.Deceleration;

        // Why: MoveTowards로 부드러운 속도 전환 (FixedDeltaTime 사용)
        Vector3 newVelocity = Vector3.MoveTowards(
            currentVelocity,
            targetVelocity,
            accelerationRate * Runner.DeltaTime
        );

        _rb.linearVelocity = newVelocity;
    }

    #endregion

    #region Dash

    private void StartDash()
    {
        if (_playerStats == null) return;

        IsDashing = true;
        DashTimer = TickTimer.CreateFromSeconds(Runner, _playerStats.DashDuration);
        _dashVelocity = MoveDirection * _playerStats.DashSpeed;
    }

    private void ProcessDash()
    {
        if (_playerStats == null) return;

        if (IsDashing)
        {
            if (DashTimer.Expired(Runner))
            {
                IsDashing = false;
                DashCooldownTimer = TickTimer.CreateFromSeconds(Runner, _playerStats.DashCooldown);
            }
            else
            {
                // 대시 중 이동 - Rigidbody linearVelocity 사용
                Vector3 velocity = _dashVelocity;
                velocity.y = _rb.linearVelocity.y; // Why: Y축(중력)은 유지
                _rb.linearVelocity = velocity;
            }
        }
    }

    #endregion
}
