using FishNet.Connection;

/// <summary>
/// 게임 모드 열거형
/// </summary>
public enum GameMode
{
    None,
    FourPlayer,     // 4인 매칭
    EightPlayer,    // 8인 매칭
    Custom,         // 커스텀 방
    PracticeRange   // 연습장
}

/// <summary>
/// 매칭 상태
/// </summary>
public enum MatchmakingState
{
    Idle,                   // 대기
    InitializingLobby,      // 로비 초기화 중
    SearchingPlayers,       // 플레이어 검색 중
    CreatingCustomRoom,     // 커스텀 방 생성 중
    WaitingInCustomRoom,    // 커스텀 방 대기 중
    JoiningCustomRoom,      // 커스텀 방 입장 중
    SelectingServer,        // 서버 선택 중
    ConnectingToServer,     // 서버 연결 중
    WaitingForPlayers,      // Lobby에서 플레이어 대기 중
    Failed,                 // 실패
    Completed,              // 완료
    InGame                  // 게임 진행 중 (연습장 포함)
}

/// <summary>
/// 서버 정보
/// </summary>
[System.Serializable]
public struct ServerInfo
{
    public string ServerId;       // "local-server", "server-1" 등
    public string IpAddress;      // Public IP
    public int Port;              // 서버 포트 (기본: 27015)
    public string Region;         // "Asia", "US", "EU" 등
    
    // 별칭 프로퍼티
    public string ServerAddress => IpAddress;
}

/// <summary>
/// 게임 연결 정보
/// </summary>
[System.Serializable]
public struct GameConnectionInfo
{
    public string SessionName;     // 세션 이름
    public ServerInfo ServerInfo;  // 서버 정보
    public GameMode GameMode;     // 게임 모드
    public int MaxPlayers;         // 최대 플레이어 수
    public string RoomCode;        // 방 코드 (커스텀 모드)

    // 직접 연결용
    public string DirectServerIP;  // 서버 공인 IP
    public int DirectServerPort;   // 서버 포트

    /// <summary>
    /// 직접 연결 사용 여부
    /// </summary>
    public bool UseDirectConnection => !string.IsNullOrEmpty(DirectServerIP);
}
