using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 모바일 전용 UIManager입니다.
/// 리로드/스킬 통합 버튼:
/// - 원거리 무기: 리로드 버튼 (클릭=재장전)
/// - 근접 무기 + 스킬: 스킬 버튼 (드래그=조준, 릴리즈=발동)
/// - 근접 무기 + 스킬 없음: 버튼 숨김
/// </summary>
public class MobileUIManager : UIManager
{
    [Header("Mobile Debug Settings")]
    [SerializeField] private bool _isForceMobile = true;

    public static bool IsForceMobile { get; private set; }

    [Header("Dash Button")]
    [SerializeField] private Image _dashButtonImage;
    [SerializeField] private Image _dashCooldownFill;
    [SerializeField] private Color _dashNormalColor = Color.white;
    [SerializeField] private Color _dashDisabledColor = new Color(1f, 1f, 1f, 0.5f);

    [Header("Action Button (리로드/스킬)")]
    [Tooltip("ActionButton 오브젝트 - 자식에 CooldownFill(Filled 타입)이 있어야 함")]
    [SerializeField] private Button _actionButton;
    [SerializeField] private Sprite _defaultSkillIcon;
    [SerializeField] private float _dragThreshold = 20f;

    // 자동으로 찾을 참조들
    private RectTransform _actionButtonRect;
    private Image _actionButtonIcon;
    private Image _actionCooldownFill;
    private Sprite _originalButtonSprite; // 기본 이미지 (리로드 아이콘)

    private PlayerController _localPlayer;
    private NetworkedWeapon _localWeapon;
    private PlayerInputHandler _inputHandler;
    private bool _isDashCooldown = false;
    
    private enum ActionButtonMode { Hidden, Reload, Skill }
    private ActionButtonMode _currentMode = ActionButtonMode.Hidden;
    
    private bool _isActionButtonPressed = false;
    private bool _isDragging = false;
    private Vector2 _pointerDownPos;
    private Vector2 _currentDragDir;

    protected override void Awake()
    {
        base.Awake();
        CheckMobilePlatform();
        SetupActionButton();
        Debug.Log("[MobileUIManager] Mobile UI Initialized");
    }

    private void SetupActionButton()
    {
        if (_actionButton == null) return;
        
        _actionButtonRect = _actionButton.GetComponent<RectTransform>();
        _actionButtonIcon = _actionButton.GetComponent<Image>();
        
        // 기본 스프라이트 저장 (리로드 아이콘)
        if (_actionButtonIcon != null)
            _originalButtonSprite = _actionButtonIcon.sprite;
        
        // CooldownFill은 자식에서 찾기
        var fills = _actionButton.GetComponentsInChildren<Image>();
        foreach (var img in fills)
        {
            if (img != _actionButtonIcon && img.type == Image.Type.Filled)
            {
                _actionCooldownFill = img;
                break;
            }
        }
        
        // EventTrigger로 포인터 이벤트 연결
        var eventTrigger = _actionButton.gameObject.GetComponent<EventTrigger>();
        if (eventTrigger == null)
            eventTrigger = _actionButton.gameObject.AddComponent<EventTrigger>();
        
        // PointerDown
        var pointerDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        pointerDown.callback.AddListener((data) => OnActionPointerDown((PointerEventData)data));
        eventTrigger.triggers.Add(pointerDown);
        
        // Drag
        var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
        drag.callback.AddListener((data) => OnActionDrag((PointerEventData)data));
        eventTrigger.triggers.Add(drag);
        
        // PointerUp
        var pointerUp = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        pointerUp.callback.AddListener((data) => OnActionPointerUp((PointerEventData)data));
        eventTrigger.triggers.Add(pointerUp);
    }

    private void OnDisable() => IsForceMobile = false;

    protected override void OnDestroy()
    {
        base.OnDestroy();
        
        if (_localWeapon != null)
        {
            _localWeapon.OnWeaponChanged -= OnWeaponChanged;
        }
        
        IsForceMobile = false; 
    }

    protected override void Update()
    {
        base.Update();
        
        if (_localPlayer == null)
        {
            FindLocalPlayer();
        }
        else if (_localPlayer != null)
        {
            UpdateDashCooldown();
            if (_currentMode == ActionButtonMode.Skill)
                UpdateSkillCooldown();
        }
    }

    private void OnWeaponChanged(WeaponType weaponType)
    {
        UpdateButtonMode();
    }

    private void FindLocalPlayer()
    {
        if (FishNet.InstanceFinder.ClientManager == null) return;
        
        foreach (var pc in FindObjectsOfType<PlayerController>())
        {
            if (pc.IsOwner)
            {
                _localPlayer = pc;
                _localWeapon = pc.GetComponent<NetworkedWeapon>();
                _inputHandler = pc.GetComponent<PlayerInputHandler>();
                
                if (_localWeapon != null)
                {
                    _localWeapon.OnWeaponChanged += OnWeaponChanged;
                    UpdateButtonMode();
                }
                break;
            }
        }
    }

    #region Action Button

    private void UpdateButtonMode()
    {
        if (_actionButton == null) return;
        
        var newMode = DetermineButtonMode();
        _currentMode = newMode;
        ApplyButtonMode();
    }

