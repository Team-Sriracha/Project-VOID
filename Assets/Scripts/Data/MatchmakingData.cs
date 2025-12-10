using Fusion;

/// <summary>
/// 게임 모드 열거형
/// </summary>
public enum EGameMode
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
public enum EMatchmakingState
{
    Idle,                   // 대기
    InitializingLobby,      // 로비 초기화 중
    SearchingPlayers,       // 플레이어 검색 중
    CreatingCustomRoom,     // 커스텀 방 생성 중
    WaitingInCustomRoom,    // 커스텀 방 대기 중
    JoiningCustomRoom,      // 커스텀 방 입장 중
    SelectingServer,        // 서버 선택 중
    ConnectingToServer,     // 서버 연결 중
    Failed,                 // 실패
    Completed               // 완료
}

/// <summary>
/// 서버 정보
/// </summary>
[System.Serializable]
public struct ServerInfo
{
    public string ServerId;       // "aws-server-1", "aws-server-2" 등
    public string IpAddress;      // EC2 Public IP (예: "3.37.177.239")
    public int Port;              // 서버 포트 (기본: 27015)
    public string Region;         // "Asia", "US", "EU" 등
    public int MaxPlayers;        // 동시 접속 가능한 최대 플레이어 수
}

/// <summary>
/// 게임 연결 정보
/// </summary>
[System.Serializable]
public struct GameConnectionInfo
{
    public string SessionName;     // Photon 세션 이름
    public ServerInfo ServerInfo;  // AWS 서버 정보
    public EGameMode GameMode;     // 게임 모드
    public int MaxPlayers;         // 최대 플레이어 수
}
