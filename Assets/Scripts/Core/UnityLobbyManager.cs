using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;

/// <summary>
/// Unity Multiplayer Services 매니저 (Sessions API - 매칭 전용)
/// 서버: 세션 생성 (IP/Port 저장)
/// 클라이언트: 세션 검색 → IP/Port 획득 → FishNet 직접 연결
/// Relay 사용 안 함
/// </summary>
public class UnityLobbyManager : MonoBehaviour
{
    public static UnityLobbyManager Instance { get; private set; }

    #region Private Fields

    private bool _isInitialized = false;
    private ISession _currentSession;
    private bool _isHost = false;

    #endregion

    #region Events

    public event Action<List<SessionInfo>> OnSessionsUpdated;
    public event Action<SessionInfo> OnJoinedSession;
    public event Action OnLeftSession;
    public event Action<string> OnError;

    #endregion

    #region Properties

    public bool IsInitialized => _isInitialized;
    public bool IsInSession => _currentSession != null;
    public bool IsHost => _isHost;
    public ISession CurrentSession => _currentSession;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        
        // DontDestroyOnLoad는 루트 GameObject에서만 작동하므로, 부모가 있으면 루트로 변경
        if (transform.parent != null)
        {
            transform.SetParent(null);
        }
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            _ = LeaveSession();
            Instance = null;
        }
    }

    #endregion

    #region Public Methods - Initialization

    /// <summary>
    /// Unity Gaming Services 초기화
    /// </summary>
    public async Task<bool> Initialize()
    {
        if (_isInitialized) return true;

        try
        {
            // 각 에디터 인스턴스가 고유한 Profile을 사용하도록 설정
            // 이렇게 해야 같은 PC에서 서버/클라이언트 테스트 시 다른 Player로 인식됨
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // 고유한 프로필 이름 생성 (프로세스 ID + 타임스탬프)
                string profileName = $"Player_{System.Diagnostics.Process.GetCurrentProcess().Id}_{System.DateTime.Now.Ticks % 10000}";
                var initOptions = new InitializationOptions();
                initOptions.SetProfile(profileName);
                
                Debug.Log($"[UnityLobbyManager] Unity Services 초기화 - Profile: {profileName}");
                await UnityServices.InitializeAsync(initOptions);
            }

            // 이미 로그인 중이면 완료될 때까지 대기
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                // 로그인 진행 중이면 완료 대기
                int maxWaitMs = 10000;
                int waited = 0;
                while (!AuthenticationService.Instance.IsSignedIn && waited < maxWaitMs)
                {
                    await Task.Delay(100);
                    waited += 100;
                }

                // 로그인 안 됐으면 시도
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
            }

            _isInitialized = true;
            Debug.Log($"[UnityLobbyManager] 초기화 완료 - PlayerId: {AuthenticationService.Instance.PlayerId}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UnityLobbyManager] 초기화 실패: {ex.Message}");
            OnError?.Invoke($"초기화 실패: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Public Methods - Server (Session Creation)

    /// <summary>
    /// 서버용: 세션 생성 (IP/Port 저장, Relay 없음)
    /// </summary>
    public async Task<SessionInfo> CreateSession(string sessionName, int maxPlayers, 
        string serverIp, int serverPort, string gameMode, string roomCode = null)
    {
        if (!_isInitialized)
        {
            await Initialize();
        }

        try
        {
            Debug.Log($"[UnityLobbyManager] 세션 생성 - Name: {sessionName}, Server: {serverIp}:{serverPort}");

            var sessionOptions = new SessionOptions
            {
                MaxPlayers = maxPlayers,
                Name = sessionName,
                SessionProperties = new Dictionary<string, SessionProperty>
                {
                    { "ServerIp", new SessionProperty(serverIp, VisibilityPropertyOptions.Public) },
                    { "ServerPort", new SessionProperty(serverPort.ToString(), VisibilityPropertyOptions.Public) },
                    { "GameMode", new SessionProperty(gameMode, VisibilityPropertyOptions.Public) },
                    { "RoomCode", new SessionProperty(roomCode ?? "", VisibilityPropertyOptions.Public) },
                    { "IsGameStarted", new SessionProperty("false", VisibilityPropertyOptions.Public) }
                }
            }; // Relay 없음 - 직접 연결

            _currentSession = await MultiplayerService.Instance.CreateSessionAsync(sessionOptions);
            _isHost = true;

            Debug.Log($"[UnityLobbyManager] 세션 생성 완료 - Id: {_currentSession.Id}, Code: {_currentSession.Code}");

            var sessionInfo = ConvertToSessionInfo(_currentSession);
            OnJoinedSession?.Invoke(sessionInfo);
            return sessionInfo;
        }
        catch (SessionException ex)
        {
            Debug.LogError($"[UnityLobbyManager] 세션 생성 실패: {ex.Message}");
            OnError?.Invoke($"세션 생성 실패: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Public Methods - Client (Session Discovery)

    /// <summary>
    /// 클라이언트용: 세션 검색
    /// </summary>
    public async Task<List<SessionInfo>> QuerySessions(string gameMode = null, int maxResults = 25)
    {
        if (!_isInitialized)
        {
            await Initialize();
        }

        try
        {
            var queryOptions = new QuerySessionsOptions();
            var results = await MultiplayerService.Instance.QuerySessionsAsync(queryOptions);
            
            // API 응답 상세 로그
            Debug.Log($"[UnityLobbyManager] API 응답 - 총 {results.Sessions.Count}개 세션 반환됨");
            
            var sessions = new List<SessionInfo>();
            int count = 0;

            foreach (var sessionInfo in results.Sessions)
            {
                Debug.Log($"[UnityLobbyManager] 세션 발견: Id={sessionInfo.Id}, Name={sessionInfo.Name}, MaxPlayers={sessionInfo.MaxPlayers}, AvailableSlots={sessionInfo.AvailableSlots}");
                
                if (count >= maxResults) break;

                int currentPlayers = sessionInfo.MaxPlayers - sessionInfo.AvailableSlots;
                
                var info = new SessionInfo
                {
                    SessionId = sessionInfo.Id,
                    SessionName = sessionInfo.Name,
                    MaxPlayers = sessionInfo.MaxPlayers,
                    CurrentPlayers = currentPlayers
                };

                // 커스텀 속성 읽기
                if (sessionInfo.Properties.TryGetValue("ServerIp", out var serverIp))
                    info.ServerIp = serverIp.Value;
                if (sessionInfo.Properties.TryGetValue("ServerPort", out var serverPort))
                    int.TryParse(serverPort.Value, out info.ServerPort);
                if (sessionInfo.Properties.TryGetValue("GameMode", out var gameModeVal))
                    info.GameMode = gameModeVal.Value;
                if (sessionInfo.Properties.TryGetValue("RoomCode", out var roomCodeVal))
                    info.RoomCode = roomCodeVal.Value;
                if (sessionInfo.Properties.TryGetValue("IsGameStarted", out var started))
                    info.IsGameStarted = started.Value == "true";

                Debug.Log($"[UnityLobbyManager] 세션 속성: GameMode={info.GameMode}, IsGameStarted={info.IsGameStarted}, RoomCode={info.RoomCode}");

                // 필터 - GameMode가 빈 값("") 또는 "None"인 세션은 대기 상태이므로 통과
                // 클라이언트가 연결 후 GameMode를 설정함
                bool isWaitingSession = string.IsNullOrEmpty(info.GameMode) || info.GameMode == "None";
                if (!string.IsNullOrEmpty(gameMode) && !isWaitingSession && info.GameMode != gameMode)
                {
                    Debug.Log($"[UnityLobbyManager] 세션 필터링됨: GameMode 불일치 (검색: {gameMode}, 세션: {info.GameMode})");
                    continue;
                }
                if (info.IsGameStarted)
                {
                    Debug.Log($"[UnityLobbyManager] 세션 필터링됨: 이미 게임 시작됨");
                    continue;
                }

                sessions.Add(info);
                count++;
            }

            Debug.Log($"[UnityLobbyManager] {sessions.Count}개 세션 발견");
            OnSessionsUpdated?.Invoke(sessions);
            return sessions;
        }
        catch (SessionException ex)
        {
            Debug.LogError($"[UnityLobbyManager] 세션 검색 실패: {ex.Message}");
            OnError?.Invoke($"세션 검색 실패: {ex.Message}");
            return new List<SessionInfo>();
        }
    }

    /// <summary>
    /// 클라이언트용: 세션 코드로 참가 (IP/Port 획득)
    /// </summary>
    public async Task<SessionInfo> JoinSessionByCode(string sessionCode)
    {
        if (!_isInitialized)
        {
            await Initialize();
        }

        try
        {
            Debug.Log($"[UnityLobbyManager] 세션 코드로 참가: {sessionCode}");

            _currentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(sessionCode);
            _isHost = false;

            Debug.Log($"[UnityLobbyManager] 세션 참가 완료 - Id: {_currentSession.Id}");

            var sessionInfo = ConvertToSessionInfo(_currentSession);
            OnJoinedSession?.Invoke(sessionInfo);
            return sessionInfo;
        }
        catch (SessionException ex)
        {
            Debug.LogError($"[UnityLobbyManager] 세션 참가 실패: {ex.Message}");
            OnError?.Invoke($"세션 참가 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 클라이언트용: 세션 ID로 참가
    /// </summary>
    public async Task<SessionInfo> JoinSessionById(string sessionId)
    {
        if (!_isInitialized)
        {
            await Initialize();
        }

        try
        {
            Debug.Log($"[UnityLobbyManager] 세션 ID로 참가: {sessionId}");

            _currentSession = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId);
            _isHost = false;

            var sessionInfo = ConvertToSessionInfo(_currentSession);
            OnJoinedSession?.Invoke(sessionInfo);
            return sessionInfo;
        }
        catch (SessionException ex)
        {
            Debug.LogError($"[UnityLobbyManager] 세션 참가 실패: {ex.Message}");
            OnError?.Invoke($"세션 참가 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 클라이언트용: 빠른 매칭
    /// </summary>
    public async Task<SessionInfo> QuickMatch(string gameMode, int maxPlayers)
    {
        if (!_isInitialized)
        {
            await Initialize();
        }

        try
        {
            Debug.Log($"[UnityLobbyManager] 빠른 매칭 - GameMode: {gameMode}");

            // 세션 검색 후 첫 번째 참가
            var sessions = await QuerySessions(gameMode, 10);
            if (sessions.Count > 0)
            {
                var session = sessions[0];
                return await JoinSessionById(session.SessionId);
            }

            Debug.Log("[UnityLobbyManager] 매칭할 세션 없음");
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UnityLobbyManager] 빠른 매칭 실패: {ex.Message}");
            OnError?.Invoke($"빠른 매칭 실패: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Public Methods - Session Management

    /// <summary>
    /// 세션 나가기
    /// </summary>
    public async Task LeaveSession()
    {
        if (_currentSession == null) return;

        try
        {
            await _currentSession.LeaveAsync();
            Debug.Log("[UnityLobbyManager] 세션 나가기 완료");
        }
        catch (SessionException ex)
        {
            Debug.LogWarning($"[UnityLobbyManager] 세션 나가기 실패: {ex.Message}");
        }
        finally
        {
            _currentSession = null;
            _isHost = false;
            OnLeftSession?.Invoke();
        }
    }

    /// <summary>
    /// 게임 시작 표시 - 호스트 전용
    /// </summary>
    public async Task MarkGameStarted()
    {
        if (_currentSession == null || !_isHost) return;

        try
        {
            if (_currentSession is IHostSession hostSession)
            {
                hostSession.SetProperty("IsGameStarted", new SessionProperty("true", VisibilityPropertyOptions.Public));
                await hostSession.SavePropertiesAsync();
                Debug.Log("[UnityLobbyManager] 게임 시작 표시 완료");
            }
        }
        catch (SessionException ex)
        {
            Debug.LogError($"[UnityLobbyManager] 속성 업데이트 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 세션 상태 업데이트 - 호스트 전용
    /// GameStateManager에서 주기적으로 호출
    /// </summary>
    public async Task UpdateSessionStatus(string gameState, int currentPhase, int alivePlayers, 
        int totalPlayers, float elapsedTime, bool isGameStarted, bool isGameEnded)
    {
        if (_currentSession == null || !_isHost) return;

        try
        {
            if (_currentSession is IHostSession hostSession)
            {
                hostSession.SetProperty("GameState", new SessionProperty(gameState, VisibilityPropertyOptions.Public));
                hostSession.SetProperty("CurrentPhase", new SessionProperty(currentPhase.ToString(), VisibilityPropertyOptions.Public));
                hostSession.SetProperty("AlivePlayers", new SessionProperty(alivePlayers.ToString(), VisibilityPropertyOptions.Public));
                hostSession.SetProperty("TotalPlayers", new SessionProperty(totalPlayers.ToString(), VisibilityPropertyOptions.Public));
                hostSession.SetProperty("ElapsedTime", new SessionProperty(((int)elapsedTime).ToString(), VisibilityPropertyOptions.Public));
                hostSession.SetProperty("IsGameStarted", new SessionProperty(isGameStarted ? "true" : "false", VisibilityPropertyOptions.Public));
                hostSession.SetProperty("IsGameEnded", new SessionProperty(isGameEnded ? "true" : "false", VisibilityPropertyOptions.Public));
                await hostSession.SavePropertiesAsync();
            }
        }
        catch (SessionException ex)
        {
            Debug.LogWarning($"[UnityLobbyManager] 상태 업데이트 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// 세션의 GameMode와 MaxPlayers 업데이트 - 호스트 전용
    /// 첫 번째 클라이언트가 연결할 때 호출됨
    /// </summary>
    public async Task<bool> UpdateSessionGameModeAsync(string gameMode, int maxPlayers)
    {
        if (_currentSession == null || !_isHost)
        {
            Debug.LogWarning("[UnityLobbyManager] UpdateSessionGameModeAsync 실패: 세션 없음 또는 호스트 아님");
            return false;
        }

        try
        {
            if (_currentSession is IHostSession hostSession)
            {
                Debug.Log($"[UnityLobbyManager] 세션 GameMode 업데이트: {gameMode}, MaxPlayers: {maxPlayers}");
                
                hostSession.SetProperty("GameMode", new SessionProperty(gameMode, VisibilityPropertyOptions.Public));
                // MaxPlayers는 세션 생성 시 설정되므로 별도 변경 불가 (Lobby API 제한)
                // 대신 TargetPlayers 속성을 추가로 설정
                hostSession.SetProperty("TargetPlayers", new SessionProperty(maxPlayers.ToString(), VisibilityPropertyOptions.Public));
                
                await hostSession.SavePropertiesAsync();
                
                Debug.Log($"[UnityLobbyManager] 세션 GameMode 업데이트 완료");
                return true;
            }
            return false;
        }
        catch (SessionException ex)
        {
            Debug.LogError($"[UnityLobbyManager] GameMode 업데이트 실패: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Private Methods

    private SessionInfo ConvertToSessionInfo(ISession session)
    {
        int currentPlayers = session.MaxPlayers - session.AvailableSlots;
        
        var info = new SessionInfo
        {
            SessionId = session.Id,
            SessionName = session.Name,
            MaxPlayers = session.MaxPlayers,
            CurrentPlayers = currentPlayers,
            SessionCode = session.Code,
            IsHost = session.IsHost
        };

        if (session.Properties.TryGetValue("ServerIp", out var serverIp))
            info.ServerIp = serverIp.Value;
        if (session.Properties.TryGetValue("ServerPort", out var serverPort))
            int.TryParse(serverPort.Value, out info.ServerPort);
        if (session.Properties.TryGetValue("GameMode", out var gameMode))
            info.GameMode = gameMode.Value;
        if (session.Properties.TryGetValue("RoomCode", out var roomCode))
            info.RoomCode = roomCode.Value;
        if (session.Properties.TryGetValue("IsGameStarted", out var started))
            info.IsGameStarted = started.Value == "true";

        // RoomCode가 비어있으면 SessionCode 사용 (Unity가 자동 생성한 코드)
        if (string.IsNullOrEmpty(info.RoomCode))
        {
            info.RoomCode = session.Code;
        }

        return info;
    }

    #endregion
}

/// <summary>
/// 세션 정보 (Relay 없음 - IP/Port 직접 연결)
/// </summary>
[Serializable]
public class SessionInfo
{
    public string SessionId;
    public string SessionName;
    public string SessionCode;    // Unity Sessions Code (방 입장용)
    public int MaxPlayers;
    public int CurrentPlayers;
    public string ServerIp;       // 서버 공인 IP
    public int ServerPort;        // 서버 포트
    public string RoomCode;       // 커스텀 방 코드
    public string GameMode;
    public bool IsGameStarted;
    public bool IsHost;
}
