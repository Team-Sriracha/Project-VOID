using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어 입력을 수집하고 NetworkInputData로 변환합니다.
/// NetworkManager의 OnInput에서 참조됩니다.
/// </summary>
public class PlayerInputHandler : MonoBehaviour
{
    #region Serialized Fields

    [SerializeField] private InputActionAsset _inputActions;

    #endregion

    #region Private Fields

    private InputActionMap _playerActionMap;
    private InputAction _moveAction;
    private InputAction _dashAction;
    private InputAction _aimAction;
    private InputAction _attackAction;
    private InputAction _reloadAction;

    // Why: 클라이언트에서 edge detection 수행 (서버 부담 감소)
    private bool _previousAttackState;
    private bool _previousReloadState;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_inputActions == null) return;

        _playerActionMap = _inputActions.FindActionMap("Player");
        if (_playerActionMap != null)
        {
            _moveAction = _playerActionMap.FindAction("Move");
            _dashAction = _playerActionMap.FindAction("Dash");
            _aimAction = _playerActionMap.FindAction("Aim");
            _attackAction = _playerActionMap.FindAction("Attack");
            _reloadAction = _playerActionMap.FindAction("Reload");
        }
    }

    private void OnEnable() => _playerActionMap?.Enable();
    private void OnDisable() => _playerActionMap?.Disable();

    #endregion

    #region Public Methods

    /// <summary>
    /// 현재 프레임의 모든 입력 데이터를 반환합니다.
    /// </summary>
    public NetworkInputData GetCurrentInput()
    {
        // Why: 클라이언트에서 edge detection 수행 (서버 부담 감소)
        bool currentAttackState = _attackAction != null && _attackAction.IsPressed();
        bool attackJustPressed = !_previousAttackState && currentAttackState;

        bool currentReloadState = _reloadAction != null && _reloadAction.IsPressed();
        bool reloadJustPressed = !_previousReloadState && currentReloadState;

        var data = new NetworkInputData
        {
            MoveDirection = GetMoveInput(),
            DashPressed = GetDashInput(),
            AimDirection = GetAimDirection(),
            AimPressed = IsAiming(),           // 우클릭 홀드
            FirePressed = attackJustPressed,   // 단발용 (edge detection)
            AttackHeld = currentAttackState,   // 연사용 (홀드 상태)
            ReloadPressed = reloadJustPressed  // 재장전 (edge detection)
        };

        _previousAttackState = currentAttackState;
        _previousReloadState = currentReloadState;
        return data;
    }

    #endregion

    #region Input Reading

    private Vector3 GetMoveInput()
    {
        if (_moveAction == null) return Vector3.zero;

        Vector2 moveInput = _moveAction.ReadValue<Vector2>();
        return new Vector3(moveInput.x, 0, moveInput.y);
    }

    private bool GetDashInput()
    {
        return _dashAction != null && _dashAction.IsPressed();
    }

    private Vector3 GetAimDirection()
    {
        if (Mouse.current == null || Camera.main == null) return Vector3.zero;

        // Why: Ground 레이어 Raycast로 3D 공간의 조준 방향 계산
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        LayerMask groundLayer = LayerMask.GetMask("Ground");

        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, groundLayer))
        {
            Vector3 aimTarget = hit.point;
            aimTarget.y = transform.position.y; // 수평 방향만 사용
            return (aimTarget - transform.position).normalized;
        }

        return Vector3.zero;
    }

    private bool IsAiming()
    {
        // Why: 우클릭 홀드로 조준 모드 진입
        return _aimAction != null && _aimAction.IsPressed();
    }

    #endregion
}
