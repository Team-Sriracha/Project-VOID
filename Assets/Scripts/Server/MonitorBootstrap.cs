using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// 모니터링 모드 부트스트랩
/// 커맨드라인 인자 --monitor 또는 -monitor 로 실행 시 모니터링 모드 진입
/// 
/// 사용법:
/// GameServer.exe --monitor
/// 또는 Unity Editor에서 MonitorBootstrap 오브젝트가 있는 씬으로 시작
/// </summary>
public class MonitorBootstrap : MonoBehaviour
{
    #region Serialized Fields

    [Header("설정")]
    [SerializeField] private GameObject _monitorServerPrefab;
    
    [Header("디버그")]
    [SerializeField] private bool _forceMonitorMode = false;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (IsMonitorMode())
        {
            Debug.Log("[MonitorBootstrap] 모니터링 모드 시작...");
            StartMonitorMode();
        }
        else
        {
            Debug.Log("[MonitorBootstrap] 게임 모드입니다. 모니터링 비활성화.");
            Destroy(gameObject);
        }
    }

    #endregion

    #region Private Methods

    private bool IsMonitorMode()
    {
        if (_forceMonitorMode)
            return true;

        string[] args = Environment.GetCommandLineArgs();
        return args.Any(arg => 
            arg.Equals("--monitor", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-monitor", StringComparison.OrdinalIgnoreCase));
    }

    private void StartMonitorMode()
    {
        DontDestroyOnLoad(gameObject);
        
        if (_monitorServerPrefab != null)
        {
            var monitor = Instantiate(_monitorServerPrefab);
            monitor.name = "ServerMonitor";
            DontDestroyOnLoad(monitor);
        }
        else
        {
            var monitorObj = new GameObject("ServerMonitor");
            monitorObj.AddComponent<ServerMonitorHttpServer>();
            DontDestroyOnLoad(monitorObj);
        }

        Debug.Log("[MonitorBootstrap] 모니터링 서버 시작됨!");
        Debug.Log("[MonitorBootstrap] 대시보드: http://localhost:8080/");
    }

    #endregion
}
