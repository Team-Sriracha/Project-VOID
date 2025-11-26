using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

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
    private InputAction _dropWeaponAction;

    // Why: 클라이언트에서 edge detection 수행 (서버 부담 감소)
    private bool _previousAttackState;
    private bool _previousReloadState;
    private bool _previousDropWeaponState;

    private Camera _mainCamera;
    private LayerMask _groundLayer;

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
            _dropWeaponAction = _playerActionMap.FindAction("Drop");
        }

        _mainCamera = Camera.main;
        _groundLayer = LayerMask.GetMask("Ground");
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
        // Why: 게임 종료 시 모든 입력 차단
        if (GameStateManager.Instance != null && GameStateManager.Instance.IsGameEnded)
        {
            return new NetworkInputData(); // 빈 입력 반환
        }

        // Why: 아이템 관련 UI 위에 마우스가 있거나 드래그 중이면 조준/발사 입력 무시 (재장전은 허용)
        bool isPointerOverBlockingUI = IsPointerOverBlockingUI() || ItemPickupEntry.IsAnyItemDragging;

        // Why: 클라이언트에서 edge detection 수행 (서버 부담 감소)
        bool currentAttackState = _attackAction != null && _attackAction.IsPressed();
        bool attackJustPressed = !_previousAttackState && currentAttackState;

        bool currentReloadState = _reloadAction != null && _reloadAction.IsPressed();
        bool reloadJustPressed = !_previousReloadState && currentReloadState;

        bool currentDropWeaponState = _dropWeaponAction != null && _dropWeaponAction.IsPressed();
        bool dropWeaponJustPressed = !_previousDropWeaponState && currentDropWeaponState;

        var data = new NetworkInputData
        {
            MoveDirection = GetMoveInput(),
            DashPressed = GetDashInput(),
            AimDirection = isPointerOverBlockingUI ? Vector3.zero : GetAimDirection(),
            AimPressed = isPointerOverBlockingUI ? false : IsAiming(),           // 우클릭 홀드
            FirePressed = isPointerOverBlockingUI ? false : attackJustPressed,   // 단발용 (edge detection)
            AttackHeld = isPointerOverBlockingUI ? false : currentAttackState,   // 연사용 (홀드 상태)
            ReloadPressed = reloadJustPressed, // Why: 재장전은 UI와 관계없이 작동
            DropWeaponPressed = dropWeaponJustPressed  // 무기 드랍 (edge detection)
        };

        _previousAttackState = currentAttackState;
        _previousReloadState = currentReloadState;
        _previousDropWeaponState = currentDropWeaponState;
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
        if (Mouse.current == null || _mainCamera == null) return Vector3.zero;

        Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        // Why: Ground 레이어 Raycast로 3D 공간의 조준 방향 계산
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, _groundLayer))
        {
            Vector3 aimTarget = hit.point;
            aimTarget.y = transform.position.y;
            return (aimTarget - transform.position).normalized;
        }

        // Why: Ground에 닿지 않으면 플레이어 높이의 평면에 투영
        Plane playerPlane = new Plane(Vector3.up, transform.position);
        if (playerPlane.Raycast(ray, out float enter))
        {
            Vector3 aimTarget = ray.GetPoint(enter);
            aimTarget.y = transform.position.y;
            Vector3 direction = (aimTarget - transform.position).normalized;

            // Why: 방향이 유효한지 확인 (너무 짧으면 무시)
            if (direction.sqrMagnitude > 0.01f)
            {
                return direction;
            }
        }

        return Vector3.zero;
    }

    private bool IsAiming()
    {
        // Why: 우클릭 홀드로 조준 모드 진입
        return _aimAction != null && _aimAction.IsPressed();
    }

    /// <summary>
    /// 조준/발사를 차단하는 UI 위에 마우스가 있는지 확인합니다.
    /// Why: 아이템 슬롯, 픽업 패널 등 아이템 관련 UI만 차단
    /// </summary>
    private bool IsPointerOverBlockingUI()
    {
        // Why: EventSystem이 없으면 UI 체크 불가
        if (EventSystem.current == null) return false;

        // Why: UI 위에 마우스가 없으면 false
        if (!EventSystem.current.IsPointerOverGameObject()) return false;

        // Why: 마우스 아래에 있는 UI 요소들을 가져옴
        var pointerData = new UnityEngine.EventSystems.PointerEventData(EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };

        var results = new System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        // Why: 아이템 관련 UI 컴포넌트가 있으면 차단
        foreach (var result in results)
        {
            if (result.gameObject.GetComponent<ItemSlotUI>() != null ||
                result.gameObject.GetComponent<ItemPickupEntry>() != null ||
                result.gameObject.GetComponentInParent<ItemPickupUI>() != null)
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}
