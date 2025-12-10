using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using ProjectVoid.Network;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Photon Fusion 네트워크 연결 및 플레이어 스폰을 관리합니다.
/// Server 모드에서 동작하며, INetworkRunnerCallbacks를 구현합니다.
/// Multi-Peer 환경을 지원하기 위해 Runner 기반의 인스턴스 조회를 제공합니다.
/// </summary>
public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    #region Static Map (Multi-Peer Support)

    private static Dictionary<NetworkRunner, NetworkManager> _runnerToManagerMap = new Dictionary<NetworkRunner, NetworkManager>();

    /// <summary>
    /// 특정 NetworkRunner에 해당하는 NetworkManager 인스턴스를 반환합니다.
    /// </summary>
    public static NetworkManager GetManager(NetworkRunner runner)
    {
        if (runner == null) return null;
        _runnerToManagerMap.TryGetValue(runner, out var manager);
        return manager;
    }

    #endregion

    #region Serialized Fields

    [Header("플레이어 설정")]
    [SerializeField] private NetworkPrefabRef _playerPrefab;

    [Header("맵 설정")]
    [SerializeField] private NetworkPrefabRef _mapManagerPrefab;

    [Header("게임 상태 관리")]
    [SerializeField] private NetworkPrefabRef _gameStateManagerPrefab;

    #endregion

    #region Properties

    public NetworkRunner Runner { get; private set; }
    public int MaxPlayers => _currentConnectionInfo?.MaxPlayers ?? 0;
    public string SessionName => _currentConnectionInfo?.SessionName ?? string.Empty;
    public EGameMode? CurrentGameMode => _currentConnectionInfo?.GameMode;

    /// <summary>
    /// 현재 세션의 GameStateManager
    /// </summary>
    public GameStateManager GameStateManager => _gameStateManager;

    /// <summary>
    /// 현재 세션의 NetworkMapManager
    /// </summary>
    public NetworkMapManager NetworkMapManager => _networkMapManager;

    #endregion

    #region Private Fields

    private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    private PlayerInputHandler _cachedInputHandler;
    
    // 매니저 참조 저장 (Singleton 제거)
    private GameStateManager _gameStateManager;
    private NetworkMapManager _networkMapManager;
    
    private bool _mapManagerSpawned = false;
    private bool _gameStateManagerSpawned = false;

    private GameConnectionInfo? _currentConnectionInfo;
    private string _lobbySceneName = "Lobby";

    private List<PlayerRef> _pendingPlayerSpawns = new List<PlayerRef>();
    private bool _isWaitingForGameStart = false;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Runner != null)
        {
            _runnerToManagerMap.Remove(Runner);
        }
    }

    #endregion

    #region Public Methods

    public void SetRunner(NetworkRunner runner)
    {
        if (Runner != null)
        {
            _runnerToManagerMap.Remove(Runner);
        }

        Runner = runner;
        
        if (runner != null)
        {
            _runnerToManagerMap[runner] = this;
        }
    }

    public IReadOnlyDictionary<PlayerRef, NetworkObject> GetSpawnedPlayers()
    {
        return _spawnedPlayers;
    }

    public NetworkObject GetPlayerNetworkObject(PlayerRef player)
    {
        _spawnedPlayers.TryGetValue(player, out NetworkObject networkObject);
        return networkObject;
    }

    public async Task<bool> StartGameServer(GameConnectionInfo connectionInfo)
    {
        _currentConnectionInfo = connectionInfo;

        if (Runner != null)
        {
            _runnerToManagerMap.Remove(Runner);
            await Runner.Shutdown();
            Destroy(Runner);
            Runner = null;
        }

        Runner = gameObject.AddComponent<NetworkRunner>();
        _runnerToManagerMap[Runner] = this; // 등록
        Runner.ProvideInput = true;

        var result = await Runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Server,
            SessionName = connectionInfo.SessionName,
            Scene = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            PlayerCount = connectionInfo.MaxPlayers,
            SessionProperties = new Dictionary<string, SessionProperty>
            {
                { "ServerId", connectionInfo.ServerInfo.ServerId },
                { "MaxPlayers", connectionInfo.MaxPlayers },
                { "GameMode", (int)connectionInfo.GameMode }
            }
        });

        return result.Ok;
    }

    public async Task<bool> JoinGameServer(GameConnectionInfo connectionInfo)
    {
        _currentConnectionInfo = connectionInfo;

        if (Runner != null)
        {
            _runnerToManagerMap.Remove(Runner);
            await Runner.Shutdown();
            Destroy(Runner);
            Runner = null;
        }

        Runner = gameObject.AddComponent<NetworkRunner>();
        _runnerToManagerMap[Runner] = this; // 등록
        Runner.ProvideInput = true;

        // Why: SceneManager를 설정하지 않으면 씬 동기화가 일어나지 않음
        // 클라이언트는 LobbyScene에 머물면서 서버의 NetworkObject만 동기화
        var startArgs = new StartGameArgs()
        {
            GameMode = GameMode.Client,
            SessionName = connectionInfo.SessionName,
            // SceneManager 제거 - 씬 동기화 비활성화
        };

        var result = await Runner.StartGame(startArgs);

        return result.Ok;
    }

    public async Task ReturnToLobby()
    {
        if (Runner != null && Runner.IsRunning)
        {
            _runnerToManagerMap.Remove(Runner);
            await Runner.Shutdown();
        }

        if (Runner != null)
        {
            Destroy(Runner);
            Runner = null;
        }

        _spawnedPlayers.Clear();
        _pendingPlayerSpawns.Clear();
        _mapManagerSpawned = false;
        _gameStateManagerSpawned = false;
        _cachedInputHandler = null;
        _gameStateManager = null;
        _networkMapManager = null;
        _currentConnectionInfo = null;
        _isWaitingForGameStart = false;

        SceneManager.LoadScene(_lobbySceneName);
    }

    public GameConnectionInfo? GetCurrentConnectionInfo()
    {
        return _currentConnectionInfo;
    }

    #endregion

    #region INetworkRunnerCallbacks - Player

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)
        {
            if (runner.GameMode == GameMode.Server && player == runner.LocalPlayer)
            {
                return;
            }

            // Why: 실제 클라이언트가 조인했으므로 ServerLauncher가 이를 감지하고(Update 루프) 자동으로 새 프로세스를 띄웁니다.
            // ServerLauncher.Instance?.OnPlayerJoinedAnySession(); // 제거됨: Multi-Process 구조에서는 ServerLauncher가 스스로 체크함

            if (_spawnedPlayers.Count == 0 && _pendingPlayerSpawns.Count == 0)
            {
                SpawnGameObjects(runner);
            }

            // Why: GameStateManager에 플레이어 조인 알림 (게임 시작 조건 체크)
            if (_gameStateManager != null)
            {
                _gameStateManager.OnPlayerJoined();
            }

            // Why: 플레이어를 즉시 스폰하지 않고 대기 목록에 추가
            // 게임이 시작될 때 WaitForGameStartAndSpawnPlayers 코루틴이 스폰
            if (!_pendingPlayerSpawns.Contains(player))
            {
                _pendingPlayerSpawns.Add(player);
                Debug.Log($"[NetworkManager] Player {player.PlayerId} added to pending spawns. Total pending: {_pendingPlayerSpawns.Count}");
            }

            // Why: 맵 및 게임 시작 대기 코루틴 시작 (중복 실행 방지)
            if (!_isWaitingForGameStart)
            {
                _isWaitingForGameStart = true;
                StartCoroutine(WaitForGameStartAndSpawnPlayers(runner));
            }
        }
    }

    private IEnumerator WaitForGameStartAndSpawnPlayers(NetworkRunner runner)
    {
        float waitTime = 0f;
        float maxWaitTime = 120f; // 최대 2분 대기 (맵 생성 시간 고려)
        float checkInterval = 0.5f;

        Debug.Log($"[NetworkManager] Waiting for map and game start... (Pending players: {_pendingPlayerSpawns.Count})");

        // Why: 맵이 준비될 때까지 반드시 대기 (게임 시작 여부와 무관하게 스폰)
        bool mapReady = false;
        bool gameStarted = false;

        while (waitTime < maxWaitTime)
        {
            mapReady = _networkMapManager != null && _networkMapManager.IsReady();
            gameStarted = _gameStateManager != null && _gameStateManager.IsGameStarted;

            // 주기적으로 상태 로그 출력 (5초마다)
            if (Mathf.Approximately(waitTime % 5f, 0f))
            {
                Debug.Log($"[NetworkManager] Wait status - MapReady: {mapReady}, GameStarted: {gameStarted}, Elapsed: {waitTime}s");
            }

            // Why: 맵이 준비되면 게임 시작 여부와 상관없이 스폰을 진행
            if (mapReady)
            {
                if (!gameStarted)
                {
                    Debug.LogWarning("[NetworkManager] Map ready but game not marked started yet. Spawning players anyway so Alive count can trigger start.");
                }
                Debug.Log("[NetworkManager] Map ready! Spawning all pending players...");
                break;
            }

            waitTime += checkInterval;
            yield return new WaitForSeconds(checkInterval);
        }

        // Why: 맵이 준비되지 않았으면 절대 스폰하지 않음
        if (!mapReady)
        {
            Debug.LogError($"[NetworkManager] CRITICAL: Map not ready after {maxWaitTime}s! Cannot spawn players!");
            Debug.LogError($"[NetworkManager] MapManager exists: {_networkMapManager != null}, IsReady: {_networkMapManager?.IsReady()}");
            _isWaitingForGameStart = false;
            yield break; // 스폰하지 않고 종료
        }

        // Why: 게임이 시작되지 않았지만 맵은 준비된 경우 경고 (그래도 스폰은 진행)
        if (!gameStarted)
        {
            Debug.LogWarning($"[NetworkManager] Game not started yet, but map is ready. Spawning players anyway.");
        }

        // Why: 맵이 준비된 경우에만 플레이어 스폰
        Debug.Log($"[NetworkManager] Spawning {_pendingPlayerSpawns.Count} pending players...");
        foreach (var pendingPlayer in _pendingPlayerSpawns.ToArray())
        {
            SpawnPlayer(runner, pendingPlayer);
        }
        _pendingPlayerSpawns.Clear();

        _isWaitingForGameStart = false;
    }

    private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedPlayers.ContainsKey(player)) return;

        Vector3 spawnPosition = GetSpawnPositionForPlayer(player);
        Quaternion spawnRotation = GetSpawnRotationForPlayer(player);

        NetworkObject networkPlayerObject = runner.Spawn(_playerPrefab, spawnPosition, spawnRotation, player);
        runner.SetPlayerObject(player, networkPlayerObject);

        _spawnedPlayers.Add(player, networkPlayerObject);

        if (runner.IsServer && _gameStateManager != null)
        {
            _gameStateManager.OnPlayerSpawnedAlive();
        }

        Debug.Log($"[NetworkManager] Player {player.PlayerId} spawned at {spawnPosition}. Total spawned: {_spawnedPlayers.Count}");
    }

    private Vector3 GetSpawnPositionForPlayer(PlayerRef player)
    {
        // Why: 맵이 준비되지 않은 상태에서는 절대 호출되면 안 됨
        if (_networkMapManager == null || !_networkMapManager.IsReady())
        {
            Debug.LogError($"[NetworkManager] CRITICAL: GetSpawnPositionForPlayer called but map not ready! Player {player.PlayerId}");
            Debug.LogError("[NetworkManager] This should never happen! Check WaitForGameStartAndSpawnPlayers logic.");
            // Why: 에러 상황이지만 크래시를 방지하기 위해 원점 반환
            return Vector3.zero;
        }

        int playerIndex = _spawnedPlayers.Count;
        Vector3 spawnPos = _networkMapManager.GetPlayerSpawnPosition(playerIndex);
        Debug.Log($"[NetworkManager] Player {player.PlayerId} (index {playerIndex}) spawn position: {spawnPos}");
        return spawnPos;
    }

    private Quaternion GetSpawnRotationForPlayer(PlayerRef player)
    {
        // Why: 맵이 준비되지 않은 상태에서는 절대 호출되면 안 됨
        if (_networkMapManager == null || !_networkMapManager.IsReady())
        {
            Debug.LogError($"[NetworkManager] CRITICAL: GetSpawnRotationForPlayer called but map not ready! Player {player.PlayerId}");
            return Quaternion.identity;
        }

        int playerIndex = _spawnedPlayers.Count;
        float rotation = _networkMapManager.GetPlayerSpawnRotation(playerIndex);
        Debug.Log($"[NetworkManager] Player {player.PlayerId} (index {playerIndex}) spawn rotation: {rotation}°");
        return Quaternion.Euler(0, rotation, 0);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[NetworkManager] OnPlayerLeft - Player {player.PlayerId}");

        // 1. 이미 스폰된 플레이어인 경우
        if (_spawnedPlayers.TryGetValue(player, out NetworkObject networkObject))
        {
            bool wasAlive = true;
            if (networkObject != null && networkObject.TryGetComponent<PlayerCombat>(out var combat))
            {
                wasAlive = combat.IsAlive;
            }

            if (runner.IsServer && _gameStateManager != null)
            {
                Debug.Log($"[NetworkManager] Spawned player left - calling OnPlayerLeft() | WasAlive: {wasAlive}");
                _gameStateManager.OnPlayerLeft(wasAlive);
            }

            runner.Despawn(networkObject);
            _spawnedPlayers.Remove(player);
        }
        // 2. 스폰 대기 중인 플레이어인 경우 (맵 생성 중 나간 경우)
        else if (_pendingPlayerSpawns.Contains(player))
        {
            _pendingPlayerSpawns.Remove(player);
            Debug.Log($"[NetworkManager] Pending player left - removed from pending list. Remaining pending: {_pendingPlayerSpawns.Count}");

            // Why: OnPlayerJoined에서 OnPlayerJoined()를 호출했으므로 OnPlayerLeft()도 호출해야 함
            // 매칭 취소 시 플레이어 수가 제대로 감소하도록 보장
            if (runner.IsServer && _gameStateManager != null)
            {
                Debug.Log($"[NetworkManager] Pending player left - calling OnPlayerLeft()");
                _gameStateManager.OnPlayerLeft(false);
            }
        }
        else
        {
            Debug.LogWarning($"[NetworkManager] Player {player.PlayerId} not found in spawned or pending lists");
        }
    }

    #endregion

    #region INetworkRunnerCallbacks - Input

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // 매치메이킹 패널이 열려있고 아직 게임이 시작되지 않았다면 입력 차단 (로비/대기 상태)
        if (UIManager.Instance != null && UIManager.Instance.IsMatchmakingPanelActive)
        {
            var gs = GameStateManager.Instance;
            bool gameStarted = gs != null && gs.IsGameStarted;
            if (!gameStarted)
            {
                return;
            }
        }

        if (_cachedInputHandler != null)
        {
            var netObj = _cachedInputHandler.GetComponent<NetworkObject>();
            // Why: 캐시가 다른 플레이어를 가리키거나 소유권을 잃었으면 무효화
            if (netObj == null || !netObj.HasInputAuthority)
            {
                _cachedInputHandler = null;
            }
            else
            {
                // Why: 로컬 플레이어가 죽었으면 해당 클라이언트만 입력을 멈춤
                var combat = _cachedInputHandler.GetComponent<PlayerCombat>();
                if (combat != null && !combat.IsAlive)
                {
                    return;
                }

                NetworkInputData data = _cachedInputHandler.GetCurrentInput();
                input.Set(data);
                return;
            }
        }

        var allInputHandlers = FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None);
        foreach (var inputHandler in allInputHandlers)
        {
            NetworkObject netObj = inputHandler.GetComponent<NetworkObject>();
            if (netObj != null && netObj.HasInputAuthority)
            {
                var combat = inputHandler.GetComponent<PlayerCombat>();
                // Why: 로컬 플레이어가 죽었으면 입력을 멈추지만 다른 클라이언트에는 영향 없음
                if (combat != null && !combat.IsAlive)
                {
                    return;
                }

                _cachedInputHandler = inputHandler;
                NetworkInputData data = inputHandler.GetCurrentInput();
                input.Set(data);
                return;
            }
        }
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

    #endregion

    #region INetworkRunnerCallbacks - Connection

    void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner runner)
    {
        // Why: 클라이언트가 서버에 연결되었을 때, 게임 모드 정보를 서버에 전달
        if (!runner.IsServer && _currentConnectionInfo.HasValue)
        {
            EGameMode gameMode = _currentConnectionInfo.Value.GameMode;
            int targetPlayers = _currentConnectionInfo.Value.MaxPlayers;

            Debug.Log($"[NetworkManager] Connected to server. Waiting for GameStateManager to send game mode info - GameMode: {gameMode}, TargetPlayers: {targetPlayers}");

            // Why: GameStateManager가 동기화될 때까지 대기 후 RPC 호출
            StartCoroutine(WaitForGameStateManagerAndSendGameMode(gameMode, targetPlayers));
        }
    }

    private IEnumerator WaitForGameStateManagerAndSendGameMode(EGameMode gameMode, int targetPlayers)
    {
        float waitTime = 0f;
        float maxWaitTime = 10f;
        float checkInterval = 0.1f;

        while (waitTime < maxWaitTime)
        {
            if (GameStateManager.Instance != null && GameStateManager.Instance.Object != null && GameStateManager.Instance.Object.IsValid)
            {
                Debug.Log($"[NetworkManager] GameStateManager found. Sending game mode info via RPC - GameMode: {gameMode}, TargetPlayers: {targetPlayers}");
                GameStateManager.Instance.RPC_SetGameModeInfo((int)gameMode, targetPlayers);
                yield break;
            }

            waitTime += checkInterval;
            yield return new WaitForSeconds(checkInterval);
        }

        Debug.LogWarning("[NetworkManager] Timeout waiting for GameStateManager! Could not send game mode info.");
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { request.Accept(); }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }

    #endregion

    #region INetworkRunnerCallbacks - Session

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }

    #endregion

    #region INetworkRunnerCallbacks - Misc

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        if (!runner.IsServer) return;
        // 씬 로드 완료 시 처리가 필요하다면 여기 추가
    }

    private void SpawnGameObjects(NetworkRunner runner)
    {
        if (!runner.IsServer) return;

        // GameStateManager 스폰
        if (!_gameStateManagerSpawned)
        {
            if (_gameStateManagerPrefab.IsValid)
            {
                try 
                {
                    NetworkObject gameStateManagerObj = runner.Spawn(_gameStateManagerPrefab, Vector3.zero, Quaternion.identity);
                    if (gameStateManagerObj != null)
                    {
                        _gameStateManagerSpawned = true;
                        _gameStateManager = gameStateManagerObj.GetComponent<GameStateManager>();

                        // Why: TargetPlayerCount는 클라이언트가 RPC로 전달하므로 여기서는 초기화만
                        // 서버는 첫 번째 클라이언트의 RPC_SetGameModeInfo를 통해 TargetPlayerCount를 받음
                        if (_gameStateManager != null)
                        {
                            _gameStateManager.TargetPlayerCount = 0; // RPC 대기 중
                            Debug.Log("[NetworkManager] GameStateManager spawned. Waiting for client to send game mode info via RPC...");
                        }
                    }
                }
                catch (System.Exception ex) { Debug.LogError($"[NetworkManager] Failed to spawn GameStateManager: {ex.Message}"); }
            }
        }

        // NetworkMapManager 스폰
        if (!_mapManagerSpawned)
        {
            if (_mapManagerPrefab.IsValid)
            {
                try
                {
                    NetworkObject mapManagerObj = runner.Spawn(_mapManagerPrefab, Vector3.zero, Quaternion.identity);
                    if (mapManagerObj != null)
                    {
                        _mapManagerSpawned = true;
                        _networkMapManager = mapManagerObj.GetComponent<NetworkMapManager>();

                        // Why: 맵 초기화는 RPC를 통해 게임 모드 정보를 받은 후에 수행
                        // 일단 기본값(8인)으로 초기화하고, RPC에서 재초기화
                        if (_networkMapManager != null)
                        {
                            Debug.Log("[NetworkManager] NetworkMapManager spawned. Will initialize after receiving game mode info via RPC...");
                        }
                    }
                }
                catch (System.Exception) { }
            }
        }
    }

    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    #endregion
}
