using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
#if UNITY_SERVER || ENABLE_UCS_SERVER
using Unity.Services.Authentication.Server;
#endif
using Unity.Services.Multiplayer;
using UnityEngine.Networking;

/// <summary>
/// Unity Multiplayer Services 매니저 (Sessions API - 매칭 전용)
/// 서버: 세션 생성 (IP/Port 저장)
/// 클라이언트: 세션 검색 → IP/Port 획득 → FishNet 직접 연결
/// Relay 사용 안 함
/// </summary>
public class UnityLobbyManager : MonoBehaviour
{
    public static UnityLobbyManager Instance { get; private set; }

    #region Constants

    private const string IDENTITY_VERIFICATION_READY_PROPERTY = "IdentityVerificationReady";
    private const string IDENTITY_VERIFICATION_STATUS_PROPERTY = "IdentityVerificationStatus";
    private const int MAX_SESSION_STATUS_LENGTH = 120;
    private const string UGS_SERVICE_ACCOUNT_KEY_ID_ENV_KEY = "PROJECTVOID_UGS_SERVICE_ACCOUNT_KEY_ID";
    private const string UGS_SERVICE_ACCOUNT_KEY_SECRET_ENV_KEY = "PROJECTVOID_UGS_SERVICE_ACCOUNT_KEY_SECRET";
    private const string UGS_SERVICE_ACCOUNT_KEY_ID_ARG_PREFIX = "--ugs-service-account-key-id=";
    private const string UGS_SERVICE_ACCOUNT_KEY_SECRET_ARG_PREFIX = "--ugs-service-account-key-secret=";

    #endregion

    #region Private Fields

    private bool _isInitialized = false;
    private Task<bool> _initializeTask;
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
    public Task<bool> Initialize()
    {
        if (_isInitialized)
        {
            return Task.FromResult(true);
        }

        if (_initializeTask != null && !_initializeTask.IsCompleted)
        {
            return _initializeTask;
        }

        _initializeTask = InitializeInternalAsync();
        return _initializeTask;
    }

    private async Task<bool> InitializeInternalAsync()
    {
#if UNITY_SERVER || ENABLE_UCS_SERVER
        if (ShouldUseServerRuntimePath())
        {
            return await InitializeServerInternalAsync();
        }
#endif

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
        finally
        {
            if (!_isInitialized)
            {
                _initializeTask = null;
            }
        }
    }

#if UNITY_SERVER || ENABLE_UCS_SERVER
    private async Task<bool> InitializeServerInternalAsync()
    {
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                Debug.Log("[UnityLobbyManager] 서버 전용 Unity Services 초기화");
                await UnityServices.InitializeAsync();
            }

            string authMode = await AuthenticateServerAsync();

            _isInitialized = true;
            Debug.Log($"[UnityLobbyManager] 서버 인증 완료 - Mode: {authMode}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[UnityLobbyManager] 서버 초기화 실패: {ex.Message}");
            OnError?.Invoke($"서버 초기화 실패: {ex.Message}");
            return false;
        }
        finally
        {
            if (!_isInitialized)
            {
                _initializeTask = null;
            }
        }
    }
