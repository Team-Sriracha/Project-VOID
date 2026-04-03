using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Object; // Added for NetworkObject
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectVoid.Core; // Added for TransportUtils
using ProjectVoid.Practice;

/// <summary>
/// Fishnet 기반 매칭 매니저
/// 서버 검색, 방 생성/참가, 씬 전환 관리
/// </summary>
public class MatchmakingManager : MonoBehaviour
{
    #region Serialized Fields

    [Header("프리팹")]
    [SerializeField] private GameObject _networkManagerPrefab;
    [SerializeField] private NetworkObject _practiceModeManagerPrefab; // New field for dynamic spawn

    [Header("씬 이름")]
    [SerializeField] private string _gamePlaySceneName = "GamePlay";
    [SerializeField] private string _lobbySceneName = "Lobby";

    [Header("연결 설정")]
    [SerializeField] private float _connectionTimeout = 10f;
    [SerializeField] private float _sessionSearchTimeout = 30f;
    [SerializeField] private float _sessionSearchInterval = 2f;

    [Header("사운드")]
    [SerializeField] private AudioCue _matchCompleteAudioCue;
    [SerializeField] private AudioCue _sceneTransitionAudioCue;
    [SerializeField] private AudioCue _gameStartedAudioCue;
    [SerializeField] private AudioCue _cancelAudioCue;

    #endregion

    #region Constants

    private const int DEFAULT_CUSTOM_GAME_TIME_SECONDS = 600;
    private const int MIN_CUSTOM_GAME_TIME_SECONDS = 180;
    private const int MAX_CUSTOM_GAME_TIME_SECONDS = 900;
    private const int DEFAULT_CUSTOM_PLAYER_COUNT = 4;
    private const int MIN_CUSTOM_PLAYER_COUNT = 2;
    private const int MAX_CUSTOM_PLAYER_COUNT = 8;
    private const float IDENTITY_VERIFICATION_TIMEOUT_SECONDS = 15f;
    private const int IDENTITY_VERIFICATION_MAX_RETRY_COUNT = 1;
    private const int IDENTITY_VERIFICATION_RETRY_DELAY_MS = 1000;
    private const int DISCONNECT_RETURN_DELAY_MS = 1500;
    private const string GUEST_RANKED_BLOCKED_MESSAGE = "게스트는 랭크 매칭에 참여할 수 없습니다. Google 또는 Apple 로그인 후 다시 시도해 주세요.";
    private const string LOBBY_RETURN_DISCONNECT_MESSAGE = "인터넷 연결 또는 서버 연결이 끊어져 로비로 돌아갑니다.";

    #endregion
    
    #region Public Properties
    
    public static MatchmakingManager Instance { get; private set; }
    public MatchmakingState CurrentState => _currentState;
    public GameMode CurrentGameMode => _currentGameMode;
    public bool IsMatchmaking => _currentState != MatchmakingState.Idle && _currentState != MatchmakingState.Failed;
    public bool IsCustomGame => _currentGameMode == GameMode.Custom;
    public string StatusMessage => _statusMessage;
    public int CurrentPlayers => _currentPlayersInLobby;
    public int MaxPlayers => _targetPlayerCount;
    public string RoomCode => _currentRoomCode;
    public GameConnectionInfo? PendingGameConnection => _pendingGameConnection;
    public CustomRoomSettings CurrentCustomRoomSettings => _currentCustomRoomSettings;

    #endregion

    #region Private Fields

    private MatchmakingState _currentState = MatchmakingState.Idle;
    private GameMode _currentGameMode;
    private int _targetPlayerCount;
    private string _currentRoomCode;
    private GameConnectionInfo? _pendingGameConnection;

    private Dictionary<string, string> _roomCodeToSessionMap = new Dictionary<string, string>();
    private Dictionary<string, string> _sessionToRoomCodeMap = new Dictionary<string, string>();

    private string _statusMessage = "Idle";
    private int _currentPlayersInLobby = 0;

    private bool _isCancelling = false;
    private bool _isReturningToLobbyAfterDisconnect = false;
    
    // Prepare된 매칭 정보 (Matching 씬에서 실제 접속 시 사용)
    private GameMode _preparedGameMode = GameMode.None;
    private int _preparedPlayerCount = 0;
    private string _preparedRoomCode = null;
    private bool _isPreparedToCreate = false;
    private CustomRoomSettings _preparedCustomRoomSettings = CustomRoomSettings.Disabled;
    private CustomRoomSettings _currentCustomRoomSettings = CustomRoomSettings.Disabled;

    // 서버 목록 캐시
    private List<SessionData> _availableSessions = new List<SessionData>();
    
    // GameState 모니터링
    private Coroutine _gameStateMonitorCoroutine;
    private bool _isMonitoringGameState = false;
    private bool _hasGameStartNotified = false;


    // 세션 병합 (매칭 중 더 나은 세션으로 이동)
    private Coroutine _sessionConsolidationCoroutine;
    private bool _isConsolidating = false;
    private string _currentSessionId;

    // 내 공인 IP (NAT Loopback 우회용)
    private string _myPublicIp;
    private static string _pendingLobbyRestrictionMessage = string.Empty;

    #endregion

    #region Events

    public event Action<string> OnMatchmakingUIUpdate;
    public event Action<string> OnCustomRoomCreated;
    public event Action<string> OnRoomJoined;
    public event Action<string> OnMatchmakingFailed;
    public event Action OnMatchmakingCancelled;
    public event Action<int, int> OnPlayerCountChanged;
    public event Action OnMatchmakingSuccess;
    public event Action OnAllPlayersReady;
    public event Action MatchCompleted;

    #endregion
    
    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        transform.SetParent(null);
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        _ = FetchMyPublicIp();
#endif
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    #endregion

    #region Public Methods - Prepare (Lobby에서 호출)

    /// <summary>
    /// 일반 매칭 준비 (Lobby에서 호출, Matching 씬에서 실제 접속)
    /// </summary>
    public void PrepareMatchmaking(GameMode mode)
    {
        if (mode == GameMode.PracticeRange)
        {
            PreparePracticeMode(string.Empty);
            return;
        }

        Debug.Log($"[Matchmaking] PrepareMatchmaking - Mode: {mode}");
        ResetState();

        if (IsGuestRankedBlocked(mode))
        {
            UpdateStatus(GUEST_RANKED_BLOCKED_MESSAGE);
            Debug.LogWarning($"[Matchmaking] 게스트 랭크 차단: Mode={mode}");
            return;
        }
        
        _preparedGameMode = mode;
        _preparedPlayerCount = GameModeCatalog.GetMaxPlayers(mode, mode == GameMode.PracticeRange ? 1 : 4);
        _preparedRoomCode = null;
        _isPreparedToCreate = false;
        _preparedCustomRoomSettings = CustomRoomSettings.Disabled;
        _currentCustomRoomSettings = CustomRoomSettings.Disabled;
        
        _currentGameMode = mode;
        _targetPlayerCount = _preparedPlayerCount;
    }

