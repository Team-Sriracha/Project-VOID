using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using ProjectVoid.Network;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Fishnet 네트워크 연결 및 플레이어 스폰 관리
/// 서버/클라이언트 모드에서 동작
/// </summary>
public class NetworkManager : MonoBehaviour
{
    #region Singleton
    
    public static NetworkManager Instance { get; private set; }
    
    #endregion

    #region Serialized Fields

    [Header("플레이어 설정")]
    [SerializeField] private GameObject _playerPrefab;

    [Header("맵 설정")]
    [SerializeField] private GameObject _mapManagerPrefab;

    [Header("게임 상태 관리")]
    [SerializeField] private GameObject _gameStateManagerPrefab;

    #endregion

    #region Properties

    /// <summary>
    /// Fishnet NetworkManager 참조
    /// </summary>
    public FishNet.Managing.NetworkManager FishnetManager => InstanceFinder.NetworkManager;
    
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
    
    public bool HasSpawnedMapManager => _mapManagerSpawned && _networkMapManager != null;
    
    /// <summary>
    /// 서버인지 확인
    /// </summary>
    public bool IsServer => InstanceFinder.IsServerStarted;
    
    /// <summary>
    /// 클라이언트인지 확인
    /// </summary>
    public bool IsClient => InstanceFinder.IsClientStarted;
    
    /// <summary>
    /// 호스트인지 확인 (서버 + 클라이언트)
    /// </summary>
    public bool IsHost => InstanceFinder.IsHostStarted;

    /// <summary>
    /// 오프라인 모드인지 확인 (연습장 모드)
    /// </summary>
    public bool IsOfflineMode => _currentConnectionInfo?.GameMode == GameMode.PracticeRange;

    #endregion

    #region Private Fields

    private Dictionary<NetworkConnection, NetworkObject> _spawnedPlayers = new Dictionary<NetworkConnection, NetworkObject>();
    
    // 매니저 참조 저장
    private GameStateManager _gameStateManager;
    private NetworkMapManager _networkMapManager;
    
    private bool _mapManagerSpawned = false;
    private bool _isGameStarted = false;

    private GameConnectionInfo? _currentConnectionInfo;
    private string _lobbySceneName = "Lobby";

    private List<NetworkConnection> _pendingPlayerSpawns = new List<NetworkConnection>();
    private bool _isWaitingForGameStart = false;
    
    // 맵 초기화 대기용
    private GameMode _pendingGameMode;
    private int _pendingPlayerCount;
    private bool _hasPendingMapInit = false;
    
