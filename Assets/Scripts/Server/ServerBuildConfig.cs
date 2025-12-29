using UnityEngine;

/// <summary>
/// 서버 빌드 설정
/// </summary>
[CreateAssetMenu(fileName = "ServerBuildConfig", menuName = "Project VOID/Server/Server Build Config")]
public class ServerBuildConfig : ScriptableObject
{
    [Header("서버 식별")]
    public string DefaultServerId = "local-server";

    [Header("네트워크 설정")]
    [Tooltip("서버 공인 IP (직접 연결용). 비워두면 Photon Relay 사용")]
    public string ServerPublicIP = "";

    [Tooltip("서버 포트 (UDP)")]
    public ushort DefaultPort = 27015;

    [Header("세션 설정")]
    [Tooltip("세션당 최대 플레이어 수")]
    public int DefaultMaxPlayersPerSession = 8;
}
