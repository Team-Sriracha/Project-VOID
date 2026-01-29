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
    // [SerializeField] private string _practiceSceneName = "PracticeRange"; // Removed
    [SerializeField] private string _lobbySceneName = "Lobby";

    [Header("연결 설정")]
    [SerializeField] private float _connectionTimeout = 10f;
    [SerializeField] private float _sessionSearchTimeout = 30f;
    [SerializeField] private float _sessionSearchInterval = 2f;

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
    
    // Prepare된 매칭 정보 (Matching 씬에서 실제 접속 시 사용)
    private GameMode _preparedGameMode = GameMode.None;
    private int _preparedPlayerCount = 0;
    private string _preparedRoomCode = null;
    private bool _isPreparedToCreate = false;

    // 서버 목록 캐시
    private List<SessionData> _availableSessions = new List<SessionData>();
    
    // GameState 모니터링
    private Coroutine _gameStateMonitorCoroutine;
    private bool _isMonitoringGameState = false;


    // 세션 병합 (매칭 중 더 나은 세션으로 이동)
    private Coroutine _sessionConsolidationCoroutine;
    private bool _isConsolidating = false;
    private string _currentSessionId;

    // 내 공인 IP (NAT Loopback 우회용)
    private string _myPublicIp;

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
        Debug.Log($"[Matchmaking] PrepareMatchmaking - Mode: {mode}");
        ResetState();
        
        _preparedGameMode = mode;
        _preparedPlayerCount = mode switch
        {
            GameMode.FourPlayer => 4,
            GameMode.EightPlayer => 8,
            GameMode.PracticeRange => 1,
            _ => 4
        };
        _preparedRoomCode = null;
        _isPreparedToCreate = false;
        
        _currentGameMode = mode;
        _targetPlayerCount = _preparedPlayerCount;
    }

    /// <summary>
    /// 커스텀 방 생성 준비 (Lobby에서 호출, Matching 씬에서 실제 생성)
    /// </summary>
    public void PrepareCustomRoom(int playerCount)
    {
        Debug.Log($"[Matchmaking] PrepareCustomRoom - PlayerCount: {playerCount}");
        ResetState();
        
        _preparedGameMode = GameMode.Custom;
        _preparedPlayerCount = playerCount;
        _preparedRoomCode = null;
        _isPreparedToCreate = true;
        
        _currentGameMode = GameMode.Custom;
        _targetPlayerCount = playerCount;
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
        
        _currentGameMode = GameMode.Custom;
        _currentRoomCode = _preparedRoomCode;
    }

    /// <summary>
    /// Matching 씬에서 호출 - 준비된 매칭 정보로 실제 서버 접속 시작
    /// </summary>
    public void ExecuteMatchmaking()
    {
        Debug.Log($"[Matchmaking] ExecuteMatchmaking - Mode: {_preparedGameMode}, PlayerCount: {_preparedPlayerCount}, RoomCode: {_preparedRoomCode}, IsCreate: {_isPreparedToCreate}");
        
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
                _ = CreateCustomRoomAsync(_preparedPlayerCount);
            }
            else if (!string.IsNullOrEmpty(_preparedRoomCode))
            {
                _ = JoinCustomRoomAsync(_preparedRoomCode);
            }
        }
        else if (_preparedGameMode == GameMode.PracticeRange)
        {
            _ = StartPracticeModeAsync();
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

    #endregion

    #region Public Methods - Matchmaking

    public async void StartMatchmaking(GameMode mode)
    {
        await StartMatchmakingAsync(mode);
    }

    public async void CreateCustomRoom(int playerCount)
    {
        await CreateCustomRoomAsync(playerCount);
    }

    public async void JoinCustomRoom(string roomCode)
    {
        await JoinCustomRoomAsync(roomCode);
    }
    
    public void EnterPracticeRange()
    {
        _ = StartPracticeModeAsync();
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
        
        // GameState 모니터링 중지
        StopMonitoringGameState();

        try
        {
            // Fishnet 연결 해제
            if (InstanceFinder.IsClientStarted)
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
        MatchCompleted?.Invoke();
    }

    /// <summary>
    /// 서버의 씬 전환 시 호출
    /// </summary>
    public void OnServerRequestedSceneTransition()
    {
        Debug.Log("[Matchmaking] 서버에서 씬 전환 요청!");
        
        _currentState = MatchmakingState.ConnectingToServer;
        UpdateStatus("게임 로딩 중...");
        
        if (LoadingUIManager.Instance != null)
        {
            LoadingUIManager.Instance.ShowLoadingAndWaitForMap();
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

        if (mode != GameMode.FourPlayer && mode != GameMode.EightPlayer)
        {
            Debug.LogError($"[Matchmaking] Invalid mode for matchmaking: {mode}");
            return;
        }

        if (!TryBeginMatchmaking())
            return;

        _currentGameMode = mode;
        _targetPlayerCount = mode == GameMode.FourPlayer ? 4 : 8;

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

    private async Task CreateCustomRoomAsync(int playerCount)
    {
        Debug.Log($"[Matchmaking] CreateCustomRoom - PlayerCount: {playerCount}");

        if (playerCount < 1 || playerCount > 8)
        {
            Debug.LogError($"[Matchmaking] Invalid player count: {playerCount}");
            return;
        }

        if (!TryBeginMatchmaking())
            return;

        _currentGameMode = GameMode.Custom;
        _targetPlayerCount = playerCount;

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

    private async Task StartPracticeModeAsync()
    {
        Debug.Log("[Matchmaking] StartPracticeMode with Yak Transport (Offline)");

        ResetState();

        if (_currentState == MatchmakingState.SelectingServer || _currentState == MatchmakingState.ConnectingToServer)
        {
            Debug.LogWarning($"[Matchmaking] Already in progress. Current state: {_currentState}");
            return;
        }

        _currentState = MatchmakingState.ConnectingToServer;
        _currentGameMode = GameMode.PracticeRange;
        _targetPlayerCount = 1;
        UpdateStatus("연습장 준비 중...");

        try
        {
            // Yak Transport를 사용한 완전 오프라인 모드
            // 서버 검색 없이 로컬 Host + Client 즉시 시작
            await StartOfflinePracticeMode();
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
    private async Task StartOfflinePracticeMode()
    {
        Debug.Log("[Matchmaking] Starting offline practice mode with Yak Transport");

        // FishNet NetworkManager 확인
        var fishnetNetworkManager = InstanceFinder.NetworkManager;
        if (fishnetNetworkManager == null && _networkManagerPrefab != null)
        {
            Debug.Log("[Matchmaking] Creating NetworkManager from prefab...");
            var go = Instantiate(_networkManagerPrefab);
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

        // Yak Transport로 전환 (Fish-Net Pro 필수)
        if (!TransportUtils.SwitchToYakTransport(fishnetNetworkManager))
        {
            HandleFailure("Yak Transport를 찾을 수 없습니다. Fish-Net Pro와 Yak Transport가 필요합니다.");
            return;
        }
        
        Debug.Log("[Matchmaking] Yak Transport activated successfully!");

        // 연결 정보 설정 (오프라인 모드)
        _pendingGameConnection = new GameConnectionInfo
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
            RoomCode = ""
        };

        // NetworkManager에 연결 정보 전달
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SetConnectionInfo(_pendingGameConnection.Value);
        }

        // GamePlay 씬으로 전환
        Debug.Log($"[Matchmaking] Transitioning to {_gamePlaySceneName} scene for offline practice...");
        _currentState = MatchmakingState.WaitingForPlayers;
        UpdateStatus("연습장 로딩 중...");
        
        // Load GamePlay Scene asynchronously
        await LoadPracticeSceneAsync();
        
        // Start Host (Yak) in the new scene
        Debug.Log("[Matchmaking] Scene loaded. Starting Host...");
        
        // Scene Loaded
        await Task.Delay(100); // 1. Wait a bit for Unity to settle scene objects

        // Find and destroy duplicate NetworkManagers
        var fishnetManagers = UnityEngine.Object.FindObjectsByType<FishNet.Managing.NetworkManager>(FindObjectsSortMode.None);
        if (fishnetManagers.Length > 1)
        {
            Debug.LogWarning($"[Matchmaking] Detected {fishnetManagers.Length} NetworkManagers. Cleaning up duplicates...");
            foreach (var nm in fishnetManagers)
            {
                // If this is NOT the one attached to our Singleton's GameObject
                if (nm.gameObject != NetworkManager.Instance.gameObject)
                {
                    Debug.LogWarning($"[Matchmaking] Destroying duplicate NetworkManager on GameObject '{nm.gameObject.name}' (ID: {nm.GetInstanceID()})");
                    UnityEngine.Object.DestroyImmediate(nm.gameObject);
                }
            }
        }
        
        // Refresh InstanceFinder just in case (Cannot assign to read-only, assuming FishNet handles it or it's implicitly updated)
        // InstanceFinder.NetworkManager = NetworkManager.Instance.GetComponent<FishNet.Managing.NetworkManager>(); 

        // MatchmakingManager.cs

        // ... existing duplicate cleanup ...
        
        // Debug Singleton
        Debug.Log($"[Matchmaking] InstanceFinder.NM ID: {InstanceFinder.NetworkManager?.GetInstanceID()}, My NM.Instance ID: {NetworkManager.Instance?.FishnetManager?.GetInstanceID()}");
        
        // Force Re-apply Yak Transport
        Debug.Log("[Matchmaking] Forcing Yak Transport re-initialization for GamePlay scene...");
        bool switchResult = TransportUtils.SwitchToYakTransport(InstanceFinder.NetworkManager);
        if (!switchResult)
        {
             HandleFailure("Failed to initialize Yak Transport.");
             return;
        }

        // Validate Transport
        Debug.Log($"[Matchmaking] Current Transport: {InstanceFinder.NetworkManager.TransportManager.Transport?.GetType().Name}");

        // Start Local Host (Offline Server + Client)
        Debug.Log("[Matchmaking] Local Host (Practice) starting... Waiting for Internal Server to be ready...");

        // Use TaskCompletionSource to wait for OnServerConnectionState
        var tcs = new TaskCompletionSource<bool>();
        
        void OnServerConnectionState(FishNet.Transporting.ServerConnectionStateArgs args)
        {
            Debug.Log($"[Matchmaking] Local Server Connection State: {args.ConnectionState}");
            if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Started)
            {
                Debug.Log("[Matchmaking] Local Server Started (Event Received).");
                tcs.TrySetResult(true);
            }
            else if (args.ConnectionState == FishNet.Transporting.LocalConnectionState.Stopped)
            {
                Debug.LogWarning("[Matchmaking] Local Server Stopped unexpectedly.");
                tcs.TrySetResult(false);
            }
        }

        // MatchmakingManager.cs
        
        // ... (subscription logic remains) ...
        Debug.Log("[Matchmaking] Subscribing to OnServerConnectionState.");
        InstanceFinder.ServerManager.OnServerConnectionState += OnServerConnectionState;

        bool success = false;
        try
        {
            // DIRECT START for debugging
            Debug.Log("[Matchmaking] [DIRECT] Starting Server Connection...");
            success = InstanceFinder.ServerManager.StartConnection();
            Debug.Log($"[Matchmaking] [DIRECT] StartConnection returned: {success}");

            // Wait for server to actually start before starting client
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Matchmaking] [DIRECT] StartConnection failed with exception: {ex.Message}");
            success = false;
        }
        
        if (success)
        {
            // If already started (rare if we just called it, but possible), set true immediately
            // ... (rest of the logic) ...
            if (InstanceFinder.ServerManager.Started)
            {
                 Debug.Log("[Matchmaking] Local Server already started.");
                 tcs.TrySetResult(true);
            }

            // Wait with timeout
            var timeoutTask = Task.Delay(5000);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            // Unsubscribe
            InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;

            if (completedTask == tcs.Task && tcs.Task.Result)
            {
                Debug.Log($"[Matchmaking] Local Server Ready! (Started: {InstanceFinder.ServerManager.Started})");

                // Start Client NOW that server is ready
                Debug.Log("[Matchmaking] [DIRECT] Server Ready. NOW Starting Client Connection...");
                InstanceFinder.ClientManager.StartConnection("localhost");
                
                // Dynamically spawn PracticeModeManager
                SpawnPracticeModeManager();
                
                // FORCE GAME START FOR PRACTICE MODE
                if (GameStateManager.Instance != null && InstanceFinder.IsServerStarted)
                {
                     // Ensure connection info is set so NetworkManager knows what to spawn
                     if (NetworkManager.Instance != null)
                     {
                         NetworkManager.Instance.SetConnectionInfo(new GameConnectionInfo { GameMode = GameMode.PracticeRange, MaxPlayers = 1 });
                     }
                     
                     GameStateManager.Instance.TargetPlayerCount.Value = 1;
                     // Give it a moment for client to actually connect before switching state, 
                     // although in Host mode it's immediate. 
                     // But strictly speaking, we want the "WaitingForPlayers" -> "Playing" transition to happen.
                     GameStateManager.Instance.CurrentGameState.Value = GameState.Playing;
                     Debug.Log("[Matchmaking] Forced Game State to Playing for Practice Mode.");
                }

                _currentState = MatchmakingState.InGame; // Actually playing
                UpdateStatus("연습장 시작됨");
            }
            else
            {
                Debug.LogError($"[Matchmaking] Server Start Timed Out or Stopped (Manager: {InstanceFinder.ServerManager != null}, Started: {InstanceFinder.ServerManager?.Started})");
                HandleFailure("ServerManager failed to start in time.");
            }
        }
        else
        {
             // Unsubscribe if failed immediately
             InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;
             HandleFailure("Failed to start Practice Host.");
        }
    }

    private void SpawnPracticeModeManager()
    {
        if (_practiceModeManagerPrefab == null)
        {
            Debug.LogError("[Matchmaking] PracticeModeManagerPrefab is not assigned!");
            return;
        }

        if (InstanceFinder.ServerManager == null || !InstanceFinder.ServerManager.Started)
        {
             Debug.LogError("[Matchmaking] ServerManager is not started. Cannot spawn PracticeModeManager.");
             return;
        }

        Debug.Log("[Matchmaking] Spawning PracticeModeManager...");
        var go = Instantiate(_practiceModeManagerPrefab);
        InstanceFinder.ServerManager.Spawn(go);
    }

    private async Task LoadPracticeSceneAsync()
    {
        // Use GamePlay scene for practice too
        var op = SceneManager.LoadSceneAsync(_gamePlaySceneName);
        if (op == null)
        {
            Debug.LogError($"[Matchmaking] Failed to load scene: {_gamePlaySceneName}");
            return;
        }
        
        while (!op.isDone)
        {
            await Task.Yield();
        }
        
        // Wait a frame for safety
        await Task.Yield();
    }

    // SwitchToYakTransport removed. Use TransportUtils.SwitchToYakTransport instead.

    /// <summary>
    /// 연습장 모드 서버 상태 변경 이벤트
    /// </summary>
    private void OnPracticeServerStateChanged(ServerConnectionStateArgs args)
    {
        Debug.Log($"[Matchmaking] Practice server state: {args.ConnectionState}");
    }

    /// <summary>
    /// 연습장 모드 클라이언트 상태 변경 이벤트
    /// </summary>
    private void OnPracticeClientStateChanged(ClientConnectionStateArgs args)
    {
        Debug.Log($"[Matchmaking] Practice client state: {args.ConnectionState}");

        if (args.ConnectionState == LocalConnectionState.Started)
        {
            _currentPlayersInLobby = 1;
            OnPlayerCountChanged?.Invoke(_currentPlayersInLobby, _targetPlayerCount);
        }
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
                        var gameMode = _currentGameMode.ToString();
                        Debug.Log($"[Matchmaking] Unity Sessions 검색 중... GameMode: {gameMode}");
                        var sessions = await UnityLobbyManager.Instance.QuerySessions(gameMode);
                        
                        if (sessions != null && sessions.Count > 0)
                        {
                            Debug.Log($"[Matchmaking] {sessions.Count}개의 세션 발견!");
                            foreach (var session in sessions)
                            {
                                if (session.CurrentPlayers < session.MaxPlayers && !session.IsGameStarted)
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
                        Debug.Log($"[Matchmaking] Found session by code: {session.SessionName} at {session.ServerIp}:{session.ServerPort}");
                        _currentRoomCode = roomCode;
                        
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

        _pendingGameConnection = new GameConnectionInfo
        {
            SessionName = serverInfo.ServerId,
            ServerInfo = serverInfo,
            GameMode = _currentGameMode,
            MaxPlayers = _targetPlayerCount,
            DirectServerIP = serverInfo.IpAddress,
            DirectServerPort = serverInfo.Port,
            RoomCode = _currentRoomCode
        };

        // NetworkManager에 연결 정보 전달
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.SetConnectionInfo(_pendingGameConnection.Value);
        }

        Debug.Log($"[Matchmaking] Connecting to server: {serverInfo.IpAddress}:{serverInfo.Port}");

        try
        {
            // FishNet NetworkManager 사용
            var fishnetNetworkManager = InstanceFinder.NetworkManager;
            if (fishnetNetworkManager == null && _networkManagerPrefab != null)
            {
                Debug.Log("[Matchmaking] Creating NetworkManager from prefab...");
                var go = Instantiate(_networkManagerPrefab);
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

            // Transport 설정 (Tugboat - 직접 연결)
            var transport = fishnetNetworkManager.TransportManager.Transport;
            if (transport is FishNet.Transporting.Tugboat.Tugboat tugboat)
            {

            // [NAT Loopback Fix] 로컬 개발 환경에서 공인 IP로 접속 시도 시 Localhost로 우회
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (serverInfo.IpAddress == _myPublicIp && !string.IsNullOrEmpty(_myPublicIp))
            {
                Debug.Log($"[Matchmaking] Detected connection to self (Public IP: {_myPublicIp}). Redirecting to localhost.");
                tugboat.SetClientAddress("127.0.0.1");
            }
            else
            {
                tugboat.SetClientAddress(serverInfo.IpAddress);
            }
#else
            tugboat.SetClientAddress(serverInfo.IpAddress);
#endif
            tugboat.SetPort((ushort)serverInfo.Port);
            Debug.Log($"[Matchmaking] Tugboat configured: {tugboat.GetClientAddress()}:{serverInfo.Port}");
            }

            // 연결 이벤트 구독
            fishnetNetworkManager.ClientManager.OnClientConnectionState += OnClientConnectionStateChanged;

            // 클라이언트 연결 시작
            fishnetNetworkManager.ClientManager.StartConnection();

            // 연결 완료 대기 (타임아웃)
            float elapsed = 0f;
            while (!InstanceFinder.IsClientStarted && elapsed < _connectionTimeout)
            {
                await Task.Delay(100);
                elapsed += 0.1f;
            }

            if (!InstanceFinder.IsClientStarted)
            {
                fishnetNetworkManager.ClientManager.OnClientConnectionState -= OnClientConnectionStateChanged;
                HandleFailure("서버 연결 시간 초과");
                return;
            }

            _currentState = MatchmakingState.WaitingForPlayers;
            UpdateStatus("플레이어 대기 중...");
            Debug.Log("[Matchmaking] 서버 연결 성공!");
            
            StartMonitoringGameState();
            
            // 세션 병합 모니터링 시작 (Custom 모드 제외)
            StartSessionConsolidation();
        }
        catch (Exception ex)
        {
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
                    HandleFailure("서버 연결이 끊어졌습니다");
                }
                break;
        }
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
                Debug.Log("[Matchmaking] 게임 시작됨! OnMatchmakingSuccess 호출.");
                UpdateStatus("게임 시작!");
                OnMatchmakingSuccess?.Invoke();
                StopMonitoringGameState();
                yield break;
            }

            yield return new WaitForSeconds(checkInterval);
        }

        _isMonitoringGameState = false;
    }

    #endregion

    #region Private Methods - Utilities

    private void UpdateStatus(string message)
    {
        _statusMessage = message;
        OnMatchmakingUIUpdate?.Invoke(message);
    }
    
    private void HandleFailure(string errorMessage)
    {
        _currentState = MatchmakingState.Failed;
        UpdateStatus(errorMessage);
        OnMatchmakingFailed?.Invoke(errorMessage);
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
        _availableSessions.Clear();

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
            Debug.Log("[Matchmaking] Lobby scene detected - calling ResetState");
            ResetState();
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
            var gameMode = _currentGameMode.ToString();
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
                if (!session.IsGameStarted && 
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
