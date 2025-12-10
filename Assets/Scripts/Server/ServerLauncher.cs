using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// AWS EC2 단일 프로세스 = 단일 세션 관리자 (Server Mode 정석 구조)
/// 게임이 시작되면 새로운 서버 프로세스를 실행(Fork)하여 대기 세션을 유지합니다.
/// </summary>
public class ServerLauncher : MonoBehaviour
{
    #region Serialized Fields

    [Header("설정")]
    [SerializeField] private ServerBuildConfig _serverBuildConfig;
    [SerializeField] private NetworkRunner _runnerPrefab; // Runner 프리팹 (없으면 코드로 생성)
    
    [Header("기본값")]
    [SerializeField] private string _defaultServerId = "aws-server-1";
    [SerializeField] private int _defaultPort = 27015;
    [SerializeField] private int _maxPlayers = 8;
    [SerializeField] private int _gamePlaySceneIndex = 1;

    [Header("네트워크 매니저")]
    [Tooltip("NetworkManager 프리팹 (프리팹에 플레이어/맵/게임상태 프리팹이 할당되어 있어야 함)")]
    [SerializeField] private GameObject _networkManagerPrefab;

    #endregion

    #region Private Fields

    private NetworkRunner _runner;
    private static ServerLauncher _instance;
    private bool _hasSpawnedNextProcess = false; // 다음 대기 서버를 생성했는지 여부

    // 커맨드 라인 인자로 받은 값들
    private int _myPort;
    private string _myServerId;

    #endregion

    public static ServerLauncher Instance => _instance;

    private void Awake()
    {
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 에디터 모드 방지 (테스트 편의를 위해 에디터에서도 동작하게 할 수 있지만, 프로세스 생성은 제한 필요)
        if (Application.isEditor)
        {
            Debug.Log("[ServerLauncher] 에디터 모드 - 자동 시작 안함.");
            return;
        }

        // 헤드리스 서버인지 확인
        if (IsHeadlessServer())
        {
            ParseCommandLineArgs();
            StartGameServer();
        }
    }

