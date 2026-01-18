using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 로비 UI 관리
/// 드롭다운으로 게임 모드 선택 후 시작 버튼으로 매치메이킹 및 씬 전환
/// </summary>
public class LobbyUI : MonoBehaviour
{
    #region Serialized Fields

    [Header("Panels")]
    [SerializeField] private GameObject _mainPanel;
    [SerializeField] private GameObject _customRoomPanel;
    [SerializeField] private GameObject _createRoomSubPanel;
    [SerializeField] private GameObject _joinRoomSubPanel;

    [Header("Main Panel")]
    [SerializeField] private TMP_Dropdown _gameModeDropdown;
    [SerializeField] private Button _startButton;

    [Header("Custom Room Panel")]
    [SerializeField] private TMP_Text _customRoomTitleText;
    [SerializeField] private Button _createRoomButton;
    [SerializeField] private Button _joinRoomButton;

    [Header("Create Room Sub Panel")]
    [SerializeField] private TMP_Text _playerCountText;
    [SerializeField] private Button _playerCountLeftButton;
    [SerializeField] private Button _playerCountRightButton;

    [Header("Join Room Sub Panel")]
    [SerializeField] private TMP_InputField _roomCodeInputField;

    [Header("Matchmaking Panel")]
    [SerializeField] private GameObject _matchmakingPanel;
    [SerializeField] private TMP_Text _matchmakingStatusText;
    [SerializeField] private TMP_Text _matchmakingPlayerCountText;
    [SerializeField] private TMP_Text _matchmakingRoomCodeText;
    [SerializeField] private Button _matchmakingCancelButton;

    [Header("References")]
    [SerializeField] private MatchmakingManager _matchmakingManager;

    #endregion

    #region Constants

    private const int MIN_PLAYER_COUNT = 1;
    private const int MAX_PLAYER_COUNT = 8;
    private const string GAMEPLAY_SCENE_NAME = "GamePlay";

    #endregion

    #region UI State

    private enum UIState { Main, CustomRoom }
    private enum CustomRoomMode { None, Create, Join }
    private enum GameModeOption { FourPlayer = 0, EightPlayer = 1, CustomRoom = 2, PracticeRange = 3 }

    private UIState _currentUIState = UIState.Main;
    private CustomRoomMode _customRoomMode = CustomRoomMode.None;

    #endregion

    #region Private Fields

    private int _selectedPlayerCount = 4;
    private bool _isCustomMode;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        FindMatchmakingManager();
        InitializeUI();
        RegisterEventHandlers();
        SetUIState(UIState.Main);

