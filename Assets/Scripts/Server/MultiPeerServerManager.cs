using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

/// <summary>
/// 여러 NetworkRunner 세션을 관리하는 서버용 매니저입니다.
/// 자동으로 대기 세션을 유지하여 클라이언트가 항상 입장 가능하도록 합니다.
/// </summary>
public class MultiPeerServerManager : MonoBehaviour
{
    #region Serialized Fields

    [SerializeField] private GameSessionController _sessionControllerPrefab;
    [SerializeField] private SessionPoolManager _sessionPool;
    [SerializeField] private ServerBuildConfig _serverBuildConfig;

    [Header("자동 세션 관리")]
    [Tooltip("서버 ID (ServerPoolConfig의 ServerId와 일치해야 함)")]
    [SerializeField] private string _serverId = "local-server";

    [Tooltip("서버 최대 동시 접속 인원 (ServerPoolConfig와 일치해야 함)")]
    [SerializeField] private int _maxTotalPlayers = 40;

    [Tooltip("각 게임 모드별 최소 대기 세션 수")]
    [SerializeField] private int _minWaitingSessionsPerMode = 1;

    [Tooltip("세션 상태 체크 주기 (초)")]
    [SerializeField] private float _sessionCheckInterval = 1f; // 1초마다 즉시 반응

    #endregion

    #region Private Fields

    private readonly Dictionary<string, GameSessionController> _activeSessions = new Dictionary<string, GameSessionController>();
    private int _sessionCounter = 0; // 세션 이름 생성용 카운터

    #endregion

    #region Public Properties (Monitoring)

    /// <summary>
    /// 서버 최대 동시 접속 인원
    /// </summary>
    public int MaxTotalPlayers => _maxTotalPlayers;

    /// <summary>
    /// 현재 활성 세션 수
    /// </summary>
    public int ActiveSessionCount => _activeSessions.Count;

    /// <summary>
    /// 서버 ID
    /// </summary>
    public string ServerId => _serverId;

    #endregion

    #region Public Methods (Monitoring)

    /// <summary>
    /// 현재 모든 세션의 상태 목록을 반환합니다 (HTTP 모니터링용).
    /// </summary>
    public List<SessionStatus> GetSessionStatusList()
    {
        var result = new List<SessionStatus>();

        foreach (var kvp in _activeSessions)
        {
            var controller = kvp.Value;
            if (controller == null || controller.Runner == null) continue;

            var runner = controller.Runner;
            var sessionInfo = runner.SessionInfo;

            // 기본값
            string roomCode = "";
            int maxPlayers = 8;
            bool isInGame = false;
            string gameMode = "대기 중";
            string phase = "대기";

            // Session Properties에서 기본 정보 추출
            if (sessionInfo.Properties != null)
            {
                if (sessionInfo.Properties.TryGetValue("RoomCode", out var roomCodeProp))
                    roomCode = roomCodeProp.PropertyValue?.ToString() ?? "";
                if (sessionInfo.Properties.TryGetValue("MaxPlayers", out var maxPlayersProp))
                    maxPlayers = maxPlayersProp.IsInt ? (int)maxPlayersProp.PropertyValue : 8;
                if (sessionInfo.Properties.TryGetValue("IsInGame", out var isInGameProp))
                    isInGame = isInGameProp.IsInt ? (int)isInGameProp.PropertyValue != 0 : (bool)isInGameProp.PropertyValue;
            }

            // GameStateManager에서 직접 게임 모드와 Phase 읽기 (가장 정확함)
            try
            {
                var gameStateManagers = runner.GetAllBehaviours<GameStateManager>();
                if (gameStateManagers != null)
                {
                    foreach (var gsm in gameStateManagers)
                    {
                        if (gsm != null && gsm.Object != null && gsm.Object.IsValid)
                        {
                            int targetPlayers = gsm.TargetPlayerCount;
                            gameMode = targetPlayers switch
                            {
                                1 => "연습장",
                                4 => "4인 모드",
                                8 => "8인 모드",
                                _ when targetPlayers > 0 => $"커스텀({targetPlayers}인)",
                                _ => "대기 중"
                            };
                            
                            isInGame = gsm.IsGameStarted;
                            phase = gsm.IsGameStarted ? $"Phase {gsm.CurrentPhase}" : "대기";
                            break;
                        }
                    }
                }
            }
            catch { }

            // NetworkObject 수 계산
            int objectCount = 0;
            try
            {
                objectCount = runner.GetAllBehaviours<Fusion.NetworkBehaviour>().Count();
            }
            catch { }

            result.Add(new SessionStatus
            {
                Name = kvp.Key,
                PlayerCount = runner.ActivePlayers.Count(),
                MaxPlayers = maxPlayers,
                Status = isInGame ? "InGame" : "Waiting",
                RoomCode = roomCode,
                GameMode = gameMode,
                Phase = phase,
                Duration = runner.SimulationTime,
                ObjectCount = objectCount
            });
        }

        return result;
    }

    #endregion

    #region Unity Lifecycle

