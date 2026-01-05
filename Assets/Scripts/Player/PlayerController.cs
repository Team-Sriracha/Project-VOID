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

    [Header("이동 설정")]
    [Tooltip("가속 시간 (초) - 낮을수록 빠르게 최대 속도 도달")]
    [SerializeField] private float _accelerationTime = 0.15f;

    [Tooltip("감속 시간 (초) - 낮을수록 빠르게 정지")]
    [SerializeField] private float _decelerationTime = 0.1f;

    [Header("이동 회전 설정")]
    [SerializeField] private float _moveRotationSpeed = 8f;

    [Header("Ground Ring")]
    [SerializeField] private GameObject _groundRingPrefab;

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
    private Vector3 _currentVelocity;
    private Vector3 _velocitySmoothRef;
    private GroundRingRotator _groundRingInstance;

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

            // Why: 서버만 물리 시뮬레이션 실행
            _rb.isKinematic = false;

            // Why: Rigidbody 초기화 (velocity 리셋)
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
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

        // Ground Ring 생성 (로컬에서만, 네트워크 동기화 불필요)
        if (_groundRingPrefab != null)
        {
            var ringObj = Instantiate(_groundRingPrefab, transform.position, Quaternion.Euler(90f, 0f, 0f));
            _groundRingInstance = ringObj.GetComponent<GroundRingRotator>();
            _groundRingInstance.Initialize(this, Object.HasInputAuthority);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        try
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.UnregisterPlayerOverheadUI(transform);
            }

            if (_groundRingInstance != null)
            {
                Destroy(_groundRingInstance.gameObject);
                _groundRingInstance = null;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[PlayerController] Error in Despawned: {ex}");
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (Runner == null || !Runner.IsRunning) return;
        if (!HasStateAuthority) return;

        if (GetInput(out NetworkInputData input))
        {
            MoveDirection = input.MoveDirection.normalized;

            if (input.DashPressed && CanDash)
            {
                StartDash();
            }

            bool isAimingOrAttacking = input.AimPressed || input.AttackHeld || input.FirePressed;
            if (!isAimingOrAttacking && MoveDirection.sqrMagnitude > 0.01f)
            {
                ProcessMoveRotation(MoveDirection);
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

        bool isAccelerating = targetVelocity.sqrMagnitude > _currentVelocity.sqrMagnitude;
        float smoothTime = isAccelerating ? _accelerationTime : _decelerationTime;

        _currentVelocity = Vector3.SmoothDamp(_currentVelocity, targetVelocity, ref _velocitySmoothRef, smoothTime);
        _currentVelocity.y = _rb.linearVelocity.y;

        _rb.linearVelocity = _currentVelocity;

        if (_animationController != null)
        {
            bool isMoving = _currentVelocity.sqrMagnitude > 0.1f;
            Vector3 localDirection = transform.InverseTransformDirection(_currentVelocity.normalized);
            _animationController.SetMovement(isMoving, localDirection.x, localDirection.z);
        }
    }

    private void ProcessMoveRotation(Vector3 direction)
    {
        Quaternion targetRotation = Quaternion.LookRotation(direction);
        Quaternion newRotation = Quaternion.Slerp(_rb.rotation, targetRotation, _moveRotationSpeed * Runner.DeltaTime);
        _rb.MoveRotation(newRotation);
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
