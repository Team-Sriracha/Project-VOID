using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MatchmakingManager : MonoBehaviour, INetworkRunnerCallbacks
{
    #region Serialized Fields

    [Header("설정")]
    [SerializeField] private ServerPoolConfig _serverPoolConfig;

    [Header("프리팹")]
    [SerializeField] private GameObject _networkManagerPrefab;

    [Header("씬 이름")]
    [SerializeField] private string _gamePlaySceneName = "GameplayScene";
    [SerializeField] private string _lobbySceneName = "Lobby";

    #endregion
    
    #region Public Properties
    
    public static MatchmakingManager Instance { get; private set; }
    public EMatchmakingState CurrentState => _currentState;
    public EGameMode CurrentGameMode => _currentGameMode;
    public bool IsMatchmaking => _currentState != EMatchmakingState.Idle && _currentState != EMatchmakingState.Failed;
    public bool IsCustomGame => _currentGameMode == EGameMode.Custom;
    public string StatusMessage => _statusMessage;
    public int CurrentPlayers => _currentPlayersInLobby;
    public int MaxPlayers => _targetPlayerCount;
    public string RoomCode => _currentRoomCode;
    public GameConnectionInfo? PendingGameConnection => _pendingGameConnection;

    #endregion

    #region Private Fields

    private NetworkRunner _lobbyRunner;
    private EMatchmakingState _currentState = EMatchmakingState.Idle;
    private EGameMode _currentGameMode;
    private int _targetPlayerCount;
    private string _currentRoomCode;
    private GameConnectionInfo? _pendingGameConnection;
    private List<SessionInfo> _availableSessions = new List<SessionInfo>();

    private Dictionary<string, string> _roomCodeToSessionMap = new Dictionary<string, string>();
    private Dictionary<string, string> _sessionToRoomCodeMap = new Dictionary<string, string>();

    private bool _isMonitoringGameState = false;
    private Coroutine _gameStateMonitorCoroutine;

    private string _statusMessage = "Idle";
    private int _currentPlayersInLobby = 0;

    private bool _isCancelling = false; // 취소 중복 방지

    #endregion

    #region Events

    public event Action<string> OnMatchmakingUIUpdate;
    public event Action<string> OnCustomRoomCreated;
    public event Action<string> OnRoomJoined;
    public event Action<string> OnMatchmakingFailed;
    public event Action OnMatchmakingCancelled;
    public event Action<int, int> OnPlayerCountChanged;
    public event Action OnMatchmakingSuccess;

    #endregion
    
    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        transform.SetParent(null); // 부모 객체가 파괴되어도 살아남도록 분리
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
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

    #region Public Methods

    public async void StartMatchmaking(EGameMode mode)
    {
        Debug.Log($"[Matchmaking] StartMatchmaking called - Mode: {mode}, Current State: {_currentState}");

        if (mode != EGameMode.FourPlayer && mode != EGameMode.EightPlayer)
        {
            Debug.LogError($"[Matchmaking] Invalid mode for matchmaking: {mode}");
            return;
        }

        // Why: 매칭 시작 전 상태 초기화 (이전 세션 정보 제거)
        // 상태 체크보다 먼저 호출하여 Completed 상태에서도 재시작 가능하도록 함
        ResetState();

        // Why: 현재 매칭 진행 중이면 무시 (SelectingServer, ConnectingToServer 상태만 체크)
        if (_currentState == EMatchmakingState.SelectingServer || _currentState == EMatchmakingState.ConnectingToServer)
        {
            Debug.LogWarning($"[Matchmaking] Already in progress. Current state: {_currentState}");
            return;
        }

        _currentState = EMatchmakingState.SelectingServer;
        _currentGameMode = mode;
        _targetPlayerCount = mode == EGameMode.FourPlayer ? 4 : 8;

        UpdateStatus("사용 가능한 서버 검색 중...");

        try
        {
            var availableSession = await FindAvailableGameSession(_targetPlayerCount);
            if (availableSession == null)
            {
                HandleFailure("사용 가능한 게임 세션이 없습니다");
                return;
            }

            string serverId = availableSession.Properties.ContainsKey("ServerId") ? availableSession.Properties["ServerId"].PropertyValue.ToString() : "aws-server-1";
            var serverInfo = _serverPoolConfig?.GetServerById(serverId);
            if (serverInfo == null)
            {
                HandleFailure("서버 정보를 찾을 수 없습니다");
                return;
            }

            await ConnectToGameServer(serverInfo.Value, availableSession.Name);
        }
        catch (Exception ex)
        {
            HandleFailure($"오류: {ex.Message}");
            Debug.LogError($"[Matchmaking] Error: {ex}");
        }
    }

    public async void CreateCustomRoom(int playerCount)
    {
        Debug.Log($"[Matchmaking] CreateCustomRoom called - PlayerCount: {playerCount}, Current State: {_currentState}");

        if (playerCount < 1 || playerCount > 8)
        {
            Debug.LogError($"[Matchmaking] Invalid player count: {playerCount}");
            return;
        }

        // Why: 매칭 시작 전 상태 초기화 (이전 세션 정보 제거)
        ResetState();

        // Why: 현재 매칭 진행 중이면 무시
        if (_currentState == EMatchmakingState.SelectingServer || _currentState == EMatchmakingState.ConnectingToServer)
        {
            Debug.LogWarning($"[Matchmaking] Already in progress. Current state: {_currentState}");
            return;
        }

        _currentState = EMatchmakingState.SelectingServer;
        _currentGameMode = EGameMode.Custom;
        _targetPlayerCount = playerCount;

        UpdateStatus("사용 가능한 서버 검색 중...");

        try
        {
            var availableSession = await FindAvailableGameSession(playerCount);
            if (availableSession == null)
            {
                HandleFailure("사용 가능한 게임 세션이 없습니다");
                return;
            }

            string serverId = availableSession.Properties.ContainsKey("ServerId") ? availableSession.Properties["ServerId"].PropertyValue.ToString() : "aws-server-1";
            var serverInfo = _serverPoolConfig?.GetServerById(serverId);
            if (serverInfo == null)
            {
                HandleFailure("서버 정보를 찾을 수 없습니다");
                return;
            }

            string roomCode = ExtractRoomCodeFromSessionName(availableSession.Name);
            Debug.Log($"[Matchmaking] ExtractRoomCodeFromSessionName - SessionName: '{availableSession.Name}', RoomCode: '{roomCode}'");

            if (!string.IsNullOrEmpty(roomCode))
            {
                _currentRoomCode = roomCode;
                _roomCodeToSessionMap[roomCode] = availableSession.Name;
                _sessionToRoomCodeMap[availableSession.Name] = roomCode;
                Debug.Log($"[Matchmaking] Invoking OnCustomRoomCreated with roomCode: '{roomCode}'");
                OnCustomRoomCreated?.Invoke(roomCode);
            }
            else
            {
                Debug.LogWarning($"[Matchmaking] Failed to extract room code from session name: '{availableSession.Name}'");
            }

            await ConnectToGameServer(serverInfo.Value, availableSession.Name);

            if (_currentState == EMatchmakingState.Completed)
            {
                StartMonitoringGameState();
            }
        }
        catch (Exception ex)
        {
            HandleFailure($"오류: {ex.Message}");
            Debug.LogError($"[Matchmaking] Error: {ex}");
        }
    }

    public async void JoinCustomRoom(string roomCode)
    {
        Debug.Log($"[Matchmaking] JoinCustomRoom called - RoomCode: {roomCode}, Current State: {_currentState}");

        // Why: ABC-123 형식의 입력을 ABC123으로 정규화 (하이픈 제거, 대문자 변환)
        roomCode = RoomCodeGenerator.Normalize(roomCode);

        if (string.IsNullOrEmpty(roomCode) || !RoomCodeGenerator.IsValidCode(roomCode))
        {
            HandleFailure("잘못된 방 코드 형식입니다");
            return;
        }

        // Why: 매칭 시작 전 상태 초기화 (이전 세션 정보 제거)
        ResetState();

        // Why: 현재 매칭 진행 중이면 무시
        if (_currentState == EMatchmakingState.SelectingServer || _currentState == EMatchmakingState.ConnectingToServer)
        {
            Debug.LogWarning($"[Matchmaking] Already in progress. Current state: {_currentState}");
            return;
        }

        _currentState = EMatchmakingState.SelectingServer;
        _currentGameMode = EGameMode.Custom;
        UpdateStatus("세션 검색 중...");

        try
        {
            string sessionName = _roomCodeToSessionMap.ContainsKey(roomCode)
                ? _roomCodeToSessionMap[roomCode]
                : await FindSessionNameByRoomCode(roomCode);

            if (sessionName == null)
            {
                HandleFailure("방 코드를 찾을 수 없습니다");
                return;
            }

            var targetSession = await FindSpecificGameSession(sessionName);
            if (targetSession == null)
            {
                HandleFailure("세션을 찾을 수 없거나 인원이 가득 찼습니다");
                return;
            }

            string serverId = targetSession.Properties.ContainsKey("ServerId") ? targetSession.Properties["ServerId"].PropertyValue.ToString() : "aws-server-1";
            var serverInfo = _serverPoolConfig?.GetServerById(serverId);
            if (serverInfo == null)
            {
                HandleFailure("서버 정보를 찾을 수 없습니다");
                return;
            }

            _currentRoomCode = roomCode;
            await ConnectToGameServer(serverInfo.Value, targetSession.Name);

            if (_currentState == EMatchmakingState.Completed)
            {
                OnRoomJoined?.Invoke(roomCode);
            }
        }
        catch (Exception ex)
        {
            HandleFailure($"오류: {ex.Message}");
            Debug.LogError($"[Matchmaking] Error: {ex}");
        }
    }
    
    public void EnterPracticeRange()
    {
        StartPracticeMode();
    }

    /// <summary>
    /// 모든 게임 세션 목록을 반환합니다 (방 코드 중복 체크용)
    /// </summary>
    public async Task<List<SessionInfo>> GetAllGameSessions()
    {
        return await FindAllGameSessions();
    }

    public async void CancelMatchmaking()
    {
        // Why: 이미 취소 중이면 중복 호출 방지
        if (_isCancelling)
        {
            Debug.LogWarning("[Matchmaking] CancelMatchmaking already in progress, ignoring...");
            return;
        }

        _isCancelling = true;
        Debug.Log("[Matchmaking] CancelMatchmaking - Start");
        UpdateStatus("취소 중...");
        StopMonitoringGameState();

        try
        {
            // Why: 게임 서버 연결부터 끊기 (가장 중요)
            var networkManager = FindFirstObjectByType<NetworkManager>();
            if (networkManager != null && networkManager.Runner != null && networkManager.Runner.IsRunning)
            {
                Debug.Log($"[Matchmaking] Disconnecting from game server... (PlayerCount: {networkManager.Runner.ActivePlayers.Count()})");
                await networkManager.ReturnToLobby();
                Debug.Log("[Matchmaking] Game server disconnected");
            }

            // Why: 세션 검색 중인 모든 Runner 찾아서 종료
            var allSearchRunners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None)
                .Where(r => r != null && r.gameObject != null && r.gameObject.name.Contains("SessionSearchRunner"))
                .ToList();

            Debug.Log($"[Matchmaking] Found {allSearchRunners.Count} search runners to shutdown");
            foreach (var searchRunner in allSearchRunners)
            {
                Debug.Log($"[Matchmaking] Shutting down search runner: {searchRunner.gameObject.name}");
                if (searchRunner.IsRunning)
                {
                    await searchRunner.Shutdown();
                }
                Destroy(searchRunner.gameObject);
            }

            // Why: 로비 러너 종료 (세션 검색용)
            if (_lobbyRunner != null)
            {
                Debug.Log("[Matchmaking] Shutting down lobby runner...");
                if (_lobbyRunner.IsRunning)
                {
                    await _lobbyRunner.Shutdown();
                }
                Destroy(_lobbyRunner.gameObject);
                _lobbyRunner = null;
                Debug.Log("[Matchmaking] Lobby runner shutdown complete");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Matchmaking] Error during cancel shutdown: {ex.Message}\nStackTrace: {ex.StackTrace}");
        }
        finally
        {
            _isCancelling = false;
            ResetState();
            OnMatchmakingCancelled?.Invoke();
            Debug.Log("[Matchmaking] CancelMatchmaking - Complete");
        }
    }
    
    #endregion

    #region Private Methods

    private async void StartPracticeMode()
    {
        Debug.Log($"[Matchmaking] StartPracticeMode called - Current State: {_currentState}");

        // Why: 매칭 시작 전 상태 초기화 (이전 세션 정보 제거)
        ResetState();

        // Why: 현재 매칭 진행 중이면 무시
        if (_currentState == EMatchmakingState.SelectingServer || _currentState == EMatchmakingState.ConnectingToServer)
        {
            Debug.LogWarning($"[Matchmaking] Already in progress. Current state: {_currentState}");
            return;
        }

        _currentState = EMatchmakingState.SelectingServer;
        _currentGameMode = EGameMode.PracticeRange;
        _targetPlayerCount = 1;
        UpdateStatus("연습장 검색 중...");

        try
        {
            var availableSession = await FindAvailableGameSession(1);
            if (availableSession == null)
            {
                HandleFailure("사용 가능한 연습장 세션이 없습니다");
                return;
            }

            string serverId = availableSession.Properties.ContainsKey("ServerId") ? availableSession.Properties["ServerId"].PropertyValue.ToString() : "aws-server-1";
            var serverInfo = _serverPoolConfig?.GetServerById(serverId);
            if (serverInfo == null)
            {
                HandleFailure("서버 정보를 찾을 수 없습니다");
                return;
            }

            await ConnectToGameServer(serverInfo.Value, availableSession.Name);
        }
        catch (Exception ex)
        {
            HandleFailure($"오류: {ex.Message}");
            Debug.LogError($"[Matchmaking] Error: {ex}");
        }
    }

    private async Task<string> FindSessionNameByRoomCode(string roomCode)
    {
        var allSessions = await FindAllGameSessions();
        foreach (var session in allSessions)
        {
            if (session.Name.EndsWith($"_{roomCode}"))
            {
                _roomCodeToSessionMap[roomCode] = session.Name;
                _sessionToRoomCodeMap[session.Name] = roomCode;
                return session.Name;
            }
        }
        return null;
    }

    private void UpdateStatus(string message)
    {
        _statusMessage = message;
        OnMatchmakingUIUpdate?.Invoke(message);
    }
    
    private void HandleFailure(string errorMessage)
    {
        _currentState = EMatchmakingState.Failed;
        OnMatchmakingFailed?.Invoke(errorMessage);
    }

    private void ResetState()
    {
        Debug.Log($"[Matchmaking] ResetState - Start (Current State: {_currentState})");

        // Why: 남아있는 모든 세션 검색 Runner 정리
        var allSearchRunners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None)
            .Where(r => r != null && r.gameObject != null && r.gameObject.name.Contains("SessionSearchRunner"))
            .ToList();

        Debug.Log($"[Matchmaking] Found {allSearchRunners.Count} search runners to destroy");

        foreach (var searchRunner in allSearchRunners)
        {
            Debug.Log($"[Matchmaking] Destroying leftover search runner: {searchRunner.gameObject.name}");
            Destroy(searchRunner.gameObject);
        }

        if (_lobbyRunner != null)
        {
            Debug.Log("[Matchmaking] Destroying lobby runner in ResetState");
            Destroy(_lobbyRunner.gameObject);
            _lobbyRunner = null;
        }

        if (!string.IsNullOrEmpty(_currentRoomCode))
        {
            Debug.Log($"[Matchmaking] Releasing room code: {_currentRoomCode}");
            RoomCodeGenerator.ReleaseCode(_currentRoomCode);
            if (_roomCodeToSessionMap.ContainsKey(_currentRoomCode))
            {
                string sessionName = _roomCodeToSessionMap[_currentRoomCode];
                _sessionToRoomCodeMap.Remove(sessionName);
                _roomCodeToSessionMap.Remove(_currentRoomCode);
            }
        }

        _currentState = EMatchmakingState.Idle;
        _currentGameMode = EGameMode.None;
        _currentRoomCode = null;
        _pendingGameConnection = null;
        _currentPlayersInLobby = 0;
        _targetPlayerCount = 0;
        _availableSessions.Clear();

        UpdateStatus("Idle");
        Debug.Log($"[Matchmaking] ResetState - Complete. New State: {_currentState}");
    }

    private async Task ConnectToGameServer(ServerInfo serverInfo, string sessionName)
    {
        _currentState = EMatchmakingState.ConnectingToServer;
        UpdateStatus("서버에 연결 중...");

        if (_lobbyRunner != null)
        {
            if (_lobbyRunner.IsRunning) await _lobbyRunner.Shutdown();
            Destroy(_lobbyRunner.gameObject);
            _lobbyRunner = null;
        }

        _pendingGameConnection = new GameConnectionInfo
        {
            SessionName = sessionName,
            ServerInfo = serverInfo,
            GameMode = _currentGameMode,
            MaxPlayers = _targetPlayerCount
        };

        var networkManager = FindFirstObjectByType<NetworkManager>();
        if(networkManager == null && _networkManagerPrefab != null)
        {
            networkManager = Instantiate(_networkManagerPrefab).GetComponent<NetworkManager>();
        }

        if (networkManager != null)
        {
            bool success = await networkManager.JoinGameServer(_pendingGameConnection.Value);
            if (success)
            {
                _currentState = EMatchmakingState.Completed;
                Debug.Log("[Matchmaking] 서버 연결 성공!");
                UpdateStatus("플레이어 대기 중...");
                StartMonitoringGameState();
            }
            else
            {
                HandleFailure("게임 서버 연결에 실패했습니다");
            }
        }
        else
        {
            Debug.LogError("[Matchmaking] NetworkManager not found!");
            HandleFailure("NetworkManager를 찾을 수 없습니다");
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[Matchmaking] OnSceneLoaded - Scene: {scene.name}, LobbySceneName: {_lobbySceneName}");

        if (scene.name == _lobbySceneName)
        {
            Debug.Log("[Matchmaking] Lobby scene detected - calling ResetState");
            ResetState();
        }
    }

    private async Task<SessionInfo> FindAvailableGameSession(int targetPlayerCount = 0)
    {
        Debug.Log("[Matchmaking] FindAvailableGameSession - Start");

        if (_lobbyRunner != null)
        {
            if (_lobbyRunner.IsRunning) await _lobbyRunner.Shutdown();
            Destroy(_lobbyRunner.gameObject);
        }

        var searchRunnerGo = new GameObject("SessionSearchRunner");
        DontDestroyOnLoad(searchRunnerGo);
        var searchRunner = searchRunnerGo.AddComponent<NetworkRunner>();
        searchRunner.AddCallbacks(this);

        _availableSessions.Clear();

        try
        {
            Debug.Log("[Matchmaking] Attempting to join session lobby...");
            var result = await searchRunner.JoinSessionLobby(SessionLobby.ClientServer);
            Debug.Log($"[Matchmaking] JoinSessionLobby result: {result.Ok}, Reason: {result.ShutdownReason}");

            if (!result.Ok) throw new Exception($"Failed to join lobby: {result.ShutdownReason}");

            Debug.Log("[Matchmaking] Waiting 3 seconds for session list...");

            // Why: WebGL에서 Task.Delay가 작동하지 않으므로 코루틴과 TaskCompletionSource 사용
            var tcs = new TaskCompletionSource<bool>();
            StartCoroutine(WaitForSecondsCoroutine(3f, tcs));
            await tcs.Task;

            Debug.Log($"[Matchmaking] Wait complete. Filtering {_availableSessions.Count} sessions...");

            foreach (var s in _availableSessions)
            {
                bool isServer = s.Name.StartsWith("Server_");
                bool notFull = s.PlayerCount < s.MaxPlayers;
                bool isInGame = s.Properties.TryGetValue("IsInGame", out var prop) && (prop.IsInt ? (int)prop != 0 : (bool)prop);
                bool isReserved = s.Properties.TryGetValue("IsReserved", out var reservedProp) && (reservedProp.IsInt ? (int)reservedProp != 0 : (bool)reservedProp);
                int currentMatchingMode = s.Properties.TryGetValue("CurrentMatchingMode", out var cmm) ? (cmm.IsInt ? (int)cmm : 0) : 0;
                int matchingTargetPlayers = s.Properties.TryGetValue("MatchingTargetPlayers", out var mtp) ? (mtp.IsInt ? (int)mtp : 0) : 0;

                Debug.Log($"[Matchmaking] Session: {s.Name} | Server: {isServer} | NotFull: {notFull} ({s.PlayerCount}/{s.MaxPlayers}) | InGame: {isInGame} | Reserved: {isReserved} | CurrentMatchingMode: {currentMatchingMode} | MatchingTargetPlayers: {matchingTargetPlayers}");
            }

            // 1) 게임 중이 아니고, 매칭 중이며 동일 모드/타겟인 세션 우선 (커스텀은 동일 세션 이름)
            var usableSessions = _availableSessions.Where(s =>
            {
                bool isServer = s.Name.StartsWith("Server_");
                bool notFull = s.PlayerCount < s.MaxPlayers;
                bool notInGame = !s.Properties.TryGetValue("IsInGame", out var inGameProp) || (inGameProp.IsInt ? (int)inGameProp == 0 : !(bool)inGameProp);
                bool notReserved = !s.Properties.TryGetValue("IsReserved", out var reservedProp) || (reservedProp.IsInt ? (int)reservedProp == 0 : !(bool)reservedProp);
                bool maxPlayersMatches = targetPlayerCount <= 0 || s.MaxPlayers >= targetPlayerCount;
                return isServer && notFull && notInGame && notReserved && maxPlayersMatches;
            }).ToList();

            int desiredMode = (int)_currentGameMode;
            var sameModeSessions = usableSessions
                .Where(s =>
                {
                    int sessionMode = s.Properties.TryGetValue("CurrentMatchingMode", out var modeProp) ? (modeProp.IsInt ? (int)modeProp : 0) : 0;
                    int sessionTarget = s.Properties.TryGetValue("MatchingTargetPlayers", out var targetProp) ? (targetProp.IsInt ? (int)targetProp : 0) : 0;

                    bool modeMatches = sessionMode != 0 && sessionMode == desiredMode;
                    bool targetMatches = sessionTarget == 0 || targetPlayerCount == 0 || sessionTarget == targetPlayerCount;

                    // 커스텀 모드는 방 이름으로도 매칭 (동일 이름 우선)
                    if (_currentGameMode == EGameMode.Custom)
                    {
                        // Create/Start에서는 roomCode가 없을 수 있으므로 이름 매칭은 건너뜀
                        modeMatches = modeMatches || (!string.IsNullOrEmpty(_currentRoomCode) && s.Name.EndsWith($"_{_currentRoomCode}"));
                    }

                    return modeMatches && targetMatches;
                })
                .OrderByDescending(s => s.PlayerCount) // 이미 매칭 중인 큐에 합류하도록 인원 많은 순
                .ToList();

            if (sameModeSessions.Count > 0)
            {
                var selected = sameModeSessions.First();
                Debug.Log($"[Matchmaking] Selected SAME-MODE session: {selected.Name} (Players {selected.PlayerCount}/{selected.MaxPlayers})");
                return selected;
            }

            // 2) 동일 모드 매칭 중 세션이 없으면 빈 대기 세션 선택 (모드/타겟 미설정)
            var waitingSessions = usableSessions
                .Where(s =>
                {
                    int sessionMode = s.Properties.TryGetValue("CurrentMatchingMode", out var modeProp) ? (modeProp.IsInt ? (int)modeProp : 0) : 0;
                    int sessionTarget = s.Properties.TryGetValue("MatchingTargetPlayers", out var targetProp) ? (targetProp.IsInt ? (int)targetProp : 0) : 0;
                    return sessionMode == 0 && sessionTarget == 0;
                })
                .OrderBy(s => s.PlayerCount) // 새 매칭 대기열을 만들기 위해 가장 비어 있는 세션 선택
                .ToList();

            if (waitingSessions.Count > 0)
            {
                var selected = waitingSessions.First();
                Debug.Log($"[Matchmaking] Selected WAITING session: {selected.Name} (Players {selected.PlayerCount}/{selected.MaxPlayers})");
                return selected;
            }

            Debug.Log("[Matchmaking] No suitable sessions found for this mode.");
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Matchmaking] Error finding game session: {e.Message}\nStackTrace: {e.StackTrace}");
            return null;
        }
        finally
        {
            Debug.Log("[Matchmaking] Cleanup - shutting down search runner...");
            if (searchRunner != null && searchRunner.IsRunning) await searchRunner.Shutdown();
            if (searchRunnerGo != null) Destroy(searchRunnerGo);
            Debug.Log("[Matchmaking] FindAvailableGameSession - End");
        }
    }
    
    private async Task<List<SessionInfo>> FindAllGameSessions()
    {
        Debug.Log("[Matchmaking] FindAllGameSessions - Start");

        if (_lobbyRunner != null)
        {
            if (_lobbyRunner.IsRunning) await _lobbyRunner.Shutdown();
            Destroy(_lobbyRunner.gameObject);
        }

        var searchRunnerGo = new GameObject("SessionSearchRunner");
        DontDestroyOnLoad(searchRunnerGo);
        var searchRunner = searchRunnerGo.AddComponent<NetworkRunner>();
        searchRunner.AddCallbacks(this);

        _availableSessions.Clear();

        try
        {
            Debug.Log("[Matchmaking] Attempting to join session lobby (FindAll)...");
            var result = await searchRunner.JoinSessionLobby(SessionLobby.ClientServer);
            Debug.Log($"[Matchmaking] JoinSessionLobby result (FindAll): {result.Ok}");

            if (!result.Ok) throw new Exception($"Failed to join lobby: {result.ShutdownReason}");

            Debug.Log("[Matchmaking] Waiting 3 seconds for session list (FindAll)...");

            // Why: WebGL에서 Task.Delay가 작동하지 않으므로 코루틴과 TaskCompletionSource 사용
            var tcs = new TaskCompletionSource<bool>();
            StartCoroutine(WaitForSecondsCoroutine(3f, tcs));
            await tcs.Task;

            Debug.Log($"[Matchmaking] Found {_availableSessions.Count} sessions total");
            return new List<SessionInfo>(_availableSessions);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Matchmaking] Error finding all sessions: {e.Message}\nStackTrace: {e.StackTrace}");
            return new List<SessionInfo>();
        }
        finally
        {
            Debug.Log("[Matchmaking] Cleanup (FindAll) - shutting down search runner...");
            if (searchRunner != null && searchRunner.IsRunning) await searchRunner.Shutdown();
            if (searchRunnerGo != null) Destroy(searchRunnerGo);
            Debug.Log("[Matchmaking] FindAllGameSessions - End");
        }
    }

    private string ExtractRoomCodeFromSessionName(string sessionName)
    {
        if (string.IsNullOrEmpty(sessionName)) return null;
        int lastUnderscore = sessionName.LastIndexOf('_');
        if (lastUnderscore == -1 || lastUnderscore == sessionName.Length - 1) return null;
        
        string roomCode = sessionName.Substring(lastUnderscore + 1);
        return RoomCodeGenerator.IsValidCode(roomCode) ? roomCode : null;
    }
    
    private async Task<SessionInfo> FindSpecificGameSession(string targetSessionName)
    {
        var sessions = await FindAllGameSessions();
        return sessions.FirstOrDefault(s => s.Name == targetSessionName && s.PlayerCount < s.MaxPlayers);
    }
    
    private void StartMonitoringGameState()
    {
        if (_isMonitoringGameState) return;
        _isMonitoringGameState = true;
        _gameStateMonitorCoroutine = StartCoroutine(MonitorGameStateCoroutine());
    }

    private void StopMonitoringGameState()
    {
        if (_gameStateMonitorCoroutine != null) StopCoroutine(_gameStateMonitorCoroutine);
        _isMonitoringGameState = false;
    }

    private IEnumerator MonitorGameStateCoroutine()
    {
        GameStateManager gameStateManager = null;
        float maxWaitTime = 30f, waitTime = 0f, checkInterval = 0.5f;

        while (gameStateManager == null && waitTime < maxWaitTime)
        {
            gameStateManager = GameStateManager.Instance;
            if (gameStateManager == null || gameStateManager.Object == null || !gameStateManager.Object.IsValid)
            {
                gameStateManager = null;
                waitTime += checkInterval;
                yield return new WaitForSeconds(checkInterval);
            }
        }

        if (gameStateManager == null)
        {
            Debug.LogError("[Matchmaking] GameStateManager를 찾을 수 없습니다!");
            HandleFailure("GameStateManager를 찾을 수 없습니다.");
            _isMonitoringGameState = false;
            yield break;
        }

        while (_isMonitoringGameState)
        {
            if (gameStateManager == null || gameStateManager.Object == null || !gameStateManager.Object.IsValid)
            {
                Debug.LogWarning("[Matchmaking] GameStateManager가 유효하지 않음");
                break;
            }

            int currentPlayerCount = gameStateManager.ConnectedPlayers;
            int targetPlayerCount = gameStateManager.TargetPlayerCount;
            int aliveCount = gameStateManager.AlivePlayers;

            Debug.Log($"[Matchmaking] MonitorGameStateCoroutine - Connected: {currentPlayerCount}, Alive: {aliveCount}, Target: {targetPlayerCount}");

            if (currentPlayerCount != _currentPlayersInLobby || targetPlayerCount != _targetPlayerCount)
            {
                Debug.Log($"[Matchmaking] Player/Target count changed. Old: {_currentPlayersInLobby}/{_targetPlayerCount}, New (from GS): {currentPlayerCount}/{targetPlayerCount}");
                _currentPlayersInLobby = currentPlayerCount;
                _targetPlayerCount = targetPlayerCount;
                OnPlayerCountChanged?.Invoke(_currentPlayersInLobby, _targetPlayerCount);
                Debug.Log($"[Matchmaking] MatchmakingManager counts updated to: {_currentPlayersInLobby}/{_targetPlayerCount}");
            }

            if (gameStateManager.IsGameStarted)
            {
                Debug.Log("[Matchmaking] Game started! Invoking OnMatchmakingSuccess.");
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

    #region INetworkRunnerCallbacks
    
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) 
    {
        _availableSessions = sessionList;
        Debug.Log($"[Matchmaking] Session list updated: {sessionList.Count} sessions found");
    }

    // Unused callbacks
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    #endregion

    #region Helper Methods

    /// <summary>
    /// WebGL 호환 대기 코루틴 (Task.Delay 대체)
    /// </summary>
    private IEnumerator WaitForSecondsCoroutine(float seconds, TaskCompletionSource<bool> tcs)
    {
        yield return new WaitForSeconds(seconds);
        tcs.SetResult(true);
    }

    #endregion
}
