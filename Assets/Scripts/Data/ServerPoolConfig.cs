using UnityEngine;

/// <summary>
/// AWS 서버 풀 설정 (ScriptableObject)
/// </summary>
[CreateAssetMenu(fileName = "ServerPoolConfig", menuName = "Project VOID/Server/Server Pool Config")]
public class ServerPoolConfig : ScriptableObject
{
    [Header("서버 풀")]
    [Tooltip("AWS EC2 서버 목록")]
    public ServerInfo[] ServerPool = new ServerInfo[]
    {
        new ServerInfo
        {
            ServerId = "aws-server-1",
            IpAddress = "YOUR_SERVER_IP",  // EC2 Public IP
            Port = 27015,
            Region = "Asia",
            MaxPlayers = 40  // 동시 접속 가능한 최대 플레이어 수 (8명 세션 5개)
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
