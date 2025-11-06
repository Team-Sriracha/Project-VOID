using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어 이동 및 대시를 처리
/// </summary>
public class PlayerController : NetworkBehaviour
{
    #region Serialized Fields

    [Header("이동 설정")]
    [SerializeField] private float _moveSpeed = 5f;
    [SerializeField] private float _rotationSpeed = 10f;

    [Header("대시 설정")]
    [SerializeField] private float _dashSpeed = 15f;
    [SerializeField] private float _dashDuration = 0.3f;
    [SerializeField] private float _dashCooldown = 1f;

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

    #endregion

    #region Properties

    /// <summary>
    /// 대시 가능 여부를 반환
    /// </summary>
    public bool CanDash => DashCooldownTimer.ExpiredOrNotRunning(Runner) && !IsDashing;

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

    public override void FixedUpdateNetwork()
    {
        // Why: State Authority(서버)만 물리 처리
        if (!HasStateAuthority)
            return;

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

    #endregion

    #region Movement

    private void ProcessMovement()
    {
        if (IsDashing)
            return;

        if (MoveDirection.magnitude > 0.1f)
        {
            // 이동 - Rigidbody linearVelocity 사용
            Vector3 velocity = MoveDirection * _moveSpeed;
            velocity.y = _rb.linearVelocity.y; // Why: Y축(중력)은 유지
            _rb.linearVelocity = velocity;

            // Why: 회전은 PlayerAimController가 전담 (조준 중에만 회전)
        }
        else
        {
            // 정지 시 속도 0
            Vector3 velocity = _rb.linearVelocity;
            velocity.x = 0;
            velocity.z = 0;
            _rb.linearVelocity = velocity;
        }
    }

    #endregion

    #region Dash

    private void StartDash()
    {
        IsDashing = true;
        DashTimer = TickTimer.CreateFromSeconds(Runner, _dashDuration);
        _dashVelocity = MoveDirection * _dashSpeed;
    }

    private void ProcessDash()
    {
        if (IsDashing)
        {
            if (DashTimer.Expired(Runner))
            {
                IsDashing = false;
                DashCooldownTimer = TickTimer.CreateFromSeconds(Runner, _dashCooldown);
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