#endif

    #endregion

    #region Public Methods - Server (Session Creation)

    /// <summary>
    /// 서버용: 세션 생성 (IP/Port 저장, Relay 없음)
    /// </summary>
    public async Task<SessionInfo> CreateSession(string sessionName, int maxPlayers, 
        string serverIp, int serverPort, string gameMode,
        bool isIdentityVerificationReady = true,
        string identityVerificationStatus = "")
    {
        if (!_isInitialized)
        {
            await Initialize();
        }

        try
        {
            Debug.Log($"[UnityLobbyManager] 세션 생성 - Name: {sessionName}, Server: {serverIp}:{serverPort}");

            SessionOptions sessionOptions = await BuildServerSessionOptionsAsync(
                sessionName,
                maxPlayers,
                serverIp,
                serverPort,
                gameMode,
                isIdentityVerificationReady,
                identityVerificationStatus);

#if UNITY_SERVER || ENABLE_UCS_SERVER
            if (ShouldUseServerRuntimePath())
            {
                if (MultiplayerServerService.Instance == null)
                {
                    throw new InvalidOperationException("MultiplayerServerService가 초기화되지 않았습니다.");
                }

                _currentSession = await MultiplayerServerService.Instance.CreateSessionAsync(sessionOptions);
            }
            else
#endif
            {
                _currentSession = await MultiplayerService.Instance.CreateSessionAsync(sessionOptions);
            }

            _isHost = true;

            // [Auto] 생성된 세션의 Code(Join Code)를 RoomCode 속성에 저장하여 모니터에서 조회 가능하게 함
            if (_currentSession is IHostSession hostSession)
            {
                Debug.Log($"[UnityLobbyManager] Saving Session Code to Properties: {_currentSession.Code}");
                hostSession.SetProperty("RoomCode", new SessionProperty(_currentSession.Code, VisibilityPropertyOptions.Public));
                await hostSession.SavePropertiesAsync();
            }

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

    /// <summary>
    /// 서버용: 인증 백엔드 준비 상태를 세션 메타데이터에 반영합니다.
    /// </summary>
    public async Task<bool> UpdateSessionIdentityVerificationStatusAsync(bool isReady, string statusMessage)
    {
        if (_currentSession == null || !_isHost)
        {
            Debug.LogWarning("[UnityLobbyManager] UpdateSessionIdentityVerificationStatusAsync 실패: 세션 없음 또는 호스트 아님");
            return false;
        }

        try
        {
            if (_currentSession is IHostSession hostSession)
            {
                string normalizedStatus = NormalizeSessionStatus(statusMessage);
                hostSession.SetProperty(
                    IDENTITY_VERIFICATION_READY_PROPERTY,
                    new SessionProperty(isReady ? "true" : "false", VisibilityPropertyOptions.Public));
                hostSession.SetProperty(
                    IDENTITY_VERIFICATION_STATUS_PROPERTY,
                    new SessionProperty(normalizedStatus, VisibilityPropertyOptions.Public));
                await hostSession.SavePropertiesAsync();

                Debug.Log($"[UnityLobbyManager] 세션 인증 상태 업데이트: Ready={isReady}, Status={normalizedStatus}");
                return true;
            }

            return false;
        }
        catch (SessionException ex)
        {
            Debug.LogError($"[UnityLobbyManager] 세션 인증 상태 업데이트 실패: {ex.Message}");
            return false;
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
                if (sessionInfo.Properties.TryGetValue("GameState", out var gameStateVal))
                    info.GameState = gameStateVal.Value;
                if (sessionInfo.Properties.TryGetValue("RoomCode", out var roomCodeVal))
                    info.RoomCode = roomCodeVal.Value;
                if (sessionInfo.Properties.TryGetValue("IsGameStarted", out var started))
                    info.IsGameStarted = started.Value == "true";
                if (sessionInfo.Properties.TryGetValue("IsGameEnded", out var ended))
                    info.IsGameEnded = ended.Value == "true";
                if (sessionInfo.Properties.TryGetValue("TargetPlayers", out var targetPlayersVal) &&
                    int.TryParse(targetPlayersVal.Value, out int targetPlayers) &&
                    targetPlayers > 0)
                {
                    info.TargetPlayers = targetPlayers;
                    info.MaxPlayers = targetPlayers;
                }
                if (sessionInfo.Properties.TryGetValue("CustomGameTime", out var customGameTimeVal) &&
                    int.TryParse(customGameTimeVal.Value, out int customGameTime))
                {
                    info.CustomGameTimeSeconds = Mathf.Max(0, customGameTime);
                }
                if (sessionInfo.Properties.TryGetValue("CustomMapTemplate", out var customMapTemplateVal))
                {
                    info.CustomMapTemplate = customMapTemplateVal.Value;
                }
                if (sessionInfo.Properties.TryGetValue(IDENTITY_VERIFICATION_READY_PROPERTY, out var identityReadyVal))
                {
                    info.HasIdentityVerificationReady = true;
                    info.IsIdentityVerificationReady = string.Equals(identityReadyVal.Value, "true", StringComparison.OrdinalIgnoreCase);
                }
                if (sessionInfo.Properties.TryGetValue(IDENTITY_VERIFICATION_STATUS_PROPERTY, out var identityStatusVal))
                {
                    info.IdentityVerificationStatus = identityStatusVal.Value ?? string.Empty;
                }

                Debug.Log($"[UnityLobbyManager] 세션 속성: GameMode={info.GameMode}, GameState={info.GameState}, IsGameStarted={info.IsGameStarted}, IsGameEnded={info.IsGameEnded}, RoomCode={info.RoomCode}, AuthReady={(info.HasIdentityVerificationReady ? info.IsIdentityVerificationReady.ToString() : "Unknown")}, AuthStatus={info.IdentityVerificationStatus}");

                // 필터 - GameMode가 빈 값("") 또는 "None"인 세션은 대기 상태이므로 통과
                // 클라이언트가 연결 후 GameMode를 설정함
                bool isWaitingSession = string.IsNullOrWhiteSpace(info.GameMode) ||
                                        string.Equals(info.GameMode, "None", StringComparison.OrdinalIgnoreCase);
                bool hasSearchMode = !string.IsNullOrWhiteSpace(gameMode);
                bool isSameMode = GameModeCatalog.AreSameModeKey(info.GameMode, gameMode);
                if (hasSearchMode && !isWaitingSession && !isSameMode)
                {
                    Debug.Log($"[UnityLobbyManager] 세션 필터링됨: GameMode 불일치 (검색: {gameMode}, 세션: {info.GameMode})");
                    continue;
                }
                if (info.IsGameStarted)
                {
                    Debug.Log($"[UnityLobbyManager] 세션 필터링됨: 이미 게임 시작됨");
                    continue;
                }
                if (info.HasIdentityVerificationReady && !info.IsIdentityVerificationReady)
                {
                    Debug.Log($"[UnityLobbyManager] 세션 필터링됨: 인증 백엔드 준비 안 됨 ({info.IdentityVerificationStatus})");
                    continue;
                }
                if (!IsJoinableSession(info))
                {
                    Debug.Log($"[UnityLobbyManager] 세션 필터링됨: 진행 중 또는 종료된 세션 (GameState={info.GameState}, Started={info.IsGameStarted}, Ended={info.IsGameEnded})");
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
    public async Task<bool> UpdateSessionGameModeAsync(
        string gameMode,
        int maxPlayers,
        int customGameTimeSeconds = 0,
        string customMapTemplateName = "")
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
                Debug.Log($"[UnityLobbyManager] 세션 GameMode 업데이트: {gameMode}, MaxPlayers: {maxPlayers}, CustomTime: {customGameTimeSeconds}, CustomMap: {customMapTemplateName}");
                
                hostSession.SetProperty("GameMode", new SessionProperty(gameMode, VisibilityPropertyOptions.Public));
                // MaxPlayers는 세션 생성 시 설정되므로 별도 변경 불가 (Lobby API 제한)
                // 대신 TargetPlayers 속성을 추가로 설정
                hostSession.SetProperty("TargetPlayers", new SessionProperty(maxPlayers.ToString(), VisibilityPropertyOptions.Public));
                hostSession.SetProperty("CustomGameTime", new SessionProperty(Mathf.Max(0, customGameTimeSeconds).ToString(), VisibilityPropertyOptions.Public));
                hostSession.SetProperty("CustomMapTemplate", new SessionProperty(customMapTemplateName ?? string.Empty, VisibilityPropertyOptions.Public));
                
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

    private async Task<SessionOptions> BuildServerSessionOptionsAsync(
        string sessionName,
        int maxPlayers,
        string serverIp,
        int serverPort,
        string gameMode,
        bool isIdentityVerificationReady,
        string identityVerificationStatus)
    {
        return new SessionOptions
        {
            MaxPlayers = maxPlayers,
            Name = sessionName,
            SessionProperties = new Dictionary<string, SessionProperty>
            {
                { "ServerIp", new SessionProperty(serverIp, VisibilityPropertyOptions.Public) },
                { "ServerPort", new SessionProperty(serverPort.ToString(), VisibilityPropertyOptions.Public) },
                { "GameMode", new SessionProperty(gameMode, VisibilityPropertyOptions.Public) },
                { "TargetPlayers", new SessionProperty(maxPlayers.ToString(), VisibilityPropertyOptions.Public) },
                { "CustomGameTime", new SessionProperty("0", VisibilityPropertyOptions.Public) },
                { "CustomMapTemplate", new SessionProperty(string.Empty, VisibilityPropertyOptions.Public) },
                { "IsGameStarted", new SessionProperty("false", VisibilityPropertyOptions.Public) },
                { IDENTITY_VERIFICATION_READY_PROPERTY, new SessionProperty(isIdentityVerificationReady ? "true" : "false", VisibilityPropertyOptions.Public) },
                { IDENTITY_VERIFICATION_STATUS_PROPERTY, new SessionProperty(NormalizeSessionStatus(identityVerificationStatus), VisibilityPropertyOptions.Public) },
                { "ServerRegion", new SessionProperty(await GetRegionFromIp() ?? "Unknown", VisibilityPropertyOptions.Public) },
                { "ServerHostname", new SessionProperty(System.Net.Dns.GetHostName(), VisibilityPropertyOptions.Public) }
            }
        };
    }

#if UNITY_SERVER || ENABLE_UCS_SERVER
    private static bool ShouldUseServerRuntimePath()
    {
        return Application.isBatchMode;
    }

    private static async Task<string> AuthenticateServerAsync()
    {
        string apiKeyIdentifier = ResolveArgumentOrEnvironment(
            UGS_SERVICE_ACCOUNT_KEY_ID_ARG_PREFIX,
            UGS_SERVICE_ACCOUNT_KEY_ID_ENV_KEY);
        string apiKeySecret = ResolveArgumentOrEnvironment(
            UGS_SERVICE_ACCOUNT_KEY_SECRET_ARG_PREFIX,
            UGS_SERVICE_ACCOUNT_KEY_SECRET_ENV_KEY);

        if (!string.IsNullOrWhiteSpace(apiKeyIdentifier) || !string.IsNullOrWhiteSpace(apiKeySecret))
        {
            if (string.IsNullOrWhiteSpace(apiKeyIdentifier) || string.IsNullOrWhiteSpace(apiKeySecret))
            {
                throw new InvalidOperationException(
                    "UGS 서비스 계정 설정이 불완전합니다. KEY_ID와 KEY_SECRET를 모두 제공해야 합니다.");
            }

            await ServerAuthenticationService.Instance.SignInWithServiceAccountAsync(apiKeyIdentifier, apiKeySecret);
            return "service-account";
        }

        await ServerAuthenticationService.Instance.SignInFromServerAsync();
        return "server-proxy";
    }
#endif

    private static string ResolveArgumentOrEnvironment(string argumentPrefix, string environmentKey)
    {
        string valueFromEnvironment = Environment.GetEnvironmentVariable(environmentKey)?.Trim();
        if (!string.IsNullOrWhiteSpace(valueFromEnvironment))
        {
            return valueFromEnvironment;
        }

        string[] args = Environment.GetCommandLineArgs();
        foreach (string arg in args)
        {
            if (arg.StartsWith(argumentPrefix, StringComparison.Ordinal))
            {
                return arg.Substring(argumentPrefix.Length).Trim();
            }
        }

        return string.Empty;
    }

    private async Task<string> GetRegionFromIp()
    {
        try
        {
            // Use ipapi.co (HTTPS supported) to avoid Unity cleartext blocking
            string url = "https://ipapi.co/json/";
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                // Set User-Agent as polite behavior
                request.SetRequestHeader("User-Agent", "UnityGameServer/1.0");
                
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string json = request.downloadHandler.text;
                    var data = JsonUtility.FromJson<IpApiResponse>(json);
                    
                    if (!string.IsNullOrEmpty(data.country_code) && !string.IsNullOrEmpty(data.region))
                        return $"{data.country_code}-{data.region}";
                    if (!string.IsNullOrEmpty(data.country_name))
                         return data.country_name;
                }
                else
                {
                     Debug.LogWarning($"[UnityLobbyManager] GeoIP Request Failed: {request.error}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UnityLobbyManager] GeoIP Exception: {ex.Message}");
        }
        return "Unknown";
    }

    [Serializable]
    private struct IpApiResponse
    {
        public string country_name;
        public string country_code;
        public string region;
    }

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
        if (session.Properties.TryGetValue("GameState", out var gameState))
            info.GameState = gameState.Value;
        if (session.Properties.TryGetValue("IsGameStarted", out var started))
            info.IsGameStarted = started.Value == "true";
        if (session.Properties.TryGetValue("IsGameEnded", out var ended))
            info.IsGameEnded = ended.Value == "true";
        if (session.Properties.TryGetValue("TargetPlayers", out var targetPlayers) &&
            int.TryParse(targetPlayers.Value, out int targetPlayerValue) &&
            targetPlayerValue > 0)
        {
            info.TargetPlayers = targetPlayerValue;
            info.MaxPlayers = targetPlayerValue;
        }
        if (session.Properties.TryGetValue("CustomGameTime", out var customGameTime) &&
            int.TryParse(customGameTime.Value, out int customGameTimeValue))
        {
            info.CustomGameTimeSeconds = Mathf.Max(0, customGameTimeValue);
        }
        if (session.Properties.TryGetValue("CustomMapTemplate", out var customMapTemplate))
        {
            info.CustomMapTemplate = customMapTemplate.Value;
        }
        if (session.Properties.TryGetValue(IDENTITY_VERIFICATION_READY_PROPERTY, out var identityReady))
        {
            info.HasIdentityVerificationReady = true;
            info.IsIdentityVerificationReady = string.Equals(identityReady.Value, "true", StringComparison.OrdinalIgnoreCase);
        }
        if (session.Properties.TryGetValue(IDENTITY_VERIFICATION_STATUS_PROPERTY, out var identityStatus))
        {
            info.IdentityVerificationStatus = identityStatus.Value ?? string.Empty;
        }

        // RoomCode는 항상 Unity Join Code 사용
        info.RoomCode = session.Code;

        return info;
    }

    private static bool IsJoinableSession(SessionInfo info)
    {
        if (info == null)
        {
            return false;
        }

        if (info.IsGameStarted || info.IsGameEnded)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(info.GameState) ||
               string.Equals(info.GameState, GameState.WaitingForPlayers.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSessionStatus(string statusMessage)
    {
        if (string.IsNullOrWhiteSpace(statusMessage))
        {
            return string.Empty;
        }

        string trimmed = statusMessage.Trim();
        if (trimmed.Length <= MAX_SESSION_STATUS_LENGTH)
        {
            return trimmed;
        }

        return trimmed.Substring(0, MAX_SESSION_STATUS_LENGTH);
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
    public int TargetPlayers;
    public int CurrentPlayers;
    public string ServerIp;       // 서버 공인 IP
    public int ServerPort;        // 서버 포트
    public string RoomCode;       // 커스텀 방 코드
    public string GameMode;
    public string GameState;
    public int CustomGameTimeSeconds;
    public string CustomMapTemplate;
    public bool HasIdentityVerificationReady;
    public bool IsIdentityVerificationReady = true;
    public string IdentityVerificationStatus;
    public bool IsGameStarted;
    public bool IsGameEnded;
    public bool IsHost;
}