        // 매칭 패널 초기화
        if (_matchmakingPanel != null)
        {
            _matchmakingPanel.SetActive(false);
        }
    }

    private void Update()
    {
        // 매칭 중일 때 UI 갱신
        if (_matchmakingPanel != null && _matchmakingPanel.activeSelf && _matchmakingManager != null)
        {
            UpdateMatchmakingUI();
        }
    }

    private void OnDestroy()
    {
        UnregisterEventHandlers();
    }

    #endregion

    #region Initialization

    private void FindMatchmakingManager()
    {
        // 항상 Instance 사용 (씬 재로드 시 stale reference 방지)
        _matchmakingManager = MatchmakingManager.Instance;

        if (_matchmakingManager == null)
        {
            _matchmakingManager = FindFirstObjectByType<MatchmakingManager>();
        }

        if (_matchmakingManager == null)
        {
            Debug.LogError("[LobbyUI] MatchmakingManager not found!");
        }
    }

    private void InitializeUI()
    {
        UpdatePlayerCountText();
    }

    private void RegisterEventHandlers()
    {
        // UI 버튼 이벤트
        _startButton?.onClick.AddListener(OnStartClicked);
        _gameModeDropdown?.onValueChanged.AddListener(OnGameModeChanged);
        _createRoomButton?.onClick.AddListener(OnCreateRoomModeClicked);
        _joinRoomButton?.onClick.AddListener(OnJoinRoomModeClicked);
        _playerCountLeftButton?.onClick.AddListener(OnPlayerCountLeftClicked);
        _playerCountRightButton?.onClick.AddListener(OnPlayerCountRightClicked);
        _matchmakingCancelButton?.onClick.AddListener(OnCancelMatchmakingClicked);

        if (_roomCodeInputField != null)
        {
            _roomCodeInputField.characterLimit = 6; // ABC123 형식 (하이픈 없음)
            _roomCodeInputField.onValueChanged.AddListener(OnRoomCodeInputChanged);
        }

        // MatchmakingManager 이벤트
        if (_matchmakingManager != null)
        {
            _matchmakingManager.OnMatchmakingUIUpdate += OnMatchmakingStatusUpdate;
            _matchmakingManager.OnCustomRoomCreated += OnCustomRoomCreated;
            _matchmakingManager.OnRoomJoined += OnRoomJoined;
            _matchmakingManager.OnMatchmakingFailed += OnMatchmakingFailed;
            _matchmakingManager.OnMatchmakingCancelled += OnMatchmakingCancelled;
            _matchmakingManager.OnPlayerCountChanged += OnPlayerCountChanged;
            _matchmakingManager.OnMatchmakingSuccess += OnMatchmakingSuccess;
            _matchmakingManager.OnAllPlayersReady += OnAllPlayersReady;
        }
    }

    private void UnregisterEventHandlers()
    {
        if (_matchmakingManager != null)
        {
            _matchmakingManager.OnMatchmakingUIUpdate -= OnMatchmakingStatusUpdate;
            _matchmakingManager.OnCustomRoomCreated -= OnCustomRoomCreated;
            _matchmakingManager.OnRoomJoined -= OnRoomJoined;
            _matchmakingManager.OnMatchmakingFailed -= OnMatchmakingFailed;
            _matchmakingManager.OnMatchmakingCancelled -= OnMatchmakingCancelled;
            _matchmakingManager.OnPlayerCountChanged -= OnPlayerCountChanged;
            _matchmakingManager.OnMatchmakingSuccess -= OnMatchmakingSuccess;
            _matchmakingManager.OnAllPlayersReady -= OnAllPlayersReady;
        }
    }

    #endregion

    #region UI State Management

    private void SetUIState(UIState newState)
    {
        _mainPanel?.SetActive(true);
        _customRoomPanel?.SetActive(newState == UIState.CustomRoom);

        if (newState != UIState.CustomRoom)
        {
            _customRoomMode = CustomRoomMode.None;
            UpdateCustomRoomSubPanels();
        }

        _currentUIState = newState;
    }

    private void UpdateCustomRoomSubPanels()
    {
        _createRoomSubPanel?.SetActive(_customRoomMode == CustomRoomMode.Create);
        _joinRoomSubPanel?.SetActive(_customRoomMode == CustomRoomMode.Join);

        bool showModeButtons = _customRoomMode == CustomRoomMode.None;
        _customRoomTitleText?.gameObject.SetActive(showModeButtons);
        _createRoomButton?.gameObject.SetActive(showModeButtons);
        _joinRoomButton?.gameObject.SetActive(showModeButtons);
    }

    #endregion

    #region Main Panel Handlers

    private void OnGameModeChanged(int index)
    {
        var selectedMode = (GameModeOption)index;
        SetUIState(selectedMode == GameModeOption.CustomRoom ? UIState.CustomRoom : UIState.Main);
    }

    private void OnStartClicked()
    {
        // 버튼 클릭 시점에 MatchmakingManager 참조 재확인 (안전장치)
        if (_matchmakingManager == null || !_matchmakingManager)
        {
            FindMatchmakingManager();
        }

        if (_matchmakingManager == null)
        {
            Debug.LogError("[LobbyUI] MatchmakingManager is null!");
            return;
        }

        if (_currentUIState == UIState.CustomRoom)
        {
            if (_customRoomMode == CustomRoomMode.Create)
            {
                // 설정만 저장하고 Matching 씬에서 실제 접속
                _matchmakingManager.PrepareCustomRoom(_selectedPlayerCount);
                NavigateToMatchmakingScene();
            }
            else if (_customRoomMode == CustomRoomMode.Join)
            {
                string roomCode = _roomCodeInputField?.text;
                if (string.IsNullOrEmpty(roomCode) || roomCode.Length != 6)
                {
                    Debug.LogWarning("[LobbyUI] Invalid room code");
                    return;
                }
                Debug.Log($"[LobbyUI] Joining custom room with code: {roomCode}");
                // 설정만 저장하고 Matching 씬에서 실제 접속
                _matchmakingManager.PrepareJoinRoom(roomCode);
                NavigateToMatchmakingScene();
            }
            return;
        }

        var selectedMode = (GameModeOption)_gameModeDropdown.value;

        switch (selectedMode)
        {
            case GameModeOption.FourPlayer:
                _matchmakingManager.PrepareMatchmaking(GameMode.FourPlayer);
                NavigateToMatchmakingScene();
                break;
            case GameModeOption.EightPlayer:
                _matchmakingManager.PrepareMatchmaking(GameMode.EightPlayer);
                NavigateToMatchmakingScene();
                break;
            case GameModeOption.PracticeRange:
                _matchmakingManager.PrepareMatchmaking(GameMode.PracticeRange);
                NavigateToMatchmakingScene();
                break;
        }
    }

    /// <summary>
    /// Matchmaking 씬 이동. 실제 서버 접속은 Matchmaking 씬에서 수행
    /// </summary>
    private void NavigateToMatchmakingScene()
    {
        SceneManager.LoadScene("Matchmaking");
    }

    #endregion

    #region Matchmaking Event Handlers

    private void OnCancelMatchmakingClicked()
    {
        _matchmakingManager?.CancelMatchmaking();
    }

    private void OnMatchmakingStatusUpdate(string status)
    {
        if (_matchmakingStatusText != null)
        {
            _matchmakingStatusText.text = status;
        }
    }

    private void OnCustomRoomCreated(string roomCode)
    {
        _isCustomMode = true;
        UpdateRoomCodeDisplay(roomCode);
        if (_matchmakingStatusText != null)
        {
            _matchmakingStatusText.text = "플레이어 대기 중...";
        }
    }

    private void OnRoomJoined(string roomCode)
    {
        _isCustomMode = true;
        UpdateRoomCodeDisplay(roomCode);
        if (_matchmakingStatusText != null)
        {
            _matchmakingStatusText.text = "게임 시작 대기 중...";
        }
    }

    private void OnMatchmakingFailed(string errorMessage)
    {
        Debug.LogError($"[LobbyUI] Matchmaking failed: {errorMessage}");
        if (_matchmakingStatusText != null)
        {
            _matchmakingStatusText.text = $"오류: {errorMessage}";
        }
        HideMatchmakingPanel();
    }

    private void OnMatchmakingCancelled()
    {
        HideMatchmakingPanel();
    }

    private void OnPlayerCountChanged(int current, int max)
    {
        if (_matchmakingPlayerCountText != null)
        {
            _matchmakingPlayerCountText.text = $"{current}/{max}";
        }
    }

    private void OnMatchmakingSuccess()
    {
        Debug.Log("[LobbyUI] Matchmaking success - keeping panel visible for GamePlay scene");
        // 매칭 성공 시에도 패널 유지 (GamePlay 씬으로 전환됨)
    }
    
    private void OnAllPlayersReady()
    {
        Debug.Log("[LobbyUI] All players ready - hiding matchmaking panel before scene transition");
        HideMatchmakingPanel();
    }

    #endregion

    #region Matchmaking UI

    private void ShowMatchmakingPanel()
    {
        if (_matchmakingPanel == null) return;

        _isCustomMode = _matchmakingManager != null && _matchmakingManager.IsCustomGame;

        // 방 코드 표시 여부 설정
        if (_matchmakingRoomCodeText != null)
        {
            _matchmakingRoomCodeText.gameObject.SetActive(_isCustomMode);
        }

        // 메인 패널 숨기기
        if (_mainPanel != null)
        {
            _mainPanel.SetActive(false);
        }

        _matchmakingPanel.SetActive(true);
    }

    private void HideMatchmakingPanel()
    {
        if (_matchmakingPanel != null)
        {
            _matchmakingPanel.SetActive(false);
        }

        // 메인 패널 다시 표시
        if (_mainPanel != null)
        {
            _mainPanel.SetActive(true);
        }
    }

    private void UpdateMatchmakingUI()
    {
        if (_matchmakingManager == null) return;

        // 상태 및 플레이어 수 갱신
        if (_matchmakingStatusText != null)
        {
            _matchmakingStatusText.text = _matchmakingManager.StatusMessage;
        }

        if (_matchmakingPlayerCountText != null)
        {
            _matchmakingPlayerCountText.text = $"{_matchmakingManager.CurrentPlayers}/{_matchmakingManager.MaxPlayers}";
        }

        if (_isCustomMode && _matchmakingRoomCodeText != null)
        {
            UpdateRoomCodeDisplay(_matchmakingManager.RoomCode);
        }
    }

    private void UpdateRoomCodeDisplay(string roomCode)
    {
        if (_matchmakingRoomCodeText != null && !string.IsNullOrEmpty(roomCode))
        {
            string formattedCode = roomCode?.ToUpper().Trim() ?? "";
            _matchmakingRoomCodeText.text = $"방 코드: {formattedCode}";
        }
    }

    #endregion

    #region Custom Room Panel Handlers

    private void OnCreateRoomModeClicked()
    {
        _customRoomMode = CustomRoomMode.Create;
        UpdateCustomRoomSubPanels();
    }

    private void OnJoinRoomModeClicked()
    {
        _customRoomMode = CustomRoomMode.Join;
        if (_roomCodeInputField != null) _roomCodeInputField.text = "";
        UpdateCustomRoomSubPanels();
    }

    #endregion

    #region Other Handlers

    private void OnPlayerCountLeftClicked()
    {
        _selectedPlayerCount = Mathf.Max(MIN_PLAYER_COUNT, _selectedPlayerCount - 1);
        UpdatePlayerCountText();
    }

    private void OnPlayerCountRightClicked()
    {
        _selectedPlayerCount = Mathf.Min(MAX_PLAYER_COUNT, _selectedPlayerCount + 1);
        UpdatePlayerCountText();
    }

    private void UpdatePlayerCountText()
    {
        if (_playerCountText != null) _playerCountText.text = $"{_selectedPlayerCount}";
    }
    
    private void OnRoomCodeInputChanged(string value)
    {
        // 대문자로 변환 및 유효하지 않은 문자 제거
        string normalized = value?.ToUpper().Replace("-", "").Replace(" ", "").Trim() ?? "";

        if (normalized != value)
        {
            _roomCodeInputField.text = normalized;
            _roomCodeInputField.caretPosition = normalized.Length;
        }
    }

    #endregion
}
