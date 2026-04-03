/// <summary>
/// 서버 런타임 공용 정보입니다.
/// 전용 서버 빌드와 공용 게임 코드 사이의 최소 공유 상태만 둡니다.
/// </summary>
public static class ServerRuntimeInfo
{
    #region Constants

    private const int DEFAULT_SERVER_PORT = 7777;

    #endregion

    #region Properties

    /// <summary>
    /// 현재 서버 포트입니다.
    /// </summary>
    public static int ServerPort { get; private set; } = DEFAULT_SERVER_PORT;

    /// <summary>
    /// 현재 서버 바인드 IP입니다.
    /// </summary>
    public static string ServerIp { get; private set; } = string.Empty;

    #endregion

    #region Public Methods

    /// <summary>
    /// 서버 포트를 갱신합니다.
    /// </summary>
    public static void SetServerPort(int port)
    {
        if (port > 0)
        {
            ServerPort = port;
        }
    }

    /// <summary>
    /// 서버 IP를 갱신합니다.
    /// </summary>
    public static void SetServerIp(string ipAddress)
    {
        ServerIp = string.IsNullOrWhiteSpace(ipAddress)
            ? string.Empty
            : ipAddress.Trim();
    }

    #endregion
}
