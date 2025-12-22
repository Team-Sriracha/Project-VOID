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
    [SerializeField] private string _gamePlaySceneName = "GamePlay";
    [SerializeField] private string _lobbySceneName = "Lobby";
    [SerializeField] private string _matchingSceneName = "Matchmaking";

    [Header("세션 검색 설정")]
    [SerializeField, Tooltip("세션 검색 재시도 간격 (초)")]
    private float _sessionSearchInterval = 2f;
    
    [SerializeField, Tooltip("세션 검색 타임아웃 (초). 이 시간 동안 세션을 찾지 못하면 로비로 돌아갑니다.")]
    private float _sessionSearchTimeout = 30f;

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

    private NetworkRunner _lobbyRunner;
    private MatchmakingState _currentState = MatchmakingState.Idle;
    private GameMode _currentGameMode;
    private int _targetPlayerCount;
    private string _currentRoomCode;
    private GameConnectionInfo? _pendingGameConnection;
    private List<SessionInfo> _availableSessions = new List<SessionInfo>();

    private Dictionary<string, string> _roomCodeToSessionMap = new Dictionary<string, string>();
    private Dictionary<string, string> _sessionToRoomCodeMap = new Dictionary<string, string>();

    private bool _isMonitoringGameState = false;
    private Coroutine _gameStateMonitorCoroutine;
    
    // Why: 세션 통합 모니터링 (대기 중 더 나은 세션으로 이동)
    private Coroutine _sessionConsolidationCoroutine;
    private bool _isConsolidating = false;

    private string _statusMessage = "Idle";
    private int _currentPlayersInLobby = 0;

    private bool _isCancelling = false; // 취소 중복 방지
    
    // Prepare된 매칭 정보 (Matching 씬에서 실제 접속 시 사용)
    private GameMode _preparedGameMode = GameMode.None;
    private int _preparedPlayerCount = 0;
    private string _preparedRoomCode = null;
    private bool _isPreparedToCreate = false; // true = 방 생성, false = 방 참가

    #endregion

    #region Events

    public event Action<string> OnMatchmakingUIUpdate;
    public event Action<string> OnCustomRoomCreated;
    public event Action<string> OnRoomJoined;
    public event Action<string> OnMatchmakingFailed;
    public event Action OnMatchmakingCancelled;
    public event Action<int, int> OnPlayerCountChanged;
    public event Action OnMatchmakingSuccess;
    public event Action OnAllPlayersReady; // Why: 서버에서 모든 플레이어 도착 알림 시 씬 전환
    public event Action MatchCompleted; // Why: 매칭 완료 시 "매칭완료" 텍스트 표시

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
        _preparedPlayerCount = 0; // 서버에서 결정
        _preparedRoomCode = RoomCodeGenerator.Normalize(roomCode);
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
                CreateCustomRoom(_preparedPlayerCount);
            }
            else if (!string.IsNullOrEmpty(_preparedRoomCode))
            {
                JoinCustomRoom(_preparedRoomCode);
            }
        }
        else if (_preparedGameMode == GameMode.PracticeRange)
        {
            EnterPracticeRange();
        }
        else
        {
            StartMatchmaking(_preparedGameMode);
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

    #region Public Methods - Matchmaking (기존)

    public async void StartMatchmaking(GameMode mode)
    {
        Debug.Log($"[Matchmaking] StartMatchmaking called - Mode: {mode}, Current State: {_currentState}");

        if (mode != GameMode.FourPlayer && mode != GameMode.EightPlayer)
        {
            Debug.LogError($"[Matchmaking] Invalid mode for matchmaking: {mode}");
            return;
        }

        // Why: 매칭 시작 전 공통 초기화 (이전 세션 정보 제거 및 상태 체크)
        if (!TryBeginMatchmaking())
        {
            return;
        }

        _currentGameMode = mode;
        _targetPlayerCount = mode == GameMode.FourPlayer ? 4 : 8;

        UpdateStatus("사용 가능한 서버 검색 중...");

        try
        {
            var availableSession = await FindAvailableGameSessionWithRetry(_targetPlayerCount, "게임 서버");
            if (availableSession == null)
            {
                HandleFailure($"사용 가능한 게임 세션이 없습니다 ({_sessionSearchTimeout}초 타임아웃)");
                return;
            }

            string serverId = availableSession.Properties.ContainsKey("ServerId") ? availableSession.Properties["ServerId"].PropertyValue.ToString() : "local-server";
            var serverInfo = _serverPoolConfig?.GetServerById(serverId);
            if (serverInfo == null)
            {
                HandleFailure($"서버 정보를 찾을 수 없습니다 (ServerId: {serverId})");
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

        // Why: 매칭 시작 전 공통 초기화
        if (!TryBeginMatchmaking())
        {
            return;
        }

        _currentGameMode = GameMode.Custom;
        _targetPlayerCount = playerCount;

        UpdateStatus("사용 가능한 서버 검색 중...");

        try
        {
            var availableSession = await FindAvailableGameSessionWithRetry(playerCount, "커스텀 방 서버");
            if (availableSession == null)
            {
                HandleFailure($"사용 가능한 게임 세션이 없습니다 ({_sessionSearchTimeout}초 타임아웃)");
                return;
            }

            string serverId = availableSession.Properties.ContainsKey("ServerId") ? availableSession.Properties["ServerId"].PropertyValue.ToString() : "local-server";
            var serverInfo = _serverPoolConfig?.GetServerById(serverId);
            if (serverInfo == null)
            {
                HandleFailure("서버 정보를 찾을 수 없습니다");
                return;
            }

            // Why: Session Properties에서 RoomCode 가져오기
            string roomCode = null;
            if (availableSession.Properties != null && availableSession.Properties.TryGetValue("RoomCode", out var roomCodeProp))
            {
                roomCode = roomCodeProp.PropertyValue?.ToString();
            }
            Debug.Log($"[Matchmaking] Session Properties RoomCode: '{roomCode}' (SessionName: '{availableSession.Name}')");

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
                Debug.LogWarning($"[Matchmaking] No RoomCode found in Session Properties for session: '{availableSession.Name}'");
            }

            await ConnectToGameServer(serverInfo.Value, availableSession.Name);

            if (_currentState == MatchmakingState.Completed)
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

        // Why: 매칭 시작 전 공통 초기화
        if (!TryBeginMatchmaking())
        {
            return;
        }

        _currentGameMode = GameMode.Custom;
        
        // Why: 세션 검색 재시도 로직 - 타임아웃 시간 동안 주기적으로 검색
        float elapsedTime = 0f;
        int attemptCount = 0;
        string sessionName = null;
        
        while (elapsedTime < _sessionSearchTimeout && !_isCancelling)
        {
            attemptCount++;
            // Why: UI에는 단순한 검색 중 메시지만 표시 (재시도 정보는 로그에만)
            Debug.Log($"[Matchmaking] Session search attempt {attemptCount} - RoomCode: {roomCode}, Elapsed: {elapsedTime:F1}s / {_sessionSearchTimeout}s");

            try
            {
                // Why: 캐시에 있으면 바로 사용, 없으면 세션 검색
                sessionName = _roomCodeToSessionMap.ContainsKey(roomCode)
                    ? _roomCodeToSessionMap[roomCode]
                    : await FindSessionNameByRoomCode(roomCode);

                if (sessionName != null)
                {
                    Debug.Log($"[Matchmaking] Session found: {sessionName} on attempt {attemptCount}");
                    break;
                }

                Debug.Log($"[Matchmaking] Session not found on attempt {attemptCount}, waiting {_sessionSearchInterval}s before retry...");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Matchmaking] Search attempt {attemptCount} failed: {ex.Message}");
            }

            // Why: 다음 재시도 전 대기
            var waitTcs = new TaskCompletionSource<bool>();
            StartCoroutine(WaitForSecondsCoroutine(_sessionSearchInterval, waitTcs));
            await waitTcs.Task;
            elapsedTime += _sessionSearchInterval;
        }

        // Why: 취소된 경우 처리
        if (_isCancelling)
        {
            Debug.Log("[Matchmaking] Session search cancelled by user.");
            return;
        }

        // Why: 타임아웃된 경우 로비로 돌아감
        if (sessionName == null)
        {
            Debug.LogWarning($"[Matchmaking] Session search timed out after {_sessionSearchTimeout}s ({attemptCount} attempts)");
            HandleFailure($"세션을 찾을 수 없습니다. ({_sessionSearchTimeout}초 타임아웃)");
            // Why: 타임아웃 시 로비로 돌아가기
            CancelAndReturnToLobby();
            return;
        }

        try
        {
            var targetSession = await FindSpecificGameSession(sessionName);
            if (targetSession == null)
            {
                HandleFailure("세션을 찾을 수 없거나 인원이 가득 찼습니다");
                return;
            }

            string serverId = targetSession.Properties.ContainsKey("ServerId") ? targetSession.Properties["ServerId"].PropertyValue.ToString() : "local-server";
            var serverInfo = _serverPoolConfig?.GetServerById(serverId);
            if (serverInfo == null)
            {
                HandleFailure("서버 정보를 찾을 수 없습니다");
                return;
            }

            _currentRoomCode = roomCode;
            await ConnectToGameServer(serverInfo.Value, targetSession.Name);

            if (_currentState == MatchmakingState.Completed)
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
        StopSessionConsolidation();

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
            
            // Why: 로딩 패널이 켜져 있을 수 있으므로 숨김
            if (LoadingUIManager.Instance != null)
            {
                LoadingUIManager.Instance.HideLoadingScreen();
            }
            
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
        if (_currentState == MatchmakingState.SelectingServer || _currentState == MatchmakingState.ConnectingToServer)
        {
            Debug.LogWarning($"[Matchmaking] Already in progress. Current state: {_currentState}");
            return;
        }

        _currentState = MatchmakingState.SelectingServer;
        _currentGameMode = GameMode.PracticeRange;
        _targetPlayerCount = 1;
        UpdateStatus("연습장 검색 중...");

        try
        {
            var availableSession = await FindAvailableGameSessionWithRetry(1, "연습장 서버");
            if (availableSession == null)
            {
                HandleFailure($"사용 가능한 연습장 세션이 없습니다 ({_sessionSearchTimeout}초 타임아웃)");
                return;
            }

            string serverId = availableSession.Properties.ContainsKey("ServerId") ? availableSession.Properties["ServerId"].PropertyValue.ToString() : "local-server";
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
            // Why: Session Properties에서 RoomCode 비교
            if (session.Properties != null && session.Properties.TryGetValue("RoomCode", out var roomCodeProp))
            {
                string sessionRoomCode = roomCodeProp.PropertyValue?.ToString();
                if (string.Equals(sessionRoomCode, roomCode, StringComparison.OrdinalIgnoreCase))
                {
                    _roomCodeToSessionMap[roomCode] = session.Name;
                    _sessionToRoomCodeMap[session.Name] = roomCode;
                    return session.Name;
                }
            }
        }
        return null;
    }

    private void UpdateStatus(string message)
    {
        _statusMessage = message;
        OnMatchmakingUIUpdate?.Invoke(message);
    }

    /// <summary>
    /// 세션 검색을 재시도 로직과 함께 수행합니다.
    /// 타임아웃 시간 동안 주기적으로 검색을 시도합니다.
    /// </summary>
    private async Task<SessionInfo> FindAvailableGameSessionWithRetry(int targetPlayers, string searchDescription = "세션")
    {
        float elapsedTime = 0f;
        int attemptCount = 0;
        SessionInfo foundSession = null;

        while (elapsedTime < _sessionSearchTimeout && !_isCancelling)
        {
            attemptCount++;
            // Why: UI에는 단순한 검색 중 메시지만 표시 (재시도 정보는 로그에만)
            Debug.Log($"[Matchmaking] Session search attempt {attemptCount} - TargetPlayers: {targetPlayers}, Elapsed: {elapsedTime:F1}s / {_sessionSearchTimeout}s");

            try
            {
                foundSession = await FindAvailableGameSession(targetPlayers);

                if (foundSession != null)
                {
                    Debug.Log($"[Matchmaking] Session found: {foundSession.Name} on attempt {attemptCount}");
                    return foundSession;
                }

                Debug.Log($"[Matchmaking] Session not found on attempt {attemptCount}, waiting {_sessionSearchInterval}s before retry...");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Matchmaking] Search attempt {attemptCount} failed: {ex.Message}");
            }

            // Why: 다음 재시도 전 대기
            var waitTcs = new TaskCompletionSource<bool>();
            StartCoroutine(WaitForSecondsCoroutine(_sessionSearchInterval, waitTcs));
            await waitTcs.Task;
            elapsedTime += _sessionSearchInterval;
        }

        // Why: 취소된 경우
        if (_isCancelling)
        {
            Debug.Log("[Matchmaking] Session search cancelled by user.");
            return null;
        }

        // Why: 타임아웃
        Debug.LogWarning($"[Matchmaking] Session search timed out after {_sessionSearchTimeout}s ({attemptCount} attempts)");
        return null;
    }
    
    private void HandleFailure(string errorMessage)
    {
        _currentState = MatchmakingState.Failed;
        OnMatchmakingFailed?.Invoke(errorMessage);
    }

    /// <summary>
    /// 매칭 시작 전 공통 초기화를 수행합니다.
    /// Why: 중복 코드 제거 - StartMatchmaking, CreateCustomRoom, JoinCustomRoom에서 동일한 패턴 사용
    /// </summary>
    /// <returns>초기화 성공 여부 (false면 매칭이 이미 진행 중이므로 중단)</returns>
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

        // Why: 남아있는 모든 세션 검색 Runner 정리 (LINQ 대신 for 루프로 할당 최소화)
        var allRunners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
        int destroyedCount = 0;
        foreach (var runner in allRunners)
        {
            if (runner != null && runner.gameObject != null && runner.gameObject.name.Contains("SessionSearchRunner"))
            {
                Debug.Log($"[Matchmaking] Destroying leftover search runner: {runner.gameObject.name}");
                Destroy(runner.gameObject);
                destroyedCount++;
            }
        }

        Debug.Log($"[Matchmaking] Destroyed {destroyedCount} search runners");

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

        _currentState = MatchmakingState.Idle;
        _currentGameMode = GameMode.None;
        _currentRoomCode = null;
        _pendingGameConnection = null;
        _currentPlayersInLobby = 0;
        _targetPlayerCount = 0;
        _availableSessions.Clear();
        
        // Why: 세션 통합 모니터링 정리
        StopSessionConsolidation();
        _isConsolidating = false;

        UpdateStatus("Idle");
        Debug.Log($"[Matchmaking] ResetState - Complete. New State: {_currentState}");
    }

    private async Task ConnectToGameServer(ServerInfo serverInfo, string sessionName)
    {
        // Why: 세션 접속 중 상태 (이 상태에서는 취소 버튼 비활성화)
        _currentState = MatchmakingState.ConnectingToServer;
        UpdateStatus("세션 접속 중...");

        if (_lobbyRunner != null)
        {
            if (_lobbyRunner.IsRunning) await _lobbyRunner.Shutdown();
            Destroy(_lobbyRunner.gameObject);
            _lobbyRunner = null;
        }

        // Why: 서버 접속 정보 저장
        _pendingGameConnection = new GameConnectionInfo
        {
            SessionName = sessionName,
            ServerInfo = serverInfo,
            GameMode = _currentGameMode,
            MaxPlayers = _targetPlayerCount
        };

        Debug.Log($"[Matchmaking] Lobby에서 세션 접속 시작: {sessionName}");

        // Why: Lobby에서 세션에 접속 (SceneManager=null로 씬 동기화 비활성화)
        await JoinServerFromLobby();
    }

    /// <summary>
    /// 세션 프로퍼티를 모니터링하여 목표 인원이 모이면 GamePlay 씬으로 전환합니다.
    /// </summary>
    private async void MonitorSessionUntilReady(string targetSessionName)
    {
        float refreshInterval = 1.0f; // 1초마다 세션 목록 갱신
        float maxWaitTime = 300f; // 최대 5분 대기
        float waitTime = 0f;

        // Why: SessionLobby에 접속하여 세션 목록 조회
        var monitorRunnerGo = new GameObject("SessionMonitorRunner");
        DontDestroyOnLoad(monitorRunnerGo);
        var monitorRunner = monitorRunnerGo.AddComponent<NetworkRunner>();
        monitorRunner.AddCallbacks(this);

        var lobbyResult = await monitorRunner.JoinSessionLobby(SessionLobby.ClientServer);
        if (!lobbyResult.Ok)
        {
            Debug.LogError($"[Matchmaking] SessionLobby 접속 실패: {lobbyResult.ShutdownReason}");
            Destroy(monitorRunnerGo);
            HandleFailure("세션 목록 조회 실패");
            return;
        }

        Debug.Log("[Matchmaking] SessionLobby 접속 성공. 세션 모니터링 시작...");
        _lobbyRunner = monitorRunner;

        while (waitTime < maxWaitTime && _currentState == MatchmakingState.WaitingForPlayers)
        {
            // Why: 취소된 경우 중단
            if (_isCancelling)
            {
                Debug.Log("[Matchmaking] 매칭 취소됨");
                break;
            }

            await Task.Delay((int)(refreshInterval * 1000));
            waitTime += refreshInterval;

            // Why: 세션 목록에서 대상 세션 찾기
            var targetSession = _availableSessions.FirstOrDefault(s => s.Name == targetSessionName);
            if (targetSession != null)
            {
                int currentPlayers = targetSession.PlayerCount;
                _currentPlayersInLobby = currentPlayers;
                OnPlayerCountChanged?.Invoke(currentPlayers, _targetPlayerCount);

                Debug.Log($"[Matchmaking] 세션 모니터링 - {targetSessionName}: {currentPlayers}/{_targetPlayerCount}");

                // Why: 목표 인원 도달!
                if (currentPlayers >= _targetPlayerCount)
                {
                    Debug.Log($"[Matchmaking] 목표 인원 도달! GamePlay 씬으로 전환...");
                    
                    // Why: 모니터링 Runner 정리
                    if (_lobbyRunner != null && _lobbyRunner.IsRunning)
                    {
                        await _lobbyRunner.Shutdown();
                    }
                    Destroy(monitorRunnerGo);
                    _lobbyRunner = null;

                    // Why: 상태 변경 후 씬 전환
                    _currentState = MatchmakingState.ConnectingToServer;
                    OnAllPlayersReady?.Invoke();
                    SceneManager.LoadScene(_gamePlaySceneName);
                    return;
                }
            }
            else
            {
                Debug.LogWarning($"[Matchmaking] 세션 '{targetSessionName}'을 찾을 수 없음");
            }
        }

        // Why: 타임아웃 또는 취소
        Debug.LogWarning("[Matchmaking] 세션 모니터링 종료");
        if (monitorRunner != null && monitorRunner.IsRunning) await monitorRunner.Shutdown();
        if (monitorRunnerGo != null) Destroy(monitorRunnerGo);
        _lobbyRunner = null;
        
        if (!_isCancelling && _currentState == MatchmakingState.WaitingForPlayers)
        {
            HandleFailure("매칭 시간 초과");
        }
    }

    /// <summary>
    /// Lobby 씬에서 서버에 접속합니다.
    /// </summary>
    private async Task JoinServerFromLobby()
    {
        if (!_pendingGameConnection.HasValue)
        {
            Debug.LogError("[Matchmaking] No pending game connection!");
            HandleFailure("서버 접속 정보가 없습니다");
            return;
        }

        var connectionInfo = _pendingGameConnection.Value;

        // Why: Lobby에서 사용할 Runner 생성
        var runnerGo = new GameObject($"LobbyGameRunner_{connectionInfo.SessionName}");
        DontDestroyOnLoad(runnerGo);
        var runner = runnerGo.AddComponent<NetworkRunner>();
        runner.AddCallbacks(this);

        var args = new StartGameArgs
        {
            GameMode = Fusion.GameMode.Client,
            SessionName = connectionInfo.SessionName,
            PlayerCount = connectionInfo.MaxPlayers,
            // Why: NetworkSceneManagerDefault를 사용하여 서버의 씬 전환을 자동으로 동기화받음
            SceneManager = runnerGo.AddComponent<NetworkSceneManagerDefault>(),
        };

        Debug.Log($"[Matchmaking] Joining session: {connectionInfo.SessionName}");

        var result = await runner.StartGame(args);

        if (result.Ok)
        {
            // Why: 클라이언트 측 NetworkManager 생성 및 초기화 (RPC 전송 담당)
            if (_networkManagerPrefab != null)
            {
                var netMgrGo = Instantiate(_networkManagerPrefab);
                netMgrGo.name = "NetworkManager_Client";
                var networkManager = netMgrGo.GetComponent<NetworkManager>();
                if (networkManager != null)
                {
                    Debug.Log("[Matchmaking] Instantiating Client NetworkManager...");
                    networkManager.SetExistingRunner(runner);
                }
            }
            else
            {
                Debug.LogError("[Matchmaking] NetworkManager Prefab is missing!");
            }

            _lobbyRunner = runner;
            _currentState = MatchmakingState.WaitingForPlayers;
            UpdateStatus("플레이어 대기 중...");
            OnMatchmakingSuccess?.Invoke();
            
            // Why: 세션 통합 모니터링 시작 (대기 중 더 나은 세션으로 이동)
            StartSessionConsolidation();
            
            Debug.Log("[Matchmaking] 서버 연결 성공! 다른 플레이어 대기 중...");
        }
        else
        {
            Debug.LogError($"[Matchmaking] Failed to join game: {result.ShutdownReason}");
            Destroy(runnerGo);
            HandleFailure($"서버 연결 실패: {result.ShutdownReason}");
        }
    }

    /// <summary>
    /// 서버에서 매칭 완료 알림이 왔을 때 호출됩니다.
    /// 1초간 "매칭완료" 텍스트를 표시합니다.
    /// </summary>
    public void HandleMatchComplete()
    {
        Debug.Log("[Matchmaking] 매칭 완료! '매칭완료' 텍스트 표시...");
        
        // Why: 상태 업데이트
        UpdateStatus("매칭완료!");
        
        // Why: 이벤트 발생 (UI가 "매칭완료" 표시)
        MatchCompleted?.Invoke();
    }

    /// <summary>
    /// 서버의 씬 전환 시 NetworkSceneManager로부터 호출됩니다.
    /// NetworkSceneManager가 자동으로 씬을 로드하므로 수동 LoadScene은 필요 없습니다.
    /// </summary>
    public void OnServerRequestedSceneTransition()
    {
        Debug.Log("[Matchmaking] 서버에서 씬 전환 요청! NetworkSceneManager가 자동으로 GamePlay 씬 로드...");
        
        // Why: 상태 업데이트 - 매칭 완료
        _currentState = MatchmakingState.ConnectingToServer;
        UpdateStatus("게임 로딩 중...");
        
        // Why: 로딩 패널 표시 (GamePlay 씬 전환 시점에 켜짐)
        if (LoadingUIManager.Instance != null)
        {
            LoadingUIManager.Instance.ShowLoadingAndWaitForMap();
        }
        
        // Why: 이벤트 발생 (LobbyUI가 패널을 숨김)
        OnAllPlayersReady?.Invoke();
        
        // Why: 수동 씬 로드 제거 - NetworkSceneManager가 서버의 Runner.LoadScene() 호출을 자동으로 동기화함
        // SceneManager.LoadScene(_gamePlaySceneName);  // 제거됨
    }

    /// <summary>
    /// (레거시) 서버에서 모든 플레이어가 도착했다고 알려줄 때 호출됩니다.
    /// </summary>
    public void NotifyAllPlayersReady()
    {
        // Why: OnServerRequestedSceneTransition으로 대체됨
        OnServerRequestedSceneTransition();
    }

    private System.Collections.IEnumerator UnloadLobbyAndLoadGamePlay()
    {
        // Why: GamePlay 씬이 이미 Fusion에 의해 로드되었는지 확인
        var gamePlayScene = SceneManager.GetSceneByName(_gamePlaySceneName);
        bool gamePlayAlreadyLoaded = gamePlayScene.isLoaded;

        if (gamePlayAlreadyLoaded)
        {
            Debug.Log("[Matchmaking] GamePlay 씬이 이미 로드됨 - 활성 씬으로 설정");
            SceneManager.SetActiveScene(gamePlayScene);
        }

        // Why: Lobby 씬 강제 언로드
        var lobbyScene = SceneManager.GetSceneByName(_lobbySceneName);
        if (lobbyScene.isLoaded)
        {
            Debug.Log($"[Matchmaking] Lobby 씬 언로드 중: {_lobbySceneName}");
            var unloadOp = SceneManager.UnloadSceneAsync(lobbyScene);
            if (unloadOp != null)
            {
                yield return unloadOp;
            }
            Debug.Log("[Matchmaking] Lobby 씬 언로드 완료");
        }

        // Why: GamePlay 씬이 이미 로드되지 않았다면 로드
        if (!gamePlayAlreadyLoaded)
        {
            Debug.Log($"[Matchmaking] GamePlay 씬 로드 중: {_gamePlaySceneName}");
            SceneManager.LoadScene(_gamePlaySceneName, LoadSceneMode.Single);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"[Matchmaking] OnSceneLoaded - Scene: {scene.name}, LobbySceneName: {_lobbySceneName}, GamePlaySceneName: {_gamePlaySceneName}");

        if (scene.name == _lobbySceneName)
        {
            Debug.Log("[Matchmaking] Lobby scene detected - calling ResetState");
            ResetState();
        }
        else if (scene.name == _gamePlaySceneName)
        {
            // Why: 이미 Completed 상태이면 중복 처리 방지
            if (_currentState == MatchmakingState.Completed)
            {
                Debug.Log("[Matchmaking] GamePlay scene already handled (Completed), skipping...");
                return;
            }

            Debug.Log($"[Matchmaking] Gameplay scene detected - State: {_currentState}, LobbyRunner: {_lobbyRunner != null}");

            // Why: Matchmaking 씬이 로드되어 있다면 강제 언로드 (Additive 문제 해결)
            // Why: Lobby에서 세션에 접속한 상태로 씬 전환됨 → Runner를 NetworkManager에 전달
            if (_lobbyRunner != null && _lobbyRunner.IsRunning && _pendingGameConnection.HasValue)
            {
                Debug.Log("[Matchmaking] LobbyRunner를 NetworkManager에 전달...");
                _ = TransferRunnerToGamePlay();
            }
            // Why: 기존 플로우 (새로 접속하는 경우)
            else if (_pendingGameConnection.HasValue && _currentState == MatchmakingState.ConnectingToServer)
            {
                Debug.Log("[Matchmaking] GamePlay 씬 로드 완료. 서버 접속 시작...");
                _ = JoinPendingGameServer();
            }
        }
    }

    /// <summary>
    /// Lobby 씬이 로드되어 있으면 언로드합니다.
    /// </summary>
    private System.Collections.IEnumerator UnloadLobbySceneIfLoaded()
    {
        var lobbyScene = SceneManager.GetSceneByName(_lobbySceneName);
        if (lobbyScene.isLoaded)
        {
            Debug.Log($"[Matchmaking] Lobby 씬 언로드 시작: {_lobbySceneName}");
            var op = SceneManager.UnloadSceneAsync(lobbyScene);
            if (op != null)
            {
                yield return op;
                Debug.Log("[Matchmaking] Lobby 씬 언로드 완료");
            }
        }
        else
        {
            Debug.Log("[Matchmaking] Lobby 씬이 이미 언로드됨");
        }
    }

    /// <summary>
    /// Lobby에서 연결된 Runner를 GamePlay 씬의 NetworkManager로 이전합니다.
    /// </summary>
    private async Task TransferRunnerToGamePlay()
    {
        var networkManager = FindFirstObjectByType<NetworkManager>();
        if (networkManager == null && _networkManagerPrefab != null)
        {
            networkManager = Instantiate(_networkManagerPrefab).GetComponent<NetworkManager>();
        }

        if (networkManager != null && _lobbyRunner != null)
        {
            // Why: NetworkManager에 이미 연결된 Runner 전달
            networkManager.SetExistingRunner(_lobbyRunner);
            _currentState = MatchmakingState.Completed;
            UpdateStatus("게임 로딩 중...");
            Debug.Log("[Matchmaking] Runner를 NetworkManager에 전달 완료");
            
            // Why: LobbyRunner 참조 해제 (NetworkManager가 관리)
            _lobbyRunner = null;
            _pendingGameConnection = null;
        }
        else
        {
            Debug.LogError("[Matchmaking] NetworkManager not found or Runner is null!");
            HandleFailure("NetworkManager를 찾을 수 없습니다");
        }
    }

    /// <summary>
    /// GamePlay 씬 로드 후 대기 중인 서버에 접속합니다.
    /// </summary>
    private async Task JoinPendingGameServer()
    {
        if (!_pendingGameConnection.HasValue)
        {
            Debug.LogError("[Matchmaking] No pending game connection!");
            HandleFailure("서버 접속 정보가 없습니다");
            return;
        }

        UpdateStatus("서버에 연결 중...");

        var networkManager = FindFirstObjectByType<NetworkManager>();
        if (networkManager == null && _networkManagerPrefab != null)
        {
            networkManager = Instantiate(_networkManagerPrefab).GetComponent<NetworkManager>();
        }

        if (networkManager != null)
        {
            bool success = await networkManager.JoinGameServer(_pendingGameConnection.Value);
            if (success)
            {
                _currentState = MatchmakingState.Completed;
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

            // Why: 0.5초로 단축 (기존 3초 → 0.5초)
            Debug.Log("[Matchmaking] Waiting 0.5 seconds for session list...");
            var tcs = new TaskCompletionSource<bool>();
            StartCoroutine(WaitForSecondsCoroutine(0.5f, tcs));
            await tcs.Task;

            Debug.Log($"[Matchmaking] Wait complete. Filtering {_availableSessions.Count} sessions...");

            // Why: 검색 결과 로그
            foreach (var s in _availableSessions)
            {
                bool isServer = s.Name.StartsWith("Server_");
                bool notFull = s.PlayerCount < s.MaxPlayers;
                bool isInGame = s.Properties.TryGetValue("IsInGame", out var prop) && (prop.IsInt ? (int)prop != 0 : (bool)prop);
                bool isCustom = s.Properties.TryGetValue("IsCustom", out var customProp) && (customProp.IsInt ? (int)customProp != 0 : (bool)customProp);
                int currentMatchingMode = s.Properties.TryGetValue("CurrentMatchingMode", out var cmm) ? (cmm.IsInt ? (int)cmm : 0) : 0;
                int matchingTargetPlayers = s.Properties.TryGetValue("MatchingTargetPlayers", out var mtp) ? (mtp.IsInt ? (int)mtp : 0) : 0;

                Debug.Log($"[Matchmaking] Session: {s.Name} | Server: {isServer} | NotFull: {notFull} ({s.PlayerCount}/{s.MaxPlayers}) | InGame: {isInGame} | IsCustom: {isCustom} | Mode: {currentMatchingMode} | Target: {matchingTargetPlayers}");
            }

            // Why: 1차 필터링 - 기본 조건 (서버, 미진행, 빈자리, 커스텀 제외)
            int desiredMode = (int)_currentGameMode;
            bool isCustomSearch = _currentGameMode == GameMode.Custom;
            
            var usableSessions = _availableSessions.Where(s =>
            {
                bool isServer = s.Name.StartsWith("Server_");
                bool notFull = s.PlayerCount < s.MaxPlayers;
                bool notInGame = !s.Properties.TryGetValue("IsInGame", out var inGameProp) || (inGameProp.IsInt ? (int)inGameProp == 0 : !(bool)inGameProp);
                
                // Why: Mode=0인 대기 세션인지 확인 (아직 모드가 결정되지 않은 세션)
                int sessionMode = s.Properties.TryGetValue("CurrentMatchingMode", out var modeProp) ? (modeProp.IsInt ? (int)modeProp : 0) : 0;
                int sessionTarget = s.Properties.TryGetValue("MatchingTargetPlayers", out var targetProp) ? (targetProp.IsInt ? (int)targetProp : 0) : 0;
                bool isWaitingSession = sessionMode == 0 && sessionTarget == 0;
                
                // Why: 커스텀 모드가 아닐 때는 IsCustom=true 세션 제외
                // Why: 단, 대기 세션(Mode=0)은 커스텀 필터 우회 (아직 모드 미결정)
                bool isCustomSession = s.Properties.TryGetValue("IsCustom", out var customProp) && (customProp.IsInt ? (int)customProp != 0 : (bool)customProp);
                bool customFilter = isWaitingSession || (isCustomSearch ? isCustomSession : !isCustomSession);
                
                bool maxPlayersMatches = isWaitingSession || targetPlayerCount <= 0 || s.MaxPlayers >= targetPlayerCount;

                return isServer && notFull && notInGame && customFilter && maxPlayersMatches;
            })
            // Why: 세션 이름 순서로 정렬 (핑퐁 방지 - 모든 클라이언트가 같은 순서로 선택)
            .OrderBy(s => s.Name)
            .ToList();

            // Why: 1순위 - 같은 모드 + 같은 타겟 인원 세션 (인원 많은 순)
            var sameModeSessions = usableSessions
                .Where(s =>
                {
                    int sessionMode = s.Properties.TryGetValue("CurrentMatchingMode", out var modeProp) ? (modeProp.IsInt ? (int)modeProp : 0) : 0;
                    int sessionTarget = s.Properties.TryGetValue("MatchingTargetPlayers", out var targetProp) ? (targetProp.IsInt ? (int)targetProp : 0) : 0;
                    
                    // Why: Mode가 설정되어 있고 같은 모드면 조인 가능
                    bool modeMatches = sessionMode != 0 && sessionMode == desiredMode;
                    bool targetMatches = sessionTarget == 0 || targetPlayerCount == 0 || sessionTarget == targetPlayerCount;

                    return modeMatches && targetMatches;
                })
                .OrderByDescending(s => s.PlayerCount) // 인원 많은 순
                .ThenBy(s => s.Name) // 동률이면 세션명 순
                .ToList();

            if (sameModeSessions.Count > 0)
            {
                var selected = sameModeSessions.First();
                Debug.Log($"[Matchmaking] Selected SAME-MODE session: {selected.Name} (Players {selected.PlayerCount}/{selected.MaxPlayers})");
                return selected;
            }

            // Why: 2순위 - 빈 대기 세션 (모드/타겟 미설정, 예약 안됨)
            // 인원이 많은 세션을 먼저 선택하여 경쟁 조건 완화
            var waitingSessions = usableSessions
                .Where(s =>
                {
                    int sessionMode = s.Properties.TryGetValue("CurrentMatchingMode", out var modeProp) ? (modeProp.IsInt ? (int)modeProp : 0) : 0;
                    int sessionTarget = s.Properties.TryGetValue("MatchingTargetPlayers", out var targetProp) ? (targetProp.IsInt ? (int)targetProp : 0) : 0;
                    
                    // Why: Mode가 아직 설정되지 않은 대기 세션
                    return sessionMode == 0 && sessionTarget == 0;
                })
                // Why: 인원이 많은 세션 우선 선택 (기존 세션에 합류 유도)
                // 동률이면 세션명 순서로 선택 (모든 클라이언트가 같은 세션 선택)
                .OrderByDescending(s => s.PlayerCount)
                .ThenBy(s => s.Name)
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

    /// <summary>
    /// 세션 통합 모니터링 시작 - 대기 중 더 나은 세션으로 이동
    /// </summary>
    private void StartSessionConsolidation()
    {
        if (_isConsolidating || _currentGameMode == GameMode.Custom) return;
        _isConsolidating = true;
        _sessionConsolidationCoroutine = StartCoroutine(SessionConsolidationCoroutine());
    }

    private void StopSessionConsolidation()
    {
        if (_sessionConsolidationCoroutine != null) StopCoroutine(_sessionConsolidationCoroutine);
        _isConsolidating = false;
    }

    /// <summary>
    /// 대기 중 같은 모드 + 더 많은 인원 세션 발견 시 이동
    /// </summary>
    private IEnumerator SessionConsolidationCoroutine()
    {
        float checkInterval = 2f; // 2초마다 체크 (세션 목록 갱신 비용 고려)
        string currentSessionName = _pendingGameConnection?.SessionName;
        
        Debug.Log($"[Matchmaking] SessionConsolidation started for session: {currentSessionName}");

        while (_isConsolidating && _currentState == MatchmakingState.WaitingForPlayers)
        {
            yield return new WaitForSeconds(checkInterval);
            
            if (_isCancelling || _currentState != MatchmakingState.WaitingForPlayers) break;

            // Why: 세션 목록 갱신 - _lobbyRunner가 게임 세션에 연결되어 있으면 세션 목록이 갱신되지 않음
            var refreshTask = RefreshSessionListForConsolidation();
            yield return new WaitUntil(() => refreshTask.IsCompleted);
            
            if (refreshTask.Exception != null || _availableSessions.Count == 0)
            {
                Debug.Log($"[Matchmaking] SessionConsolidation - Failed to refresh session list or no sessions found");
                continue;
            }

            // Why: 현재 세션 정보 확인
            var currentSession = _availableSessions.FirstOrDefault(s => s.Name == currentSessionName);
            if (currentSession == null) 
            {
                Debug.Log($"[Matchmaking] SessionConsolidation - Current session not found in list: {currentSessionName}");
                continue;
            }

            int currentPlayers = currentSession.PlayerCount;
            int desiredMode = (int)_currentGameMode;

            Debug.Log($"[Matchmaking] SessionConsolidation - Checking for better session (current: {currentSessionName}, players: {currentPlayers}, mode: {desiredMode})");

            // Why: 같은 모드 + 더 많은 인원 세션 검색
            var betterSession = _availableSessions
                .Where(s =>
                {
                    if (s.Name == currentSessionName) return false;
                    
                    bool notFull = s.PlayerCount < s.MaxPlayers;
                    bool notInGame = !s.Properties.TryGetValue("IsInGame", out var inGameProp) || 
                                     (inGameProp.IsInt ? (int)inGameProp == 0 : !(bool)inGameProp);
                    int sessionMode = s.Properties.TryGetValue("CurrentMatchingMode", out var modeProp) ? 
                                     (modeProp.IsInt ? (int)modeProp : 0) : 0;
                    int sessionTarget = s.Properties.TryGetValue("MatchingTargetPlayers", out var targetProp) ? 
                                       (targetProp.IsInt ? (int)targetProp : 0) : 0;
                    bool isCustom = s.Properties.TryGetValue("IsCustom", out var customProp) && 
                                   (customProp.IsInt ? (int)customProp != 0 : (bool)customProp);
                    
                    bool modeMatches = sessionMode == desiredMode;
                    bool targetMatches = sessionTarget == 0 || sessionTarget == _targetPlayerCount;
                    bool hasMorePlayers = s.PlayerCount > currentPlayers;
                    // Why: 동률이면 세션명 순서가 앞인 쪽 선택 (핑퐁 방지)
                    bool isBetterSession = hasMorePlayers || (s.PlayerCount == currentPlayers && string.Compare(s.Name, currentSessionName) < 0);
                    
                    return notFull && notInGame && !isCustom && modeMatches && targetMatches && isBetterSession;
                })
                .OrderByDescending(s => s.PlayerCount)
                .ThenBy(s => s.Name)
                .FirstOrDefault();

            if (betterSession != null)
            {
                Debug.Log($"[Matchmaking] Found better session: {betterSession.Name} ({betterSession.PlayerCount} players) vs current: {currentSessionName} ({currentPlayers} players)");
                
                // Why: 이동 전 목표 인원 도달 체크
                if (currentPlayers >= _targetPlayerCount)
                {
                    Debug.Log("[Matchmaking] Current session reached target, cancelling migration");
                    continue;
                }
                
                // Why: 세션 이동 실행
                yield return MigrateToSession(betterSession);
                break;
            }
        }

        _isConsolidating = false;
        Debug.Log("[Matchmaking] SessionConsolidation stopped");
    }

    /// <summary>
    /// 세션 통합용 세션 목록 갱신
    /// </summary>
    private async Task RefreshSessionListForConsolidation()
    {
        NetworkRunner searchRunner = null;
        GameObject searchRunnerGo = null;
        
        try
        {
            searchRunnerGo = new GameObject("ConsolidationSearchRunner");
            DontDestroyOnLoad(searchRunnerGo);
            searchRunner = searchRunnerGo.AddComponent<NetworkRunner>();
            searchRunner.AddCallbacks(this);

            var result = await searchRunner.JoinSessionLobby(SessionLobby.ClientServer);
            
            if (!result.Ok)
            {
                Debug.LogWarning($"[Matchmaking] ConsolidationSearch - Failed to join lobby: {result.ShutdownReason}");
                return;
            }

            // Why: 잠시 대기하여 세션 목록 수신
            await Task.Delay(500);
            
            Debug.Log($"[Matchmaking] ConsolidationSearch - Refreshed session list: {_availableSessions.Count} sessions");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Matchmaking] ConsolidationSearch error: {e.Message}");
        }
        finally
        {
            if (searchRunner != null && searchRunner.IsRunning) await searchRunner.Shutdown();
            if (searchRunnerGo != null) Destroy(searchRunnerGo);
        }
    }

    /// <summary>
    /// 다른 세션으로 이동
    /// </summary>
    private IEnumerator MigrateToSession(SessionInfo targetSession)
    {
        Debug.Log($"[Matchmaking] Migrating to session: {targetSession.Name}");
        UpdateStatus($"더 나은 세션으로 이동 중...");

        // Why: 현재 세션 나가기
        if (_lobbyRunner != null && _lobbyRunner.IsRunning)
        {
            var shutdownTask = _lobbyRunner.Shutdown();
            while (!shutdownTask.IsCompleted) yield return null;
            Destroy(_lobbyRunner.gameObject);
            _lobbyRunner = null;
        }

        // Why: 새 세션으로 접속
        string serverId = targetSession.Properties.ContainsKey("ServerId") ? 
                         targetSession.Properties["ServerId"].PropertyValue.ToString() : "local-server";
        var serverInfo = _serverPoolConfig?.GetServerById(serverId);
        
        if (serverInfo != null)
        {
            _pendingGameConnection = new GameConnectionInfo
            {
                SessionName = targetSession.Name,
                ServerInfo = serverInfo.Value,
                GameMode = _currentGameMode,
                MaxPlayers = _targetPlayerCount
            };
            
            var joinTask = JoinServerFromLobby();
            while (!joinTask.IsCompleted) yield return null;
        }
        else
        {
            Debug.LogError($"[Matchmaking] Server info not found for session: {targetSession.Name}");
        }
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

    // Lobby 매칭 콜백
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // Why: Lobby에서 매칭 대기 중일 때만 플레이어 수 체크
        if (_currentState != MatchmakingState.WaitingForPlayers) return;
        if (_lobbyRunner == null || runner != _lobbyRunner) return;

        int currentPlayerCount = runner.ActivePlayers.Count();
        _currentPlayersInLobby = currentPlayerCount;
        OnPlayerCountChanged?.Invoke(currentPlayerCount, _targetPlayerCount);
        
        Debug.Log($"[Matchmaking] OnPlayerJoined - 현재 인원: {currentPlayerCount}/{_targetPlayerCount}");
        
        // Why: 클라이언트에서는 씬 전환을 직접 트리거하지 않음
        // 서버의 GameStateManager가 목표 인원 도달을 감지하고 RPC_NotifySceneTransition을 
        // 모든 클라이언트에게 동시에 보내므로, 모든 클라이언트가 동시에 씬 전환됨
        if (currentPlayerCount >= _targetPlayerCount && _targetPlayerCount > 0)
        {
            Debug.Log($"[Matchmaking] 목표 인원 도달! 서버의 씬 전환 RPC 대기 중...");
            // NotifyAllPlayersReady(); // 제거 - 서버 RPC로 동기화
        }
    }
    
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // Why: Lobby에서 매칭 대기 중일 때만 플레이어 수 업데이트
        if (_currentState != MatchmakingState.WaitingForPlayers) return;
        if (_lobbyRunner == null || runner != _lobbyRunner) return;

        int currentPlayerCount = runner.ActivePlayers.Count();
        _currentPlayersInLobby = currentPlayerCount;
        OnPlayerCountChanged?.Invoke(currentPlayerCount, _targetPlayerCount);
        
        Debug.Log($"[Matchmaking] OnPlayerLeft - 현재 인원: {currentPlayerCount}/{_targetPlayerCount}");
    }
    
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
