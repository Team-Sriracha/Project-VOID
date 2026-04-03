#if UNITY_EDITOR || UNITY_SERVER || PROJECTVOID_SERVER_RUNTIME
using FishNet;
using UnityEngine;

/// <summary>
/// 씬 로드 시 자동 서버 시작
/// 커맨드라인 인자로 포트를 지정할 수 있습니다:
///   -port 7777      (공백 구분)
///   --port=7777     (= 구분, Linux systemd 친화적)
/// </summary>
public class AutoStartServer : MonoBehaviour
{
    #region Constants

    private const int DEFAULT_PORT = 7777;

    #endregion

    #region Serialized Fields

    [Header("설정")]
    [SerializeField] private bool _startServerOnAwake = true;
    [SerializeField] private bool _startClientOnAwake = false;
    [SerializeField] private float _startDelay = 0.1f;

    #endregion

    #region Private Fields

    private static int _serverPort = DEFAULT_PORT;
    private static string _serverIp = string.Empty;

    #endregion

    #region Properties

    /// <summary>
    /// 현재 서버 포트
    /// </summary>
    public static int ServerPort => _serverPort;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // 커맨드라인 인자 파싱 (가능한 빨리)
        ParseCommandLineArgs();
    }

    private void Start()
    {
        // Monitor 모드에서는 FishNet 서버를 시작하지 않음 (HTTP 모니터만 사용)
        if (IsMonitorMode())
        {
            Debug.Log("[AutoStartServer] Monitor 모드 감지됨");

            // 혹시 FishNet AutoStart가 이미 서버를 켰다면 강제 종료
            if (InstanceFinder.IsServerStarted)
            {
                Debug.LogWarning("[AutoStartServer] FishNet이 자동으로 서버를 시작했습니다. Monitor 모드이므로 강제 종료합니다.");
                InstanceFinder.ServerManager.StopConnection(true);
            }
            return;
        }

        if (_startServerOnAwake)
        {
            Invoke(nameof(StartServer), _startDelay);
        }

        if (_startClientOnAwake)
        {
            Invoke(nameof(StartClient), _startDelay + 0.1f);
        }
    }

    private bool IsMonitorMode()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        return System.Array.Exists(args, arg => arg == "-monitor");
    }

    #endregion

    #region Command Line Parsing

    private void ParseCommandLineArgs()
    {
        string[] args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            // 형식 1: -port 7777 (공백 구분)
            if ((arg == "-port" || arg == "--port") && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int port))
                {
                    _serverPort = port;
                    ServerRuntimeInfo.SetServerPort(_serverPort);
                    Debug.Log($"[AutoStartServer] 포트 설정 (공백 형식): {_serverPort}");
                }
                continue;
            }

            // 형식 2: --port=7777 (= 구분)
            if (arg.StartsWith("--port="))
            {
                string portStr = arg.Substring("--port=".Length);
                if (int.TryParse(portStr, out int port))
                {
                    _serverPort = port;
                    ServerRuntimeInfo.SetServerPort(_serverPort);
                    Debug.Log($"[AutoStartServer] 포트 설정 (= 형식): {_serverPort}");
                }
                continue;
            }

            // 형식 3: -ip 0.0.0.0 (공백 구분)
            if ((arg == "-ip" || arg == "--ip") && i + 1 < args.Length)
            {
                _serverIp = args[i + 1];
                ServerRuntimeInfo.SetServerIp(_serverIp);
                Debug.Log($"[AutoStartServer] IP 설정 (공백 형식): {_serverIp}");
                continue;
            }

            // 형식 4: --ip=0.0.0.0 (= 구분)
            if (arg.StartsWith("--ip="))
            {
                _serverIp = arg.Substring("--ip=".Length);
                ServerRuntimeInfo.SetServerIp(_serverIp);
                Debug.Log($"[AutoStartServer] IP 설정 (= 형식): {_serverIp}");
                continue;
            }
        }
    }

    #endregion

    #region Private Methods

    private void StartServer()
    {
        if (InstanceFinder.ServerManager != null && !InstanceFinder.IsServerStarted)
        {
            // Transport에 포트 설정
            var transport = InstanceFinder.NetworkManager?.TransportManager?.Transport;
            if (transport is FishNet.Transporting.Tugboat.Tugboat tugboat)
            {
                tugboat.SetPort((ushort)_serverPort);
                ServerRuntimeInfo.SetServerPort(_serverPort);
                Debug.Log($"[AutoStartServer] Tugboat 포트 설정: {_serverPort}");

                if (!string.IsNullOrEmpty(_serverIp))
                {
                    tugboat.SetServerBindAddress(_serverIp, FishNet.Transporting.IPAddressType.IPv4);
                    ServerRuntimeInfo.SetServerIp(_serverIp);
                    Debug.Log($"[AutoStartServer] Tugboat IP 바인딩: {_serverIp}");
                }
            }

            Debug.Log($"[AutoStartServer] 서버 시작 중... (Port: {_serverPort})");
            InstanceFinder.ServerManager.StartConnection();

            // 세션 매니저 로그 (외부 모니터링용)
            Debug.Log($"[SESSION_STATUS] STARTED port={_serverPort}");
        }
    }

    private void StartClient()
    {
        if (InstanceFinder.ClientManager != null && !InstanceFinder.IsClientStarted)
        {
            Debug.Log("[AutoStartServer] 클라이언트 자동 시작 중...");
            InstanceFinder.ClientManager.StartConnection();
        }
    }

    #endregion
}
#else
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 클라이언트 빌드에서는 서버 자동 시작 로직을 제외하기 위한 플레이스홀더입니다.
/// </summary>
public sealed class AutoStartServer : MonoBehaviour
{
    private void Awake()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (Application.isBatchMode ||
            (activeScene.IsValid() && activeScene.name == "ServerScene"))
        {
            Debug.LogError("[AutoStartServer] 서버 런타임 define가 비활성화되어 있습니다. Standalone 서버 빌드 전 PROJECTVOID_SERVER_RUNTIME를 켜야 합니다.");
        }
    }
}
#endif