    private async void Start()
    {
        Debug.Log("[MultiPeerServerManager] 서버 시작 - 초기 대기 세션 생성 중...");
        // Why: 서버 시작 시 하나의 빈 대기 세션만 생성
        await CreateWaitingSession();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 새 세션을 생성하고 시작합니다.
    /// </summary>
    public async Task<bool> StartSession(string sessionName, StartGameArgs args)
    {
        if (string.IsNullOrEmpty(sessionName))
        {
            Debug.LogWarning("[MultiPeerServerManager] sessionName is null or empty.");
            return false;
        }

        if (_activeSessions.ContainsKey(sessionName))
        {
            Debug.LogWarning($"[MultiPeerServerManager] Session '{sessionName}' already exists.");
            return false;
        }

        GameSessionController controller = AcquireController();
        if (controller == null)
        {
            Debug.LogError("[MultiPeerServerManager] Failed to acquire session controller.");
            return false;
        }

        // Why: 서버 매니저 참조를 전달하여 플레이어 입장/퇴장 이벤트 수신
        bool ok = await controller.StartSession(args, this);
        if (ok)
        {
            _activeSessions[sessionName] = controller;
            Debug.Log($"[MultiPeerServerManager] Session '{sessionName}' started.");
        }
        else
        {
            ReleaseController(controller);
        }

        return ok;
    }

    /// <summary>
    /// 세션을 종료하고 풀에 반환합니다.
    /// </summary>
    public async Task StopSession(string sessionName)
    {
        if (!_activeSessions.TryGetValue(sessionName, out var controller))
        {
            return;
        }

        await controller.StopSession();
        ReleaseController(controller);
        _activeSessions.Remove(sessionName);
        Debug.Log($"[MultiPeerServerManager] Session '{sessionName}' stopped.");
    }

    /// <summary>
    /// 모든 세션을 종료합니다.
    /// </summary>
    public async Task StopAllSessions()
    {
        var keys = new List<string>(_activeSessions.Keys);
        foreach (var session in keys)
        {
            await StopSession(session);
        }
    }

    #endregion

    #region Helper Methods

    private GameSessionController AcquireController()
    {
        if (_sessionPool != null)
        {
            var controller = _sessionPool.Get();
            if (controller != null) return controller;
        }

        if (_sessionControllerPrefab == null)
        {
            Debug.LogWarning("[MultiPeerServerManager] SessionController prefab is not assigned.");
            return null;
        }

        var instance = Instantiate(_sessionControllerPrefab);
        if (instance == null)
        {
            Debug.LogError("[MultiPeerServerManager] Failed to instantiate SessionController prefab.");
        }
        return instance;
    }

    private void ReleaseController(GameSessionController controller)
    {
        if (_sessionPool != null)
        {
            _sessionPool.Return(controller);
            return;
        }

        if (controller != null)
        {
            Destroy(controller.gameObject);
        }
    }

    #endregion

    #region Auto Session Management

    /// <summary>
    /// 플레이어가 빈 대기 세션에 입장했을 때 호출됩니다.
    /// 즉시 새로운 빈 대기 세션을 생성합니다.
    /// </summary>
    public async void OnPlayerJoinedEmptySession(string sessionName)
    {
        Debug.Log($"[MultiPeerServerManager] 플레이어가 빈 세션에 입장: {sessionName}");

        // Why: 현재 여유 인원 확인
        int totalPlayerCount = GetTotalPlayerCount();
        int availableSlots = _maxTotalPlayers - totalPlayerCount;

        Debug.Log($"[MultiPeerServerManager] 현재 접속 인원: {totalPlayerCount}/{_maxTotalPlayers} (여유: {availableSlots})");

        const int MAX_POSSIBLE_SESSION_SIZE = 8;
        if (!CanCreateSession(MAX_POSSIBLE_SESSION_SIZE))
        {
            Debug.LogWarning($"[MultiPeerServerManager] 서버 여유 인원 부족! (최대 필요: {MAX_POSSIBLE_SESSION_SIZE}, 여유: {availableSlots})");
            return;
        }

        // Why: 즉시 새로운 빈 대기 세션 생성
        Debug.Log($"[MultiPeerServerManager] 새로운 빈 대기 세션 생성 중...");
        bool created = await CreateWaitingSession();

        if (created)
        {
            Debug.Log($"[MultiPeerServerManager] 새 대기 세션 생성 완료!");
        }
        else
        {
            Debug.LogError($"[MultiPeerServerManager] 새 대기 세션 생성 실패!");
        }
    }

    /// <summary>
    /// 게임 시작 전에 모든 플레이어가 나갔을 때 호출됩니다.
    /// 해당 세션을 종료합니다.
    /// </summary>
    public async void OnSessionBecameEmpty(string sessionName)
    {
        Debug.Log($"[MultiPeerServerManager] 세션이 비워짐: {sessionName}");

        if (!_activeSessions.TryGetValue(sessionName, out var controller))
        {
            return;
        }

        // Why: 게임이 시작되지 않은 세션만 종료
        if (controller.IsWaiting)
        {
            Debug.Log($"[MultiPeerServerManager] 빈 대기 세션 종료: {sessionName}");
            await StopSession(sessionName);
        }
    }

    /// <summary>
    /// 새로운 대기 세션을 생성합니다.
    /// MaxPlayers는 크게 설정하고, MatchingTargetPlayers는 첫 플레이어가 설정합니다.
    /// </summary>
    private async Task<bool> CreateWaitingSession()
    {
        // 1. 세션 이름 생성
        string sessionName = GenerateSessionName();
        Debug.Log($"[MultiPeerServerManager] CreateWaitingSession - sessionName: {sessionName}");

        // 1-0. 방 코드 생성 (랜덤 + 로컬 중복 체크)
        string roomCode = RoomCodeGenerator.GenerateCode();
        Debug.Log($"[MultiPeerServerManager] Generated RoomCode: {roomCode}");

        // 1-1. Matchmaking 씬 인덱스 조회 (대기용 씬)
        int matchmakingIndex = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/Matchmaking.unity");
        bool hasValidScene = matchmakingIndex >= 0 && matchmakingIndex < UnityEngine.SceneManagement.SceneManager.sceneCountInBuildSettings;
        if (!hasValidScene)
        {
            Debug.LogWarning($"[MultiPeerServerManager] Matchmaking scene not found in Build Settings. Proceeding without Scene load.");
        }
        else
        {
            Debug.Log($"[MultiPeerServerManager] Matchmaking scene index: {matchmakingIndex}");
        }

        // 2. StartGameArgs 구성 - Matchmaking 씬에서 대기
        // Why: 세션 최대 인원은 ServerBuildConfig.DefaultMaxPlayersPerSession 사용
        int maxPlayersPerSession = _serverBuildConfig != null ? _serverBuildConfig.DefaultMaxPlayersPerSession : 8;
        
        var args = new StartGameArgs
        {
            GameMode = Fusion.GameMode.Server,
            SessionName = sessionName,
            PlayerCount = maxPlayersPerSession, // Why: 세션 최대 인원 (동적으로 설정됨)
            Scene = hasValidScene ? SceneRef.FromIndex(matchmakingIndex) : SceneRef.None,
            SceneManager = hasValidScene ? new GameObject($"SceneManager_{sessionName}").AddComponent<NetworkSceneManagerDefault>() : null,
            // ObjectProvider 제거 - Fusion 기본 방식 사용 (직접 파괴)
            SessionProperties = new Dictionary<string, SessionProperty>
            {
                { "ServerId", _serverId },
                { "MaxPlayers", maxPlayersPerSession },  // Why: 세션 최대 인원
                { "CurrentMatchingMode", 0 },            // 0 = 모드 미설정 (첫 플레이어가 결정)
                { "MatchingTargetPlayers", 0 },          // 0 = 목표 인원 미설정 (첫 플레이어가 결정)
                { "IsInGame", false },                   // 게임 시작 안 됨
                { "IsCustom", false },                   // Why: 커스텀 모드 아님 (일반 매칭 세션)
                { "RoomCode", roomCode }                 // Why: 방 코드 (커스텀 모드에서 사용)
            }
        };

        Debug.Log($"[MultiPeerServerManager] StartGameArgs -> PlayerCount: {args.PlayerCount}, SceneValid: {hasValidScene}, SceneIndex: {(hasValidScene ? matchmakingIndex : -1)}");

        // 3. 세션 시작
        bool success = await StartSession(sessionName, args);

        if (success)
        {
            Debug.Log($"[MultiPeerServerManager] 대기 세션 생성 완료: {sessionName}");
        }
        else
        {
            Debug.LogError($"[MultiPeerServerManager] 대기 세션 생성 실패: {sessionName}");
        }

        return success;
    }

    /// <summary>
    /// 세션 이름을 생성합니다.
    /// 형식: Server_{serverId}_{counter}
    /// </summary>
    private string GenerateSessionName()
    {
        _sessionCounter++;
        return $"Server_{_serverId}_{_sessionCounter:D3}"; // 001, 002, 003...
    }

    /// <summary>
    /// 현재 모든 세션의 전체 플레이어 수를 계산합니다.
    /// </summary>
    public int GetTotalPlayerCount()
    {
        int totalCount = 0;

        foreach (var kvp in _activeSessions)
        {
            var controller = kvp.Value;
            if (controller != null && controller.Runner != null && controller.Runner.IsRunning)
            {
                totalCount += controller.Runner.ActivePlayers.Count();
            }
        }

        return totalCount;
    }

    /// <summary>
    /// 새로운 세션을 생성할 수 있는지 확인합니다.
    /// </summary>
    private bool CanCreateSession(int requiredMaxPlayers)
    {
        int currentTotal = GetTotalPlayerCount();
        int availableSlots = _maxTotalPlayers - currentTotal;

        return availableSlots >= requiredMaxPlayers;
    }

    #endregion
}
