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
    private PlayerAnimationController _animationController;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _animationController = GetComponent<PlayerAnimationController>();
    }

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            PlayerID = $"Player_{Object.InputAuthority.PlayerId}";
            MoveSpeedMultiplier = 1f;

            // Why: NetworkRigidbody3D 동기화 이슈 방지를 위해 Rigidbody 위치를 Transform과 동기화
            _rb.position = transform.position;
            _rb.rotation = transform.rotation;

            // Why: 서버만 물리 시뮬레이션 실행
            _rb.isKinematic = false;
        }
        else
        {
            // Why: 클라이언트는 NetworkRigidbody3D가 동기화한 위치만 표시
            _rb.isKinematic = true;
        }

        if (UIManager.Instance != null)
        {
            UIManager.Instance.RegisterPlayerOverheadUI(transform);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.UnregisterPlayerOverheadUI(transform);
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Why: Runner가 종료 중이거나 실행 중이 아니면 처리하지 않음
        if (Runner == null || !Runner.IsRunning) return;

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

        Vector3 targetVelocity = MoveDirection * GetCurrentMoveSpeed();
        targetVelocity.y = _rb.linearVelocity.y;

        _rb.linearVelocity = targetVelocity;

        if (_animationController != null)
        {
            bool isMoving = MoveDirection.magnitude > 0.1f;
            Vector3 localDirection = transform.InverseTransformDirection(MoveDirection);
            _animationController.SetMovement(isMoving, localDirection.x, localDirection.z);
        }
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