    /// <summary>
    /// 연습장 모드 준비 시 선택한 맵 템플릿 이름을 보존합니다.
    /// </summary>
    /// <param name="mapTemplateName">선택한 맵 템플릿 이름</param>
    public void PreparePracticeMode(string mapTemplateName)
    {
        string normalizedMapTemplateName = string.IsNullOrWhiteSpace(mapTemplateName)
            ? string.Empty
            : mapTemplateName.Trim();

        Debug.Log($"[Matchmaking] PreparePracticeMode - Map: {normalizedMapTemplateName}");
        ResetState();

        CustomRoomSettings practiceSettings = string.IsNullOrWhiteSpace(normalizedMapTemplateName)
            ? CustomRoomSettings.Disabled
            : new CustomRoomSettings
            {
                IsEnabled = true,
                PlayerCount = 1,
                GameTimeSeconds = 0,
                MapTemplateName = normalizedMapTemplateName
            };

        _preparedGameMode = GameMode.PracticeRange;
        _preparedPlayerCount = 1;
        _preparedRoomCode = null;
        _isPreparedToCreate = false;
        _preparedCustomRoomSettings = practiceSettings;
        _currentCustomRoomSettings = practiceSettings;

        _currentGameMode = GameMode.PracticeRange;
        _targetPlayerCount = 1;
    }

    /// <summary>
    /// 커스텀 방 생성 준비 (Lobby에서 호출, Matching 씬에서 실제 생성)
    /// </summary>
    public void PrepareCustomRoom(int playerCount)
    {
        PrepareCustomRoom(new CustomRoomSettings
        {
            IsEnabled = true,
            PlayerCount = playerCount,
            GameTimeSeconds = DEFAULT_CUSTOM_GAME_TIME_SECONDS,
            MapTemplateName = string.Empty
        });
    }

    /// <summary>
    /// 커스텀 방 생성 준비 (Lobby에서 호출, Matching 씬에서 실제 생성)
    /// </summary>
    public void PrepareCustomRoom(CustomRoomSettings customSettings)
    {
        CustomRoomSettings normalizedSettings = NormalizeCustomRoomSettings(customSettings);
        Debug.Log($"[Matchmaking] PrepareCustomRoom - PlayerCount: {normalizedSettings.PlayerCount}, Time: {normalizedSettings.GameTimeSeconds}, Map: {normalizedSettings.MapTemplateName}");
        ResetState();
        
        _preparedGameMode = GameMode.Custom;
        _preparedPlayerCount = normalizedSettings.PlayerCount;
        _preparedRoomCode = null;
        _isPreparedToCreate = true;
        _preparedCustomRoomSettings = normalizedSettings;
        _currentCustomRoomSettings = normalizedSettings;
        
        _currentGameMode = GameMode.Custom;
        _targetPlayerCount = normalizedSettings.PlayerCount;
    }

    /// <summary>
    /// 커스텀 방 참가 준비 (Lobby에서 호출, Matching 씬에서 실제 참가)
    /// </summary>
    public void PrepareJoinRoom(string roomCode)
    {
        Debug.Log($"[Matchmaking] PrepareJoinRoom - RoomCode: {roomCode}");
        ResetState();
        
        _preparedGameMode = GameMode.Custom;
        _preparedPlayerCount = 0;
        _preparedRoomCode = roomCode?.ToUpper().Trim() ?? "";
        _isPreparedToCreate = false;
        _preparedCustomRoomSettings = CustomRoomSettings.Disabled;
        _currentCustomRoomSettings = CustomRoomSettings.Disabled;
        
        _currentGameMode = GameMode.Custom;
        _currentRoomCode = _preparedRoomCode;
    }

    /// <summary>
    /// Matching 씬에서 호출 - 준비된 매칭 정보로 실제 서버 접속 시작
    /// </summary>
    public void ExecuteMatchmaking()
    {
        Debug.Log($"[Matchmaking] ExecuteMatchmaking - Mode: {_preparedGameMode}, PlayerCount: {_preparedPlayerCount}, RoomCode: {_preparedRoomCode}, IsCreate: {_isPreparedToCreate}, CustomTime: {_preparedCustomRoomSettings.GameTimeSeconds}, CustomMap: {_preparedCustomRoomSettings.MapTemplateName}");
        
        if (_preparedGameMode == GameMode.None)
        {
            Debug.LogWarning("[Matchmaking] No prepared matchmaking info! Returning to Lobby.");
            SceneManager.LoadScene(_lobbySceneName);
            return;
        }
        
        // 실제 매칭 시작
        if (_preparedGameMode == GameMode.Custom)
        {
            if (_isPreparedToCreate)
            {
                _ = CreateCustomRoomAsync(_preparedCustomRoomSettings);
            }
            else if (!string.IsNullOrEmpty(_preparedRoomCode))
            {
                _ = JoinCustomRoomAsync(_preparedRoomCode);
            }
        }
        else if (_preparedGameMode == GameMode.PracticeRange)
        {
            _ = StartPracticeModeAsync(_preparedCustomRoomSettings);
        }
        else
        {
            _ = StartMatchmakingAsync(_preparedGameMode);
        }
    }

    /// <summary>
    /// 매칭 취소 후 Lobby로 돌아가기
    /// </summary>
    public void CancelAndReturnToLobby()
    {
        CancelMatchmaking();
        SceneManager.LoadScene(_lobbySceneName);
    }

    /// <summary>
    /// 로비 진입 시 잔여 매칭 상태를 정리합니다.
    /// </summary>
    public void ResetForLobbyEntry()
    {
        // Why: 로비 진입 직후 이전 세션의 연결/콜백/준비 상태가 남아 자동 전환되는 현상을 방지합니다.
        _isCancelling = false;
        UnsubscribeClientConnectionState();
        StopMonitoringGameState();

        if (UnityLobbyManager.Instance != null && UnityLobbyManager.Instance.IsInSession)
        {
            _ = UnityLobbyManager.Instance.LeaveSession();
        }

        ResetState();
    }

    #endregion

    #region Public Methods - Matchmaking

    public async void StartMatchmaking(GameMode mode)
    {
        await StartMatchmakingAsync(mode);
    }

    public async void CreateCustomRoom(int playerCount)
    {
        await CreateCustomRoomAsync(new CustomRoomSettings
        {
            IsEnabled = true,
            PlayerCount = playerCount,
            GameTimeSeconds = DEFAULT_CUSTOM_GAME_TIME_SECONDS,
            MapTemplateName = string.Empty
        });
    }

    public async void CreateCustomRoom(CustomRoomSettings customSettings)
    {
        await CreateCustomRoomAsync(customSettings);
    }

    public async void JoinCustomRoom(string roomCode)
    {
        await JoinCustomRoomAsync(roomCode);
    }
    
    public void EnterPracticeRange()
    {
        _ = StartPracticeModeAsync(CustomRoomSettings.Disabled);
    }

    public void CancelMatchmaking()
    {
        if (_isCancelling)
        {
            Debug.LogWarning("[Matchmaking] CancelMatchmaking already in progress, ignoring...");
            return;
        }

        _isCancelling = true;
        Debug.Log("[Matchmaking] CancelMatchmaking - Start");
        UpdateStatus("취소 중...");
        AudioManager.Instance?.PlayUi(_cancelAudioCue);
        
        // GameState 모니터링 중지
        StopMonitoringGameState();

        // [Fix] OnClientConnectionStateChanged 이벤트 해제 (중복 구독 방지)
        if (InstanceFinder.ClientManager != null)
        {
            InstanceFinder.ClientManager.OnClientConnectionState -= OnClientConnectionStateChanged;
        }

        try
        {
            // Fishnet 연결 해제
            // 호스트(서버+클라이언트)인 경우 서버도 종료해야 함
            if (InstanceFinder.IsServerStarted)
            {
                InstanceFinder.ServerManager.StopConnection(true);
                Debug.Log("[Matchmaking] Server (and Client) connection stopped");
            }
            else if (InstanceFinder.IsClientStarted)
            {
                InstanceFinder.ClientManager.StopConnection();
                Debug.Log("[Matchmaking] Client connection stopped");
            }

            // [Fix] Unity Lobby Session 나가기
            if (UnityLobbyManager.Instance != null && UnityLobbyManager.Instance.IsInSession)
            {
                Debug.Log("[Matchmaking] Unity Lobby Session 나가기 요청");
                _ = UnityLobbyManager.Instance.LeaveSession();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Matchmaking] Error during cancel: {ex.Message}");
        }
        finally
        {
            _isCancelling = false;
            ResetState();
            
            if (LoadingUIManager.Instance != null)
            {
                LoadingUIManager.Instance.HideLoadingScreen();
            }
            
            OnMatchmakingCancelled?.Invoke();
            Debug.Log("[Matchmaking] CancelMatchmaking - Complete");
        }
    }

