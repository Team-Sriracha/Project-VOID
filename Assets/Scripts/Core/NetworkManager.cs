using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>
/// Photon Fusion 네트워크 연결 및 플레이어 스폰을 관리합니다.
/// Server 모드에서 동작하며, INetworkRunnerCallbacks를 구현합니다.
/// </summary>
public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    #region Serialized Fields

    [Header("네트워크 설정")]
    [SerializeField] private string _roomName = "TestRoom";
    [SerializeField] private int _maxPlayers = 8;

    [Header("플레이어 설정")]
    [SerializeField] private NetworkPrefabRef _playerPrefab;

    [Header("맵 설정")]
    [SerializeField] private NetworkPrefabRef _mapManagerPrefab;

    [Header("게임 상태 관리")]
    [SerializeField] private NetworkPrefabRef _gameStateManagerPrefab;

    #endregion

    #region Properties

    /// <summary>
    /// 현재 NetworkRunner 인스턴스를 반환합니다.
    /// </summary>
    public NetworkRunner Runner { get; private set; }

    #endregion

    #region Private Fields

    private Dictionary<PlayerRef, NetworkObject> _spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    private PlayerInputHandler _cachedInputHandler;
    private bool _mapManagerSpawned = false;
    private bool _gameStateManagerSpawned = false;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Server 모드로 게임을 시작합니다.
    /// </summary>
    public async void StartServer()
    {
        Runner = gameObject.AddComponent<NetworkRunner>();
        Runner.ProvideInput = true;

        var result = await Runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Server,
            SessionName = _roomName,
            Scene = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>(),
            PlayerCount = _maxPlayers  // Why: 최대 플레이어 수 설정
        });

        if (result.Ok)
        {
            Debug.Log($"[NetworkManager] 서버 시작 성공 - 세션명: {_roomName}");
        }
        else
        {
            Debug.LogError($"[NetworkManager] 서버 시작 실패: {result.ShutdownReason}");
        }
    }

    /// <summary>
    /// Client 모드로 서버에 연결합니다.
    /// </summary>
    public async void StartClient()
    {
        Runner = gameObject.AddComponent<NetworkRunner>();
        Runner.ProvideInput = true;

        Debug.Log($"[NetworkManager] 클라이언트 연결 시도 중 - 세션명: {_roomName}");

        var result = await Runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Client,
            SessionName = _roomName,
            Scene = SceneRef.FromIndex(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex),
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        });

        if (result.Ok)
        {
            Debug.Log($"[NetworkManager] 클라이언트 연결 성공 - 세션명: {_roomName}");
        }
        else
        {
            Debug.LogError($"[NetworkManager] 클라이언트 연결 실패: {result.ShutdownReason}");
        }
    }

    #endregion

    #region INetworkRunnerCallbacks - Player

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[NetworkManager] OnPlayerJoined: Player={player}, IsServer={runner.IsServer}");

        // Why: Server만 플레이어 스폰 처리
        if (runner.IsServer)
        {
            //TODO : 스폰포인트 지정 시스템 구현
            Vector3 spawnPosition = new Vector3(UnityEngine.Random.Range(-5f, 5f), 1f, UnityEngine.Random.Range(-5f, 5f));

            // Why: 네 번째 파라미터로 player 전달 시 자동으로 InputAuthority 할당됨
            NetworkObject networkPlayerObject = runner.Spawn(_playerPrefab, spawnPosition, Quaternion.identity, player);

            // Why: Runner.GetPlayerObject()가 작동하려면 SetPlayerObject() 호출 필수!
            runner.SetPlayerObject(player, networkPlayerObject);

            _spawnedPlayers.Add(player, networkPlayerObject);
            Debug.Log($"[NetworkManager] Server: 플레이어 스폰 완료 {player}");

            // Why: GameStateManager에 플레이어 참가 알림
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.OnPlayerJoined();
            }
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedPlayers.TryGetValue(player, out NetworkObject networkObject))
        {
            runner.Despawn(networkObject);
            _spawnedPlayers.Remove(player);

            // Why: GameStateManager에 플레이어 퇴장 알림
            if (runner.IsServer && GameStateManager.Instance != null)
            {
                GameStateManager.Instance.OnPlayerLeft();
            }
        }
    }

    #endregion

    #region INetworkRunnerCallbacks - Input

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // Why: 캐시된 InputHandler가 유효하면 재사용
        if (_cachedInputHandler != null)
        {
            NetworkInputData data = _cachedInputHandler.GetCurrentInput();
            input.Set(data);
            return;
        }

        // Why: 캐시가 없을 때만 찾기 (초기 1회만)
        var allInputHandlers = FindObjectsByType<PlayerInputHandler>(FindObjectsSortMode.None);
        foreach (var inputHandler in allInputHandlers)
        {
            NetworkObject netObj = inputHandler.GetComponent<NetworkObject>();
            if (netObj != null && netObj.HasInputAuthority)
            {
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

    public void OnConnectedToServer(NetworkRunner runner) { }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
        request.Accept();
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"[NetworkManager] 연결 실패: {reason}");
    }

    #endregion

    #region INetworkRunnerCallbacks - Session

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[NetworkManager] 종료: {shutdownReason}");
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        Debug.Log($"[NetworkManager] 사용 가능한 세션 수: {sessionList.Count}");
        foreach (var session in sessionList)
        {
            Debug.Log($"[NetworkManager] 세션 발견: {session.Name} (플레이어: {session.PlayerCount}/{session.MaxPlayers})");
        }
    }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }

    #endregion

    #region INetworkRunnerCallbacks - Misc

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log($"[NetworkManager] OnSceneLoadDone - IsServer: {runner.IsServer}, HasStateAuthority: {runner.IsServer}");

        if (!runner.IsServer) return;

        // GameStateManager 스폰
        if (!_gameStateManagerSpawned && _gameStateManagerPrefab != null)
        {
            Debug.Log("[NetworkManager] GameStateManager Spawn 시도...");
            NetworkObject gameStateManager = runner.Spawn(_gameStateManagerPrefab, Vector3.zero, Quaternion.identity);
            _gameStateManagerSpawned = true;
            Debug.Log($"[NetworkManager] GameStateManager Spawn 완료! NetworkId: {gameStateManager.Id}");
        }

        // NetworkMapManager 스폰
        if (!_mapManagerSpawned && _mapManagerPrefab != null)
        {
            Debug.Log("[NetworkManager] NetworkMapManager Spawn 시도...");
            NetworkObject mapManager = runner.Spawn(_mapManagerPrefab, Vector3.zero, Quaternion.identity);
            _mapManagerSpawned = true;
            Debug.Log($"[NetworkManager] NetworkMapManager Spawn 완료! NetworkId: {mapManager.Id}");
        }
    }

    public void OnSceneLoadStart(NetworkRunner runner) { }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    public void OnDisconnectedFromServer(NetworkRunner runner) { }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request)
    {
        request.Accept();
    }

    #endregion
}
