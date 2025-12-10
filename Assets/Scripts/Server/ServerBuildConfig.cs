using UnityEngine;

/// <summary>
/// AWS 서버 빌드 설정
/// </summary>
[CreateAssetMenu(fileName = "ServerBuildConfig", menuName = "Project VOID/Server/Server Build Config")]
public class ServerBuildConfig : ScriptableObject
{
    [Header("서버 식별")]
    [Tooltip("서버 ID (AWS에서 환경변수로 덮어쓸 수 있음)")]
    public string DefaultServerId = "local-server";

    [Header("네트워크 설정")]
    [Tooltip("기본 포트 번호")]
    public int BasePort = 27015;

    [Tooltip("인스턴스당 최대 동시 접속 플레이어 수")]
    public int MaxConcurrentPlayers = 40;

    [Header("게임 설정")]
    [Tooltip("세션당 기본 최대 플레이어 수")]
    public int DefaultMaxPlayersPerSession = 8;

    [Tooltip("세션 타임아웃 (초) - 플레이어가 없으면 자동 종료")]
    public float SessionTimeoutSeconds = 300f;

    [Header("AWS 설정")]
    [Tooltip("AWS 리전")]
    public string AwsRegion = "ap-northeast-2";

    [Tooltip("CloudWatch 로깅 활성화")]
    public bool EnableCloudWatchLogs = true;

    [Header("디버그")]
    [Tooltip("상세 로그 출력")]
    public bool VerboseLogging = false;
}