    /// <summary>
    /// 서버에서 매칭 완료 알림이 왔을 때 호출
    /// </summary>
    public void HandleMatchComplete()
    {
        Debug.Log("[Matchmaking] 매칭 완료!");
        UpdateStatus("매칭완료!");
        AudioManager.Instance?.PlayUi(_matchCompleteAudioCue);
        MatchCompleted?.Invoke();
    }

    /// <summary>
    /// 게임 시작 즉시 UI 상태를 갱신합니다.
    /// </summary>
    public void HandleGameStartedImmediate()
    {
        if (_hasGameStartNotified)
        {
            return;
        }

        _hasGameStartNotified = true;
        _currentState = MatchmakingState.Completed;
        UpdateStatus("게임 시작!");
        AudioManager.Instance?.PlayUi(_gameStartedAudioCue);
        OnMatchmakingSuccess?.Invoke();
        StopMonitoringGameState();
    }

    /// <summary>
    /// 서버의 씬 전환 시 호출
    /// </summary>
    public void OnServerRequestedSceneTransition()
    {
        Debug.Log("[Matchmaking] 서버에서 씬 전환 요청!");
        
        _currentState = MatchmakingState.ConnectingToServer;
        UpdateStatus("게임 로딩 중...");
        AudioManager.Instance?.PlayUi(_sceneTransitionAudioCue);

        bool isPracticeMode = GameModeCatalog.IsPracticeMode(_currentGameMode);
        if (LoadingUIManager.Instance != null)
        {
            if (isPracticeMode)
            {
                // Why: 연습장에서는 RPC_StartGameCountdown이 5초 카운트 UI를 직접 제어합니다.
                // 이 시점에 HideLoadingScreen()을 호출하면 카운트 패널이 즉시 사라져 UX가 깨집니다.
                // 따라서 연습장 씬 전환 시에는 로딩 UI를 재호출/강제종료하지 않습니다.
            }
            else
            {
                LoadingUIManager.Instance.ShowLoadingAndWaitForMap();
            }
        }
        
        OnAllPlayersReady?.Invoke();
    }

    public void NotifyAllPlayersReady()
    {
        OnServerRequestedSceneTransition();
    }

    #endregion

    #region Private Methods - Async Matchmaking

    private async Task StartMatchmakingAsync(GameMode mode)
    {
        Debug.Log($"[Matchmaking] StartMatchmaking - Mode: {mode}");

        if (!GameModeCatalog.IsQueueMatchmakingMode(mode))
        {
            Debug.LogError($"[Matchmaking] Invalid mode for matchmaking: {mode}");
            return;
        }

        if (IsGuestRankedBlocked(mode))
        {
            HandleFailure(GUEST_RANKED_BLOCKED_MESSAGE);
            return;
        }

        if (!TryBeginMatchmaking())
            return;

        _currentGameMode = mode;
        _targetPlayerCount = GameModeCatalog.GetMaxPlayers(mode, 8);
        _currentCustomRoomSettings = CustomRoomSettings.Disabled;

        UpdateStatus("사용 가능한 서버 검색 중...");

        try
        {
            var serverInfo = await FindAvailableServerAsync(_targetPlayerCount);
            if (serverInfo == null)
            {
                HandleFailure($"사용 가능한 게임 서버가 없습니다 ({_sessionSearchTimeout}초 타임아웃)");
                return;
            }

            await ConnectToServerAsync(serverInfo.Value);
        }
        catch (Exception ex)
        {
            HandleFailure($"오류: {ex.Message}");
            Debug.LogError($"[Matchmaking] Error: {ex}");
        }
    }

    private async Task CreateCustomRoomAsync(CustomRoomSettings customSettings)
    {
        CustomRoomSettings normalizedSettings = NormalizeCustomRoomSettings(customSettings);
        int playerCount = normalizedSettings.PlayerCount;
        Debug.Log($"[Matchmaking] CreateCustomRoom - PlayerCount: {playerCount}, Time: {normalizedSettings.GameTimeSeconds}, Map: {normalizedSettings.MapTemplateName}");

        int minPlayers = MIN_CUSTOM_PLAYER_COUNT;
        int maxPlayers = MAX_CUSTOM_PLAYER_COUNT;

        if (playerCount < minPlayers || playerCount > maxPlayers)
        {
            Debug.LogError($"[Matchmaking] Invalid player count: {playerCount}");
            return;
        }

        if (!TryBeginMatchmaking())
            return;

        _currentGameMode = GameMode.Custom;
        _targetPlayerCount = playerCount;
        _currentCustomRoomSettings = normalizedSettings;

        UpdateStatus("사용 가능한 서버 검색 중...");

        try
        {
            var serverInfo = await FindAvailableServerAsync(playerCount);
            if (serverInfo == null)
            {
                HandleFailure($"사용 가능한 게임 서버가 없습니다");
                return;
            }

            // 세션에 참가하여 RoomCode 획득
            if (UnityLobbyManager.Instance != null && !string.IsNullOrEmpty(serverInfo.Value.ServerId))
            {
                Debug.Log($"[Matchmaking] JoinSessionById 시도 - ServerId: {serverInfo.Value.ServerId}");
                var sessionInfo = await UnityLobbyManager.Instance.JoinSessionById(serverInfo.Value.ServerId);
                
                if (sessionInfo != null)
                {
                    Debug.Log($"[Matchmaking] 세션 참가 성공 - SessionCode: {sessionInfo.SessionCode}, RoomCode: {sessionInfo.RoomCode}");
                    
                    if (!string.IsNullOrEmpty(sessionInfo.RoomCode))
                    {
                        _currentRoomCode = sessionInfo.RoomCode;
                        _roomCodeToSessionMap[_currentRoomCode] = serverInfo.Value.ServerId;
                        Debug.Log($"[Matchmaking] RoomCode 설정 완료: {_currentRoomCode}");
                    }
                    else
                    {
                        Debug.LogWarning("[Matchmaking] sessionInfo.RoomCode가 비어있습니다!");
                    }
                }
                else
                {
                    Debug.LogWarning("[Matchmaking] JoinSessionById 실패 - sessionInfo가 null");
                }
            }
            else
            {
                Debug.LogWarning($"[Matchmaking] 세션 참가 스킵 - UnityLobbyManager: {UnityLobbyManager.Instance != null}, ServerId: {serverInfo.Value.ServerId}");
            }

            // 서버에 연결 (FishNet 직접 연결)
            await ConnectToServerAsync(serverInfo.Value);
            
            Debug.Log($"[Matchmaking] Created room with code: {_currentRoomCode}, IsCustomGame: {IsCustomGame}");
            OnCustomRoomCreated?.Invoke(_currentRoomCode);
        }
        catch (Exception ex)
        {
            HandleFailure($"오류: {ex.Message}");
            Debug.LogError($"[Matchmaking] Error: {ex}");
        }
    }

