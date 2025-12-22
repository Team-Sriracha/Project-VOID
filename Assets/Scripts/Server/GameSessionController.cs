using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>
/// 단일 NetworkRunner 세션을 관리합니다.
/// </summary>
public class GameSessionController : MonoBehaviour, INetworkRunnerCallbacks
{
    #region Private Fields

    private NetworkRunner _runner;
    private MultiPeerServerManager _serverManager;
    private int _previousPlayerCount = 0;
    private bool _hadPlayersJoin = false;
    [SerializeField] private NetworkManager _networkManagerPrefab;
    private NetworkManager _networkManagerInstance;

    #endregion

    #region Properties

    public NetworkRunner Runner => _runner;

    /// <summary>
    /// 이 세션이 대기 중인지 확인합니다 (게임 시작 안 됨).
    /// </summary>
    public bool IsWaiting
    {
        get
        {
            if (_runner == null || !_runner.IsRunning) return false;

            // SessionProperties에서 IsInGame 확인
            if (_runner.SessionInfo.Properties.TryGetValue("IsInGame", out var prop))
            {
                bool isInGame = prop.IsInt ? (int)prop != 0 : (bool)prop;
                return !isInGame; // 게임 진행 중이 아니면 대기 중
            }

            // IsInGame 속성이 없으면 대기 중으로 간주
            return true;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 세션을 시작합니다. Runner가 없으면 생성합니다.
    /// </summary>
    public async Task<bool> StartSession(StartGameArgs args, MultiPeerServerManager serverManager = null)
    {
        if (this == null || gameObject == null)
        {
            Debug.LogError("[GameSessionController] StartSession called on destroyed controller. Aborting.");
            return false;
        }

        _serverManager = serverManager;
        _hadPlayersJoin = false;

        if (_runner == null)
        {
            // Why: GameObject 이름을 세션 이름으로 변경하여 NetworkSceneManager의 씬 이름 충돌 방지
            gameObject.name = $"GameSessionController_{args.SessionName}";
            
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = false;
            Debug.Log($"[GameSessionController] NetworkRunner created on {gameObject.name} | SessionName: {args.SessionName}");
        }

        // Why: 플레이어 입장/퇴장 이벤트를 받기 위해 콜백 등록
        _runner.AddCallbacks(this);

        Debug.Log($"[GameSessionController] Starting Runner session '{args.SessionName}' | GameMode: {args.GameMode} | PlayerCount: {args.PlayerCount}");
        var result = await _runner.StartGame(args);

        // Why: StartGame 완료 후 오브젝트가 파괴되었는지 체크 (연결 실패 시 발생 가능)
        if (this == null || gameObject == null)
        {
            Debug.LogWarning("[GameSessionController] Controller was destroyed during StartGame. Aborting session setup.");
            return false;
        }

        if (result.Ok)
        {
            ServiceLocator.Register(_runner, this);
            _previousPlayerCount = 0;

            var networkManager = GetOrCreateNetworkManager();
            if (networkManager != null)
            {
                networkManager.SetRunner(_runner);
                if (_runner.IsServer)
                {
                    // Why: 매니저 스폰이 완료될 때까지 대기 (클라이언트 접속 전에 준비 완료 보장)
                    await networkManager.EnsureSessionManagersAsync();

                    // Why: 비동기 대기 후 오브젝트 파괴 여부 재확인
                    if (this == null || gameObject == null)
                    {
                        Debug.LogWarning("[GameSessionController] Controller was destroyed during EnsureSessionManagersAsync.");
                        return false;
                    }
                }

                Debug.Log($"[GameSessionController] NetworkManager attached to session '{_runner.SessionInfo.Name}' | RunnerIsServer: {_runner.IsServer}");
            }
            else
            {
                Debug.LogWarning("[GameSessionController] NetworkManager not found in scene. Session managers will not be spawned automatically.");
            }
        }
        else
        {
            Debug.LogError($"[GameSessionController] StartGame failed for session '{args.SessionName}' | Reason: {result.ShutdownReason}");
        }

        return result.Ok;
    }

    private NetworkManager GetOrCreateNetworkManager()
    {
        if (this == null || gameObject == null)
        {
            Debug.LogError("[GameSessionController] GetOrCreateNetworkManager called on destroyed controller.");
            return null;
        }

        if (_networkManagerInstance != null) return _networkManagerInstance;

        var existing = GetComponentInChildren<NetworkManager>();
        if (existing != null)
        {
            _networkManagerInstance = existing;
            Debug.Log("[GameSessionController] Using existing NetworkManager found in children.");
            return _networkManagerInstance;
        }

        if (_networkManagerPrefab == null)
        {
            Debug.LogError("[GameSessionController] NetworkManager prefab is not assigned.");
            return null;
        }

        _networkManagerInstance = Instantiate(_networkManagerPrefab, transform);
        Debug.Log("[GameSessionController] Instantiated NetworkManager from prefab.");
        return _networkManagerInstance;
    }

    /// <summary>
    /// 세션을 종료합니다.
    /// </summary>
    public async Task StopSession()
    {
        if (_runner == null) return;



        ServiceLocator.Clear(_runner);
        await _runner.Shutdown();
    }

    #endregion

    #region Fusion Callbacks

    /// <summary>
    /// 플레이어가 세션에 입장했을 때 호출됩니다.
    /// </summary>
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (_runner == null || _serverManager == null) return;

        int currentPlayerCount = _runner.ActivePlayers.Count();

        Debug.Log($"[GameSessionController] 플레이어 입장: {player} (세션: {_runner.SessionInfo.Name}, 인원: {_previousPlayerCount}→{currentPlayerCount})");

        // Why: 0명 → 1명이 되면 빈 세션에 첫 플레이어가 입장한 것
        if (_previousPlayerCount == 0 && currentPlayerCount == 1 && IsWaiting)
        {
            Debug.Log($"[GameSessionController] 빈 세션에 첫 플레이어 입장 감지!");
            _serverManager.OnPlayerJoinedEmptySession(_runner.SessionInfo.Name);
        }

        if (currentPlayerCount > 0)
        {
            _hadPlayersJoin = true;
        }

        _previousPlayerCount = currentPlayerCount;
    }

    /// <summary>
    /// 플레이어가 세션에서 퇴장했을 때 호출됩니다.
    /// </summary>
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_runner == null || _serverManager == null) return;

        int currentPlayerCount = _runner.ActivePlayers.Count();

        Debug.Log($"[GameSessionController] 플레이어 퇴장: {player} (세션: {_runner.SessionInfo.Name}, 인원: {_previousPlayerCount}→{currentPlayerCount})");

        // Why: 매칭 후 모두 이탈한 경우를 명확히 감지
        if (currentPlayerCount == 0 && IsWaiting && _hadPlayersJoin)
        {
            Debug.Log($"[GameSessionController] 매칭 취소 감지! (모든 플레이어 퇴장)");
            _serverManager.OnSessionBecameEmpty(_runner.SessionInfo.Name);
        }

        _previousPlayerCount = currentPlayerCount;
    }

    // Unused INetworkRunnerCallbacks methods
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    #endregion
}
