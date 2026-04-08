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
    private InputActionAsset _runtimeInputActions;
    private InputAction _moveAction;
    private InputAction _dashAction;
    private InputAction _lookAction;
    private InputAction _aimAction;
    private InputAction _attackAction;
    private InputAction _reloadAction;
    private InputAction _dropWeaponAction;

    private bool _wasStickFirePressed;
    private Vector2 _currentStickInput;
    private Vector3 _lastMobileAimDirection;
    private const float STICK_AIM_THRESHOLD = 0.2f;
    private const float STICK_FIRE_THRESHOLD = 0.9f;

    private bool _externalReloadTriggered;

    private Camera _mainCamera;
    private LayerMask _groundLayer;

    private readonly List<UnityEngine.EventSystems.RaycastResult> _raycastResults 
        = new List<UnityEngine.EventSystems.RaycastResult>(16);

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_inputActions == null) return;

        // InputActionAsset은 ScriptableObject 에셋 공유 인스턴스이므로,
        // 플레이어별 런타임 복제본을 사용하여 OnDisable 시 전체 입력이 꺼지는 문제를 방지합니다.
        _runtimeInputActions = Instantiate(_inputActions);
        _playerActionMap = _runtimeInputActions.FindActionMap("Player");
        if (_playerActionMap != null)
        {
            _moveAction = _playerActionMap.FindAction("Move");
            _dashAction = _playerActionMap.FindAction("Dash");
            _lookAction = _playerActionMap.FindAction("Look");
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

    private void OnDestroy()
    {
        if (_runtimeInputActions != null)
        {
            Destroy(_runtimeInputActions);
            _runtimeInputActions = null;
        }
    }

    private void Update()
    {
        _currentStickInput = ReadCurrentStickInput();
        UpdateMobileAimState();
    }

    private void LateUpdate()
    {
        // Update previous state for next frame
        _wasStickFirePressed = _currentStickInput.magnitude > STICK_FIRE_THRESHOLD;
        _externalReloadTriggered = false;
        _externalSkillReleased = false;
    }

    #endregion

    #region Public Getter Methods

    // 외부 접근용 getter
    public Vector3 GetMoveDirection() => GetMoveInput();
    public bool IsDashPressed() => GetDashInput();
    public Vector3 GetAimDirection() => GetAimDirectionInternal();
    public bool IsAimPressed() 
    {
        if (IsMobileInputMode())
        {
            return IsMobileAimHeld();
        }

        bool aiming = IsAiming();
        bool isStick = _currentStickInput.magnitude > STICK_AIM_THRESHOLD;
        
        // 스틱 입력 중이면 UI 블로킹 무시 (모바일 조이스틱 터치 등 허용)
        if (isStick) return true;

        return aiming && !IsPointerOverBlockingUI();
    }
    public bool IsAttackHeld() 
    {
        bool isStickHolding = _currentStickInput.magnitude > STICK_FIRE_THRESHOLD;
        
        if (MobileUIManager.IsForceMobile) return isStickHolding; // 스틱 사용 시 UI 체크 무시

        return ((_attackAction != null && _attackAction.IsPressed()) || isStickHolding) && !IsPointerOverBlockingUI();
    }
    public bool IsFirePressed() 
    {
        bool isStickPressed = _currentStickInput.magnitude > STICK_FIRE_THRESHOLD && !_wasStickFirePressed;

        if (MobileUIManager.IsForceMobile) return isStickPressed; // 스틱 사용 시 UI 체크 무시

        return ((_attackAction != null && _attackAction.WasPressedThisFrame()) || isStickPressed) && !IsPointerOverBlockingUI();
    }
    public bool IsReloadPressed() => (_reloadAction != null && _reloadAction.WasPressedThisFrame()) || _externalReloadTriggered;
    public void TriggerReload() => _externalReloadTriggered = true;
    
    // 스킬 입력 (홀드 방식)
    private bool _externalSkillHeld;
    private bool _externalSkillReleased;
    
    /// <summary>
    /// 스킬 키가 눌려있는 동안 true (PC: R키 홀드, 모바일: 스킬 버튼 홀드)
    /// </summary>
    public bool IsSkillHeld() => (_reloadAction != null && _reloadAction.IsPressed()) || _externalSkillHeld;
    
    /// <summary>
    /// 스킬 키를 뗐을 때 true (한 프레임만)
    /// </summary>
    public bool WasSkillReleased() => (_reloadAction != null && _reloadAction.WasReleasedThisFrame()) || _externalSkillReleased;
    
    /// <summary>
    /// 모바일 UI에서 스킬 홀드 상태 설정
    /// </summary>
    public void SetSkillHeld(bool held) => _externalSkillHeld = held;
    
    /// <summary>
    /// 모바일 UI에서 스킬 릴리즈 트리거
    /// </summary>
    public void TriggerSkillRelease() => _externalSkillReleased = true;
    
    private Vector3 _mobileSkillAimDirection;
    
    /// <summary>
    /// 모바일 UI에서 스킬 조준 방향 설정
    /// </summary>
    public void SetSkillAimDirection(Vector3 dir) => _mobileSkillAimDirection = dir;
    
    /// <summary>
    /// 모바일에서 설정된 스킬 조준 방향 반환 (드래그용)
    /// </summary>
    public Vector3 GetMobileSkillAimDirection() => _mobileSkillAimDirection;
    
    public bool IsDropWeaponPressed() => _dropWeaponAction != null && _dropWeaponAction.WasPressedThisFrame();

    #endregion

    #region Input Reading

    private Vector3 GetMoveInput()
    {
        if (_moveAction == null) return Vector3.zero;

        // 모바일 강제 모드일 때 키보드 입력 차단 (New Input System Gamepad 경로만 허용)
        if (MobileUIManager.IsForceMobile && !(_moveAction.activeControl?.device is Gamepad))
        {
            return Vector3.zero;
        }

        Vector2 moveInput = _moveAction.ReadValue<Vector2>();
        return new Vector3(moveInput.x, 0, moveInput.y);
    }

    private bool GetDashInput()
    {
        // 모바일 강제 모드일 때 키보드 입력 차단 (Gamepad만 허용)
        if (MobileUIManager.IsForceMobile && !(_dashAction.activeControl?.device is Gamepad))
        {
            return false;
        }

        return _dashAction != null && _dashAction.WasPressedThisFrame();
    }

    private Vector3 GetAimDirectionInternal()
    {
        if (IsMobileInputMode())
        {
            return GetMobileAimDirection();
        }

        // 1. Stick Input Priority
        if (_currentStickInput.magnitude > STICK_AIM_THRESHOLD)
        {
            return new Vector3(_currentStickInput.x, 0, _currentStickInput.y).normalized;
        }

        // 2. Mouse Input Fallback
        // 모바일 모드이거나 실제 모바일 플랫폼인 경우 마우스(터치) 조준 비활성화
        if (MobileUIManager.IsForceMobile || Application.isMobilePlatform) return Vector3.zero;

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
        // 우클릭 홀드 OR 스틱을 살짝이라도 기울이면 조준 모드 진입
        bool isStickAiming = _currentStickInput.magnitude > STICK_AIM_THRESHOLD;

        // 모바일 모드면 마우스 우클릭(Action) 무시
        if (IsMobileInputMode())
        {
            return isStickAiming;
        }

        return (_aimAction != null && _aimAction.IsPressed()) || isStickAiming;
    }

    private Vector3 GetMobileAimDirection()
    {
        if (MobileAimInputState.HasTracker)
        {
            return IsMobileAimHeld() ? _lastMobileAimDirection : Vector3.zero;
        }

        if (_currentStickInput.magnitude > STICK_AIM_THRESHOLD)
        {
            return new Vector3(_currentStickInput.x, 0f, _currentStickInput.y).normalized;
        }

        return Vector3.zero;
    }

    private Vector2 ReadCurrentStickInput()
    {
        if (_lookAction != null && _lookAction.activeControl?.device is Gamepad)
        {
            return _lookAction.ReadValue<Vector2>();
        }

        if (Gamepad.current != null)
        {
            return Gamepad.current.rightStick.ReadValue();
        }

        return Vector2.zero;
    }

    private void UpdateMobileAimState()
    {
        if (!IsMobileInputMode())
        {
            _lastMobileAimDirection = Vector3.zero;
            return;
        }

        if (!IsMobileAimHeld())
        {
            _lastMobileAimDirection = Vector3.zero;
            return;
        }

        if (_currentStickInput.magnitude > STICK_AIM_THRESHOLD)
        {
            _lastMobileAimDirection = new Vector3(_currentStickInput.x, 0f, _currentStickInput.y).normalized;
        }
    }

    private bool IsMobileAimHeld()
    {
        if (MobileAimInputState.HasTracker)
        {
            return MobileAimInputState.IsAimStickPressed;
        }

        return _currentStickInput.magnitude > STICK_AIM_THRESHOLD;
    }

    private bool IsMobileInputMode()
    {
        return MobileUIManager.IsForceMobile || Application.isMobilePlatform;
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

/// <summary>
/// 모바일 우측 조준 스틱의 런타임 상태를 공유합니다.
/// </summary>
public static class MobileAimInputState
{
    #region Properties

    public static bool HasTracker { get; set; }
    public static bool IsAimStickPressed { get; set; }

    #endregion
}
