using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>
/// 플레이어 입력을 수집합니다.
/// CSP(Client-Side Prediction) 환경에서 PlayerController가 개별 getter 메서드를 통해 입력을 조회합니다.
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

    // 서버의 부담을 줄이기 위해 클라이언트에서 Edge Detection(상태 변화 감지) 수행
    private bool _previousAttackState;
    private bool _previousReloadState;
    private bool _previousDropWeaponState;

    private Camera _mainCamera;
    private LayerMask _groundLayer;

    // 매 프레임 리스트 할당을 방지하기 위해 캐싱하여 재사용
    private readonly List<UnityEngine.EventSystems.RaycastResult> _raycastResults 
        = new List<UnityEngine.EventSystems.RaycastResult>(16);

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

    #region Public Getter Methods

    // 외부 접근용 getter
    public Vector3 GetMoveDirection() => GetMoveInput();
    public bool IsDashPressed() => GetDashInput();
    public Vector3 GetAimDirection() => GetAimDirectionInternal();
    public bool IsAimPressed() => IsAiming() && !IsPointerOverBlockingUI();
    public bool IsAttackHeld() => _attackAction != null && _attackAction.IsPressed() && !IsPointerOverBlockingUI();
    public bool IsFirePressed() => _attackAction != null && _attackAction.WasPressedThisFrame() && !IsPointerOverBlockingUI();
    public bool IsReloadPressed() => _reloadAction != null && _reloadAction.WasPressedThisFrame();
    public bool IsDropWeaponPressed() => _dropWeaponAction != null && _dropWeaponAction.WasPressedThisFrame();

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

    private Vector3 GetAimDirectionInternal()
    {
        if (Mouse.current == null || _mainCamera == null) return Vector3.zero;

        Ray ray = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        // Ground 레이어 Raycast로 3D 공간의 정확한 조준 지점 계산
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, _groundLayer))
        {
            Vector3 aimTarget = hit.point;
            aimTarget.y = transform.position.y;
            return (aimTarget - transform.position).normalized;
        }

        // Ground에 닿지 않는 경우(허공 등) 플레이어 높이의 가상 평면에 투영하여 방향 계산
        Plane playerPlane = new Plane(Vector3.up, transform.position);
        if (playerPlane.Raycast(ray, out float enter))
        {
            Vector3 aimTarget = ray.GetPoint(enter);
            aimTarget.y = transform.position.y;
            Vector3 direction = (aimTarget - transform.position).normalized;

            // 방향 벡터의 크기가 너무 작으면(제자리) 유효하지 않은 것으로 처리
            if (direction.sqrMagnitude > 0.01f)
            {
                return direction;
            }
        }

        return Vector3.zero;
    }

    private bool IsAiming()
    {
        // 우클릭 홀드 시 조준 모드 진입
        return _aimAction != null && _aimAction.IsPressed();
    }

    /// <summary>
    /// 조준/발사를 차단하는 UI 위에 마우스가 있는지 확인합니다.
    /// 아이템이 있는 슬롯이나 픽업 엔트리만 차단하며, 빈 슬롯은 허용
    /// </summary>
    private bool IsPointerOverBlockingUI()
    {
        // EventSystem이 없으면 UI 상호작용 체크 불가
        if (EventSystem.current == null) return false;

        // UI 위에 마우스가 없으면 차단하지 않음
        if (!EventSystem.current.IsPointerOverGameObject()) return false;

        // 마우스 포인터 아래에 있는 모든 UI 요소 검사
        var pointerData = new UnityEngine.EventSystems.PointerEventData(EventSystem.current)
        {
            position = Mouse.current.position.ReadValue()
        };

        _raycastResults.Clear();
        EventSystem.current.RaycastAll(pointerData, _raycastResults);

        // 아이템 관련 컴포넌트가 있는 UI만 조준 차단
        // IInputBlocker 인터페이스를 구현한 컴포넌트가 있는지 확인
        foreach (var result in _raycastResults)
        {
            // 인터페이스 확인 (자기 자신 또는 부모에서 찾기)
            var blocker = result.gameObject.GetComponentInParent<IInputBlocker>();
            if (blocker != null && blocker.ShouldBlockInput())
            {
                return true;
            }

            // Selectable 상속(Button, Toggle, Slider, InputField 등)은 기본적으로 차단
            if (result.gameObject.GetComponent<UnityEngine.UI.Selectable>() != null || 
                result.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() != null)
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}