    private ActionButtonMode DetermineButtonMode()
    {
        if (_localWeapon == null || _localWeapon.CurrentWeaponData == null)
            return ActionButtonMode.Hidden;

        if (_localWeapon.CurrentWeaponData is MeleeWeaponData meleeData)
            return meleeData.HasSkill ? ActionButtonMode.Skill : ActionButtonMode.Hidden;

        return ActionButtonMode.Reload;
    }

    private void ApplyButtonMode()
    {
        switch (_currentMode)
        {
            case ActionButtonMode.Hidden:
                SetActionButtonInteractable(false);
                _actionButton.gameObject.SetActive(false);
                break;

            case ActionButtonMode.Reload:
                _actionButton.gameObject.SetActive(true);
                SetActionButtonInteractable(true);
                if (_actionButtonIcon != null) _actionButtonIcon.sprite = _originalButtonSprite;
                if (_actionCooldownFill != null) _actionCooldownFill.fillAmount = 0f;
                break;

            case ActionButtonMode.Skill:
                _actionButton.gameObject.SetActive(true);
                SetActionButtonInteractable(true);
                if (_actionButtonIcon != null && _localWeapon.CurrentWeaponData is MeleeWeaponData meleeData)
                {
                    var icon = meleeData.SkillData?.SkillIcon;
                    _actionButtonIcon.sprite = icon != null ? icon : _defaultSkillIcon;
                }
                break;
        }
    }

    private void SetActionButtonInteractable(bool interactable)
    {
        if (_actionButton == null)
            return;

        _actionButton.interactable = interactable;

        if (_actionButton.targetGraphic != null)
            _actionButton.targetGraphic.raycastTarget = interactable;
    }

    private void UpdateSkillCooldown()
    {
        if (_actionCooldownFill == null || _localWeapon == null) return;
        if (_localWeapon.CurrentWeaponData is not MeleeWeaponData meleeData || !meleeData.HasSkill) return;

        float remaining = _localWeapon.SkillCooldownRemainingTime;
        float cooldown = meleeData.SkillData.Cooldown;
        _actionCooldownFill.fillAmount = remaining <= 0f ? 0f : remaining / cooldown;
    }

    #endregion

    #region Pointer Events

    private void OnActionPointerDown(PointerEventData eventData)
    {
        if (_currentMode == ActionButtonMode.Hidden) return;

        _isActionButtonPressed = true;
        _pointerDownPos = eventData.position;
        _isDragging = false;
        _currentDragDir = Vector2.zero;

        if (_currentMode == ActionButtonMode.Skill)
            _inputHandler?.SetSkillHeld(true);
    }

    private void OnActionDrag(PointerEventData eventData)
    {
        if (!_isActionButtonPressed || _currentMode != ActionButtonMode.Skill) return;

        Vector2 delta = eventData.position - _pointerDownPos;
        
        if (delta.magnitude > _dragThreshold)
        {
            _isDragging = true;
            _currentDragDir = delta.normalized;
            _inputHandler?.SetSkillAimDirection(new Vector3(_currentDragDir.x, 0f, _currentDragDir.y));
        }
    }

    private void OnActionPointerUp(PointerEventData eventData)
    {
        if (!_isActionButtonPressed) return;
        _isActionButtonPressed = false;

        if (_inputHandler == null) return;

        if (_currentMode == ActionButtonMode.Skill)
        {
            _inputHandler.SetSkillHeld(false);
            if (_isDragging && _currentDragDir.sqrMagnitude > 0.01f)
                _inputHandler.SetSkillAimDirection(new Vector3(_currentDragDir.x, 0f, _currentDragDir.y));
            _inputHandler.TriggerSkillRelease();
        }
        else if (_currentMode == ActionButtonMode.Reload)
        {
            _inputHandler.TriggerReload();
        }

        _isDragging = false;
        _currentDragDir = Vector2.zero;
    }

    #endregion

    #region Dash

    private void UpdateDashCooldown()
    {
        bool isUnavailable = !_localPlayer.CanDash;
        uint currentTick = FishNet.InstanceFinder.TimeManager.LocalTick;
        bool isOnCooldown = currentTick < _localPlayer.DashCooldownEndTick;

        if (_isDashCooldown != isUnavailable)
        {
            _isDashCooldown = isUnavailable;
            if (_dashButtonImage != null)
            {
                _dashButtonImage.color = isUnavailable ? _dashDisabledColor : _dashNormalColor;
                _dashButtonImage.raycastTarget = !isUnavailable;
            }
        }

        if (_dashCooldownFill != null)
        {
            if (isOnCooldown)
            {
                float remainingTicks = _localPlayer.DashCooldownEndTick - currentTick;
                float totalTicks = _localPlayer.GetDashCooldown() * _localPlayer.TickRate;
                _dashCooldownFill.fillAmount = totalTicks > 0 ? remainingTicks / totalTicks : 0f;
            }
            else
            {
                _dashCooldownFill.fillAmount = 0f;
            }
        }
    }

    #endregion

    private void CheckMobilePlatform()
    {
        bool isMobile = Application.isMobilePlatform;
#if UNITY_EDITOR
        if (_isForceMobile) isMobile = true;
#endif
        IsForceMobile = isMobile;
    }
}
