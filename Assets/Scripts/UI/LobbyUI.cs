using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using ProjectVoid.Map;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 로비 모드 선택 및 커스텀 방 설정 UI를 관리합니다.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    #region Constants

    private const string MATCHMAKING_SCENE_NAME = "Matchmaking";
    private const string LOGIN_SCENE_NAME = "Login";
    private const string EMPTY_MAP_LABEL = "템플릿 없음";
    private const int ROOM_CODE_LENGTH = 6;
    private const int MIN_CUSTOM_PLAYERS = 2;
    private const int MAX_CUSTOM_PLAYERS = 8;
    private const int MIN_CUSTOM_GAME_TIME_MINUTES = 3;
    private const int MAX_CUSTOM_GAME_TIME_MINUTES = 15;
    private const int DEFAULT_CUSTOM_GAME_TIME_MINUTES = 10;
    private const float DEFAULT_RESTRICTION_MESSAGE_FADE_IN_SECONDS = 0.24f;
    private const float DEFAULT_RESTRICTION_MESSAGE_VISIBLE_SECONDS = 2.0f;
    private const float DEFAULT_RESTRICTION_MESSAGE_FADE_OUT_SECONDS = 0.32f;
    private const float NETWORK_STATE_POLL_INTERVAL_SECONDS = 1f;
    private const string GUEST_RANKED_BLOCKED_MESSAGE = "게스트는 랭크 매칭을 사용할 수 없습니다. Google 또는 Apple 로그인 후 이용해 주세요.";
    private const string OFFLINE_PRACTICE_ONLY_MESSAGE = "오프라인 상태에서는 연습장 모드만 시작할 수 있습니다.";

    #endregion

    #region Serialized Fields

    [Header("Mode Switch")]
    [SerializeField] private UISwitchButton _modeSwitch;
    [FormerlySerializedAs("_typeSwitch")]
    [SerializeField] private UISwitchButton _subSwitch;
    [FormerlySerializedAs("_typeSwitchRoot")]
    [SerializeField] private GameObject _subSwitchRoot;
    [SerializeField] private bool _preserveSubSwitchLayoutSpace = true;
    [SerializeField] private CanvasGroup _subSwitchCanvasGroup;
    [SerializeField] private Button _matchStartButton;
    [Header("Restriction Message")]
    [SerializeField] private TMP_Text _modeRestrictionText;
    [SerializeField] private bool _forceRestrictionMessageToScreenCenter = true;
    [SerializeField] private Vector2 _restrictionMessageCenterOffset = Vector2.zero;
    [SerializeField] [Min(0f)] private float _restrictionMessageFadeInSeconds = DEFAULT_RESTRICTION_MESSAGE_FADE_IN_SECONDS;
    [SerializeField] [Min(0f)] private float _restrictionMessageVisibleSeconds = DEFAULT_RESTRICTION_MESSAGE_VISIBLE_SECONDS;
    [SerializeField] [Min(0f)] private float _restrictionMessageFadeOutSeconds = DEFAULT_RESTRICTION_MESSAGE_FADE_OUT_SECONDS;

    [Header("Sub Switch Labels")]
    [SerializeField] private string _normalSubFirstLabel = "신속";
    [SerializeField] private string _normalSubSecondLabel = "클래식";
    [SerializeField] private string _rankSubLabel = "랭크(8인)";
    [SerializeField] private string _customSubFirstLabel = "방 만들기";
    [SerializeField] private string _customSubSecondLabel = "방 참가";

    [Header("Custom Panels")]
    [SerializeField] private GameObject _roomSettingPanel;
    [SerializeField] private GameObject _roomCodePanel;

    [Header("Custom Player Count")]
    [SerializeField] private TMP_Text _customPlayerCountText;
    [SerializeField] private Button _customPlayerCountLeftButton;
    [SerializeField] private Button _customPlayerCountRightButton;

    [Header("Custom Game Time")]
    [SerializeField] private TMP_Text _customGameTimeText;
    [SerializeField] private Button _customGameTimeLeftButton;
    [SerializeField] private Button _customGameTimeRightButton;

    [Header("Custom Map")]
    [SerializeField] private TMP_Text _customMapText;
    [SerializeField] private Button _customMapLeftButton;
    [SerializeField] private Button _customMapRightButton;

    [Header("Custom Map Preview")]
    [SerializeField] private RawImage _customMapPreviewImage;
    [SerializeField] private Color _previewEmptyColor = new(0.12f, 0.12f, 0.12f, 1f);
    [SerializeField] private Color _previewShapeColor = new(0.85f, 0.85f, 0.85f, 1f);
    [SerializeField] [Range(1, 16)] private int _previewPixelsPerCell = 8;
    [SerializeField] private Color _previewGridLineColor = new(0.05f, 0.05f, 0.05f, 1f);

    [Header("Custom Options")]
    [SerializeField] private Toggle _practiceToggle;

    [Header("Custom Join")]
    [SerializeField] private TMP_InputField _roomCodeInputField;

    [Header("References")]
    [SerializeField] private MapGenerationSettings _mapGenerationSettings;
    [SerializeField] private MatchmakingManager _matchmakingManager;

    [Header("사운드")]
    [SerializeField] private AudioCue _uiClickAudioCue;
    [SerializeField] private AudioCue _selectionAudioCue;

    #endregion

    #region Enums

    private enum MainModeSelection
    {
        Normal = 0,
        Ranked = 1,
        Custom = 2
    }

    private enum NormalModeSelection
    {
        FourPlayer = 0,
        EightPlayer = 1
    }

    private enum CustomModeSelection
    {
        CreateRoom = 0,
        JoinRoom = 1
    }

    #endregion

    #region Private Fields

    private MainModeSelection _mainModeSelection = MainModeSelection.Normal;
    private NormalModeSelection _normalModeSelection = NormalModeSelection.FourPlayer;
    private CustomModeSelection _customModeSelection = CustomModeSelection.CreateRoom;

    private int _selectedCustomPlayerCount = 4;
    private int _selectedCustomGameTimeMinutes = DEFAULT_CUSTOM_GAME_TIME_MINUTES;
    private int _selectedCustomMapIndex;
    private readonly List<string> _customMapOptions = new();
    private Texture2D _customMapPreviewTexture;
    private Coroutine _subSwitchLayoutRefreshCoroutine;
    private Sequence _restrictionMessageSequence;
    private IAuthService _authService;
    private Task _unityLobbyPrewarmTask;
    private float _nextNetworkStatePollTime;
    private NetworkReachability _lastNetworkReachability;
    private bool _suppressSelectionSound;
    private static readonly Color[] _previewChunkGroupPalette =
    {
        new(1f, 0.5f, 0.5f, 1f),
        new(0.5f, 1f, 0.5f, 1f),
        new(0.5f, 0.5f, 1f, 1f),
        new(1f, 1f, 0.5f, 1f),
        new(1f, 0.5f, 1f, 1f),
        new(0.5f, 1f, 1f, 1f),
        new(1f, 0.7f, 0.5f, 1f),
        new(0.7f, 0.5f, 1f, 1f),
        new(0.5f, 1f, 0.7f, 1f)
    };

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        if (!ValidateSerializedReferences())
        {
            enabled = false;
            return;
        }

        _suppressSelectionSound = true;
        FindMatchmakingManager();
        if (_matchmakingManager != null)
        {
            _matchmakingManager.ResetForLobbyEntry();
        }
        ResolveAuthService();
        RegisterEventHandlers();
        InitializeState();
        _lastNetworkReachability = Application.internetReachability;
        ShowPendingLobbyRestrictionMessageIfAny();
        StartUnityLobbyPrewarmIfNeeded();
        StartCoroutine(ReleaseInitialSelectionSoundSuppression());
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextNetworkStatePollTime)
        {
            return;
        }

        _nextNetworkStatePollTime = Time.unscaledTime + NETWORK_STATE_POLL_INTERVAL_SECONDS;

        NetworkReachability currentReachability = Application.internetReachability;
        if (_lastNetworkReachability == currentReachability)
        {
            return;
        }

        _lastNetworkReachability = currentReachability;
        RefreshUIState();

        if (currentReachability == NetworkReachability.NotReachable && !IsOfflinePracticeSelection())
        {
            ShowRestrictionMessage(OFFLINE_PRACTICE_ONLY_MESSAGE);
        }
    }

    private void OnDestroy()
    {
        UnregisterEventHandlers();

        if (_authService != null)
        {
            _authService.OnAuthStateChanged -= HandleAuthStateChanged;
            _authService = null;
        }

        if (_subSwitchLayoutRefreshCoroutine != null)
        {
            StopCoroutine(_subSwitchLayoutRefreshCoroutine);
            _subSwitchLayoutRefreshCoroutine = null;
        }

        StopRestrictionMessageAnimation();
        ReleaseCustomMapPreviewTexture();
    }

    #endregion

    #region Initialization

    private bool ValidateSerializedReferences()
    {
        bool isValid = true;

        if (_modeSwitch == null)
        {
            Debug.LogError("[LobbyUI] ModeSwitch 참조가 비어 있습니다.");
            isValid = false;
        }

        if (_subSwitch == null)
        {
            Debug.LogError("[LobbyUI] SubSwitch 참조가 비어 있습니다.");
            isValid = false;
        }

        if (_subSwitchRoot == null)
        {
            Debug.LogError("[LobbyUI] SubSwitchRoot 참조가 비어 있습니다.");
            isValid = false;
        }

        if (_matchStartButton == null)
        {
            Debug.LogError("[LobbyUI] MatchStartButton 참조가 비어 있습니다.");
            isValid = false;
        }

        if (_roomSettingPanel == null)
        {
            Debug.LogError("[LobbyUI] RoomSettingPanel 참조가 비어 있습니다.");
            isValid = false;
        }

        if (_roomCodePanel == null)
        {
            Debug.LogError("[LobbyUI] RoomCodePanel 참조가 비어 있습니다.");
            isValid = false;
        }

        if (_practiceToggle == null)
        {
            Debug.LogError("[LobbyUI] PracticeToggle 참조가 비어 있습니다.");
            isValid = false;
        }

        if (_roomCodeInputField == null)
        {
            Debug.LogError("[LobbyUI] RoomCodeInputField 참조가 비어 있습니다.");
            isValid = false;
        }

        if (_mapGenerationSettings == null)
        {
            Debug.LogError("[LobbyUI] MapGenerationSettings 참조가 비어 있습니다.");
            isValid = false;
        }

        if (_customGameTimeText == null)
        {
            Debug.LogWarning("[LobbyUI] CustomGameTimeText 참조가 비어 있습니다. 시간 텍스트가 갱신되지 않습니다.");
        }

        if (_customGameTimeLeftButton == null || _customGameTimeRightButton == null)
        {
            Debug.LogWarning("[LobbyUI] CustomGameTime 좌/우 버튼 참조가 비어 있습니다. 인스펙터에서 연결하거나 OnClick에 공개 메서드를 직접 바인딩하세요.");
        }

        return isValid;
    }

    private void FindMatchmakingManager()
    {
        _matchmakingManager = MatchmakingManager.Instance;

        if (_matchmakingManager == null)
        {
            _matchmakingManager = FindFirstObjectByType<MatchmakingManager>();
        }

        if (_matchmakingManager == null)
        {
            Debug.LogError("[LobbyUI] MatchmakingManager를 찾을 수 없습니다.");
        }
    }

    private void ResolveAuthService()
    {
        if (!ServiceLocator.TryGet<IAuthService>(out _authService) || _authService == null)
        {
            _authService = null;
            Debug.LogWarning("[LobbyUI] AuthService를 찾지 못해 게스트 모드 제한을 적용할 수 없습니다.");
            return;
        }

        _authService.OnAuthStateChanged -= HandleAuthStateChanged;
        _authService.OnAuthStateChanged += HandleAuthStateChanged;
    }

    private void InitializeState()
    {
        _mainModeSelection = GetMainModeSelection(_modeSwitch != null ? _modeSwitch.CurrentIndex : 0);
        ConfigureRestrictionMessagePresentation();
        HideRestrictionMessageImmediate();

        ApplySubSwitchLabelsByMainMode();
        _normalModeSelection = GetNormalModeSelection(_subSwitch != null ? _subSwitch.CurrentIndex : 0);
        _customModeSelection = GetCustomModeSelection(_subSwitch != null ? _subSwitch.CurrentIndex : 0);

        _selectedCustomPlayerCount = Mathf.Clamp(_selectedCustomPlayerCount, MIN_CUSTOM_PLAYERS, MAX_CUSTOM_PLAYERS);
        _selectedCustomGameTimeMinutes = Mathf.Clamp(_selectedCustomGameTimeMinutes, MIN_CUSTOM_GAME_TIME_MINUTES, MAX_CUSTOM_GAME_TIME_MINUTES);

        RefreshCustomMapOptions(false);
        ApplySubSwitchSelectionImmediate();
        RefreshCustomSettingTexts();
        RefreshUIState();
        ScheduleSubSwitchLayoutRefresh();
    }

    #endregion

    #region Event Registration

    private void RegisterEventHandlers()
    {
        if (_modeSwitch != null)
        {
            _modeSwitch.onValueChanged.AddListener(OnMainModeSwitchChanged);
        }

        if (_subSwitch != null)
        {
            _subSwitch.onValueChanged.AddListener(OnSubSwitchChanged);
        }

        if (_matchStartButton != null)
        {
            _matchStartButton.onClick.AddListener(OnStartClicked);
        }

        if (_practiceToggle != null)
        {
            _practiceToggle.onValueChanged.AddListener(OnPracticeToggleChanged);
        }

        if (_roomCodeInputField != null)
        {
            _roomCodeInputField.characterLimit = ROOM_CODE_LENGTH;
            _roomCodeInputField.onValueChanged.AddListener(OnRoomCodeInputChanged);
        }

        _customPlayerCountLeftButton?.onClick.AddListener(OnCustomPlayerCountLeftClicked);
        _customPlayerCountRightButton?.onClick.AddListener(OnCustomPlayerCountRightClicked);
        _customGameTimeLeftButton?.onClick.AddListener(OnCustomGameTimeLeftClicked);
        _customGameTimeRightButton?.onClick.AddListener(OnCustomGameTimeRightClicked);
        _customMapLeftButton?.onClick.AddListener(OnCustomMapLeftClicked);
        _customMapRightButton?.onClick.AddListener(OnCustomMapRightClicked);
    }

    private void UnregisterEventHandlers()
    {
        if (_modeSwitch != null)
        {
            _modeSwitch.onValueChanged.RemoveListener(OnMainModeSwitchChanged);
        }

        if (_subSwitch != null)
        {
            _subSwitch.onValueChanged.RemoveListener(OnSubSwitchChanged);
        }

        if (_matchStartButton != null)
        {
            _matchStartButton.onClick.RemoveListener(OnStartClicked);
        }

        if (_practiceToggle != null)
        {
            _practiceToggle.onValueChanged.RemoveListener(OnPracticeToggleChanged);
        }

        if (_roomCodeInputField != null)
        {
            _roomCodeInputField.onValueChanged.RemoveListener(OnRoomCodeInputChanged);
        }

        _customPlayerCountLeftButton?.onClick.RemoveListener(OnCustomPlayerCountLeftClicked);
        _customPlayerCountRightButton?.onClick.RemoveListener(OnCustomPlayerCountRightClicked);
        _customGameTimeLeftButton?.onClick.RemoveListener(OnCustomGameTimeLeftClicked);
        _customGameTimeRightButton?.onClick.RemoveListener(OnCustomGameTimeRightClicked);
        _customMapLeftButton?.onClick.RemoveListener(OnCustomMapLeftClicked);
        _customMapRightButton?.onClick.RemoveListener(OnCustomMapRightClicked);
    }

    #endregion

    #region UI Events

    private void OnMainModeSwitchChanged(int index)
    {
        PlayUiSelectionSound();
        MainModeSelection previousMode = _mainModeSelection;
        MainModeSelection nextMode = GetMainModeSelection(index);

        if (IsGuestSignedIn() && nextMode == MainModeSelection.Ranked)
        {
            Debug.LogWarning("[LobbyUI] 게스트 계정의 랭크 모드 선택을 차단했습니다.");
            ShowRestrictionMessage(GUEST_RANKED_BLOCKED_MESSAGE);
            RestoreMainModeSelection(previousMode);
            return;
        }

        _mainModeSelection = nextMode;

        ApplySubSwitchLabelsByMainMode();
        ApplySubSwitchSelectionImmediate();
        RefreshUIState();
        ScheduleSubSwitchLayoutRefresh();
    }

    private void OnSubSwitchChanged(int index)
    {
        PlayUiSelectionSound();
        if (_mainModeSelection == MainModeSelection.Normal)
        {
            _normalModeSelection = GetNormalModeSelection(index);
        }
        else if (_mainModeSelection == MainModeSelection.Custom)
        {
            _customModeSelection = GetCustomModeSelection(index);
        }

        RefreshUIState();
    }

    private void OnPracticeToggleChanged(bool isOn)
    {
        PlayUiSelectionSound();
        RefreshCustomMapOptions(true);
        RefreshUIState();
    }

    private void OnRoomCodeInputChanged(string value)
    {
        if (_roomCodeInputField == null)
        {
            return;
        }

        string normalized = NormalizeRoomCode(value);
        if (normalized == value)
        {
            return;
        }

        _roomCodeInputField.text = normalized;
        _roomCodeInputField.caretPosition = normalized.Length;
    }

    private void OnCustomPlayerCountLeftClicked()
    {
        PlayUiSelectionSound();
        _selectedCustomPlayerCount = Mathf.Max(MIN_CUSTOM_PLAYERS, _selectedCustomPlayerCount - 1);
        RefreshCustomMapOptions(true);
        RefreshCustomSettingTexts();
    }

    private void OnCustomPlayerCountRightClicked()
    {
        PlayUiSelectionSound();
        _selectedCustomPlayerCount = Mathf.Min(MAX_CUSTOM_PLAYERS, _selectedCustomPlayerCount + 1);
        RefreshCustomMapOptions(true);
        RefreshCustomSettingTexts();
    }

    private void OnCustomMapLeftClicked()
    {
        if (_customMapOptions.Count <= 1)
        {
            return;
        }

        PlayUiSelectionSound();
        _selectedCustomMapIndex = (_selectedCustomMapIndex - 1 + _customMapOptions.Count) % _customMapOptions.Count;
        RefreshCustomSettingTexts();
    }

    private void OnCustomMapRightClicked()
    {
        if (_customMapOptions.Count <= 1)
        {
            return;
        }

        PlayUiSelectionSound();
        _selectedCustomMapIndex = (_selectedCustomMapIndex + 1) % _customMapOptions.Count;
        RefreshCustomSettingTexts();
    }

    public void OnCustomGameTimeLeftClicked()
    {
        if (IsPracticeMapLocked())
        {
            return;
        }

        PlayUiSelectionSound();
        _selectedCustomGameTimeMinutes = Mathf.Max(MIN_CUSTOM_GAME_TIME_MINUTES, _selectedCustomGameTimeMinutes - 1);
        RefreshCustomSettingTexts();
    }

    public void OnCustomGameTimeRightClicked()
    {
        if (IsPracticeMapLocked())
        {
            return;
        }

        PlayUiSelectionSound();
        _selectedCustomGameTimeMinutes = Mathf.Min(MAX_CUSTOM_GAME_TIME_MINUTES, _selectedCustomGameTimeMinutes + 1);
        RefreshCustomSettingTexts();
    }

    public void OnStartClicked()
    {
        PlayUiClickSound();
        if (!EnsureAuthenticatedOrRedirect())
        {
            return;
        }

        if (!CanStartWithCurrentNetworkState())
        {
            return;
        }

        if (_matchmakingManager == null || !_matchmakingManager)
        {
            FindMatchmakingManager();
        }

        if (_matchmakingManager == null)
        {
            Debug.LogError("[LobbyUI] MatchmakingManager 참조가 없어 매칭을 시작할 수 없습니다.");
            return;
        }

        switch (_mainModeSelection)
        {
            case MainModeSelection.Normal:
                StartNormalMode();
                return;

            case MainModeSelection.Ranked:
                if (IsGuestSignedIn())
                {
                    Debug.LogWarning("[LobbyUI] 게스트 계정의 랭크 매칭 시작을 차단했습니다.");
                    ShowRestrictionMessage(GUEST_RANKED_BLOCKED_MESSAGE);
                    return;
                }

                _matchmakingManager.PrepareMatchmaking(GameMode.Ranked);
                NavigateToMatchmakingScene();
                return;

            case MainModeSelection.Custom:
                StartCustomMode();
                return;
        }
    }

    private void PlayUiClickSound()
    {
        AudioManager.Instance?.PlayUi(_uiClickAudioCue);
    }

    private void PlayUiSelectionSound()
    {
        if (_suppressSelectionSound)
        {
            return;
        }

        AudioManager.Instance?.PlayUi(_selectionAudioCue ?? _uiClickAudioCue);
    }

    #endregion

    #region Start Flow

    private void StartNormalMode()
    {
        GameMode mode = _normalModeSelection == NormalModeSelection.EightPlayer
            ? GameMode.EightPlayer
            : GameMode.FourPlayer;

        _matchmakingManager.PrepareMatchmaking(mode);
        NavigateToMatchmakingScene();
    }

    private void StartCustomMode()
    {
        if (_customModeSelection == CustomModeSelection.JoinRoom)
        {
            StartCustomJoinMode();
            return;
        }

        if (IsPracticeChecked())
        {
            _matchmakingManager.PreparePracticeMode(GetSelectedMapTemplateName());
            NavigateToMatchmakingScene();
            return;
        }

        CustomRoomSettings customSettings = new CustomRoomSettings
        {
            IsEnabled = true,
            PlayerCount = _selectedCustomPlayerCount,
            GameTimeSeconds = ResolveCustomGameTimeSeconds(),
            MapTemplateName = GetSelectedMapTemplateName()
        };

        _matchmakingManager.PrepareCustomRoom(customSettings);
        NavigateToMatchmakingScene();
    }

    private void StartCustomJoinMode()
    {
        string roomCode = NormalizeRoomCode(_roomCodeInputField != null ? _roomCodeInputField.text : string.Empty);
        if (roomCode.Length != ROOM_CODE_LENGTH)
        {
            Debug.LogWarning("[LobbyUI] 방 코드는 6자리여야 합니다.");
            return;
        }

        _matchmakingManager.PrepareJoinRoom(roomCode);
        NavigateToMatchmakingScene();
    }

    private void NavigateToMatchmakingScene()
    {
        SceneManager.LoadScene(MATCHMAKING_SCENE_NAME);
    }

    private bool EnsureAuthenticatedOrRedirect()
    {
        if (!ServiceLocator.TryGet<IAuthService>(out IAuthService authService))
        {
            Debug.LogWarning("[LobbyUI] AuthService가 등록되지 않아 로그인 씬으로 이동합니다.");
            if (Application.CanStreamedLevelBeLoaded(LOGIN_SCENE_NAME))
            {
                SceneManager.LoadScene(LOGIN_SCENE_NAME);
            }

            return false;
        }

        if (authService.IsSignedIn)
        {
            return true;
        }

        Debug.LogWarning("[LobbyUI] 로그인 상태가 아니어서 로그인 씬으로 이동합니다.");
        if (Application.CanStreamedLevelBeLoaded(LOGIN_SCENE_NAME))
        {
            SceneManager.LoadScene(LOGIN_SCENE_NAME);
        }

        return false;
    }

    private bool CanStartWithCurrentNetworkState()
    {
        if (Application.internetReachability != NetworkReachability.NotReachable)
        {
            return true;
        }

        if (IsOfflinePracticeSelection())
        {
            return true;
        }

        ShowRestrictionMessage(OFFLINE_PRACTICE_ONLY_MESSAGE);

        Debug.LogWarning("[LobbyUI] 오프라인 상태에서는 연습장 모드만 시작할 수 있습니다.");
        return false;
    }

    #endregion

    #region UI State

    private void RefreshUIState()
    {
        ApplySubSwitchVisibility(true);

        bool showRoomSettingPanel = _mainModeSelection == MainModeSelection.Custom &&
                                    _customModeSelection == CustomModeSelection.CreateRoom;
        bool showRoomCodePanel = _mainModeSelection == MainModeSelection.Custom &&
                                 _customModeSelection == CustomModeSelection.JoinRoom;

        if (_roomSettingPanel != null)
        {
            _roomSettingPanel.SetActive(showRoomSettingPanel);
        }

        if (_roomCodePanel != null)
        {
            _roomCodePanel.SetActive(showRoomCodePanel);
        }

        UpdatePracticeLockState();
        ApplyGuestModeRestrictions();
        UpdateMatchStartButtonState();
        RefreshCustomSettingTexts();
    }

    private void ApplySubSwitchSelectionImmediate()
    {
        if (_subSwitch == null)
        {
            return;
        }

        int targetIndex = 0;

        if (_mainModeSelection == MainModeSelection.Normal)
        {
            targetIndex = (int)_normalModeSelection;
        }
        else if (_mainModeSelection == MainModeSelection.Custom)
        {
            targetIndex = (int)_customModeSelection;
        }

        RunWithoutSelectionSound(() => _subSwitch.SelectOption(targetIndex, true));
    }

    private void ApplySubSwitchLabelsByMainMode()
    {
        if (_subSwitch == null)
        {
            return;
        }

        if (_mainModeSelection == MainModeSelection.Ranked)
        {
            RunWithoutSelectionSound(() => _subSwitch.SetSingleOptionText(_rankSubLabel));
            return;
        }

        if (_mainModeSelection == MainModeSelection.Custom)
        {
            RunWithoutSelectionSound(() => _subSwitch.SetOptionTexts(
                _customSubFirstLabel,
                _customSubSecondLabel,
                (int)_customModeSelection));
            return;
        }

        RunWithoutSelectionSound(() => _subSwitch.SetOptionTexts(
            _normalSubFirstLabel,
            _normalSubSecondLabel,
            (int)_normalModeSelection));
    }

    private void ScheduleSubSwitchLayoutRefresh()
    {
        if (_subSwitch == null || !isActiveAndEnabled)
        {
            return;
        }

        if (_subSwitchLayoutRefreshCoroutine != null)
        {
            StopCoroutine(_subSwitchLayoutRefreshCoroutine);
        }

        _subSwitchLayoutRefreshCoroutine = StartCoroutine(RefreshSubSwitchLayoutNextFrame());
    }

    private IEnumerator RefreshSubSwitchLayoutNextFrame()
    {
        yield return null;

        if (_subSwitch == null)
        {
            _subSwitchLayoutRefreshCoroutine = null;
            yield break;
        }

        Canvas.ForceUpdateCanvases();
        RunWithoutSelectionSound(_subSwitch.RestoreLayout);
        ApplyGuestModeRestrictions();
        _subSwitchLayoutRefreshCoroutine = null;
    }

    private void RefreshCustomMapOptions(bool keepCurrentSelection)
    {
        string currentMapName = keepCurrentSelection ? GetCurrentMapOption() : string.Empty;

        _customMapOptions.Clear();

        if (_mapGenerationSettings != null)
        {
            MapTemplateSet[] templateSets = _mapGenerationSettings.TemplateSets;
            var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (templateSets != null)
            {
                GameMode targetGameMode = IsPracticeMapLocked()
                    ? GameMode.PracticeRange
                    : GameMode.Custom;

                for (int i = 0; i < templateSets.Length; i++)
                {
                    MapTemplateSet templateSet = templateSets[i];
                    if (templateSet.GameMode != targetGameMode)
                    {
                        continue;
                    }

                    MapLayoutTemplate[] templates = templateSet.Templates;
                    if (templates == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < templates.Length; j++)
                    {
                        MapLayoutTemplate template = templates[j];
                        if (template == null || string.IsNullOrWhiteSpace(template.TemplateName))
                        {
                            continue;
                        }

                        if (uniqueNames.Add(template.TemplateName))
                        {
                            _customMapOptions.Add(template.TemplateName);
                        }
                    }
                }
            }
        }

        if (_customMapOptions.Count == 0)
        {
            _customMapOptions.Add(EMPTY_MAP_LABEL);
        }

        if (keepCurrentSelection && !string.IsNullOrWhiteSpace(currentMapName))
        {
            int foundIndex = _customMapOptions.FindIndex(x => string.Equals(x, currentMapName, StringComparison.OrdinalIgnoreCase));
            _selectedCustomMapIndex = foundIndex >= 0 ? foundIndex : 0;
        }
        else
        {
            _selectedCustomMapIndex = 0;
        }
    }

    private void RefreshCustomSettingTexts()
    {
        if (_customPlayerCountText != null)
        {
            _customPlayerCountText.text = IsPracticeMapLocked() ? "1명" : $"{_selectedCustomPlayerCount}명";
        }

        if (_customGameTimeText != null)
        {
            _customGameTimeText.text = IsPracticeMapLocked() ? "0분" : $"{_selectedCustomGameTimeMinutes}분";
        }

        if (_customMapText != null)
        {
            _customMapText.text = GetCurrentMapOption();
        }

        UpdateCustomMapPreview();
        UpdateCustomNavigationButtons();
    }

    private void UpdatePracticeLockState()
    {
        bool isLock = IsPracticeMapLocked();

        SetButtonInteractable(_customPlayerCountLeftButton, !isLock);
        SetButtonInteractable(_customPlayerCountRightButton, !isLock);
        SetButtonInteractable(_customGameTimeLeftButton, !isLock);
        SetButtonInteractable(_customGameTimeRightButton, !isLock);

        bool canChangeMap = _customMapOptions.Count > 1;
        SetButtonInteractable(_customMapLeftButton, canChangeMap);
        SetButtonInteractable(_customMapRightButton, canChangeMap);

        SetGraphicAlpha(_customPlayerCountText, isLock ? 0.6f : 1f);
        SetGraphicAlpha(_customGameTimeText, isLock ? 0.6f : 1f);
        SetGraphicAlpha(_customMapText, isLock ? 0.6f : 1f);

        UpdateCustomNavigationButtons();
    }

    private void UpdateCustomNavigationButtons()
    {
        bool isLock = IsPracticeMapLocked();

        SetButtonInteractable(_customPlayerCountLeftButton, !isLock && _selectedCustomPlayerCount > MIN_CUSTOM_PLAYERS);
        SetButtonInteractable(_customPlayerCountRightButton, !isLock && _selectedCustomPlayerCount < MAX_CUSTOM_PLAYERS);
        SetButtonInteractable(_customGameTimeLeftButton, !isLock && _selectedCustomGameTimeMinutes > MIN_CUSTOM_GAME_TIME_MINUTES);
        SetButtonInteractable(_customGameTimeRightButton, !isLock && _selectedCustomGameTimeMinutes < MAX_CUSTOM_GAME_TIME_MINUTES);

        bool canChangeMap = _customMapOptions.Count > 1;
        SetButtonInteractable(_customMapLeftButton, canChangeMap);
        SetButtonInteractable(_customMapRightButton, canChangeMap);
    }

    #endregion

    #region Helper Methods

    private void HandleAuthStateChanged(AuthResult _)
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        ApplyGuestModeRestrictions();
        StartUnityLobbyPrewarmIfNeeded();
    }

    private void StartUnityLobbyPrewarmIfNeeded()
    {
        if (!ShouldPrewarmUnityLobby())
        {
            return;
        }

        if (UnityLobbyManager.Instance == null)
        {
            Debug.LogWarning("[LobbyUI] UnityLobbyManager 인스턴스가 없어 로비 사전 초기화를 건너뜁니다.");
            return;
        }

        if (_unityLobbyPrewarmTask != null && !_unityLobbyPrewarmTask.IsCompleted)
        {
            return;
        }

        _unityLobbyPrewarmTask = PrewarmUnityLobbyAsync();
    }

    private void ShowPendingLobbyRestrictionMessageIfAny()
    {
        if (MatchmakingManager.TryConsumePendingLobbyRestrictionMessage(out string message))
        {
            ShowRestrictionMessage(message);
        }
    }

    private bool ShouldPrewarmUnityLobby()
    {
        return _authService != null &&
               _authService.IsSignedIn &&
               Application.internetReachability != NetworkReachability.NotReachable;
    }

    private static async Task PrewarmUnityLobbyAsync()
    {
        try
        {
            UnityLobbyManager lobbyManager = UnityLobbyManager.Instance;
            if (lobbyManager == null)
            {
                return;
            }

            bool initialized = await lobbyManager.Initialize();
            if (initialized)
            {
                Debug.Log("[LobbyUI] UnityLobbyManager 사전 초기화 완료");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LobbyUI] UnityLobbyManager 사전 초기화 실패: {ex.Message}");
        }
    }

    private void ApplyGuestModeRestrictions()
    {
        bool isGuest = IsGuestSignedIn();
        // Why: 게스트가 랭크 버튼을 눌렀을 때 제한 메시지를 표시해야 하므로, 클릭 이벤트는 유지합니다.
        SetMainModeButtonInteractable((int)MainModeSelection.Ranked, true);

        if (isGuest) return;

        if (_modeRestrictionText != null && string.Equals(_modeRestrictionText.text, GUEST_RANKED_BLOCKED_MESSAGE, StringComparison.Ordinal))
        {
            HideRestrictionMessageImmediate();
        }
    }

    private void UpdateMatchStartButtonState()
    {
        bool canStart = Application.internetReachability != NetworkReachability.NotReachable ||
                        IsOfflinePracticeSelection();
        SetButtonInteractable(_matchStartButton, canStart);
    }

    private bool IsGuestSignedIn()
    {
        if (_authService == null || !_authService.IsSignedIn)
        {
            return false;
        }

        return _authService.IsAnonymous;
    }

    private void SetMainModeButtonInteractable(int modeIndex, bool interactable)
    {
        if (_modeSwitch == null)
        {
            return;
        }

        List<GameObject> modeButtons = _modeSwitch.SpawnedButtons;
        if (modeButtons == null || modeIndex < 0 || modeIndex >= modeButtons.Count)
        {
            return;
        }

        if (modeButtons[modeIndex] == null)
        {
            return;
        }

        Button targetButton = modeButtons[modeIndex].GetComponent<Button>();
        if (targetButton != null)
        {
            targetButton.interactable = interactable;
        }
    }

    private void RestoreMainModeSelection(MainModeSelection restoreMode)
    {
        _mainModeSelection = restoreMode;

        if (_modeSwitch != null)
        {
            RunWithoutSelectionSound(() => _modeSwitch.SelectOption((int)_mainModeSelection, true));
            return;
        }

        ApplySubSwitchLabelsByMainMode();
        ApplySubSwitchSelectionImmediate();
        RefreshUIState();
        ScheduleSubSwitchLayoutRefresh();
    }

    private void ConfigureRestrictionMessagePresentation()
    {
        if (_modeRestrictionText == null)
        {
            return;
        }

        _modeRestrictionText.alignment = TextAlignmentOptions.Center;

        if (_forceRestrictionMessageToScreenCenter)
        {
            RectTransform textRectTransform = _modeRestrictionText.rectTransform;
            textRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            textRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            textRectTransform.pivot = new Vector2(0.5f, 0.5f);
            textRectTransform.anchoredPosition = _restrictionMessageCenterOffset;
        }
    }

    private void RunWithoutSelectionSound(Action action)
    {
        if (action == null)
        {
            return;
        }

        bool previousSuppression = _suppressSelectionSound;
        _suppressSelectionSound = true;

        try
        {
            action();
        }
        finally
        {
            _suppressSelectionSound = previousSuppression;
        }
    }

    private IEnumerator ReleaseInitialSelectionSoundSuppression()
    {
        yield return null;
        _suppressSelectionSound = false;
    }

    private void ShowRestrictionMessage(string message)
    {
        if (_modeRestrictionText == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ConfigureRestrictionMessagePresentation();
        StopRestrictionMessageAnimation();

        _modeRestrictionText.gameObject.SetActive(true);
        _modeRestrictionText.text = message;
        _modeRestrictionText.alpha = 0f;

        float fadeInSeconds = Mathf.Max(0f, _restrictionMessageFadeInSeconds);
        float visibleSeconds = Mathf.Max(0f, _restrictionMessageVisibleSeconds);
        float fadeOutSeconds = Mathf.Max(0f, _restrictionMessageFadeOutSeconds);

        _restrictionMessageSequence = DOTween.Sequence().SetUpdate(true);
        _restrictionMessageSequence.Append(_modeRestrictionText.DOFade(1f, fadeInSeconds).SetEase(Ease.OutCubic));
        _restrictionMessageSequence.AppendInterval(visibleSeconds);
        _restrictionMessageSequence.Append(_modeRestrictionText.DOFade(0f, fadeOutSeconds).SetEase(Ease.InCubic));
        _restrictionMessageSequence.OnComplete(() =>
        {
            if (_modeRestrictionText != null && string.Equals(_modeRestrictionText.text, message, StringComparison.Ordinal))
            {
                _modeRestrictionText.text = string.Empty;
                _modeRestrictionText.gameObject.SetActive(false);
                _modeRestrictionText.alpha = 0f;
            }

            _restrictionMessageSequence = null;
        });
    }

    private void HideRestrictionMessageImmediate()
    {
        StopRestrictionMessageAnimation();

        if (_modeRestrictionText == null)
        {
            return;
        }

        _modeRestrictionText.text = string.Empty;
        _modeRestrictionText.alpha = 0f;
        _modeRestrictionText.gameObject.SetActive(false);
    }

    private void StopRestrictionMessageAnimation()
    {
        _restrictionMessageSequence?.Kill();
        _restrictionMessageSequence = null;
    }

    private string GetCurrentMapOption()
    {
        if (_customMapOptions.Count == 0)
        {
            return EMPTY_MAP_LABEL;
        }

        int clampedIndex = Mathf.Clamp(_selectedCustomMapIndex, 0, _customMapOptions.Count - 1);
        return _customMapOptions[clampedIndex];
    }

    private string GetSelectedMapTemplateName()
    {
        string mapName = GetCurrentMapOption();
        return string.Equals(mapName, EMPTY_MAP_LABEL, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : mapName;
    }

    private static string NormalizeRoomCode(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.ToUpperInvariant().Replace("-", string.Empty).Replace(" ", string.Empty).Trim();
    }

    private int ResolveCustomGameTimeSeconds()
    {
        int clampedMinutes = Mathf.Clamp(_selectedCustomGameTimeMinutes, MIN_CUSTOM_GAME_TIME_MINUTES, MAX_CUSTOM_GAME_TIME_MINUTES);
        return clampedMinutes * 60;
    }


    private bool IsPracticeChecked()
    {
        return _practiceToggle != null && _practiceToggle.isOn;
    }

    private bool IsPracticeMapLocked()
    {
        return IsPracticeChecked() &&
               _mainModeSelection == MainModeSelection.Custom &&
               _customModeSelection == CustomModeSelection.CreateRoom;
    }

    private bool IsOfflinePracticeSelection()
    {
        return _mainModeSelection == MainModeSelection.Custom &&
               _customModeSelection == CustomModeSelection.CreateRoom &&
               IsPracticeChecked();
    }

    private void UpdateCustomMapPreview()
    {
        if (_customMapPreviewImage == null)
        {
            return;
        }

        MapLayoutTemplate selectedTemplate = GetSelectedMapTemplate();
        if (selectedTemplate == null)
        {
            _customMapPreviewImage.texture = null;
            return;
        }

        int width = Mathf.Max(1, selectedTemplate.Width);
        int height = Mathf.Max(1, selectedTemplate.Height);
        int pixelsPerCell = Mathf.Max(1, _previewPixelsPerCell);

        int textureWidth = width * pixelsPerCell;
        int textureHeight = height * pixelsPerCell;

        EnsureCustomMapPreviewTexture(textureWidth, textureHeight);
        if (_customMapPreviewTexture == null)
        {
            _customMapPreviewImage.texture = null;
            return;
        }

        bool hasGridLine = pixelsPerCell > 1;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                GridCell cell = selectedTemplate.GetCell(x, y);
                int groupId = selectedTemplate.GetChunkGroupId(x, y);
                Color cellColor = GetPreviewCellColor(cell, groupId);

                int startX = x * pixelsPerCell;
                int startY = (height - 1 - y) * pixelsPerCell;

                for (int pixelY = 0; pixelY < pixelsPerCell; pixelY++)
                {
                    for (int pixelX = 0; pixelX < pixelsPerCell; pixelX++)
                    {
                        bool isGridPixel = hasGridLine &&
                                           (pixelX == 0 ||
                                            pixelY == 0 ||
                                            pixelX == pixelsPerCell - 1 ||
                                            pixelY == pixelsPerCell - 1);

                        _customMapPreviewTexture.SetPixel(startX + pixelX, startY + pixelY, isGridPixel ? _previewGridLineColor : cellColor);
                    }
                }
            }
        }

        _customMapPreviewTexture.Apply(false, false);
        _customMapPreviewImage.texture = _customMapPreviewTexture;
    }

    private Color GetPreviewCellColor(GridCell cellType, int groupId)
    {
        if (cellType == GridCell.Empty)
        {
            return _previewEmptyColor;
        }

        if (groupId <= 0 || _previewChunkGroupPalette.Length == 0)
        {
            return _previewShapeColor;
        }

        int paletteIndex = (groupId - 1) % _previewChunkGroupPalette.Length;
        return _previewChunkGroupPalette[paletteIndex];
    }

    private MapLayoutTemplate GetSelectedMapTemplate()
    {
        string selectedMapTemplateName = GetSelectedMapTemplateName();
        if (string.IsNullOrWhiteSpace(selectedMapTemplateName))
        {
            return null;
        }

        if (TryGetTemplateByTemplateName(selectedMapTemplateName, out MapLayoutTemplate mapTemplate))
        {
            return mapTemplate;
        }

        return null;
    }

    private bool TryGetTemplateByTemplateName(string templateName, out MapLayoutTemplate mapTemplate)
    {
        mapTemplate = null;

        if (_mapGenerationSettings == null || string.IsNullOrWhiteSpace(templateName))
        {
            return false;
        }

        MapTemplateSet[] templateSets = _mapGenerationSettings.TemplateSets;
        if (templateSets == null)
        {
            return false;
        }

        for (int i = 0; i < templateSets.Length; i++)
        {
            MapTemplateSet currentSet = templateSets[i];
            if (currentSet.Templates == null)
            {
                continue;
            }

            for (int j = 0; j < currentSet.Templates.Length; j++)
            {
                MapLayoutTemplate template = currentSet.Templates[j];
                if (template == null || string.IsNullOrWhiteSpace(template.TemplateName))
                {
                    continue;
                }

                if (string.Equals(template.TemplateName, templateName, StringComparison.OrdinalIgnoreCase))
                {
                    mapTemplate = template;
                    return true;
                }
            }
        }

        return false;
    }

    private void EnsureCustomMapPreviewTexture(int width, int height)
    {
        if (_customMapPreviewTexture != null &&
            _customMapPreviewTexture.width == width &&
            _customMapPreviewTexture.height == height)
        {
            return;
        }

        ReleaseCustomMapPreviewTexture();

        _customMapPreviewTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
    }

    private void ReleaseCustomMapPreviewTexture()
    {
        if (_customMapPreviewTexture == null)
        {
            return;
        }

        Destroy(_customMapPreviewTexture);
        _customMapPreviewTexture = null;
    }


    private static MainModeSelection GetMainModeSelection(int index)
    {
        return index switch
        {
            1 => MainModeSelection.Ranked,
            2 => MainModeSelection.Custom,
            _ => MainModeSelection.Normal
        };
    }

    private static NormalModeSelection GetNormalModeSelection(int index)
    {
        return index == 1 ? NormalModeSelection.EightPlayer : NormalModeSelection.FourPlayer;
    }

    private static CustomModeSelection GetCustomModeSelection(int index)
    {
        return index == 1 ? CustomModeSelection.JoinRoom : CustomModeSelection.CreateRoom;
    }

    private static void SetButtonInteractable(Button button, bool interactable)
    {
        if (button != null)
        {
            button.interactable = interactable;
        }
    }

    private static void SetGraphicAlpha(Graphic graphic, float alpha)
    {
        if (graphic == null)
        {
            return;
        }

        Color color = graphic.color;
        color.a = alpha;
        graphic.color = color;
    }

    private void ApplySubSwitchVisibility(bool isVisible)
    {
        GameObject subRoot = _subSwitchRoot != null ? _subSwitchRoot : (_subSwitch != null ? _subSwitch.gameObject : null);
        if (subRoot == null)
        {
            return;
        }

        if (!_preserveSubSwitchLayoutSpace)
        {
            subRoot.SetActive(isVisible);
            return;
        }

        if (!subRoot.activeSelf)
        {
            subRoot.SetActive(true);
        }

        CanvasGroup canvasGroup = GetOrCreateSubSwitchCanvasGroup(subRoot);
        canvasGroup.alpha = isVisible ? 1f : 0f;
        canvasGroup.interactable = isVisible;
        canvasGroup.blocksRaycasts = isVisible;
    }

    private CanvasGroup GetOrCreateSubSwitchCanvasGroup(GameObject subRoot)
    {
        if (_subSwitchCanvasGroup != null)
        {
            return _subSwitchCanvasGroup;
        }

        _subSwitchCanvasGroup = subRoot.GetComponent<CanvasGroup>();
        if (_subSwitchCanvasGroup == null)
        {
            _subSwitchCanvasGroup = subRoot.AddComponent<CanvasGroup>();
        }

        return _subSwitchCanvasGroup;
    }

    #endregion
}
