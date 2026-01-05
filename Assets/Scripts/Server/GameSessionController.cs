using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>
/// 단일 NetworkRunner 세션을 관리하는 컨트롤러
/// </summary>
public class GameSessionController : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkManager _networkManagerPrefab;

    private NetworkRunner _runner;
    private NetworkManager _networkManagerInstance;
    private MultiPeerServerManager _serverManager;
    private int _previousPlayerCount;
    private bool _hadPlayersJoin;

    public NetworkRunner Runner => _runner;
    public bool IsWaiting => _runner?.IsRunning == true && !IsInGame();

    private bool IsInGame()
    {
        if (_runner?.SessionInfo.Properties == null) return false;
        if (!_runner.SessionInfo.Properties.TryGetValue("IsInGame", out var prop)) return false;
        return prop.IsInt ? (int)prop != 0 : (bool)prop;
    }

    public async Task<bool> StartSession(StartGameArgs args, MultiPeerServerManager serverManager = null)
    {
        if (this == null) return false;

        _serverManager = serverManager;
        _hadPlayersJoin = false;

        if (_runner == null)
        {
            gameObject.name = $"GameSessionController_{args.SessionName}";
            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = false;
        }

        _runner.AddCallbacks(this);
        Debug.Log($"[GameSessionController] 세션 시작: {args.SessionName}");
        
        var result = await _runner.StartGame(args);
        if (this == null) return false;

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
                    await networkManager.EnsureSessionManagersAsync();
                    if (this == null) return false;
                }
            }
        }
        else
        {
            Debug.LogError($"[GameSessionController] 세션 시작 실패: {result.ShutdownReason}");
        }

        return result.Ok;
    }

    public async Task StopSession()
    {
        if (_runner == null) return;
        ServiceLocator.Clear(_runner);
        await _runner.Shutdown();
    }

    private NetworkManager GetOrCreateNetworkManager()
    {
        if (_networkManagerInstance != null) return _networkManagerInstance;

        _networkManagerInstance = GetComponentInChildren<NetworkManager>();
        if (_networkManagerInstance != null) return _networkManagerInstance;

        if (_networkManagerPrefab == null)
        {
            Debug.LogError("[GameSessionController] NetworkManager 프리팹 미할당");
            return null;
        }

        _networkManagerInstance = Instantiate(_networkManagerPrefab, transform);
        return _networkManagerInstance;
    }

    #region Fusion Callbacks

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (_runner == null || _serverManager == null) return;

        int currentCount = _runner.ActivePlayers.Count();
        Debug.Log($"[GameSessionController] 플레이어 입장: {player} ({_previousPlayerCount}→{currentCount})");

        if (_previousPlayerCount == 0 && currentCount == 1 && IsWaiting)
        {
            _serverManager.OnPlayerJoinedEmptySession(_runner.SessionInfo.Name);
        }

        if (currentCount > 0) _hadPlayersJoin = true;
        _previousPlayerCount = currentCount;
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_runner == null || _serverManager == null) return;

        int currentCount = _runner.ActivePlayers.Count();
        Debug.Log($"[GameSessionController] 플레이어 퇴장: {player} ({_previousPlayerCount}→{currentCount})");

        if (currentCount == 0 && IsWaiting && _hadPlayersJoin)
        {
            _serverManager.OnSessionBecameEmpty(_runner.SessionInfo.Name);
        }

        _previousPlayerCount = currentCount;
    }

    // Unused callbacks
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
