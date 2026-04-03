#if UNITY_EDITOR || UNITY_SERVER || PROJECTVOID_SERVER_RUNTIME
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ServerBootstrap
{
    #region Unity Lifecycle

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        string[] args = Environment.GetCommandLineArgs();
        bool isMonitor = args.Any(arg => arg == "-monitor");

        if (!ShouldRunInCurrentProcess(isMonitor))
        {
            return;
        }

        if (isMonitor)
        {
            StartMonitorMode();
            return;
        }

        RegisterCoreServerServices();
        Debug.Log("[ServerBootstrap] Starting in GAME SERVER MODE");
    }

    #endregion

    #region Initialization

    private static void StartMonitorMode()
    {
        Debug.Log("[ServerBootstrap] Starting in MONITOR MODE");

        GameObject monitorObj = new GameObject("ServerMonitor");
        monitorObj.AddComponent<ServerMonitorHttpServer>();
        UnityEngine.Object.DontDestroyOnLoad(monitorObj);
    }

    private static void RegisterCoreServerServices()
    {
        ServiceLocator.Register<IIdentityVerificationService>(new BackendIdentityVerificationService());
        ServiceLocator.Register<IServerMatchResultService>(new BackendMatchResultService());

        Debug.Log("[ServerBootstrap] Core server services registered. IdentityVerification=BackendIdentityVerificationService, MatchResult=BackendMatchResultService");
    }

    private static bool ShouldRunInCurrentProcess(bool isMonitor)
    {
        if (isMonitor)
        {
            return true;
        }

        if (Application.isBatchMode)
        {
            return true;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        return activeScene.IsValid() &&
               string.Equals(activeScene.name, "ServerScene", StringComparison.Ordinal);
    }

    #endregion
}
#endif
