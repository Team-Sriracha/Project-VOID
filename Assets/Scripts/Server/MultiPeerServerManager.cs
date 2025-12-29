using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
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

    [Header("서버 설정")]
    [SerializeField] private string _serverId = "local-server";
    [SerializeField] private int _maxTotalPlayers = 40;

    #endregion

    #region Private Fields

    private readonly Dictionary<string, GameSessionController> _activeSessions = new();
    private int _sessionCounter = 0;

    #endregion

    #region Public Properties

    public int MaxTotalPlayers => _maxTotalPlayers;
    public int ActiveSessionCount => _activeSessions.Count;
    public string ServerId => _serverId;

    #endregion

    #region Unity Lifecycle

    private async void Start()
    {
        Debug.Log("[MultiPeerServerManager] 서버 시작 - 대기 세션 생성...");
        await CreateWaitingSession();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 현재 모든 세션의 상태 목록을 반환합니다 (HTTP 모니터링용).
    /// </summary>
    public List<SessionStatus> GetSessionStatusList()
    {
        var result = new List<SessionStatus>();

        foreach (var kvp in _activeSessions)
        {
            var controller = kvp.Value;
            if (controller?.Runner == null) continue;

            var runner = controller.Runner;
            var sessionInfo = runner.SessionInfo;

            string roomCode = "";
            int maxPlayers = 8;
            bool isInGame = false;
            bool isCustom = false;
            string gameMode = "대기 중";
            string phase = "대기";

            // Session Properties에서 정보 추출
            if (sessionInfo.Properties != null)
            {
                if (sessionInfo.Properties.TryGetValue("RoomCode", out var roomCodeProp))
                    roomCode = roomCodeProp.PropertyValue?.ToString() ?? "";
                if (sessionInfo.Properties.TryGetValue("MaxPlayers", out var maxPlayersProp))
                    maxPlayers = maxPlayersProp.IsInt ? (int)maxPlayersProp.PropertyValue : 8;
                if (sessionInfo.Properties.TryGetValue("IsInGame", out var isInGameProp))
                    isInGame = isInGameProp.IsInt ? (int)isInGameProp.PropertyValue != 0 : (bool)isInGameProp.PropertyValue;
                if (sessionInfo.Properties.TryGetValue("IsCustom", out var isCustomProp))
                    isCustom = isCustomProp.IsInt ? (int)isCustomProp.PropertyValue != 0 : (bool)isCustomProp.PropertyValue;
            }

            // GameStateManager에서 정확한 정보 읽기
            try
            {
                var gsm = runner.GetAllBehaviours<GameStateManager>()
                    .FirstOrDefault(g => g?.Object?.IsValid == true);
                    
                if (gsm != null)
                {
                    int targetPlayers = gsm.TargetPlayerCount;
                    if (targetPlayers > 0) maxPlayers = targetPlayers;
                    
                    gameMode = isCustom ? "커스텀" : targetPlayers switch
                    {
                        1 => "연습장",
                        4 => "4인 모드",
                        8 => "8인 모드",
                        _ when targetPlayers > 0 => $"{targetPlayers}인 모드",
                        _ => "대기 중"
                    };
                    
                    isInGame = gsm.IsGameStarted;
                    phase = gsm.IsGameStarted ? $"Phase {gsm.CurrentPhase}" : "대기";
                }
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
                ObjectCount = runner.GetAllBehaviours<NetworkBehaviour>().Count()
            });
        }

        return result;
    }

    /// <summary>
    /// 플레이어가 빈 대기 세션에 입장했을 때 새로운 세션 생성
    /// </summary>
    public async void OnPlayerJoinedEmptySession(string sessionName)
    {
        Debug.Log($"[MultiPeerServerManager] 플레이어 입장: {sessionName}");

        if (!CanCreateSession(8))
        {
            Debug.LogWarning("[MultiPeerServerManager] 서버 여유 인원 부족!");
            return;
        }

        await CreateWaitingSession();
    }

    /// <summary>
    /// 게임 시작 전 모든 플레이어가 나갔을 때 세션 종료
    /// </summary>
    public async void OnSessionBecameEmpty(string sessionName)
    {
        if (!_activeSessions.TryGetValue(sessionName, out var controller)) return;

        if (controller.IsWaiting)
        {
            Debug.Log($"[MultiPeerServerManager] 빈 세션 종료: {sessionName}");
            await StopSession(sessionName);
        }
    }

    public async Task StopAllSessions()
    {
        foreach (var session in new List<string>(_activeSessions.Keys))
        {
            await StopSession(session);
        }
    }

    public int GetTotalPlayerCount()
    {
        return _activeSessions.Values
            .Where(c => c?.Runner?.IsRunning == true)
            .Sum(c => c.Runner.ActivePlayers.Count());
    }

    #endregion

    #region Private Methods

    private async Task<bool> StartSession(string sessionName, StartGameArgs args)
    {
        if (string.IsNullOrEmpty(sessionName) || _activeSessions.ContainsKey(sessionName))
            return false;

        var controller = AcquireController();
        if (controller == null) return false;

        bool ok = await controller.StartSession(args, this);
        if (ok)
        {
            _activeSessions[sessionName] = controller;
            Debug.Log($"[MultiPeerServerManager] 세션 시작: {sessionName}");
        }
        else
        {
            ReleaseController(controller);
        }

        return ok;
    }

    public async Task StopSession(string sessionName)
    {
        if (!_activeSessions.TryGetValue(sessionName, out var controller)) return;

        await controller.StopSession();
        ReleaseController(controller);
        _activeSessions.Remove(sessionName);
    }

    private async Task<bool> CreateWaitingSession()
    {
        string sessionName = $"Server_{_serverId}_{++_sessionCounter:D3}";
        string roomCode = RoomCodeGenerator.GenerateCode();

        int matchmakingIndex = UnityEngine.SceneManagement.SceneUtility
            .GetBuildIndexByScenePath("Assets/Scenes/Matchmaking.unity");
        bool hasValidScene = matchmakingIndex >= 0;

        int maxPlayers = _serverBuildConfig?.DefaultMaxPlayersPerSession ?? 8;
        ushort basePort = _serverBuildConfig?.DefaultPort ?? 27015;
        string serverIP = _serverBuildConfig?.ServerPublicIP ?? "";
        bool useDirectConnection = !string.IsNullOrEmpty(serverIP);

        // 첫 번째 사용 가능한 포트 찾기 (27015부터 순차 검색)
        ushort port = GetNextAvailablePort(basePort);

        var args = new StartGameArgs
        {
            GameMode = Fusion.GameMode.Server,
            SessionName = sessionName,
            PlayerCount = maxPlayers,
            Scene = hasValidScene ? SceneRef.FromIndex(matchmakingIndex) : SceneRef.None,
            SceneManager = hasValidScene ? new GameObject($"SceneManager_{sessionName}").AddComponent<NetworkSceneManagerDefault>() : null,
            Address = useDirectConnection ? NetAddress.Any(port) : default,
            SessionProperties = new Dictionary<string, SessionProperty>
            {
                { "ServerId", _serverId },
                { "MaxPlayers", maxPlayers },
                { "CurrentMatchingMode", 0 },
                { "MatchingTargetPlayers", 0 },
                { "IsInGame", false },
                { "IsCustom", false },
                { "RoomCode", roomCode },
                { "ServerIP", serverIP },
                { "ServerPort", port }  // 동적 포트 저장
            }
        };

        Debug.Log($"[MultiPeerServerManager] 세션 생성: {sessionName} (Direct: {useDirectConnection}, Port: {port})");
        return await StartSession(sessionName, args);
    }

    /// <summary>
    /// 첫 번째 사용 가능한 포트를 찾습니다 (basePort부터 순차 검색).
    /// 최대 _maxTotalPlayers개의 포트 사용 가능.
    /// </summary>
    private ushort GetNextAvailablePort(ushort basePort)
    {
        var usedPorts = new HashSet<ushort>();
        
        foreach (var controller in _activeSessions.Values)
        {
            if (controller.Runner?.SessionInfo?.Properties?.TryGetValue("ServerPort", out var p) == true)
            {
                usedPorts.Add((ushort)(int)p.PropertyValue);
            }
        }
        
        for (int i = 0; i < _maxTotalPlayers; i++)
        {
            ushort port = (ushort)(basePort + i);
            if (!usedPorts.Contains(port))
            {
                Debug.Log($"[MultiPeerServerManager] 포트 할당: {port} (사용 중: {usedPorts.Count}개)");
                return port;
            }
        }
        
        Debug.LogWarning($"[MultiPeerServerManager] 모든 포트 사용 중! 기본 포트 반환: {basePort}");
        return basePort;
    }

    private GameSessionController AcquireController()
    {
        if (_sessionPool != null)
        {
            var controller = _sessionPool.Get();
            if (controller != null) return controller;
        }

        return _sessionControllerPrefab != null ? Instantiate(_sessionControllerPrefab) : null;
    }

    private void ReleaseController(GameSessionController controller)
    {
        if (controller == null) return;

        if (_sessionPool != null)
            _sessionPool.Return(controller);
        else
            Destroy(controller.gameObject);
    }

    private bool CanCreateSession(int requiredPlayers)
    {
        return _maxTotalPlayers - GetTotalPlayerCount() >= requiredPlayers;
    }

    #endregion
}