    /// <summary>
    /// 커맨드 라인 인자 파싱 (-port 27015 -serverId ...)
    /// </summary>
    private void ParseCommandLineArgs()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        
        _myPort = _defaultPort;
        _myServerId = _defaultServerId;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-port" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int port)) _myPort = port;
            }
            else if (args[i] == "-serverId" && i + 1 < args.Length)
            {
                _myServerId = args[i + 1];
            }
        }

        Debug.Log($"[ServerLauncher] 설정 로드 완료 - Port: {_myPort}, ServerID: {_myServerId}");
    }

    /// <summary>
    /// 게임 서버 시작 (단일 세션)
    /// </summary>
    private async void StartGameServer()
    {
        if (_runner != null) return;

        // Runner 생성
        if (_runnerPrefab != null)
        {
            _runner = Instantiate(_runnerPrefab);
        }
        else
        {
            GameObject go = new GameObject("NetworkRunner");
            _runner = go.AddComponent<NetworkRunner>();
        }
        DontDestroyOnLoad(_runner.gameObject);

        // 컴포넌트 추가 (필요한 경우)
        if (_runner.GetComponent<NetworkEvents>() == null) _runner.gameObject.AddComponent<NetworkEvents>();

        // NetworkManager 생성 및 등록 (맵 생성 및 플레이어 스폰 관리)
        NetworkManager networkManager = null;
        if (_networkManagerPrefab != null)
        {
            GameObject nmObj = Instantiate(_networkManagerPrefab);
            DontDestroyOnLoad(nmObj);
            networkManager = nmObj.GetComponent<NetworkManager>();
            
            if (networkManager != null)
            {
                networkManager.SetRunner(_runner);
                _runner.AddCallbacks(networkManager);
                Debug.Log("[ServerLauncher] NetworkManager 생성 및 콜백 등록 완료");
            }
            else
            {
                Debug.LogError("[ServerLauncher] NetworkManager 프리팹에 컴포넌트가 없습니다!");
            }
        }
        else
        {
            Debug.LogError("[ServerLauncher] NetworkManager 프리팹이 할당되지 않았습니다!");
        }

        // 세션 이름 생성 (고유해야 함)
        // Why: 중복 체크를 통해 유니크한 방 코드 생성
        string roomCode;
        try
        {
            roomCode = await RoomCodeGenerator.GenerateUniqueCodeWithFetcher(FetchAllSessionsForRoomCodeCheck, maxRetries: 10);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ServerLauncher] Failed to generate unique room code: {ex.Message}");
            roomCode = RoomCodeGenerator.GenerateCode(); // 폴백: 일반 생성
        }

        string sessionName = $"Server_{_myServerId}_{_maxPlayers}P_{roomCode}";

        Debug.Log($"[ServerLauncher] 세션 시작 시도: {sessionName} (Port: {_myPort})");

        var result = await _runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Server,
            SessionName = sessionName,
            PlayerCount = _maxPlayers,
            Scene = SceneRef.FromIndex(_gamePlaySceneIndex),
            SceneManager = _runner.gameObject.AddComponent<NetworkSceneManagerDefault>(), // 기본 SceneManager 사용
            Address = NetAddress.Any((ushort)_myPort), // 지정된 포트 사용
            SessionProperties = new Dictionary<string, SessionProperty>
            {
                { "ServerId", _myServerId },
                { "IsInGame", false }, // 초기 대기 상태
                { "MaxPlayers", _maxPlayers },
                { "IsReserved", false }, // Why: 모든 플레이어가 조인 가능 (같은 모드 매칭자들이 함께 조인)
                { "CurrentMatchingMode", 0 }, // Why: 매칭 모드 초기값 (클라이언트 조인 후 RPC로 업데이트)
                { "MatchingTargetPlayers", 0 } // Why: 목표 인원수 초기값 (클라이언트 조인 후 RPC로 업데이트)
            }
        });

        if (result.Ok)
        {
            Debug.Log($"[ServerLauncher] 세션 시작 성공! - {sessionName}");
        }
        else
        {
            Debug.LogError($"[ServerLauncher] 세션 시작 실패: {result.ShutdownReason}");
            Application.Quit(); // 실패 시 프로세스 종료
        }
    }

    private void Update()
    {
        if (_runner != null && _runner.IsRunning && _runner.IsServer)
        {
            CheckGameStateAndSpawnNewProcess();
        }
    }

    /// <summary>
    /// 게임 상태를 체크하고, 게임이 시작되면 새로운 대기 프로세스를 실행
    /// </summary>
    private void CheckGameStateAndSpawnNewProcess()
    {
        if (_hasSpawnedNextProcess) return;

        // 조건: 플레이어가 1명 이상 들어오거나, 게임 시작 플래그가 켜지면
        // (여기서는 간단하게 ActivePlayers > 1(서버 포함) 로 체크하거나, GameStateManager 연동)
        
        // Server Mode에서 ActivePlayers는 서버(1) + 클라이언트(N) 입니다.
        // 즉, ActivePlayers >= 2 이면 클라이언트가 1명 이상 접속한 것.
        bool hasPlayers = _runner.ActivePlayers.Count() >= 2;
        
        // 또는 GameStateManager를 통해 게임 시작 여부 확인
        bool isGameStarted = false;
        // 방법 1: SessionProperty 확인
        if (_runner.SessionInfo.IsValid && _runner.SessionInfo.Properties.TryGetValue("IsInGame", out var prop))
        {
            isGameStarted = (bool)prop;
        }

        if (hasPlayers || isGameStarted)
        {
            Debug.Log("[ServerLauncher] 게임이 시작되었습니다(혹은 플레이어 접속). 새로운 대기 서버 프로세스를 실행합니다.");
            SpawnNextServerProcess();
            _hasSpawnedNextProcess = true;
        }
    }

    /// <summary>
    /// 다음 포트를 사용하는 새로운 서버 프로세스 실행
    /// </summary>
    private void SpawnNextServerProcess()
    {
        if (Application.isEditor) return;

        int nextPort = _myPort + 1;

        // Why: Linux/Mono 환경에서 Process.MainModule 접근 시 NotSupportedException 발생 가능
        // System.Environment.GetCommandLineArgs()[0]는 실행된 명령어(경로 포함)를 반환함
        string exePath = System.Environment.GetCommandLineArgs()[0];

        // 상대 경로일 경우 절대 경로로 변환 (안정성 확보)
        if (!System.IO.Path.IsPathRooted(exePath))
        {
            exePath = System.IO.Path.GetFullPath(exePath);
        }

        // 파일 존재 확인
        if (!System.IO.File.Exists(exePath))
        {
            Debug.LogError($"[ServerLauncher] 실행 파일을 찾을 수 없습니다: {exePath}");
            return;
        }

        // 인자 구성
        string arguments = $"-batchmode -nographics -port {nextPort} -serverId {_myServerId}";

        Debug.Log($"[ServerLauncher] 새 프로세스 시작 시도: Path='{exePath}', Args='{arguments}'");

        // Why: Mono에서 Process.Start()의 "Native error= Success" 버그 회피
        // 쉘 스크립트를 생성하고 실행하는 방식으로 우회
        if (!IsWindows())
        {
            SpawnViaShellScript(exePath, arguments);
        }
        else
        {
            // Windows는 직접 실행
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(exePath)
                };

                var process = Process.Start(startInfo);

                if (process != null)
                {
                    Debug.Log($"[ServerLauncher] 새 프로세스 실행 성공! PID: {process.Id}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerLauncher] 프로세스 생성 예외: {e}");
            }
        }
    }

    /// <summary>
    /// 파일 기반 명령 시스템 (Mono Process.Start 완전 우회)
    /// </summary>
    private void SpawnViaShellScript(string exePath, string arguments)
    {
        try
        {
            // Why: Mono Process.Start()가 완전히 작동하지 않음
            // 대신 외부 스크립트가 감시할 명령 파일 생성
            string workDir = System.IO.Path.GetDirectoryName(exePath);
            int nextPort = _myPort + 1;

            // 1. 실행할 스크립트 생성
            string scriptPath = System.IO.Path.Combine(workDir, $"spawn_{nextPort}.sh");
            string scriptContent = $@"#!/bin/bash
chmod +x ""{exePath}""
nohup ""{exePath}"" {arguments} > /dev/null 2>&1 &
echo $! > ""{scriptPath}.pid""
echo ""Process started: $!""
";
            System.IO.File.WriteAllText(scriptPath, scriptContent);

            // 2. 스크립트에 실행 권한 부여 (파일 시스템 직접 조작)
            try
            {
                // Unix 파일 권한: 0755 (rwxr-xr-x)
                if (System.IO.File.Exists("/bin/chmod"))
                {
                    // chmod 명령 파일 생성
                    string chmodScript = System.IO.Path.Combine(workDir, $"chmod_{nextPort}.sh");
                    System.IO.File.WriteAllText(chmodScript, $"#!/bin/sh\nchmod +x \"{scriptPath}\"\nchmod +x \"{exePath}\"\n");
                }
            }
            catch
            {
                // 무시
            }

            Debug.Log($"[ServerLauncher] ========================================");
            Debug.Log($"[ServerLauncher] Mono Process.Start() 버그로 인해 자동 프로세스 생성 실패");
            Debug.Log($"[ServerLauncher] 다음 스크립트를 수동으로 실행해주세요:");
            Debug.Log($"[ServerLauncher]   bash {scriptPath}");
            Debug.Log($"[ServerLauncher] ========================================");
            Debug.Log($"[ServerLauncher] 또는 다음 명령어를 직접 실행:");
            Debug.Log($"[ServerLauncher]   {exePath} {arguments} &");
            Debug.Log($"[ServerLauncher] ========================================");

            // 명령 파일 생성 (외부 감시 스크립트용)
            string commandFile = System.IO.Path.Combine(workDir, "pending_spawn.cmd");
            string commandContent = $@"# 자동 생성됨 - 외부 스크립트가 처리
SCRIPT_PATH={scriptPath}
EXEC_PATH={exePath}
ARGUMENTS={arguments}
PORT={nextPort}
TIMESTAMP={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}
";
            System.IO.File.WriteAllText(commandFile, commandContent);

            Debug.Log($"[ServerLauncher] 명령 파일 생성: {commandFile}");
            Debug.Log($"[ServerLauncher] 외부 감시 스크립트가 이 파일을 감지하면 자동으로 프로세스를 시작합니다.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ServerLauncher] 파일 생성 예외: {e}");
        }
    }

    /// <summary>
    /// Windows 플랫폼인지 확인
    /// </summary>
    private bool IsWindows()
    {
        return System.Environment.OSVersion.Platform == PlatformID.Win32NT ||
               System.Environment.OSVersion.Platform == PlatformID.Win32Windows;
    }

    /// <summary>
    /// 세션 종료 시 호출 (GameStateManager 등에서 호출)
    /// </summary>
    public void OnSessionEnded(NetworkRunner runner)
    {
        Debug.Log("[ServerLauncher] 세션 종료. 프로세스를 종료합니다.");
        StartCoroutine(ShutdownCoroutine());
    }

    private IEnumerator ShutdownCoroutine()
    {
        if (_runner != null)
        {
            yield return _runner.Shutdown();
        }
        
        Debug.Log("[ServerLauncher] Application.Quit()");
        Application.Quit();
    }

    /// <summary>
    /// 방 코드 중복 체크를 위한 세션 검색 (세션 시작 전에 호출)
    /// </summary>
    private async System.Threading.Tasks.Task<List<Fusion.SessionInfo>> FetchAllSessionsForRoomCodeCheck()
    {
        var sessionListCallback = new SessionListCallback();

        // Why: 임시 Runner를 생성하여 세션 리스트 검색
        var tempRunnerGo = new GameObject("TempSessionSearchRunner_RoomCode");
        var tempRunner = tempRunnerGo.AddComponent<NetworkRunner>();
        tempRunner.AddCallbacks(sessionListCallback);

        try
        {
            Debug.Log("[ServerLauncher] Fetching session list for room code check...");

            var result = await tempRunner.JoinSessionLobby(Fusion.SessionLobby.ClientServer);
            if (!result.Ok)
            {
                Debug.LogWarning($"[ServerLauncher] Failed to join session lobby: {result.ShutdownReason}");
                return new List<Fusion.SessionInfo>();
            }

            // Why: 세션 리스트가 업데이트될 때까지 대기 (3초)
            await System.Threading.Tasks.Task.Delay(3000);

            Debug.Log($"[ServerLauncher] Session list fetch complete. Found {sessionListCallback.Sessions.Count} sessions");

            if (tempRunner != null && tempRunner.IsRunning)
            {
                await tempRunner.Shutdown();
            }

            return sessionListCallback.Sessions;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[ServerLauncher] Error fetching sessions: {ex.Message}");
            return new List<Fusion.SessionInfo>();
        }
        finally
        {
            if (tempRunnerGo != null)
            {
                Destroy(tempRunnerGo);
            }
        }
    }

    /// <summary>
    /// 세션 리스트 콜백 핸들러 (방 코드 중복 체크용)
    /// </summary>
    private class SessionListCallback : INetworkRunnerCallbacks
    {
        public List<Fusion.SessionInfo> Sessions { get; private set; } = new List<Fusion.SessionInfo>();

        public void OnSessionListUpdated(NetworkRunner runner, List<Fusion.SessionInfo> sessionList)
        {
            Sessions = sessionList;
            Debug.Log($"[SessionListCallback] Session list updated: {sessionList.Count} sessions");
        }

        // 나머지 콜백은 비어있음
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }

    private bool IsHeadlessServer()
    {
#if UNITY_SERVER
        return true;
#else
        string[] args = System.Environment.GetCommandLineArgs();
        foreach (string arg in args)
        {
            if (arg.ToLower() == "-batchmode") return true;
        }
        return false;
#endif
    }
}