    private async Task JoinCustomRoomAsync(string roomCode)
    {
        Debug.Log($"[Matchmaking] JoinCustomRoom - RoomCode: {roomCode}");

        roomCode = roomCode?.ToUpper().Trim() ?? "";

        if (string.IsNullOrEmpty(roomCode) || roomCode.Length != 6)
        {
            HandleFailure("잘못된 방 코드 형식입니다");
            return;
        }

        if (!TryBeginMatchmaking())
            return;

        _currentGameMode = GameMode.Custom;
        _currentRoomCode = roomCode;
        _currentCustomRoomSettings = CustomRoomSettings.Disabled;

        UpdateStatus("방 검색 중...");

        try
        {
            // 방 코드로 서버 찾기
            var serverInfo = await FindServerByRoomCodeAsync(roomCode);
            if (serverInfo == null)
            {
                HandleFailure("해당 방을 찾을 수 없습니다");
                CancelAndReturnToLobby();
                return;
            }

            OnRoomJoined?.Invoke(roomCode);
            await ConnectToServerAsync(serverInfo.Value);
        }
        catch (Exception ex)
        {
            HandleFailure($"오류: {ex.Message}");
            Debug.LogError($"[Matchmaking] Error: {ex}");
        }
    }

    private async Task StartPracticeModeAsync(CustomRoomSettings practiceSettings)
    {
        Debug.Log($"[Matchmaking] StartPracticeMode with Yak Transport (Offline) - Map: {practiceSettings.MapTemplateName}");

        ResetState();

        _currentState = MatchmakingState.ConnectingToServer;
        _currentGameMode = GameMode.PracticeRange;
        _targetPlayerCount = 1;
        _currentCustomRoomSettings = practiceSettings;
        UpdateStatus("연습장 준비 중...");

        try
        {
            await StartOfflinePracticeMode(practiceSettings);
        }
        catch (Exception ex)
        {
            HandleFailure($"오류: {ex.Message}");
            Debug.LogError($"[Matchmaking] Error: {ex}");
        }
    }

    /// <summary>
    /// Yak Transport를 사용한 오프라인 연습장 모드 시작
    /// </summary>
    private async Task StartOfflinePracticeMode(CustomRoomSettings practiceSettings)
    {
        Debug.Log("[Matchmaking] Starting offline practice mode with Yak Transport");

        FishNet.Managing.NetworkManager fishnetNetworkManager = InstanceFinder.NetworkManager;
        if (fishnetNetworkManager == null && _networkManagerPrefab != null)
        {
            Debug.Log("[Matchmaking] Creating NetworkManager from prefab...");
            GameObject go = Instantiate(_networkManagerPrefab);
            go.name = "FishNetNetworkManager";
            DontDestroyOnLoad(go);

            await Task.Delay(200);
            fishnetNetworkManager = go.GetComponent<FishNet.Managing.NetworkManager>();
        }

        if (fishnetNetworkManager == null)
        {
            HandleFailure("NetworkManager를 찾을 수 없습니다.");
            return;
        }

        if (!TransportUtils.SwitchToYakTransport(fishnetNetworkManager))
        {
            HandleFailure("Yak Transport를 찾을 수 없습니다. Fish-Net Pro와 Yak Transport가 필요합니다.");
            return;
        }

        _pendingGameConnection = CreatePracticeConnectionInfo(practiceSettings);

        if (NetworkManager.Instance == null)
        {
            HandleFailure("Project NetworkManager를 찾을 수 없습니다.");
            return;
        }

        NetworkManager.Instance.SetConnectionInfo(_pendingGameConnection.Value);

        _currentState = MatchmakingState.WaitingForPlayers;
        UpdateStatus("연습장 로딩 중...");

        bool sceneLoaded = await LoadPracticeSceneAsync();
        if (!sceneLoaded)
        {
            HandleFailure("연습장 씬 로드에 실패했습니다.");
            return;
        }

        bool serverStarted = await NetworkManager.Instance.StartGameServer(_pendingGameConnection.Value);
        if (!serverStarted)
        {
            HandleFailure("연습장 서버를 시작하지 못했습니다.");
            return;
        }

        SpawnPracticeModeManagerIfNeeded();

        _currentState = MatchmakingState.InGame;
        _currentPlayersInLobby = 1;
        OnPlayerCountChanged?.Invoke(_currentPlayersInLobby, _targetPlayerCount);
        UpdateStatus("연습장 시작됨");
    }

    private GameConnectionInfo CreatePracticeConnectionInfo(CustomRoomSettings practiceSettings)
    {
        return new GameConnectionInfo
        {
            SessionName = "PracticeMode",
            ServerInfo = new ServerInfo
            {
                ServerId = "offline-practice",
                IpAddress = "localhost",
                Port = 7777,
                Region = "Offline"
            },
            GameMode = GameMode.PracticeRange,
            MaxPlayers = 1,
            DirectServerIP = "localhost",
            DirectServerPort = 7777,
            RoomCode = string.Empty,
            CustomSettings = practiceSettings
        };
    }

    private void SpawnPracticeModeManagerIfNeeded()
    {
        if (InstanceFinder.ServerManager == null || !InstanceFinder.ServerManager.Started)
        {
            Debug.LogError("[Matchmaking] ServerManager가 시작되지 않아 PracticeModeManager를 스폰할 수 없습니다.");
            return;
        }

        PracticeModeManager existingPracticeModeManager =
            FindFirstObjectByType<PracticeModeManager>(FindObjectsInactive.Include);

        if (existingPracticeModeManager != null)
        {
            NetworkObject existingNetworkObject = existingPracticeModeManager.NetworkObject;
            if (existingNetworkObject != null && existingNetworkObject.IsSpawned)
            {
                Debug.Log("[Matchmaking] PracticeModeManager가 이미 스폰되어 추가 생성하지 않습니다.");
                return;
            }

            if (existingNetworkObject != null)
            {
                Debug.Log("[Matchmaking] 기존 PracticeModeManager 인스턴스를 네트워크 스폰합니다.");
                InstanceFinder.ServerManager.Spawn(existingNetworkObject);
                return;
            }

            Debug.LogWarning("[Matchmaking] 기존 PracticeModeManager에서 NetworkObject를 찾지 못했습니다. 프리팹으로 생성합니다.");
        }

        if (_practiceModeManagerPrefab == null)
        {
            Debug.LogError("[Matchmaking] PracticeModeManagerPrefab이 할당되지 않았습니다.");
            return;
        }

        Debug.Log("[Matchmaking] PracticeModeManager 프리팹을 새로 스폰합니다.");
        NetworkObject practiceModeManager = Instantiate(_practiceModeManagerPrefab);
        InstanceFinder.ServerManager.Spawn(practiceModeManager);
    }

    private async Task<bool> LoadPracticeSceneAsync()
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(_gamePlaySceneName);
        if (op == null)
        {
            Debug.LogError($"[Matchmaking] Failed to load scene: {_gamePlaySceneName}");
            return false;
        }

        while (!op.isDone)
        {
            await Task.Yield();
        }

