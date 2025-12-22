using UnityEngine;

/// <summary>
/// 서버 풀 설정 (ScriptableObject)
/// </summary>
[CreateAssetMenu(fileName = "ServerPoolConfig", menuName = "Project VOID/Server/Server Pool Config")]
public class ServerPoolConfig : ScriptableObject
{
    [Header("서버 풀")]
    [Tooltip("서버 목록")]
    public ServerInfo[] ServerPool = new ServerInfo[]
    {
        new ServerInfo
        {
            ServerId = "local-server", // Why: 실행 중인 서버의 ID와 일치 (Server_local-server_XXX)
            IpAddress = "127.0.0.1",  // 로컬 Editor 서버
            Port = 27016,
            Region = "Local"
            // MaxPlayers는 ServerBuildConfig에서만 관리
        }
    };

    /// <summary>
    /// 서버 ID로 서버 정보 가져오기
    /// </summary>
    public ServerInfo? GetServerById(string serverId)
    {
        foreach (var server in ServerPool)
        {
            if (server.ServerId == serverId)
                return server;
        }
        return null;
    }
}