    // 비활성화된 플레이어 오브젝트 목록
    private readonly List<NetworkObject> _deactivatedPlayers = new();

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        EnsureSingleton();
    }

    /// <summary>
    /// 싱글톤 패턴 보장 및 중복 제거
    /// </summary>
    public void EnsureSingleton()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[NetworkManager] Duplicate instance detected on {gameObject.name}. Destroying this instance.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }



    private void OnEnable()
    {
        // FishNet NetworkManager가 아직 초기화되지 않았으면 대기
        if (InstanceFinder.NetworkManager == null)
        {
            StartCoroutine(WaitForFishNetAndSubscribe());
            return;
        }
        
        SubscribeToFishNetEvents();
    }
    
    private IEnumerator WaitForFishNetAndSubscribe()
    {
        float timeout = 5f;
        float elapsed = 0f;
        
        while (InstanceFinder.NetworkManager == null && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }
        
        if (InstanceFinder.NetworkManager != null)
        {
            SubscribeToFishNetEvents();
        }
        else
        {
            Debug.LogWarning("[NetworkManager] FishNet NetworkManager not found after timeout. Make sure FishNet NetworkManager prefab is in the scene.");
        }
    }
    
    private void SubscribeToFishNetEvents()
    {
        if (InstanceFinder.ServerManager != null)
        {
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            InstanceFinder.ServerManager.OnServerConnectionState += OnServerConnectionState;
        }
        
        if (InstanceFinder.ClientManager != null)
        {
            InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionState;
        }
        
        if (InstanceFinder.SceneManager != null)
        {
            InstanceFinder.SceneManager.OnLoadEnd += OnSceneLoadEnd;
        }
    }

    private void OnDisable()
    {
        UnsubscribeFromFishNetEvents();
    }
    
    private void UnsubscribeFromFishNetEvents()
    {
        if (InstanceFinder.NetworkManager == null) return;
        
        if (InstanceFinder.ServerManager != null)
        {
            InstanceFinder.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            InstanceFinder.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }
        
        if (InstanceFinder.ClientManager != null)
        {
            InstanceFinder.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        }
        
        if (InstanceFinder.SceneManager != null)
        {
            InstanceFinder.SceneManager.OnLoadEnd -= OnSceneLoadEnd;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    #endregion

    #region Connection Methods

    /// <summary>
    /// 서버 시작
    /// </summary>
    public bool StartServer()
    {
        if (InstanceFinder.ServerManager == null) return false;
        
        return InstanceFinder.ServerManager.StartConnection();
    }

    /// <summary>
    /// 클라이언트로 서버에 연결
    /// </summary>
    public bool StartClient(string address = "localhost")
    {
        if (InstanceFinder.ClientManager == null) return false;
        
        InstanceFinder.ClientManager.StartConnection(address);
        return true;
    }

    /// <summary>
    /// 호스트로 시작 (서버 + 클라이언트)
    /// </summary>
    public bool StartHost()
    {
        if (!StartServer()) return false;
        return StartClient("localhost");
    }

    /// <summary>
    /// 연결 종료
    /// </summary>
    public void StopConnection()
    {
        if (InstanceFinder.ServerManager != null && InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.StopConnection(true);
        }
        
        if (InstanceFinder.ClientManager != null && InstanceFinder.IsClientStarted)
        {
            InstanceFinder.ClientManager.StopConnection();
        }
    }

    /// <summary>
    /// <summary>
    /// 게임 서버 시작
    /// </summary>

    /// </summary>
    public async Task<bool> StartGameServer(GameConnectionInfo connectionInfo)
    {
        _currentConnectionInfo = connectionInfo;
        
        // 현재 연결된 세션이 있다면 종료

        StopConnection();
        await Task.Delay(100); // 종료 대기
        
        // 연습장 모드는 Host 모드 (Server + Client)
        bool success;
        if (connectionInfo.GameMode == GameMode.PracticeRange)
        {
            Debug.Log("[NetworkManager] Starting Host mode for Practice Range...");
            success = StartHost();
        }
        else
        {
            // 일반 모드는 Server만 시작
            success = StartServer();
        }
        
        if (success && IsServer)
        {
            await SpawnGameObjectsAsync();
        }
        
        return success;
    }

    /// <summary>
    /// <summary>
    /// 게임 서버 참가
    /// </summary>

    /// </summary>
    public async Task<bool> JoinGameServer(GameConnectionInfo connectionInfo)
    {
        _currentConnectionInfo = connectionInfo;
        
        // 현재 연결된 세션이 있다면 종료

        StopConnection();
        await Task.Delay(100);
        
        // 서버 주소로 연결
        string address = connectionInfo.ServerInfo.ServerAddress ?? "localhost";
        bool success = StartClient(address);
        
        return success;
    }

    /// <summary>
    /// 로비로 돌아가기
    /// </summary>
    public async Task ReturnToLobby()
    {
        StopConnection();
        
        _spawnedPlayers.Clear();
        _pendingPlayerSpawns.Clear();
        _mapManagerSpawned = false;
        _gameStateManager = null;
        _networkMapManager = null;
        _currentConnectionInfo = null;
        _isWaitingForGameStart = false;
        _isGameStarted = false;
        
        await Task.Delay(100);
        
        UnityEngine.SceneManagement.SceneManager.LoadScene(_lobbySceneName);
    }

    #endregion

    #region Player Management

    public IReadOnlyDictionary<NetworkConnection, NetworkObject> GetSpawnedPlayers()
    {
        return _spawnedPlayers;
    }

    public NetworkObject GetPlayerNetworkObject(NetworkConnection conn)
    {
        _spawnedPlayers.TryGetValue(conn, out NetworkObject networkObject);
        return networkObject;
    }

    /// <summary>
    /// 클라이언트 연결 상태 변경 (서버에서 호출)
    /// </summary>
    private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Started)
        {
            OnPlayerJoined(conn);
        }
        else if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            OnPlayerLeft(conn);
        }
    }

    private void OnPlayerJoined(NetworkConnection conn)
    {
        if (!IsServer) return;
        
        Debug.Log($"[NetworkManager] Player joined - ClientId: {conn.ClientId}");
        
        // GameStateManager에 플레이어 조인 알림 (인원 카운트)
        if (_gameStateManager != null)
        {
            _gameStateManager.OnPlayerJoined();
        }
        else
        {
            Debug.LogWarning("[NetworkManager] GameStateManager not found when player joined!");
        }
        
        // 대기 목록에 추가 (맵 완료 후 스폰됨)
        if (!_pendingPlayerSpawns.Contains(conn))
        {
            _pendingPlayerSpawns.Add(conn);
            Debug.Log($"[NetworkManager] Player {conn.ClientId} added to pending spawns. Total pending: {_pendingPlayerSpawns.Count}");
        }
        
        // NetworkMapManager가 존재하면 즉시 스폰 시도, 없다면 맵 로드 후 스폰 대기

        TrySpawnPendingPlayers();
    }

    private void OnPlayerLeft(NetworkConnection conn)
    {
        Debug.Log($"[NetworkManager] OnPlayerLeft - ClientId: {conn.ClientId}");
        
        if (_spawnedPlayers.TryGetValue(conn, out NetworkObject networkObject))
        {
            bool wasAlive = true;
            if (networkObject != null && networkObject.TryGetComponent<PlayerCombat>(out var combat))
            {
                wasAlive = combat.IsAlive;
            }

            if (IsServer && _gameStateManager != null)
            {
                _gameStateManager.OnPlayerLeft(wasAlive);
            }

            _spawnedPlayers.Remove(conn);
            
            // 서버에서 오브젝트 디스폰
            if (networkObject != null && IsServer)
            {
                Debug.Log($"[NetworkManager] Despawning player object: {networkObject.name}");
                InstanceFinder.ServerManager.Despawn(networkObject);
            }
        }
        else if (_pendingPlayerSpawns.Contains(conn))
        {
            _pendingPlayerSpawns.Remove(conn);
            Debug.Log($"[NetworkManager] Pending player left - removed from pending list. Remaining pending: {_pendingPlayerSpawns.Count}");

            if (IsServer && _gameStateManager != null)
            {
                _gameStateManager.OnPlayerLeft(false);
            }
        }
    }

    private IEnumerator WaitForGameStartAndSpawnPlayers()
    {
        float waitTime = 0f;
        float maxWaitTime = 120f;
        float checkInterval = 0.5f;

        Debug.Log($"[NetworkManager] Waiting for map and game start... (Pending players: {_pendingPlayerSpawns.Count})");

        bool mapReady = false;

        while (waitTime < maxWaitTime)
        {
            mapReady = _networkMapManager != null && _networkMapManager.IsReady();

            if (Mathf.Approximately(waitTime % 5f, 0f))
            {
                bool gameStarted = _gameStateManager != null && _gameStateManager.IsGameStarted;
                Debug.Log($"[NetworkManager] Wait status - MapReady: {mapReady}, GameStarted: {gameStarted}, Elapsed: {waitTime}s");
            }

            if (mapReady)
            {
                Debug.Log("[NetworkManager] Map ready! Spawning all pending players...");
                break;
            }

            waitTime += checkInterval;
            yield return new WaitForSeconds(checkInterval);
        }

        if (!mapReady)
        {
            Debug.LogError($"[NetworkManager] CRITICAL: Map not ready after {maxWaitTime}s! Cannot spawn players!");
            _isWaitingForGameStart = false;
            yield break;
        }

        Debug.Log($"[NetworkManager] Spawning {_pendingPlayerSpawns.Count} pending players...");
        foreach (var pendingConn in _pendingPlayerSpawns.ToArray())
        {
            SpawnPlayer(pendingConn);
        }
        _pendingPlayerSpawns.Clear();

        _isWaitingForGameStart = false;
    }

    private void SpawnPlayer(NetworkConnection conn)
    {
        if (_spawnedPlayers.ContainsKey(conn)) return;
        if (_playerPrefab == null)
        {
            Debug.LogError("[NetworkManager] Player prefab is not assigned!");
            return;
        }

        Vector3 spawnPosition = GetSpawnPositionForPlayer(conn);
        Quaternion spawnRotation = GetSpawnRotationForPlayer(conn);

        // Fishnet 스폰 - Instantiate 후 Spawn
        GameObject playerInstance = Instantiate(_playerPrefab, spawnPosition, spawnRotation);
        NetworkObject networkPlayerObject = playerInstance.GetComponent<NetworkObject>();
        InstanceFinder.ServerManager.Spawn(networkPlayerObject, conn);

        _spawnedPlayers.Add(conn, networkPlayerObject);

        if (IsServer && _gameStateManager != null)
        {
            _gameStateManager.OnPlayerSpawnedAlive();
        }

        Debug.Log($"[NetworkManager] Player {conn.ClientId} spawned at {spawnPosition}. Total spawned: {_spawnedPlayers.Count}");
    }

    private Vector3 GetSpawnPositionForPlayer(NetworkConnection conn)
    {
        if (_networkMapManager == null || !_networkMapManager.IsReady())
        {
            Debug.LogError($"[NetworkManager] CRITICAL: Map not ready! Returning zero position.");
            return Vector3.zero;
        }

        int playerIndex = _spawnedPlayers.Count;
        Vector3 spawnPos = _networkMapManager.GetPlayerSpawnPosition(playerIndex);
        Debug.Log($"[NetworkManager] Player {conn.ClientId} (index {playerIndex}) spawn position: {spawnPos}");
        return spawnPos;
    }

    private Quaternion GetSpawnRotationForPlayer(NetworkConnection conn)
    {
        if (_networkMapManager == null || !_networkMapManager.IsReady())
        {
            return Quaternion.identity;
        }

        int playerIndex = _spawnedPlayers.Count;
        float rotation = _networkMapManager.GetPlayerSpawnRotation(playerIndex);
        return Quaternion.Euler(0, rotation, 0);
    }

    #endregion

    #region Server/Client State Callbacks

    private void OnServerConnectionState(ServerConnectionStateArgs args)
    {
        Debug.Log($"[NetworkManager] Server connection state: {args.ConnectionState}");
        
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            Debug.Log("[NetworkManager] Server started successfully");
            
            // 서버 시작 시 GameStateManager만 스폰 (인원 대기용)
            // NetworkMapManager는 GamePlay 씬 진입 시 스폰됨
            _ = SpawnGameStateManagerAsync();
            
            // Unity Lobby에 서버 등록 (옵션)
            _ = CreateLobbyForServer();
        }
        else if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            Debug.Log("[NetworkManager] Server stopped");
            
            // Unity Lobby에서 서버 제거
            _ = RemoveLobbyForServer();
        }
    }

    #region Unity Session Management

    /// <summary>
    /// 서버 시작 시 Unity Session 생성 (IP/Port 저장)
    /// </summary>
    private async Task CreateLobbyForServer()
    {
        // 연습장 모드는 Unity Sessions 등록 스킵 (완전 오프라인)
        if (_currentConnectionInfo.HasValue && _currentConnectionInfo.Value.GameMode == GameMode.PracticeRange)
        {
            Debug.Log("[NetworkManager] Practice mode detected. Skipping Unity Sessions registration.");
            return;
        }

        if (UnityLobbyManager.Instance == null)
        {
            Debug.LogWarning("[NetworkManager] UnityLobbyManager not found. Session will not be created.");
            return;
        }

        try
        {
            // 서버 IP와 포트 가져오기
            string serverIp = GetServerIp();
            int serverPort = GetServerPort();
            
            // 세션 이름 생성
            string sessionName = $"Game_{System.DateTime.Now:yyyyMMdd_HHmmss}";
            
            // 방 코드 (Unity Sessions가 session.Code로 자동 생성)
            string roomCode = _currentConnectionInfo?.RoomCode ?? "";
            
            // 게임 모드 - 서버는 대기 상태로 빈 값("")으로 생성
            // 첫 번째 클라이언트가 연결할 때 GameMode가 설정됨
            string gameMode = "";
            
            // 최대 플레이어 수 - 서버는 최대 인원으로 대기
            int maxPlayers = 8;

            // 세션 생성 (IP/Port 저장, Relay 없음)
            var session = await UnityLobbyManager.Instance.CreateSession(
                sessionName, maxPlayers, serverIp, serverPort, gameMode, roomCode);

            if (session != null)
            {
                Debug.Log($"[NetworkManager] Session created: {session.SessionName} (IP: {session.ServerIp}:{session.ServerPort}, RoomCode: {session.RoomCode})");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] Failed to create session: {ex.Message}");
        }
    }

    /// <summary>
    /// 서버 종료 시 Unity Session 삭제
    /// </summary>
    private async Task RemoveLobbyForServer()
    {
        if (UnityLobbyManager.Instance != null && UnityLobbyManager.Instance.IsInSession)
        {
            await UnityLobbyManager.Instance.LeaveSession();
            Debug.Log("[NetworkManager] Session removed");
        }
    }

    private string GetServerIp()
    {
        // 1. 공인 IP 명시적 지정 (--public-ip=...)
        string[] args = System.Environment.GetCommandLineArgs();
        foreach (string arg in args)
        {
            if (arg.StartsWith("--public-ip="))
            {
                string ip = arg.Substring("--public-ip=".Length);
                Debug.Log($"[NetworkManager] Using Public IP from args: {ip}");
                return ip;
            }
        }

        // 2. 바인딩 IP 사용 (--ip=...)
        foreach (string arg in args)
        {
            if (arg.StartsWith("--ip="))
            {
                string ip = arg.Substring("--ip=".Length);
                
                // 0.0.0.0은 접속 가능한 IP가 아니므로 로컬호스트로 대체
                if (ip == "0.0.0.0")
                {
                    Debug.LogWarning("[NetworkManager] Bind IP is 0.0.0.0. Advertising 127.0.0.1 for Lobby.");
                    return "127.0.0.1";
                }
                
                Debug.Log($"[NetworkManager] Using Bind IP from args: {ip}");
                return ip;
            }
        }
        
        // 기본값
        return "127.0.0.1";
    }

    private int GetServerPort()
    {
        // Tugboat 기본 포트
        if (InstanceFinder.NetworkManager?.TransportManager?.Transport != null)
        {
            var tugboat = InstanceFinder.NetworkManager.TransportManager.Transport as FishNet.Transporting.Tugboat.Tugboat;
            if (tugboat != null)
            {
                return tugboat.GetPort();
            }
        }
        return 7777;
    }

    #endregion

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        Debug.Log($"[NetworkManager] Client connection state: {args.ConnectionState}");
        
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            Debug.Log("[NetworkManager] Connected to server");
            
            // 클라이언트: 게임 모드 정보 전송
            if (_currentConnectionInfo.HasValue)
            {
                GameMode gameMode = _currentConnectionInfo.Value.GameMode;
                int targetPlayers = _currentConnectionInfo.Value.MaxPlayers;
                StartCoroutine(WaitForGameStateManagerAndSendGameMode(gameMode, targetPlayers));
            }
        }
        else if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            Debug.Log("[NetworkManager] Disconnected from server");
        }
    }

    private IEnumerator WaitForGameStateManagerAndSendGameMode(GameMode gameMode, int targetPlayers)
    {
        float waitTime = 0f;
        float maxWaitTime = 10f;
        float checkInterval = 0.1f;

        while (waitTime < maxWaitTime)
        {
            if (GameStateManager.Instance != null)
            {
                Debug.Log($"[NetworkManager] GameStateManager found. Sending game mode info - GameMode: {gameMode}, TargetPlayers: {targetPlayers}");
                GameStateManager.Instance.RPC_SetGameModeInfo((int)gameMode, targetPlayers);
                yield break;
            }

            waitTime += checkInterval;
            yield return new WaitForSeconds(checkInterval);
        }

        Debug.LogWarning("[NetworkManager] Timeout waiting for GameStateManager!");
    }

    #endregion

    #region Scene Management

    private void OnSceneLoadEnd(SceneLoadEndEventArgs args)
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        Debug.Log($"[NetworkManager] OnSceneLoadEnd - Scene: {sceneName}, IsServer: {IsServer}");
        
        if (!IsServer) return;
        
        // GamePlay 씬에서만 NetworkMapManager 스폰 및 맵 생성
        if (sceneName == "GamePlay")
        {
            Debug.Log("[NetworkManager] GamePlay scene loaded - Spawning NetworkMapManager and initializing map");
            _ = SpawnNetworkMapManagerAndInitAsync();
        }
    }

    public void SetPendingMapInit(GameMode gameMode, int playerCount)
    {
        _pendingGameMode = gameMode;
        _pendingPlayerCount = playerCount;
        _hasPendingMapInit = true;
        Debug.Log($"[NetworkManager] Pending map init set: Mode={gameMode}, Players={playerCount}");
        
        // 즉시 맵 초기화 시도 (NetworkMapManager가 이미 스폰되어 있으면 바로 생성)
        InitializePendingMap();
    }

    private void InitializePendingMap()
    {
        if (!_hasPendingMapInit) return;
        
        if (_networkMapManager != null && !_networkMapManager.IsReady())
        {
            Debug.Log($"[NetworkManager] Initializing map: Mode={_pendingGameMode}, Players={_pendingPlayerCount}");
            _networkMapManager.InitializeMap(_pendingGameMode, _pendingPlayerCount);
            _hasPendingMapInit = false;
        }
    }

    #endregion

    #region Manager Spawning

    public void EnsureSessionManagers()
    {
        _ = EnsureSessionManagersAsync();
    }

    private async Task EnsureSessionManagersAndInitMapAsync()
    {
        if (!IsServer) return;
        
        await SpawnGameObjectsAsync();
        
        if (_hasPendingMapInit)
        {
            Debug.Log("[NetworkManager] Managers spawned, now initializing pending map...");
            InitializePendingMap();
        }
    }

    public async Task EnsureSessionManagersAsync()
    {
        if (!IsServer) return;
        await SpawnGameObjectsAsync();
    }

    private async Task SpawnGameObjectsAsync()
    {
        await SpawnGameStateManagerAsync();
        await SpawnNetworkMapManagerAndInitAsync();
    }

    /// <summary>
    /// GameStateManager만 스폰 (서버 시작 시 호출)
    /// </summary>
    private async Task SpawnGameStateManagerAsync()
    {
        if (!IsServer) return;
        
        // Skip spawning in Offline/Practice mode (PracticeModeManager handles it)
        if (IsOfflineMode)
        {
            Debug.Log("[NetworkManager] Info: Skipping GameStateManager spawn in Offline Mode.");
            return;
        }

        if (_gameStateManager != null)
        {
            Debug.Log("[NetworkManager] GameStateManager already spawned, skipping");
            return;
        }
        
        if (_gameStateManagerPrefab == null)
        {
            Debug.LogError("[NetworkManager] GameStateManagerPrefab is not assigned!");
            return;
        }
        
        try
        {
            Debug.Log("[NetworkManager] Spawning GameStateManager...");
            GameObject gameStateManagerGO = Instantiate(_gameStateManagerPrefab, Vector3.zero, Quaternion.identity);
            NetworkObject gameStateManagerObj = gameStateManagerGO.GetComponent<NetworkObject>();
            InstanceFinder.ServerManager.Spawn(gameStateManagerObj);
            
            _gameStateManager = gameStateManagerObj.GetComponent<GameStateManager>();
            Debug.Log("[NetworkManager] GameStateManager spawned successfully.");
            
            await Task.Delay(100);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] Failed to spawn GameStateManager: {ex.Message}");
        }
    }

    /// <summary>
    /// NetworkMapManager 스폰 및 맵 초기화 (GamePlay 씬 진입 시 호출)
    /// </summary>
    private async Task SpawnNetworkMapManagerAndInitAsync()
    {
        if (!IsServer) return;
        
        if (_networkMapManager != null)
        {
            Debug.Log("[NetworkManager] NetworkMapManager already spawned, skipping");
            InitializePendingMap();
            // 맵이 이미 준비되었고 대기 중인 플레이어가 있다면 즉시 스폰을 시작합니다.

            TrySpawnPendingPlayers();
            return;
        }
        
        if (_mapManagerPrefab == null)
        {
            Debug.LogError("[NetworkManager] MapManagerPrefab is not assigned!");
            return;
        }
        
        try
        {
            Debug.Log("[NetworkManager] Spawning NetworkMapManager...");
            GameObject mapManagerGO = Instantiate(_mapManagerPrefab, Vector3.zero, Quaternion.identity);
            NetworkObject mapManagerObj = mapManagerGO.GetComponent<NetworkObject>();
            InstanceFinder.ServerManager.Spawn(mapManagerObj);
            
            _mapManagerSpawned = true;
            _networkMapManager = mapManagerObj.GetComponent<NetworkMapManager>();
            Debug.Log("[NetworkManager] NetworkMapManager spawned successfully.");
            
            await Task.Delay(100);
            
            // 맵 초기화
            InitializePendingMap();
            
            // 대기 중인 플레이어를 스폰합니다.

            TrySpawnPendingPlayers();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] Failed to spawn NetworkMapManager: {ex.Message}");
        }
    }
    
    /// <summary>
    /// pending 플레이어가 있으면 맵 준비 대기 후 스폰
    /// </summary>
    private void TrySpawnPendingPlayers()
    {
        if (_pendingPlayerSpawns.Count > 0 && !_isWaitingForGameStart && _networkMapManager != null)
        {
            if (_networkMapManager.IsMapReady.Value)
            {
                Debug.Log("[NetworkManager] Map is already ready. Spawning pending players immediately.");
                SpawnPendingPlayers();
            }
            else
            {
                Debug.Log($"[NetworkManager] Waiting for MapReady event for {_pendingPlayerSpawns.Count} pending players...");
                _isWaitingForGameStart = true;
                _networkMapManager.IsMapReady.OnChange += OnMapReadyChanged;
            }
        }
    }

    private void OnMapReadyChanged(bool prev, bool next, bool asServer)
    {
        if (next)
        {
            Debug.Log("[NetworkManager] Map Ready event received!");
            _networkMapManager.IsMapReady.OnChange -= OnMapReadyChanged;
            _isWaitingForGameStart = false;
            SpawnPendingPlayers();
        }
    }

    private void SpawnPendingPlayers()
    {
        Debug.Log($"[NetworkManager] Spawning {_pendingPlayerSpawns.Count} pending players.");
        List<NetworkConnection> spawns = new List<NetworkConnection>(_pendingPlayerSpawns);
        _pendingPlayerSpawns.Clear();

        foreach (var conn in spawns)
        {
            SpawnPlayer(conn);
        }
    }

    #endregion

    #region Utility

    public GameConnectionInfo? GetCurrentConnectionInfo()
    {
        return _currentConnectionInfo;
    }

    /// <summary>
    /// 연결 정보 설정 (MatchmakingManager에서 호출)
    /// </summary>
    public void SetConnectionInfo(GameConnectionInfo connectionInfo)
    {
        _currentConnectionInfo = connectionInfo;
        Debug.Log($"[NetworkManager] ConnectionInfo set - GameMode: {connectionInfo.GameMode}, MaxPlayers: {connectionInfo.MaxPlayers}");
    }

    /// <summary>
    /// 게임 시작 상태 설정 (GameStateManager에서 호출)
    /// </summary>
    public void SetGameStarted(bool started)
    {
        _isGameStarted = started;
    }

    #endregion
}
