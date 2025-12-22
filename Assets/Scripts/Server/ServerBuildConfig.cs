using UnityEngine;

/// <summary>
/// AWS 서버 빌드 설정
/// </summary>
[CreateAssetMenu(fileName = "ServerBuildConfig", menuName = "Project VOID/Server/Server Build Config")]
public class ServerBuildConfig : ScriptableObject
{
    [Header("서버 식별")]
    [Tooltip("서버 ID")]
    public string DefaultServerId = "local-server";

    [Header("네트워크 설정")]
    [Tooltip("기본 포트 번호")]
    public ushort DefaultPort = 27015;

    [Header("세션 설정")]
    [Tooltip("세션당 기본 최대 플레이어 수")]
    public int DefaultMaxPlayersPerSession = 8;

    [Tooltip("세션 타임아웃 (초) - 플레이어가 없으면 자동 종료")]
    public float SessionTimeoutSeconds = 300f;

    [Header("디버그")]
    [Tooltip("상세 로그 출력")]
    public bool VerboseLogging = false;
}
