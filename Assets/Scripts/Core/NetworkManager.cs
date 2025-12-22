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
    private NetworkRunner _callbacksRunner;

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
    public GameMode? CurrentGameMode => _currentConnectionInfo?.GameMode;

    /// <summary>
    /// 현재 세션의 GameStateManager
    /// </summary>
    public GameStateManager GameStateManager => _gameStateManager;

    /// <summary>
    /// 현재 세션의 NetworkMapManager
    /// </summary>
    public NetworkMapManager NetworkMapManager => _networkMapManager;
    public bool HasSpawnedMapManager => _mapManagerSpawned && _networkMapManager != null && _networkMapManager.Object != null && _networkMapManager.Object.IsValid;

    #endregion

    #region Private Fields

    private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    private PlayerInputHandler _cachedInputHandler;
    
    // 매니저 참조 저장 (Singleton 제거)
    private GameStateManager _gameStateManager;
    private NetworkMapManager _networkMapManager;
    
    private bool _mapManagerSpawned = false;
    private bool _gameStateManagerSpawned = false;
    private bool _spawningManagersInProgress = false; // Why: 중복 스폰 방지용 플래그
    private bool _isGameStarted = false; // Why: GameStateManager가 null이어도 게임이 시작되었음을 기억 (입력 허용)

    private GameConnectionInfo? _currentConnectionInfo;
    private string _lobbySceneName = "Lobby";

    private List<PlayerRef> _pendingPlayerSpawns = new List<PlayerRef>();
    private bool _isWaitingForGameStart = false;
    
    // Why: GamePlay 씬 전환 후 맵 초기화를 위해 저장 (NetworkManager는 DontDestroyOnLoad)
    private GameMode _pendingGameMode;
    private int _pendingPlayerCount;
    private bool _hasPendingMapInit = false;

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
            ServiceLocator.Clear(Runner);
        }

        if (_callbacksRunner != null)
        {
            _callbacksRunner.RemoveCallbacks(this);
            _callbacksRunner = null;
        }
    }

    #endregion

    #region Public Methods

    public void SetRunner(NetworkRunner runner)
    {
        bool sameRunner = _callbacksRunner == runner;

        if (_callbacksRunner != null && _callbacksRunner != runner)
        {
            _callbacksRunner.RemoveCallbacks(this);
            _callbacksRunner = null;
        }

        if (Runner != null && Runner != runner)
        {
            _runnerToManagerMap.Remove(Runner);
            ServiceLocator.Clear(Runner);
        }

        Runner = runner;
        _callbacksRunner = runner;
        
        if (runner != null)
        {
            _runnerToManagerMap[runner] = this;
            ServiceLocator.Register(runner, this);

            if (!sameRunner)
            {
                runner.AddCallbacks(this);
                // Why: ObjectProvider는 StartGameArgs에서 설정하므로 여기서는 불필요
            }

            _callbacksRunner = runner;
        }
        else
        {
            _callbacksRunner = null;
        }
    }

    /// <summary>
    /// Lobby에서 이미 연결된 Runner를 전달받아 사용합니다.
    /// </summary>
    public void SetExistingRunner(NetworkRunner runner)
    {
        Debug.Log($"[NetworkManager] SetExistingRunner called - Runner: {runner?.SessionInfo.Name}");

        // Why: 이미 같은 Runner가 설정되어 있으면 중복 스폰 방지
        if (Runner == runner && runner != null)
        {
            Debug.Log("[NetworkManager] Same runner already set, skipping duplicate initialization");
            return;
        }

        SetRunner(runner);
        
        if (runner != null && runner.IsRunning)
        {
            // Why: 연결 정보 저장
            _currentConnectionInfo = new GameConnectionInfo
            {
                SessionName = runner.SessionInfo.Name,
                GameMode = MatchmakingManager.Instance?.CurrentGameMode ?? GameMode.None,
                MaxPlayers = runner.SessionInfo.MaxPlayers
            };

            // Why: 로딩 패널은 GamePlay 씬 전환 시 MatchmakingManager에서 표시됨
            // (SetExistingRunner는 세션 진입 시점이므로 여기서 로딩 패널을 켜지 않음)

            // Why: 클라이언트: 게임 모드 정보 전송
            if (runner.IsClient)
            {
                var gameMode = MatchmakingManager.Instance?.CurrentGameMode ?? GameMode.None;
                var targetPlayers = MatchmakingManager.Instance?.MaxPlayers ?? 4;
                StartCoroutine(WaitForGameStateManagerAndSendGameMode(gameMode, targetPlayers));
            }
            // Why: 서버: 세션 매니저 스폰
            else if (runner.IsServer)
            {
                _ = EnsureSessionManagersAsync();
            }

            Debug.Log("[NetworkManager] SetExistingRunner - 초기화 완료");
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
            if (_callbacksRunner != null)
            {
                _callbacksRunner.RemoveCallbacks(this);
                _callbacksRunner = null;
            }

            _runnerToManagerMap.Remove(Runner);
            await Runner.Shutdown();
            Destroy(Runner);
            Runner = null;
        }

        Runner = gameObject.AddComponent<NetworkRunner>();
        SetRunner(Runner);
        Runner.ProvideInput = true;

        var result = await Runner.StartGame(new StartGameArgs()
        {
            GameMode = Fusion.GameMode.Server,
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

        if (result.Ok && Runner.IsServer)
        {
            await SpawnGameObjectsAsync(Runner);
        }

        return result.Ok;
    }

    public async Task<bool> JoinGameServer(GameConnectionInfo connectionInfo)
    {
        _currentConnectionInfo = connectionInfo;

        if (Runner != null)
        {
            if (_callbacksRunner != null)
            {
                _callbacksRunner.RemoveCallbacks(this);
                _callbacksRunner = null;
            }

            _runnerToManagerMap.Remove(Runner);
            await Runner.Shutdown();
            Destroy(Runner);
            Runner = null;
        }

        Runner = gameObject.AddComponent<NetworkRunner>();
        SetRunner(Runner);
        Runner.ProvideInput = true;

        // Why: SceneManager를 설정하지 않으면 씬 동기화가 일어나지 않음
        // 클라이언트는 LobbyScene에 머물면서 서버의 NetworkObject만 동기화
        var startArgs = new StartGameArgs()
        {
            GameMode = Fusion.GameMode.Client,
            SessionName = connectionInfo.SessionName,
            // ObjectProvider 제거 - Fusion 기본 방식 사용 (직접 파괴)
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
            ServiceLocator.Clear(Runner);
            await Runner.Shutdown();
        }

        if (_callbacksRunner != null)
        {
            _callbacksRunner.RemoveCallbacks(this);
            _callbacksRunner = null;
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
        ServiceLocator.ClearAll();

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
            if (runner.GameMode == Fusion.GameMode.Server && player == runner.LocalPlayer)
            {
                return;
            }

            // Why: 실제 클라이언트가 조인했으므로 ServerLauncher가 이를 감지하고(Update 루프) 자동으로 새 프로세스를 띄웁니다.
            // ServerLauncher.Instance?.OnPlayerJoinedAnySession(); // 제거됨: Multi-Process 구조에서는 ServerLauncher가 스스로 체크함

            if (_spawnedPlayers.Count == 0 && _pendingPlayerSpawns.Count == 0)
            {
                _ = SpawnGameObjectsAsync(runner);
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
        Debug.Log($"[NetworkManager] OnPlayerLeft - Player {player.PlayerId} | IsServer: {runner.IsServer}");
        
        // Debug: 현재 _spawnedPlayers 상태 출력
        Debug.Log($"[NetworkManager] Current _spawnedPlayers count: {_spawnedPlayers.Count}");
        foreach (var kvp in _spawnedPlayers)
        {
            var netObj = kvp.Value;
            string objName = netObj != null ? netObj.name : "NULL";
            int inputAuth = netObj != null && netObj.InputAuthority != PlayerRef.None ? netObj.InputAuthority.PlayerId : -1;
            Debug.Log($"[NetworkManager] - PlayerRef {kvp.Key.PlayerId} -> {objName} (InputAuth: {inputAuth})");
        }

        // 1. 이미 스폰된 플레이어인 경우
        if (_spawnedPlayers.TryGetValue(player, out NetworkObject networkObject))
        {
            Debug.Log($"[NetworkManager] Found player in _spawnedPlayers - NetworkObject: {networkObject?.name}, InputAuthority: {networkObject?.InputAuthority.PlayerId}");
            
            bool wasAlive = true;
            if (networkObject != null && networkObject.TryGetComponent<PlayerCombat>(out var combat))
            {
                wasAlive = combat.IsAlive;
            }

            if (runner.IsServer && _gameStateManager != null)
            {
                _gameStateManager.OnPlayerLeft(wasAlive);
            }

            _spawnedPlayers.Remove(player);
            
            // Why: Runner.Despawn이 시뮬레이션을 멈추는 버그가 있음
            // 대신 GameObject를 비활성화하여 시뮬레이션에서 제외
            // 비활성화된 오브젝트는 세션 종료 시 자동으로 정리됨
            if (networkObject != null && runner.IsServer)
            {
                Debug.Log($"[NetworkManager] Deactivating player object: {networkObject.name}");
                networkObject.gameObject.SetActive(false);
                _deactivatedPlayers.Add(networkObject);
            }
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

    // Why: 비활성화된 플레이어 오브젝트 목록 - 세션 종료 시 정리
    private readonly List<NetworkObject> _deactivatedPlayers = new();

    #endregion

    #region INetworkRunnerCallbacks - Input

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // Why: 게임이 시작되지 않았다면 입력 차단 (매칭/대기 상태)
        if (!_isGameStarted)
        {
            var gs = _gameStateManager ?? GameStateManager.Instance;
            if (gs != null && gs.IsGameStarted)
            {
                _isGameStarted = true;
            }
            else
            {
                return;
            }
        }

        if (_cachedInputHandler != null)
        {
            if (_cachedInputHandler == null || _cachedInputHandler.gameObject == null)
            {
                _cachedInputHandler = null;
            }
            else
            {
                var netObj = _cachedInputHandler.GetComponent<NetworkObject>();
                if (netObj == null || !netObj.HasInputAuthority)
                {
                    _cachedInputHandler = null;
                }
                else
                {
                    var combat = _cachedInputHandler.GetComponent<PlayerCombat>();
                    if (combat != null && !combat.IsAlive) return;

                    input.Set(_cachedInputHandler.GetCurrentInput());
                    return;
                }
            }
        }

        var allInputHandlers = FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None);
        
        foreach (var inputHandler in allInputHandlers)
        {
            if (inputHandler == null || inputHandler.gameObject == null) continue;
            
            NetworkObject netObj = inputHandler.GetComponent<NetworkObject>();
            if (netObj != null && netObj.HasInputAuthority)
            {
                var combat = inputHandler.GetComponent<PlayerCombat>();
                if (combat != null && !combat.IsAlive) return;

                _cachedInputHandler = inputHandler;
                input.Set(inputHandler.GetCurrentInput());
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
            GameMode gameMode = _currentConnectionInfo.Value.GameMode;
            int targetPlayers = _currentConnectionInfo.Value.MaxPlayers;

            Debug.Log($"[NetworkManager] Connected to server. Waiting for GameStateManager to send game mode info - GameMode: {gameMode}, TargetPlayers: {targetPlayers}");

            // Why: GameStateManager가 동기화될 때까지 대기 후 RPC 호출
            StartCoroutine(WaitForGameStateManagerAndSendGameMode(gameMode, targetPlayers));
        }
    }

    private IEnumerator WaitForGameStateManagerAndSendGameMode(GameMode gameMode, int targetPlayers)
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
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        Debug.Log($"[NetworkManager] OnSceneLoadDone - Scene: {sceneName}, IsServer: {runner.IsServer}");
        
        // 서버 전용: 매니저 스폰 및 맵 초기화
        if (!runner.IsServer) return;
        
        Debug.Log($"[NetworkManager] OnSceneLoadDone (Server) - HasPendingMapInit: {_hasPendingMapInit}");
        
        // Why: 씬 로드 완료 시 매니저들 생성 후 맵 초기화 실행
        _ = EnsureSessionManagersAndInitMapAsync();
    }
    
    /// <summary>
    /// 게임 모드 정보를 저장합니다 (GameStateManager에서 호출).
    /// </summary>
    public void SetPendingMapInit(GameMode gameMode, int playerCount)
    {
        _pendingGameMode = gameMode;
        _pendingPlayerCount = playerCount;
        _hasPendingMapInit = true;
        Debug.Log($"[NetworkManager] Pending map init set: Mode={gameMode}, Players={playerCount}");
    }
    
    /// <summary>
    /// 저장된 게임 모드 정보로 맵을 초기화합니다.
    /// </summary>
    private void InitializePendingMap()
    {
        if (!_hasPendingMapInit)
        {
            Debug.Log("[NetworkManager] InitializePendingMap - No pending map init");
            return;
        }
        
        if (_networkMapManager != null && !_networkMapManager.IsReady())
        {
            Debug.Log($"[NetworkManager] Initializing map: Mode={_pendingGameMode}, Players={_pendingPlayerCount}");
            _networkMapManager.InitializeMap(_pendingGameMode, _pendingPlayerCount);
            _hasPendingMapInit = false;
        }
        else
        {
            Debug.LogWarning($"[NetworkManager] Cannot init map - MapManager null: {_networkMapManager == null}, Ready: {_networkMapManager?.IsReady()}");
        }
    }

    /// <summary>
    /// 서버에서 필요한 세션 매니저(게임 상태, 맵)를 보장 생성합니다.
    /// </summary>
    public void EnsureSessionManagers()
    {
        _ = EnsureSessionManagersAsync();
    }
    
    /// <summary>
    /// 매니저 생성 후 맵 초기화까지 수행하는 통합 메서드
    /// </summary>
    private async Task EnsureSessionManagersAndInitMapAsync()
    {
        if (Runner == null || !Runner.IsServer) return;
        
        await SpawnGameObjectsAsync(Runner);
        
        // Why: 매니저 생성 완료 후 대기 중인 맵 초기화 실행
        if (_hasPendingMapInit)
        {
            Debug.Log("[NetworkManager] Managers spawned, now initializing pending map...");
            InitializePendingMap();
        }
    }

    public async Task EnsureSessionManagersAsync()
    {
        if (Runner == null || !Runner.IsServer) return;
        Debug.Log($"[NetworkManager] EnsureSessionManagers called | RunnerValid: {Runner != null}, IsServer: {Runner.IsServer}");
        await SpawnGameObjectsAsync(Runner);
    }

    private async Task SpawnGameObjectsAsync(NetworkRunner runner)
    {
        if (!runner.IsServer) return;
        
        // Why: 이미 스폰 진행 중이면 중복 스폰 방지
        if (_spawningManagersInProgress)
        {
            Debug.LogWarning("[NetworkManager] SpawnGameObjects already in progress, skipping duplicate call");
            return;
        }
        
        // Why: 이미 스폰 완료되었으면 스킵
        if (_gameStateManager != null && _networkMapManager != null)
        {
            Debug.Log("[NetworkManager] Managers already spawned, skipping");
            return;
        }
        
        _spawningManagersInProgress = true;
        Debug.Log($"[NetworkManager] SpawnGameObjects - Runner: {runner.name}, Scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        
        try
        {
            // GameStateManager 스폰
            // Why: _gameStateManagerSpawned 플래그만 믿지 말고 실제 객체가 유효한지 확인 (씬 전환 시 파괴되었을 수 있음)
            if (_gameStateManager == null)
            {
                if (_gameStateManagerPrefab.IsValid)
                {
                    try 
                    {
                        NetworkObject gameStateManagerObj = await runner.SpawnAsync(_gameStateManagerPrefab, Vector3.zero, Quaternion.identity, null, null);
                        if (gameStateManagerObj != null)
                        {
                            // Why: NetworkObject는 Runner에 종속되므로 DontDestroyOnLoad 불필요
                            
                            _gameStateManagerSpawned = true;
                            _gameStateManager = gameStateManagerObj.GetComponent<GameStateManager>();
                            Debug.Log($"[NetworkManager] GameStateManager spawned. ObjectValid: {gameStateManagerObj != null}, InstanceNull: {_gameStateManager == null}");

                            // Why: TargetPlayerCount는 GameStateManager가 Session Properties에서 직접 복원함
                            if (_gameStateManager != null)
                            {
                                Debug.Log("[NetworkManager] GameStateManager spawned. State will be restored from Session Properties.");
                                ServiceLocator.Register(runner, _gameStateManager);
                            }
                        }
                    }
                    catch (System.Exception ex) { Debug.LogError($"[NetworkManager] Failed to spawn GameStateManager: {ex.Message}"); }
                }
                else
                {
                    Debug.LogError("[NetworkManager] _gameStateManagerPrefab is not assigned. GameStateManager will not spawn.");
                }
            }

            // NetworkMapManager 스폰
            if (_networkMapManager == null)
            {
                if (_mapManagerPrefab.IsValid)
                {
                    try
                    {
                        NetworkObject mapManagerObj = await runner.SpawnAsync(_mapManagerPrefab, Vector3.zero, Quaternion.identity, null, null);
                        if (mapManagerObj != null)
                        {
                            // Why: NetworkObject는 Runner에 종속되므로 DontDestroyOnLoad 불필요

                            _mapManagerSpawned = true;
                            _networkMapManager = mapManagerObj.GetComponent<NetworkMapManager>();
                            Debug.Log($"[NetworkManager] NetworkMapManager spawned. InstanceNull: {_networkMapManager == null}");

                            // Why: 맵 초기화는 RPC를 통해 게임 모드 정보를 받은 후에 수행
                            if (_networkMapManager != null)
                            {
                                Debug.Log("[NetworkManager] NetworkMapManager spawned. Will initialize after receiving game mode info via RPC...");
                                ServiceLocator.Register(runner, _networkMapManager);
                            }
                        }
                    }
                    catch (System.Exception) { }
                }
                else
                {
                    Debug.LogError("[NetworkManager] _mapManagerPrefab is not assigned. NetworkMapManager will not spawn.");
                }
            }
        }
        finally
        {
            _spawningManagersInProgress = false;
        }
    }

    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    #endregion
}
