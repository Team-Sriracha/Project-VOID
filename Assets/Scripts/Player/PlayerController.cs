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
            PlayerID = $"Player_{Object.InputAuthority.PlayerId}";
            MoveSpeedMultiplier = 1f;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

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

    #endregion

    #region Movement

    private void ProcessMovement()
    {
        if (IsDashing || _playerStats == null) return;

        Vector3 currentVelocity = _rb.linearVelocity;
        Vector3 targetVelocity = MoveDirection * GetCurrentMoveSpeed();
        targetVelocity.y = currentVelocity.y;

        // 이동 중이면 가속, 정지 중이면 감속
        float accelerationRate = (MoveDirection.magnitude > 0.1f)
            ? _playerStats.Acceleration
            : _playerStats.Deceleration;

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
        if (!IsDashing || _playerStats == null) return;

        if (DashTimer.Expired(Runner))
        {
            IsDashing = false;
            DashCooldownTimer = TickTimer.CreateFromSeconds(Runner, _playerStats.DashCooldown);
        }
        else
        {
            Vector3 velocity = _dashVelocity;
            velocity.y = _rb.linearVelocity.y;
            _rb.linearVelocity = velocity;
        }
    }

    #endregion
}
