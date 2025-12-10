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

    [Header("References")]
    [SerializeField] private MatchmakingManager _matchmakingManager;

    #endregion

    #region Constants

    private const int MIN_PLAYER_COUNT = 1;
    private const int MAX_PLAYER_COUNT = 8;
    private const string GAMEPLAY_SCENE_NAME = "GamePlay";

    #endregion

    #region UI State

    private enum EUIState { Main, CustomRoom }
    private enum ECustomRoomMode { None, Create, Join }
    private enum EGameModeOption { FourPlayer = 0, EightPlayer = 1, CustomRoom = 2, PracticeRange = 3 }

    private EUIState _currentUIState = EUIState.Main;
    private ECustomRoomMode _customRoomMode = ECustomRoomMode.None;

    #endregion

    #region Private Fields

    private int _selectedPlayerCount = 4;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        FindMatchmakingManager();
        InitializeUI();
        RegisterEventHandlers();
        SetUIState(EUIState.Main);
    }

    #endregion

    #region Initialization

    private void FindMatchmakingManager()
    {
        // Why: 항상 Instance를 사용 (씬 재로드 시 stale reference 방지)
        _matchmakingManager = MatchmakingManager.Instance;

        if (_matchmakingManager == null)
        {
            _matchmakingManager = FindFirstObjectByType<MatchmakingManager>();
        }

        if (_matchmakingManager == null)
        {
            Debug.LogError("[LobbyUI] MatchmakingManager not found!");
        }
        else
        {
            Debug.Log($"[LobbyUI] MatchmakingManager found - State: {_matchmakingManager.CurrentState}");
        }
    }

    private void InitializeUI()
    {
        UpdatePlayerCountText();
    }

    private void RegisterEventHandlers()
    {
        _startButton?.onClick.AddListener(OnStartClicked);
        _gameModeDropdown?.onValueChanged.AddListener(OnGameModeChanged);
        _createRoomButton?.onClick.AddListener(OnCreateRoomModeClicked);
        _joinRoomButton?.onClick.AddListener(OnJoinRoomModeClicked);
        _playerCountLeftButton?.onClick.AddListener(OnPlayerCountLeftClicked);
        _playerCountRightButton?.onClick.AddListener(OnPlayerCountRightClicked);
        
        if (_roomCodeInputField != null)
        {
            _roomCodeInputField.characterLimit = 6; // ABC123 형식 (하이픈 없음)
            _roomCodeInputField.onValueChanged.AddListener(OnRoomCodeInputChanged);
        }
    }

    #endregion

    #region UI State Management

    private void SetUIState(EUIState newState)
    {
        _mainPanel?.SetActive(true);
        _customRoomPanel?.SetActive(newState == EUIState.CustomRoom);

        if (newState != EUIState.CustomRoom)
        {
            _customRoomMode = ECustomRoomMode.None;
            UpdateCustomRoomSubPanels();
        }

        _currentUIState = newState;
    }

    private void UpdateCustomRoomSubPanels()
    {
        _createRoomSubPanel?.SetActive(_customRoomMode == ECustomRoomMode.Create);
        _joinRoomSubPanel?.SetActive(_customRoomMode == ECustomRoomMode.Join);

        bool showModeButtons = _customRoomMode == ECustomRoomMode.None;
        _customRoomTitleText?.gameObject.SetActive(showModeButtons);
        _createRoomButton?.gameObject.SetActive(showModeButtons);
        _joinRoomButton?.gameObject.SetActive(showModeButtons);
    }

    #endregion

    #region Main Panel Handlers

    private void OnGameModeChanged(int index)
    {
        var selectedMode = (EGameModeOption)index;
        SetUIState(selectedMode == EGameModeOption.CustomRoom ? EUIState.CustomRoom : EUIState.Main);
    }

    private void OnStartClicked()
    {
        Debug.Log("[LobbyUI] OnStartClicked called");

        // Why: 버튼 클릭 시점에 MatchmakingManager 참조 재확인 (안전장치)
        if (_matchmakingManager == null || !_matchmakingManager)
        {
            Debug.LogWarning("[LobbyUI] MatchmakingManager reference lost - re-finding...");
            FindMatchmakingManager();
        }

        if (_matchmakingManager == null)
        {
            Debug.LogError("[LobbyUI] MatchmakingManager is null!");
            return;
        }

        Debug.Log($"[LobbyUI] MatchmakingManager State: {_matchmakingManager.CurrentState}");

        if (_currentUIState == EUIState.CustomRoom)
        {
            if (_customRoomMode == ECustomRoomMode.Create)
            {
                Debug.Log($"[LobbyUI] Creating custom room with {_selectedPlayerCount} players");
                _matchmakingManager.CreateCustomRoom(_selectedPlayerCount);
                SceneManager.LoadScene(GAMEPLAY_SCENE_NAME);
            }
            else if (_customRoomMode == ECustomRoomMode.Join)
            {
                string roomCode = _roomCodeInputField?.text;
                if (string.IsNullOrEmpty(roomCode) || roomCode.Length != 6)
                {
                    Debug.LogWarning("[LobbyUI] Invalid room code");
                    return;
                }
                Debug.Log($"[LobbyUI] Joining custom room with code: {roomCode}");
                _matchmakingManager.JoinCustomRoom(roomCode);
                SceneManager.LoadScene(GAMEPLAY_SCENE_NAME);
            }
            return;
        }

        var selectedMode = (EGameModeOption)_gameModeDropdown.value;
        Debug.Log($"[LobbyUI] Selected mode: {selectedMode}");
        bool shouldStart = true;

        switch (selectedMode)
        {
            case EGameModeOption.FourPlayer:
                Debug.Log("[LobbyUI] Starting 4-player matchmaking");
                _matchmakingManager.StartMatchmaking(EGameMode.FourPlayer);
                break;
            case EGameModeOption.EightPlayer:
                Debug.Log("[LobbyUI] Starting 8-player matchmaking");
                _matchmakingManager.StartMatchmaking(EGameMode.EightPlayer);
                break;
            case EGameModeOption.PracticeRange:
                Debug.Log("[LobbyUI] Entering practice range");
                _matchmakingManager.EnterPracticeRange();
                break;
            default:
                Debug.LogWarning($"[LobbyUI] Invalid mode selected: {selectedMode}");
                shouldStart = false;
                break;
        }

        if (shouldStart)
        {
            Debug.Log($"[LobbyUI] Loading {GAMEPLAY_SCENE_NAME} scene");
            SceneManager.LoadScene(GAMEPLAY_SCENE_NAME);
        }
    }

    #endregion

    #region Custom Room Panel Handlers

    private void OnCreateRoomModeClicked()
    {
        _customRoomMode = ECustomRoomMode.Create;
        UpdateCustomRoomSubPanels();
    }

    private void OnJoinRoomModeClicked()
    {
        _customRoomMode = ECustomRoomMode.Join;
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
        // Why: 대문자로 변환 및 유효하지 않은 문자 제거
        string normalized = RoomCodeGenerator.Normalize(value);

        if (normalized != value)
        {
            _roomCodeInputField.text = normalized;
            _roomCodeInputField.caretPosition = normalized.Length;
        }
    }

    #endregion
}