        await Task.Yield();
        return true;
    }


    #endregion

    #region Private Methods - Server Discovery

    /// <summary>
    /// 사용 가능한 서버 검색
    /// Unity Sessions API를 우선 사용하고, 실패 시 ServerPoolConfig 사용
    /// </summary>
    private async Task<ServerInfo?> FindAvailableServerAsync(int targetPlayerCount)
    {
        float elapsedTime = 0f;
        int attemptCount = 0;

        while (elapsedTime < _sessionSearchTimeout && !_isCancelling)
        {
            attemptCount++;
            Debug.Log($"[Matchmaking] Server search attempt {attemptCount}");

            try
            {
                // 1. Unity Sessions API로 검색 시도 (초기화 대기)
                if (UnityLobbyManager.Instance != null)
                {
                    // 초기화되지 않았으면 초기화 시도
                    if (!UnityLobbyManager.Instance.IsInitialized)
                    {
                        Debug.Log("[Matchmaking] UnityLobbyManager 초기화 대기 중...");
                        await UnityLobbyManager.Instance.Initialize();
                    }

                    if (UnityLobbyManager.Instance.IsInitialized)
                    {
                        var gameMode = GameModeCatalog.GetModeId(_currentGameMode);
                        Debug.Log($"[Matchmaking] Unity Sessions 검색 중... GameMode: {gameMode}");
                        var sessions = await UnityLobbyManager.Instance.QuerySessions(gameMode);
                        
                        if (sessions != null && sessions.Count > 0)
                        {
                            Debug.Log($"[Matchmaking] {sessions.Count}개의 세션 발견!");
                            foreach (var session in sessions)
                            {
                                if (session.CurrentPlayers < session.MaxPlayers && IsJoinableWaitingSession(session))
                                {
                                    Debug.Log($"[Matchmaking] Found session: {session.SessionName} at {session.ServerIp}:{session.ServerPort}");
                                    // 서버 IP/Port 직접 사용 (Relay 없음)
                                    return new ServerInfo
                                    {
                                        ServerId = session.SessionId,
                                        IpAddress = session.ServerIp,
                                        Port = session.ServerPort,
                                        Region = "Unity Sessions"
                                    };
                                }
                            }
                        }
                        else
                        {
                            Debug.Log("[Matchmaking] Unity Sessions에서 세션을 찾지 못함");
                        }
                    }
                }

                // Unity Sessions에서 못 찾으면 재시도
                Debug.Log($"[Matchmaking] No server found on attempt {attemptCount}, waiting {_sessionSearchInterval}s...");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Matchmaking] Search attempt {attemptCount} failed: {ex.Message}");
            }

            await Task.Delay((int)(_sessionSearchInterval * 1000));
            elapsedTime += _sessionSearchInterval;
        }

        if (_isCancelling)
        {
            Debug.Log("[Matchmaking] Server search cancelled by user.");
            return null;
        }

        Debug.LogWarning($"[Matchmaking] Server search timed out after {_sessionSearchTimeout}s");
        return null;
    }

    /// <summary>
    /// 방 코드로 서버를 검색합니다.
    /// Unity Sessions API의 JoinSessionByCode 사용
    /// </summary>
    private async Task<ServerInfo?> FindServerByRoomCodeAsync(string roomCode)
    {
        float elapsedTime = 0f;
        int attemptCount = 0;

        while (elapsedTime < _sessionSearchTimeout && !_isCancelling)
        {
            attemptCount++;
            Debug.Log($"[Matchmaking] Room code search attempt {attemptCount}: {roomCode}");

            try
            {
                // Unity Sessions API의 JoinSessionByCode 사용
                if (UnityLobbyManager.Instance != null)
                {
                    if (!UnityLobbyManager.Instance.IsInitialized)
                    {
                        await UnityLobbyManager.Instance.Initialize();
                    }

                    Debug.Log($"[Matchmaking] Calling JoinSessionByCode with code: {roomCode}");
                    var session = await UnityLobbyManager.Instance.JoinSessionByCode(roomCode);
                    
                    if (session != null)
                    {
                        if (!IsJoinableWaitingSession(session))
                        {
                            Debug.LogWarning($"[Matchmaking] Room code session rejected: 진행 중 또는 종료된 세션입니다. GameState={session.GameState}, Started={session.IsGameStarted}, Ended={session.IsGameEnded}");
                            UpdateStatus("이미 진행 중이거나 종료된 방입니다.");
                            await Task.Delay((int)(_sessionSearchInterval * 1000));
                            elapsedTime += _sessionSearchInterval;
                            continue;
                        }

                        if (session.HasIdentityVerificationReady && !session.IsIdentityVerificationReady)
                        {
                            string reason = string.IsNullOrWhiteSpace(session.IdentityVerificationStatus)
                                ? "인증 백엔드가 준비되지 않은 서버입니다."
                                : session.IdentityVerificationStatus;
                            Debug.LogWarning($"[Matchmaking] Room code session rejected: Auth backend unavailable. Reason={reason}");
                            UpdateStatus(reason);
                            await Task.Delay((int)(_sessionSearchInterval * 1000));
                            elapsedTime += _sessionSearchInterval;
                            continue;
                        }

                        Debug.Log($"[Matchmaking] Found session by code: {session.SessionName} at {session.ServerIp}:{session.ServerPort}");
                        _currentRoomCode = roomCode;
                        if (session.MaxPlayers > 0)
                        {
                            _targetPlayerCount = session.MaxPlayers;
                        }
                        else
                        {
                            _targetPlayerCount = GameModeCatalog.GetMaxPlayers(GameMode.Custom, 8);
                        }

                        if (session.CustomGameTimeSeconds > 0 || !string.IsNullOrWhiteSpace(session.CustomMapTemplate))
                        {
                            _currentCustomRoomSettings = NormalizeCustomRoomSettings(new CustomRoomSettings
                            {
                                IsEnabled = true,
                                PlayerCount = _targetPlayerCount > 0 ? _targetPlayerCount : session.MaxPlayers,
                                GameTimeSeconds = session.CustomGameTimeSeconds,
                                MapTemplateName = session.CustomMapTemplate
                            });
                        }
                        
                        return new ServerInfo
                        {
                            ServerId = session.SessionId,
                            IpAddress = session.ServerIp,
                            Port = session.ServerPort,
                            Region = "Unity Sessions"
                        };
                    }
                    else
                    {
                        Debug.Log($"[Matchmaking] JoinSessionByCode returned null for code: {roomCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Matchmaking] Room code search failed: {ex.Message}");
            }

            await Task.Delay((int)(_sessionSearchInterval * 1000));
            elapsedTime += _sessionSearchInterval;
        }

        Debug.LogWarning($"[Matchmaking] Room code '{roomCode}' not found after {_sessionSearchTimeout}s");
        return null;
    }

    #endregion

    #region Private Methods - Connection

    /// <summary>
    /// 서버에 연결합니다 (직접 IP/Port 연결)
    /// </summary>
    private async Task ConnectToServerAsync(ServerInfo serverInfo)
    {
        _currentState = MatchmakingState.ConnectingToServer;
        UpdateStatus("서버에 연결 중...");

        string resolvedClientAddress = ResolveClientAddress(serverInfo.IpAddress);

        _pendingGameConnection = new GameConnectionInfo
        {
            SessionName = serverInfo.ServerId,
            ServerInfo = serverInfo,
            GameMode = _currentGameMode,
            MaxPlayers = _targetPlayerCount,
            DirectServerIP = resolvedClientAddress,
            DirectServerPort = serverInfo.Port,
            RoomCode = _currentRoomCode,
            CustomSettings = (_currentGameMode == GameMode.Custom && _currentCustomRoomSettings.IsEnabled)
                ? _currentCustomRoomSettings
                : CustomRoomSettings.Disabled
        };

        // NetworkManager에 연결 정보 전달
        if (NetworkManager.Instance == null)
        {
            HandleFailure("Project NetworkManager를 찾을 수 없습니다.");
            return;
        }

        NetworkManager.Instance.SetConnectionInfo(_pendingGameConnection.Value);
        Debug.Log($"[Matchmaking] Connecting to server: {resolvedClientAddress}:{serverInfo.Port}");

        try
        {
            if (InstanceFinder.ClientManager != null)
            {
                InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionStateChanged;
            }

            bool clientStarted = await NetworkManager.Instance.JoinGameServer(_pendingGameConnection.Value);
            if (!clientStarted)
            {
                UnsubscribeClientConnectionState();
                HandleFailure("서버 연결 시작에 실패했습니다.");
                return;
            }

            // 연결 완료 대기 (타임아웃)
            float elapsed = 0f;
            while (!InstanceFinder.IsClientStarted && elapsed < _connectionTimeout)
            {
                await Task.Delay(100);
                elapsed += 0.1f;
            }

            if (!InstanceFinder.IsClientStarted)
            {
                UnsubscribeClientConnectionState();
                HandleFailure("서버 연결 시간 초과");
                return;
            }

            bool isIdentityVerified = await VerifyIdentityBeforeWaitingStateAsync();
            if (!isIdentityVerified)
            {
                UnsubscribeClientConnectionState();

                if (InstanceFinder.ClientManager != null && InstanceFinder.IsClientStarted)
                {
                    InstanceFinder.ClientManager.StopConnection();
                }

                string failReason = string.IsNullOrWhiteSpace(_statusMessage)
                    ? "신원 검증에 실패했습니다."
                    : _statusMessage;
                HandleFailure(failReason);
                return;
            }

            UnsubscribeClientConnectionState();

            _currentState = MatchmakingState.WaitingForPlayers;
            UpdateStatus("플레이어 대기 중...");
            Debug.Log("[Matchmaking] 서버 연결 성공!");
            
            StartMonitoringGameState();
            
            // 세션 병합 모니터링 시작 (Custom 모드 제외)
            StartSessionConsolidation();
        }
        catch (Exception ex)
        {
            UnsubscribeClientConnectionState();
            HandleFailure($"연결 오류: {ex.Message}");
            Debug.LogError($"[Matchmaking] Connection error: {ex}");
        }
    }

    private void OnClientConnectionStateChanged(FishNet.Transporting.ClientConnectionStateArgs args)
    {
        Debug.Log($"[Matchmaking] Client connection state: {args.ConnectionState}");

        switch (args.ConnectionState)
        {
            case LocalConnectionState.Started:
                _currentPlayersInLobby++;
                OnPlayerCountChanged?.Invoke(_currentPlayersInLobby, _targetPlayerCount);
                break;

            case LocalConnectionState.Stopped:
                if (_currentState != MatchmakingState.Idle && !_isCancelling)
                {
                    HandleFailure(LOBBY_RETURN_DISCONNECT_MESSAGE);
                    QueueLobbyRestrictionMessage(LOBBY_RETURN_DISCONNECT_MESSAGE);
                    ReturnToLobbyAfterDisconnect();
                }
                break;
        }
    }

    #endregion

    #region Private Methods - Identity Verification

    private async Task<bool> VerifyIdentityBeforeWaitingStateAsync()
    {
        if (_currentGameMode == GameMode.PracticeRange)
        {
            return true;
        }

        if (!ServiceLocator.TryGet<IAuthService>(out IAuthService authService) || authService == null || !authService.IsSignedIn)
        {
            UpdateStatus("로그인 정보가 없어 신원 검증을 진행할 수 없습니다.");
            Debug.LogWarning("[Matchmaking] AuthService가 없거나 로그인 상태가 아닙니다.");
            return false;
        }

        string idToken = await authService.GetIdTokenAsync();
        if (string.IsNullOrWhiteSpace(idToken))
        {
            UpdateStatus("ID Token을 가져오지 못해 신원 검증에 실패했습니다.");
            Debug.LogWarning("[Matchmaking] ID Token이 비어 있습니다.");
            return false;
        }

        GameStateManager gameStateManager = await WaitForSpawnedGameStateManagerAsync(_connectionTimeout);
        if (gameStateManager == null)
        {
            UpdateStatus("GameStateManager를 찾지 못해 신원 검증을 진행할 수 없습니다.");
            Debug.LogWarning("[Matchmaking] GameStateManager 대기 시간 초과");
            return false;
        }

        float timeoutSeconds = Mathf.Max(IDENTITY_VERIFICATION_TIMEOUT_SECONDS, _connectionTimeout);
        for (int attempt = 0; attempt <= IDENTITY_VERIFICATION_MAX_RETRY_COUNT; attempt++)
        {
            bool isVerified = await gameStateManager.VerifyIdentityOnServerAsync(idToken, timeoutSeconds);
            if (isVerified)
            {
                Debug.Log("[Matchmaking] 로비 진입 전 신원 검증 완료");
                return true;
            }

            string reason = string.IsNullOrWhiteSpace(gameStateManager.LastIdentityVerificationErrorMessage)
                ? "서버에서 신원 검증에 실패했습니다."
                : gameStateManager.LastIdentityVerificationErrorMessage;
            bool isRetryableFailure = IsRetryableIdentityVerificationFailure(reason);
            bool canRetry = isRetryableFailure && attempt < IDENTITY_VERIFICATION_MAX_RETRY_COUNT;
            int displayAttempt = attempt + 1;

            if (!canRetry)
            {
                UpdateStatus(reason);
                Debug.LogWarning($"[Matchmaking] 신원 검증 실패: {reason}");
                return false;
            }

            Debug.LogWarning($"[Matchmaking] 신원 검증 재시도 예정 ({displayAttempt}/{IDENTITY_VERIFICATION_MAX_RETRY_COUNT + 1}). Reason={reason}");
            await Task.Delay(IDENTITY_VERIFICATION_RETRY_DELAY_MS);
        }

        UpdateStatus("신원 검증에 실패했습니다.");
        return false;
    }

    private static bool IsRetryableIdentityVerificationFailure(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return reason.Contains("시간 초과", StringComparison.Ordinal) ||
               reason.Contains("Request timeout", StringComparison.OrdinalIgnoreCase) ||
               BackendIdentityVerificationService.IsInfrastructureFailureMessage(reason);
    }

    private static async Task<GameStateManager> WaitForSpawnedGameStateManagerAsync(float timeoutSeconds)
    {
        float elapsed = 0f;
        while (elapsed < timeoutSeconds)
        {
            GameStateManager gameStateManager = GameStateManager.Instance;
            if (gameStateManager != null &&
                gameStateManager.NetworkObject != null &&
                gameStateManager.NetworkObject.IsSpawned)
            {
                return gameStateManager;
            }

            await Task.Delay(100);
            elapsed += 0.1f;
        }

        return null;
    }

    #endregion

    #region Private Methods - GameState Monitoring

    /// <summary>
    /// GameState 모니터링 시작 (Fusion MonitorGameStateCoroutine 동등)
    /// </summary>
    private void StartMonitoringGameState()
    {
        if (_isMonitoringGameState) return;
        _isMonitoringGameState = true;
        _gameStateMonitorCoroutine = StartCoroutine(MonitorGameStateCoroutine());
    }

    /// <summary>
    /// GameState 모니터링 중지
    /// </summary>
    private void StopMonitoringGameState()
    {
        if (_gameStateMonitorCoroutine != null)
        {
            StopCoroutine(_gameStateMonitorCoroutine);
            _gameStateMonitorCoroutine = null;
        }
        _isMonitoringGameState = false;
    }

    /// <summary>
    /// GameState 모니터링 코루틴 (플레이어 수 추적, 게임 시작 감지)
    /// </summary>
    private IEnumerator MonitorGameStateCoroutine()
    {
        GameStateManager gsm = null;
        float maxWaitTime = 30f;
        float waitTime = 0f;
        float checkInterval = 0.5f;

        // GameStateManager 대기
        while (gsm == null && waitTime < maxWaitTime)
        {
            gsm = GameStateManager.Instance;
            if (gsm == null || gsm.NetworkObject == null || !gsm.NetworkObject.IsSpawned)
            {
                gsm = null;
                waitTime += checkInterval;
                yield return new WaitForSeconds(checkInterval);
            }
        }

        if (gsm == null)
        {
            Debug.LogError("[Matchmaking] GameStateManager를 찾을 수 없습니다!");
            HandleFailure("GameStateManager를 찾을 수 없습니다.");
            _isMonitoringGameState = false;
            yield break;
        }

        Debug.Log("[Matchmaking] GameStateManager 발견. 모니터링 시작.");

        // 게임 상태 모니터링
        while (_isMonitoringGameState)
        {
            if (gsm == null || gsm.NetworkObject == null || !gsm.NetworkObject.IsSpawned)
            {
                Debug.LogWarning("[Matchmaking] GameStateManager가 유효하지 않음");
                break;
            }

            int currentPlayerCount = gsm.ConnectedPlayers.Value;
            int targetPlayerCount = gsm.TargetPlayerCount.Value;

            // 플레이어 수 변경 감지
            if (currentPlayerCount != _currentPlayersInLobby || targetPlayerCount != _targetPlayerCount)
            {
                Debug.Log($"[Matchmaking] Player count changed: {_currentPlayersInLobby}/{_targetPlayerCount} -> {currentPlayerCount}/{targetPlayerCount}");
                _currentPlayersInLobby = currentPlayerCount;
                _targetPlayerCount = targetPlayerCount;
                OnPlayerCountChanged?.Invoke(_currentPlayersInLobby, _targetPlayerCount);
            }

            // 게임 시작 감지
            if (gsm.IsGameStarted)
            {
                Debug.Log("[Matchmaking] 게임 시작됨! 즉시 UI 갱신 처리.");
                HandleGameStartedImmediate();
                yield break;
            }

            yield return new WaitForSeconds(checkInterval);
        }

        _isMonitoringGameState = false;
    }

    #endregion

    #region Private Methods - Utilities

    private static bool IsGuestRankedBlocked(GameMode mode)
    {
        if (mode != GameMode.Ranked)
        {
            return false;
        }

        if (!ServiceLocator.TryGet<IAuthService>(out IAuthService authService) || authService == null)
        {
            return false;
        }

        return authService.IsSignedIn && authService.IsAnonymous;
    }

    private static bool IsJoinableWaitingSession(SessionInfo session)
    {
        if (session == null)
        {
            return false;
        }

        if (session.IsGameStarted || session.IsGameEnded)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(session.GameState) ||
               string.Equals(session.GameState, GameState.WaitingForPlayers.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateStatus(string message)
    {
        _statusMessage = message;
        OnMatchmakingUIUpdate?.Invoke(message);
    }

    private void UnsubscribeClientConnectionState()
    {
        if (InstanceFinder.ClientManager != null)
        {
            InstanceFinder.ClientManager.OnClientConnectionState -= OnClientConnectionStateChanged;
        }
    }

    private string ResolveClientAddress(string serverIpAddress)
    {
        if (string.IsNullOrWhiteSpace(serverIpAddress))
        {
            return "localhost";
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!string.IsNullOrWhiteSpace(_myPublicIp) &&
            string.Equals(serverIpAddress, _myPublicIp, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log($"[Matchmaking] Detected connection to self (Public IP: {_myPublicIp}). Redirecting to localhost.");
            return "127.0.0.1";
        }
#endif

        return serverIpAddress;
    }
    
    private void HandleFailure(string errorMessage)
    {
        _currentState = MatchmakingState.Failed;
        UpdateStatus(errorMessage);
        OnMatchmakingFailed?.Invoke(errorMessage);
    }

    private async void ReturnToLobbyAfterDisconnect()
    {
        if (_isReturningToLobbyAfterDisconnect)
        {
            return;
        }

        _isReturningToLobbyAfterDisconnect = true;

        try
        {
            await Task.Delay(DISCONNECT_RETURN_DELAY_MS);

            if (!this || !gameObject.scene.IsValid())
            {
                return;
            }

            CancelAndReturnToLobby();
        }
        finally
        {
            _isReturningToLobbyAfterDisconnect = false;
        }
    }

    public static void QueueLobbyRestrictionMessage(string message)
    {
        _pendingLobbyRestrictionMessage = string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : message.Trim();
    }

    public static bool TryConsumePendingLobbyRestrictionMessage(out string message)
    {
        message = _pendingLobbyRestrictionMessage;
        _pendingLobbyRestrictionMessage = string.Empty;
        return !string.IsNullOrWhiteSpace(message);
    }

    private bool TryBeginMatchmaking()
    {
        ResetState();
        
        if (_currentState == MatchmakingState.SelectingServer || 
            _currentState == MatchmakingState.ConnectingToServer)
        {
            Debug.LogWarning($"[Matchmaking] Already in progress. Current state: {_currentState}");
            return false;
        }
        
        _currentState = MatchmakingState.SelectingServer;
        return true;
    }

    private CustomRoomSettings NormalizeCustomRoomSettings(CustomRoomSettings customSettings)
    {
        int minPlayers = MIN_CUSTOM_PLAYER_COUNT;
        int maxPlayers = MAX_CUSTOM_PLAYER_COUNT;

        int playerCount = customSettings.PlayerCount <= 0 ? DEFAULT_CUSTOM_PLAYER_COUNT : customSettings.PlayerCount;
        playerCount = Mathf.Clamp(playerCount, minPlayers, maxPlayers);

        int gameTimeSeconds = customSettings.GameTimeSeconds <= 0
            ? DEFAULT_CUSTOM_GAME_TIME_SECONDS
            : customSettings.GameTimeSeconds;
        gameTimeSeconds = Mathf.Clamp(gameTimeSeconds, MIN_CUSTOM_GAME_TIME_SECONDS, MAX_CUSTOM_GAME_TIME_SECONDS);

        return new CustomRoomSettings
        {
            IsEnabled = customSettings.IsEnabled,
            PlayerCount = playerCount,
            GameTimeSeconds = gameTimeSeconds,
            MapTemplateName = string.IsNullOrWhiteSpace(customSettings.MapTemplateName)
                ? string.Empty
                : customSettings.MapTemplateName.Trim()
        };
    }

    private void ResetState()
    {
        Debug.Log($"[Matchmaking] ResetState - Start (Current State: {_currentState})");

        if (!string.IsNullOrEmpty(_currentRoomCode))
        {
            Debug.Log($"[Matchmaking] Clearing room code: {_currentRoomCode}");
            if (_roomCodeToSessionMap.ContainsKey(_currentRoomCode))
            {
                string sessionName = _roomCodeToSessionMap[_currentRoomCode];
                _sessionToRoomCodeMap.Remove(sessionName);
                _roomCodeToSessionMap.Remove(_currentRoomCode);
            }
        }

        _currentState = MatchmakingState.Idle;
        _currentGameMode = GameMode.None;
        _currentRoomCode = null;
        _pendingGameConnection = null;
        _currentPlayersInLobby = 0;
        _targetPlayerCount = 0;
        _currentSessionId = null;
        _preparedGameMode = GameMode.None;
        _preparedPlayerCount = 0;
        _preparedRoomCode = null;
        _isPreparedToCreate = false;
        _preparedCustomRoomSettings = CustomRoomSettings.Disabled;
        _currentCustomRoomSettings = CustomRoomSettings.Disabled;
        _availableSessions.Clear();
        _hasGameStartNotified = false;

        // 세션 병합 모니터링 정리
        StopSessionConsolidation();

        UpdateStatus("Idle");
        Debug.Log($"[Matchmaking] ResetState - Complete");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[Matchmaking] OnSceneLoaded - Scene: {scene.name}");

        if (scene.name == _lobbySceneName)
        {
            Debug.Log("[Matchmaking] Lobby scene detected - calling ResetForLobbyEntry");
            ResetForLobbyEntry();
        }
        else if (scene.name == _gamePlaySceneName)
        {
            if (_currentState == MatchmakingState.Completed)
            {
                Debug.Log("[Matchmaking] GamePlay scene already handled (Completed), skipping...");
                return;
            }

            Debug.Log($"[Matchmaking] GamePlay scene detected - State: {_currentState}");
            _currentState = MatchmakingState.Completed;
        }
    }

    #endregion

    #region Session Consolidation (세션 병합)

    /// <summary>
    /// 세션 병합 모니터링 시작 - 대기 중 더 나은 세션으로 이동
    /// Custom 모드는 병합하지 않음 (방장이 만든 방이므로)
    /// </summary>
    private void StartSessionConsolidation()
    {
        if (_isConsolidating || _currentGameMode == GameMode.Custom) return;
        
        _isConsolidating = true;
        _sessionConsolidationCoroutine = StartCoroutine(SessionConsolidationCoroutine());
        Debug.Log($"[Matchmaking] SessionConsolidation started for session: {_currentSessionId}");
    }

    private void StopSessionConsolidation()
    {
        if (_sessionConsolidationCoroutine != null)
        {
            StopCoroutine(_sessionConsolidationCoroutine);
            _sessionConsolidationCoroutine = null;
        }
        _isConsolidating = false;
        Debug.Log("[Matchmaking] SessionConsolidation stopped");
    }

    /// <summary>
    /// 대기 중 같은 모드 + 더 많은 인원 세션 발견 시 이동
    /// </summary>
    private IEnumerator SessionConsolidationCoroutine()
    {
        float checkInterval = 3f; // 3초마다 체크

        while (_isConsolidating && _currentState == MatchmakingState.WaitingForPlayers)
        {
            yield return new WaitForSeconds(checkInterval);

            if (_isCancelling || _currentState != MatchmakingState.WaitingForPlayers) break;

            // Unity Sessions에서 세션 목록 검색
            var searchTask = SearchForBetterSession();
            while (!searchTask.IsCompleted) yield return null;

            if (searchTask.Result != null)
            {
                Debug.Log($"[Matchmaking] Found better session: {searchTask.Result.SessionId} with {searchTask.Result.CurrentPlayers} players");
                
                // 현재 세션보다 더 많은 플레이어가 있는 세션으로 이동
                var migrateTask = MigrateToSessionAsync(searchTask.Result);
                while (!migrateTask.IsCompleted) yield return null;
                
                break;
            }
        }

        _isConsolidating = false;
    }

    /// <summary>
    /// 더 나은 세션 검색 (같은 GameMode + 더 많은 플레이어)
    /// </summary>
    private async Task<SessionInfo> SearchForBetterSession()
    {
        if (UnityLobbyManager.Instance == null || !UnityLobbyManager.Instance.IsInitialized)
            return null;

        try
        {
            var gameMode = GameModeCatalog.GetModeId(_currentGameMode);
            var sessions = await UnityLobbyManager.Instance.QuerySessions(gameMode);

            if (sessions == null || sessions.Count == 0)
                return null;

            // 현재 세션 플레이어 수 확인
            int currentPlayers = _currentPlayersInLobby;

            foreach (var session in sessions)
            {
                // 현재 세션 제외
                if (session.SessionId == _currentSessionId) continue;
                
                // 게임 시작 전 + 빈자리 있음 + 더 많은 플레이어
                if (IsJoinableWaitingSession(session) && 
                    session.CurrentPlayers < session.MaxPlayers && 
                    session.CurrentPlayers > currentPlayers)
                {
                    Debug.Log($"[Matchmaking] Better session found: {session.SessionName} ({session.CurrentPlayers} players vs current {currentPlayers})");
                    return session;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Matchmaking] SearchForBetterSession failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 다른 세션으로 이동
    /// </summary>
    private async Task MigrateToSessionAsync(SessionInfo targetSession)
    {
        Debug.Log($"[Matchmaking] Migrating to session: {targetSession.SessionName}");
        UpdateStatus("더 나은 세션으로 이동 중...");

        try
        {
            // 1. 현재 서버 연결 해제
            if (InstanceFinder.IsClientStarted)
            {
                InstanceFinder.ClientManager.StopConnection();
                await Task.Delay(500);
            }

            // 2. 새 세션으로 연결
            _currentSessionId = targetSession.SessionId;
            
            var serverInfo = new ServerInfo
            {
                ServerId = targetSession.SessionId,
                IpAddress = targetSession.ServerIp,
                Port = targetSession.ServerPort,
                Region = "Unity Sessions"
            };

            await ConnectToServerAsync(serverInfo);
            
            Debug.Log($"[Matchmaking] Successfully migrated to {targetSession.SessionName}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Matchmaking] Migration failed: {ex.Message}");
            HandleFailure($"세션 이동 실패: {ex.Message}");
        }
    }


    private async Task TransitionToGameScene()
    {
        Debug.Log("[Matchmaking] Loading GamePlay scene...");
        
        // 씬 로드
        var operation = SceneManager.LoadSceneAsync("GamePlay");
        while (!operation.isDone)
        {
            await Task.Delay(100);
        }
        
        Debug.Log("[Matchmaking] GamePlay scene loaded.");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private async Task FetchMyPublicIp()
    {
        try
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                _myPublicIp = await client.GetStringAsync("https://api.ipify.org");
                Debug.Log($"[Matchmaking] My Public IP fetched: {_myPublicIp}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Matchmaking] Failed to fetch Public IP: {ex.Message}");
        }
    }
#endif

    #endregion
}

/// <summary>
/// 세션 데이터 (Fishnet용)
/// </summary>
[Serializable]
public struct SessionData
{
    public string SessionId;
    public string ServerIP;
    public int ServerPort;
    public int PlayerCount;
    public int MaxPlayers;
    public bool IsInGame;
    public bool IsCustom;
    public string RoomCode;
    public GameMode GameMode;
}
