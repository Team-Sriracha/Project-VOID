using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;

public class ServerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        
        // Check for "-monitor" flag
        bool isMonitor = args.Any(arg => arg == "-monitor");

        if (isMonitor)
        {
            Debug.Log("[ServerBootstrap] Starting in MONITOR MODE");
            
            // Create Monitor Object
            GameObject monitorObj = new GameObject("ServerMonitor");
            monitorObj.AddComponent<ServerMonitorHttpServer>();
            Object.DontDestroyOnLoad(monitorObj);

            // Optional: Disable typical game server components if they exist in the initial scene
            // properties like Application.targetFrameRate are set in ServerMonitorHttpServer
        }
        else
        {
            // Normal Game Server Startup
            // If running specifically as a server build (not client), we might want to ensure ServerScene is loaded
            // But usually NetworkManager handles this or specific build settings.
            // Keeping it simple for now as per user request to just "add monitor build".
            Debug.Log("[ServerBootstrap] Starting in GAME SERVER MODE");
        }
    }
}
