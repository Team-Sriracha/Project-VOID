using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Server;
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
    #region Types

    /// <summary>
    /// 신원 검증 처리 결과입니다.
    /// </summary>
    public readonly struct IdentityVerificationResult
    {
        public bool IsSuccess { get; }
        public string ErrorMessage { get; }

        public IdentityVerificationResult(bool isSuccess, string errorMessage)
        {
            IsSuccess = isSuccess;
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? string.Empty : errorMessage.Trim();
        }
    }

    #endregion

    #region Constants

    private const string MATCHMAKING_SCENE_NAME = "Matchmaking";
    private const string UNEXPECTED_DISCONNECT_RETURN_MESSAGE = "인터넷 연결 또는 서버 연결이 끊어져 로비로 돌아갑니다.";

    #endregion

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
    public bool IsOfflineMode => _currentConnectionInfo.HasValue &&
                                 GameModeCatalog.IsPracticeMode(_currentConnectionInfo.Value.GameMode);

    #endregion

    #region Private Fields

    private Dictionary<NetworkConnection, NetworkObject> _spawnedPlayers = new Dictionary<NetworkConnection, NetworkObject>();
    private readonly Dictionary<NetworkConnection, PlayerIdentity> _playerIdentities = new();
    
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
    private string _pendingMapTemplateName;
    private bool _hasPendingMapInit = false;
    private bool _ignoreNextClientStopEvent;
    private bool _isHandlingUnexpectedClientDisconnect;
    
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
        _ignoreNextClientStopEvent = _ignoreNextClientStopEvent || InstanceFinder.IsClientStarted;

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
        if (GameModeCatalog.IsPracticeMode(connectionInfo.GameMode))
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
        
        FishNet.Managing.NetworkManager fishnetNetworkManager = InstanceFinder.NetworkManager;
        if (fishnetNetworkManager == null || fishnetNetworkManager.ClientManager == null)
        {
            Debug.LogError("[NetworkManager] FishNet NetworkManager 또는 ClientManager를 찾을 수 없습니다.");
            return false;
        }

        string address = connectionInfo.UseDirectConnection
            ? connectionInfo.DirectServerIP
            : connectionInfo.ServerInfo.ServerAddress;

        if (string.IsNullOrWhiteSpace(address))
        {
            address = "localhost";
        }

        int port = connectionInfo.UseDirectConnection
            ? connectionInfo.DirectServerPort
            : connectionInfo.ServerInfo.Port;

        if (fishnetNetworkManager.TransportManager != null &&
            fishnetNetworkManager.TransportManager.Transport is FishNet.Transporting.Tugboat.Tugboat tugboat)
        {
            tugboat.SetClientAddress(address);

            if (port > 0 && port <= ushort.MaxValue)
            {
                tugboat.SetPort((ushort)port);
            }

            Debug.Log($"[NetworkManager] Tugboat client target set to {tugboat.GetClientAddress()}:{tugboat.GetPort()}");
        }

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
        _playerIdentities.Clear();
        _pendingPlayerSpawns.Clear();
        _mapManagerSpawned = false;
        _gameStateManager = null;
        _networkMapManager = null;
        _currentConnectionInfo = null;
        _isWaitingForGameStart = false;
        _isGameStarted = false;
        _pendingMapTemplateName = string.Empty;
        
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
    /// 서버 검증을 거친 플레이어 신원을 등록합니다.
    /// </summary>
    public void RegisterPlayerIdentity(NetworkConnection conn, PlayerIdentity identity)
    {
        if (conn == null || !conn.IsValid || identity == null)
        {
            return;
        }

        _playerIdentities[conn] = identity;
        ApplyIdentityToPlayerObject(conn, identity);
        Debug.Log($"[NetworkManager] Player identity registered. ClientId={conn.ClientId}, Uid={identity.FirebaseUid}, Guest={identity.IsAnonymous}");
    }

    /// <summary>
    /// 플레이어 신원을 조회합니다.
    /// </summary>
    public bool TryGetPlayerIdentity(NetworkConnection conn, out PlayerIdentity identity)
    {
        if (conn == null)
        {
            identity = null;
            return false;
        }

        return _playerIdentities.TryGetValue(conn, out identity);
    }

    private void ApplyIdentityToPlayerObject(NetworkConnection conn, PlayerIdentity identity)
    {
        if (conn == null || identity == null)
        {
            return;
        }

        if (!_spawnedPlayers.TryGetValue(conn, out NetworkObject playerObject) || playerObject == null)
        {
            return;
        }

        if (!playerObject.TryGetComponent<PlayerIdentityRegistrar>(out PlayerIdentityRegistrar identityRegistrar) ||
            identityRegistrar == null)
        {
            Debug.LogWarning($"[NetworkManager] PlayerIdentityRegistrar를 찾지 못했습니다. ClientId={conn.ClientId}");
            return;
        }

        string displayName = string.IsNullOrWhiteSpace(identity.DisplayName)
            ? string.Empty
            : identity.DisplayName.Trim();
        string guestId = string.IsNullOrWhiteSpace(identity.GuestId)
            ? string.Empty
            : identity.GuestId.Trim();

        identityRegistrar.PlayerDisplayName.Value = displayName;
        identityRegistrar.PlayerGuestId.Value = guestId;

        Debug.Log($"[NetworkManager] Player identity applied to spawned object. ClientId={conn.ClientId}, DisplayName={displayName}");
    }

    /// <summary>
    /// ID Token 검증 후 플레이어 신원을 등록합니다.
    /// </summary>
    public async Task<bool> TryVerifyAndRegisterIdentityAsync(NetworkConnection conn, string idToken)
    {
        IdentityVerificationResult result = await TryVerifyAndRegisterIdentityWithResultAsync(conn, idToken, true);
        return result.IsSuccess;
    }

    /// <summary>
    /// ID Token을 검증하고 플레이어 신원을 등록한 뒤 결과를 반환합니다.
    /// </summary>
    /// <param name="conn">검증할 연결</param>
    /// <param name="idToken">검증할 ID Token</param>
    /// <param name="kickOnFailure">실패 시 연결 강제 종료 여부</param>
    /// <returns>검증 결과</returns>
    public async Task<IdentityVerificationResult> TryVerifyAndRegisterIdentityWithResultAsync(NetworkConnection conn, string idToken, bool kickOnFailure)
    {
        if (conn == null || !conn.IsValid)
        {
            return CreateIdentityVerificationFailureResult(conn, "유효하지 않은 네트워크 연결입니다.", kickOnFailure);
        }

        if (_playerIdentities.TryGetValue(conn, out PlayerIdentity cachedIdentity))
        {
            ApplyIdentityToPlayerObject(conn, cachedIdentity);
            return new IdentityVerificationResult(true, string.Empty);
        }

        if (IsOfflineIdentityAllowedForCurrentMode())
        {
            if (!TryBuildPracticeFallbackIdentity(idToken, out VerifiedIdentity practiceIdentity) ||
                !IsVerifiedIdentityUsable(practiceIdentity))
            {
                return CreateIdentityVerificationFailureResult(conn, "연습장 오프라인 신원 정보를 만들 수 없습니다.", kickOnFailure);
            }

            string practiceDisplayName = await ResolveRegisteredDisplayNameAsync(practiceIdentity);
            string practiceGuestId = practiceIdentity.IsAnonymous
                ? ResolveGuestId(practiceIdentity.GuestId, practiceIdentity.FirebaseUid)
                : string.Empty;

            PlayerIdentity offlineIdentity = new PlayerIdentity(
                practiceIdentity.FirebaseUid,
                practiceDisplayName,
                practiceGuestId,
                practiceIdentity.IsAnonymous,
                DateTime.UtcNow);

            RegisterPlayerIdentity(conn, offlineIdentity);
            return new IdentityVerificationResult(true, string.Empty);
        }

        VerifiedIdentity verified;
        bool tracksIdentityServiceHealth = false;
        try
        {
            IIdentityVerificationService verificationService = ResolveIdentityVerificationService();
            tracksIdentityServiceHealth = verificationService is BackendIdentityVerificationService;
            verified = await verificationService.VerifyIdTokenAsync(idToken);
        }
        catch (Exception ex)
        {
            if (tracksIdentityServiceHealth)
            {
                RefreshIdentityVerificationSessionStatus(false, $"토큰 검증 예외: {ex.Message}");
            }

            return CreateIdentityVerificationFailureResult(conn, $"토큰 검증 예외: {ex.Message}", kickOnFailure);
        }

        if (!IsVerifiedIdentityUsable(verified) &&
            TryBuildPracticeFallbackIdentity(idToken, out VerifiedIdentity practiceFallback))
        {
            Debug.LogWarning($"[NetworkManager] Practice 모드 fallback 신원 검증 사용. ClientId={conn.ClientId}, Uid={practiceFallback.FirebaseUid}");
            verified = practiceFallback;
        }

        if (!IsVerifiedIdentityUsable(verified))
        {
            string reason = verified?.ErrorMessage ?? "토큰 검증 실패";
            if (tracksIdentityServiceHealth &&
                BackendIdentityVerificationService.IsInfrastructureFailureMessage(reason))
            {
                RefreshIdentityVerificationSessionStatus(false, reason);
            }

            return CreateIdentityVerificationFailureResult(conn, reason, kickOnFailure);
        }

        string displayName = await ResolveRegisteredDisplayNameAsync(verified);
        string guestId = verified.IsAnonymous
            ? ResolveGuestId(verified.GuestId, verified.FirebaseUid)
            : string.Empty;

        PlayerIdentity identity = new PlayerIdentity(
            verified.FirebaseUid,
            displayName,
            guestId,
            verified.IsAnonymous,
            DateTime.UtcNow);

        RegisterPlayerIdentity(conn, identity);
        if (tracksIdentityServiceHealth)
        {
            RefreshIdentityVerificationSessionStatus(true, "인증 백엔드 정상");
        }

        return new IdentityVerificationResult(true, string.Empty);
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

        // 같은 연결에 대해 Started 이벤트가 중복으로 들어오는 경우 카운트 드리프트 방지
        bool isAlreadyTracked = _spawnedPlayers.ContainsKey(conn) || _pendingPlayerSpawns.Contains(conn);
        if (isAlreadyTracked)
        {
            Debug.LogWarning($"[NetworkManager] Duplicate join event ignored - ClientId: {conn.ClientId}");
            return;
        }
        
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
        _pendingPlayerSpawns.Add(conn);
        Debug.Log($"[NetworkManager] Player {conn.ClientId} added to pending spawns. Total pending: {_pendingPlayerSpawns.Count}");
        
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

            _spawnedPlayers.Remove(conn);
            _playerIdentities.Remove(conn);

            if (IsServer && _gameStateManager != null)
            {
                _gameStateManager.OnPlayerLeft(wasAlive);
            }
            
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
            _playerIdentities.Remove(conn);
            Debug.Log($"[NetworkManager] Pending player left - removed from pending list. Remaining pending: {_pendingPlayerSpawns.Count}");

            if (IsServer && _gameStateManager != null)
            {
                _gameStateManager.OnPlayerLeft(false);
            }
        }
        else
        {
            _playerIdentities.Remove(conn);
            Debug.LogWarning($"[NetworkManager] Leave event for unknown connection ignored - ClientId: {conn.ClientId}");
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
        if (_playerIdentities.TryGetValue(conn, out PlayerIdentity identity))
        {
            ApplyIdentityToPlayerObject(conn, identity);
        }

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
        // 모니터 모드는 Unity Sessions 등록 스킵 (세션 조회만 수행)
        if (IsMonitorMode())
        {
            Debug.Log("[NetworkManager] Monitor mode detected. Skipping Unity Sessions registration.");
            return;
        }

        // 연습장 모드는 Unity Sessions 등록 스킵 (완전 오프라인)
        if (_currentConnectionInfo.HasValue && GameModeCatalog.IsPracticeMode(_currentConnectionInfo.Value.GameMode))
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
            BackendIdentityVerificationService.ServiceHealthInfo identityHealth =
                await GetIdentityVerificationServiceHealthAsync();

            if (identityHealth.IsReady)
            {
                Debug.Log($"[NetworkManager] 인증 백엔드 준비 완료. Endpoint={identityHealth.Endpoint}");
            }
            else
            {
                Debug.LogWarning($"[NetworkManager] 인증 백엔드 준비 안 됨. Status={identityHealth.StatusMessage}, Endpoint={identityHealth.Endpoint}");
            }

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
            
            // 최대 플레이어 수 - 연결 정보가 있으면 해당 모드 정의 기준으로 설정
            int maxPlayers = 8;
            if (_currentConnectionInfo.HasValue)
            {
                int fallbackMaxPlayers = _currentConnectionInfo.Value.MaxPlayers > 0
                    ? _currentConnectionInfo.Value.MaxPlayers
                    : 8;
                maxPlayers = GameModeCatalog.GetMaxPlayers(_currentConnectionInfo.Value.GameMode, fallbackMaxPlayers);
            }

            // 세션 생성 (IP/Port 저장, Relay 없음)
            var session = await UnityLobbyManager.Instance.CreateSession(
                sessionName,
                maxPlayers,
                serverIp,
                serverPort,
                gameMode,
                identityHealth.IsReady,
                identityHealth.StatusMessage);

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
        // 1. 공인 포트 명시적 지정 (--public-port=...)
        string[] args = System.Environment.GetCommandLineArgs();
        foreach (string arg in args)
        {
            if (arg.StartsWith("--public-port="))
            {
                string portStr = arg.Substring("--public-port=".Length);
                if (int.TryParse(portStr, out int port))
                {
                    Debug.Log($"[NetworkManager] Using Public Port from args: {port}");
                    return port;
                }
            }
        }

        // 2. Tugboat 기본 포트 반환
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

    /// <summary>
    /// 모니터 모드인지 확인 (-monitor 플래그)
    /// </summary>
    private bool IsMonitorMode()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        return System.Array.Exists(args, arg => arg == "-monitor");
    }

    #endregion

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        Debug.Log($"[NetworkManager] Client connection state: {args.ConnectionState}");
        
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            _ignoreNextClientStopEvent = false;
            _isHandlingUnexpectedClientDisconnect = false;
            Debug.Log("[NetworkManager] Connected to server");
            
            // 클라이언트: 게임 모드 정보 전송
            if (_currentConnectionInfo.HasValue)
            {
                GameMode gameMode = _currentConnectionInfo.Value.GameMode;
                int targetPlayers = _currentConnectionInfo.Value.MaxPlayers;
                CustomRoomSettings customSettings = _currentConnectionInfo.Value.CustomSettings;
                StartCoroutine(WaitForGameStateManagerAndSendGameMode(gameMode, targetPlayers, customSettings));
            }
        }
        else if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            if (_ignoreNextClientStopEvent)
            {
                _ignoreNextClientStopEvent = false;
                Debug.Log("[NetworkManager] Intentional disconnect acknowledged");
                return;
            }

            Debug.Log("[NetworkManager] Disconnected from server");

            if (!IsOfflineMode)
            {
                HandleUnexpectedClientDisconnect();
            }
        }
    }

    private async void HandleUnexpectedClientDisconnect()
    {
        if (_isHandlingUnexpectedClientDisconnect)
        {
            return;
        }

        string activeSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (string.Equals(activeSceneName, _lobbySceneName, StringComparison.Ordinal) ||
            string.Equals(activeSceneName, MATCHMAKING_SCENE_NAME, StringComparison.Ordinal))
        {
            return;
        }

        _isHandlingUnexpectedClientDisconnect = true;

        try
        {
            MatchmakingManager.QueueLobbyRestrictionMessage(UNEXPECTED_DISCONNECT_RETURN_MESSAGE);

            if (LoadingUIManager.Instance != null)
            {
                LoadingUIManager.Instance.HideLoadingScreen();
            }

            await ReturnToLobby();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[NetworkManager] 예기치 않은 연결 종료 처리 실패: {ex.Message}");
        }
        finally
        {
            _isHandlingUnexpectedClientDisconnect = false;
        }
    }

    private IEnumerator WaitForGameStateManagerAndSendGameMode(GameMode gameMode, int targetPlayers, CustomRoomSettings customSettings)
    {
        float waitTime = 0f;
        float maxWaitTime = 10f;
        float checkInterval = 0.1f;

        while (waitTime < maxWaitTime)
        {
            if (GameStateManager.Instance != null)
            {
                int customGameTimeSeconds = customSettings.IsEnabled ? customSettings.GameTimeSeconds : 0;
                string customMapTemplateName = customSettings.IsEnabled ? customSettings.MapTemplateName : string.Empty;

                if (gameMode != GameMode.Custom)
                {
                    customGameTimeSeconds = 0;
                    customMapTemplateName = string.Empty;
                }

                Debug.Log($"[NetworkManager] GameStateManager found. Sending game mode info - GameMode: {gameMode}, TargetPlayers: {targetPlayers}, CustomTime: {customGameTimeSeconds}, CustomMap: {customMapTemplateName}");
                GameStateManager.Instance.RPC_SetGameModeInfo((int)gameMode, targetPlayers, customGameTimeSeconds, customMapTemplateName);
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
            if (IsOfflineMode)
            {
                Debug.Log("[NetworkManager] Offline mode detected - PracticeModeManager가 맵 초기화를 담당합니다.");
                return;
            }

            Debug.Log("[NetworkManager] GamePlay scene loaded - Spawning NetworkMapManager and initializing map");
            _ = SpawnNetworkMapManagerAndInitAsync();
        }
    }

    public void SetPendingMapInit(GameMode gameMode, int playerCount, string mapTemplateName = "")
    {
        _pendingGameMode = gameMode;
        _pendingPlayerCount = playerCount;
        _pendingMapTemplateName = string.IsNullOrWhiteSpace(mapTemplateName) ? string.Empty : mapTemplateName.Trim();
        _hasPendingMapInit = true;
        Debug.Log($"[NetworkManager] Pending map init set: Mode={gameMode}, Players={playerCount}, MapTemplate={_pendingMapTemplateName}");
        
        // 즉시 맵 초기화 시도 (NetworkMapManager가 이미 스폰되어 있으면 바로 생성)
        InitializePendingMap();
    }

    private void InitializePendingMap()
    {
        if (!_hasPendingMapInit) return;
        
        if (_networkMapManager != null && !_networkMapManager.IsReady())
        {
            Debug.Log($"[NetworkManager] Initializing map: Mode={_pendingGameMode}, Players={_pendingPlayerCount}, MapTemplate={_pendingMapTemplateName}");
            _networkMapManager.InitializeMap(_pendingGameMode, _pendingPlayerCount, _pendingMapTemplateName);
            _hasPendingMapInit = false;
            _pendingMapTemplateName = string.Empty;
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
        Debug.Log($"[NetworkManager] ConnectionInfo set - GameMode: {connectionInfo.GameMode}, MaxPlayers: {connectionInfo.MaxPlayers}, CustomEnabled: {connectionInfo.CustomSettings.IsEnabled}, CustomTime: {connectionInfo.CustomSettings.GameTimeSeconds}, CustomMap: {connectionInfo.CustomSettings.MapTemplateName}");
    }

    /// <summary>
    /// 게임 시작 상태 설정 (GameStateManager에서 호출)
    /// </summary>
    public void SetGameStarted(bool started)
    {
        _isGameStarted = started;
    }

    /// <summary>
    /// 세션 런타임 오브젝트를 초기화합니다.
    /// 플레이어/맵 매니저/대기열 상태를 정리하여 다음 세션을 받을 준비를 합니다.
    /// </summary>
    public void ResetSessionRuntimeObjects()
    {
        if (!IsServer)
        {
            return;
        }

        foreach (NetworkObject playerObj in _spawnedPlayers.Values.ToList())
        {
            if (playerObj != null && playerObj.IsSpawned)
            {
                InstanceFinder.ServerManager.Despawn(playerObj);
            }
        }
        _spawnedPlayers.Clear();
        _playerIdentities.Clear();
        _pendingPlayerSpawns.Clear();

        foreach (NetworkObject deactivatedPlayer in _deactivatedPlayers)
        {
            if (deactivatedPlayer != null && deactivatedPlayer.IsSpawned)
            {
                InstanceFinder.ServerManager.Despawn(deactivatedPlayer);
            }
        }
        _deactivatedPlayers.Clear();

        if (_networkMapManager != null)
        {
            NetworkObject mapManagerObject = _networkMapManager.GetComponent<NetworkObject>();
            if (mapManagerObject != null && mapManagerObject.IsSpawned)
            {
                InstanceFinder.ServerManager.Despawn(mapManagerObject);
            }

            _networkMapManager = null;
            _mapManagerSpawned = false;
        }

        _hasPendingMapInit = false;
        _pendingGameMode = GameMode.None;
        _pendingMapTemplateName = string.Empty;
        _pendingPlayerCount = 0;
        _isWaitingForGameStart = false;
        _isGameStarted = false;
    }

    #endregion

    #region Identity Helpers

    private IIdentityVerificationService ResolveIdentityVerificationService()
    {
        IIdentityVerificationService verificationService = ServiceLocator.Get<IIdentityVerificationService>();
        if (verificationService != null)
        {
            return verificationService;
        }

        Debug.LogWarning("[NetworkManager] IIdentityVerificationService가 등록되지 않아 기본 스텁 검증기를 사용합니다.");
        return new BackendIdentityVerificationService();
    }

    private async Task<BackendIdentityVerificationService.ServiceHealthInfo> GetIdentityVerificationServiceHealthAsync()
    {
        IIdentityVerificationService verificationService = ResolveIdentityVerificationService();
        if (verificationService is BackendIdentityVerificationService)
        {
            return await BackendIdentityVerificationService.CheckServiceHealthAsync();
        }

        string serviceName = verificationService == null ? "Unknown" : verificationService.GetType().Name;
        return new BackendIdentityVerificationService.ServiceHealthInfo(true, $"{serviceName} 사용 중", string.Empty);
    }

    private void RefreshIdentityVerificationSessionStatus(bool isReady, string statusMessage)
    {
        if (UnityLobbyManager.Instance == null || !UnityLobbyManager.Instance.IsInSession)
        {
            return;
        }

        _ = RefreshIdentityVerificationSessionStatusAsync(isReady, statusMessage);
    }

    private async Task RefreshIdentityVerificationSessionStatusAsync(bool isReady, string statusMessage)
    {
        if (UnityLobbyManager.Instance == null || !UnityLobbyManager.Instance.IsInSession)
        {
            return;
        }

        bool updated = await UnityLobbyManager.Instance.UpdateSessionIdentityVerificationStatusAsync(isReady, statusMessage);
        if (!updated)
        {
            Debug.LogWarning($"[NetworkManager] 세션 인증 상태 갱신 실패: Ready={isReady}, Status={statusMessage}");
        }
    }

    private IdentityVerificationResult CreateIdentityVerificationFailureResult(NetworkConnection conn, string reason, bool kickOnFailure)
    {
        string sanitizedReason = string.IsNullOrWhiteSpace(reason)
            ? "토큰 검증 실패"
            : reason.Trim();

        if (kickOnFailure)
        {
            KickConnectionForInvalidIdentity(conn, sanitizedReason);
        }
        else
        {
            string clientIdText = conn == null ? "Unknown" : conn.ClientId.ToString();
            Debug.LogWarning($"[NetworkManager] Identity verification rejected without disconnect. ClientId={clientIdText}, Reason={sanitizedReason}");
        }

        return new IdentityVerificationResult(false, sanitizedReason);
    }

    private static bool IsVerifiedIdentityUsable(VerifiedIdentity verified)
    {
        return verified != null &&
               verified.IsValid &&
               !string.IsNullOrWhiteSpace(verified.FirebaseUid);
    }

    private async Task<string> ResolveRegisteredDisplayNameAsync(VerifiedIdentity verified)
    {
        if (verified == null || string.IsNullOrWhiteSpace(verified.FirebaseUid))
        {
            return "Player_0000";
        }

        if (IsOfflineIdentityAllowedForCurrentMode() && verified.IsAnonymous)
        {
            return "Player";
        }

        if (verified.IsAnonymous)
        {
            if (!string.IsNullOrWhiteSpace(verified.DisplayName))
            {
                return verified.DisplayName.Trim();
            }

            string resolvedGuestId = ResolveGuestId(verified.GuestId, verified.FirebaseUid);
            return $"Guest_{GetSafeSuffix(resolvedGuestId, 4)}";
        }

        if (ServiceLocator.TryGet<IPlayerDataService>(out IPlayerDataService playerDataService) && playerDataService != null)
        {
            try
            {
                PlayerProfile profile = await playerDataService.GetPlayerProfileAsync(verified.FirebaseUid.Trim());
                if (profile != null && !string.IsNullOrWhiteSpace(profile.DisplayName))
                {
                    return profile.DisplayName.Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NetworkManager] 프로필 표시 이름 조회 실패: {ex.Message}");
            }
        }

        return string.IsNullOrWhiteSpace(verified.DisplayName)
            ? $"Player_{GetSafeSuffix(verified.FirebaseUid, 4)}"
            : verified.DisplayName.Trim();
    }

    private void KickConnectionForInvalidIdentity(NetworkConnection conn, string reason)
    {
        if (conn == null || !conn.IsValid)
        {
            return;
        }

        string message = $"Identity verification failed. ClientId={conn.ClientId}, Reason={reason}";
        Debug.LogWarning($"[NetworkManager] {message}");
        conn.Kick(KickReason.UnusualActivity, log: message);
    }

    private bool IsOfflineIdentityAllowedForCurrentMode()
    {
        if (!_currentConnectionInfo.HasValue)
        {
            return false;
        }

        GameMode mode = _currentConnectionInfo.Value.GameMode;
        return GameModeCatalog.IsPracticeMode(mode);
    }

    private bool TryBuildPracticeFallbackIdentity(string idToken, out VerifiedIdentity identity)
    {
        identity = null;

        if (!IsOfflineIdentityAllowedForCurrentMode() || string.IsNullOrWhiteSpace(idToken))
        {
            return false;
        }

        if (TryParseJwtClaims(
                idToken,
                out string uidFromJwt,
                out string nameFromJwt,
                out string emailFromJwt,
                out string signInProviderFromJwt))
        {
            bool isAnonymous = string.Equals(signInProviderFromJwt, "anonymous", StringComparison.OrdinalIgnoreCase) ||
                               (string.IsNullOrWhiteSpace(emailFromJwt) &&
                                string.IsNullOrWhiteSpace(nameFromJwt) &&
                                uidFromJwt.StartsWith("guest_", StringComparison.OrdinalIgnoreCase));

            identity = new VerifiedIdentity
            {
                IsValid = true,
                FirebaseUid = uidFromJwt,
                GuestId = isAnonymous ? ResolveGuestId(string.Empty, uidFromJwt) : string.Empty,
                DisplayName = string.IsNullOrWhiteSpace(nameFromJwt)
                    ? (isAnonymous ? "Player" : $"Player_{GetSafeSuffix(uidFromJwt, 4)}")
                    : nameFromJwt.Trim(),
                Email = emailFromJwt ?? string.Empty,
                IsAnonymous = isAnonymous
            };

            return true;
        }

        string fallbackUid = $"practice_{GetSafeSuffix(idToken, 16)}";
        identity = new VerifiedIdentity
        {
            IsValid = true,
            FirebaseUid = fallbackUid,
            GuestId = string.Empty,
            DisplayName = $"Player_{GetSafeSuffix(fallbackUid, 4)}",
            Email = string.Empty,
            IsAnonymous = false
        };
        return true;
    }

    private static bool TryParseJwtClaims(
        string idToken,
        out string firebaseUid,
        out string displayName,
        out string email,
        out string signInProvider)
    {
        firebaseUid = string.Empty;
        displayName = string.Empty;
        email = string.Empty;
        signInProvider = string.Empty;

        if (string.IsNullOrWhiteSpace(idToken))
        {
            return false;
        }

        string[] parts = idToken.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        string payloadJson = DecodeBase64Url(parts[1]);
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return false;
        }

        firebaseUid = FirstNonEmpty(
            ExtractJsonStringValue(payloadJson, "user_id"),
            ExtractJsonStringValue(payloadJson, "sub"));
        displayName = FirstNonEmpty(
            ExtractJsonStringValue(payloadJson, "name"),
            ExtractJsonStringValue(payloadJson, "displayName"));
        email = FirstNonEmpty(
            ExtractJsonStringValue(payloadJson, "email"),
            ExtractJsonStringValue(payloadJson, "upn"));
        signInProvider = FirstNonEmpty(
            ExtractJsonStringValue(payloadJson, "sign_in_provider"),
            ExtractJsonStringValue(payloadJson, "provider_id"));

        return !string.IsNullOrWhiteSpace(firebaseUid);
    }

    private static string DecodeBase64Url(string base64Url)
    {
        if (string.IsNullOrWhiteSpace(base64Url))
        {
            return string.Empty;
        }

        string normalized = base64Url.Replace('-', '+').Replace('_', '/');
        int padding = (4 - (normalized.Length % 4)) % 4;
        if (padding > 0)
        {
            normalized = normalized.PadRight(normalized.Length + padding, '=');
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(normalized);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractJsonStringValue(string json, string key)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        string pattern = $"\\\"{Regex.Escape(key)}\\\"\\s*:\\s*\\\"([^\\\"]*)\\\"";
        Match match = Regex.Match(json, pattern);
        if (!match.Success || match.Groups.Count < 2)
        {
            return string.Empty;
        }

        return match.Groups[1].Value.Trim();
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null)
        {
            return string.Empty;
        }

        for (int i = 0; i < values.Length; i++)
        {
            string value = values[i];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string ResolveGuestId(string guestId, string uid)
    {
        if (!string.IsNullOrWhiteSpace(guestId))
        {
            return guestId.Trim().ToUpperInvariant();
        }

        return $"G-{GetSafeSuffix(uid, 8)}";
    }

    private static string GetSafeSuffix(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0000";
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed.ToUpperInvariant()
            : trimmed[^maxLength..].ToUpperInvariant();
    }

    #endregion
}
