using System.Threading.Tasks;

/// <summary>
/// 플레이어 데이터 서비스 인터페이스입니다.
/// </summary>
public interface IPlayerDataService
{
    #region Profile

    /// <summary>
    /// 프로필을 조회합니다.
    /// </summary>
    Task<PlayerProfile> GetPlayerProfileAsync(string uid);

    /// <summary>
    /// 프로필을 생성합니다.
    /// </summary>
    Task<PlayerProfile> CreatePlayerProfileAsync(string uid, string displayName, string email, bool isGuest, string guestId = "");

    /// <summary>
    /// 게스트 프로필을 보장합니다.
    /// </summary>
    Task<PlayerProfile> EnsureGuestProfileAsync(string uid);

    /// <summary>
    /// 마지막 로그인 시간을 갱신합니다.
    /// </summary>
    Task UpdateLastLoginAtAsync(string uid);

    /// <summary>
    /// 매치 결과를 백엔드 전적 서비스에 커밋합니다.
    /// 로컬 계산은 수행하지 않습니다.
    /// </summary>
    Task<bool> ApplyMatchResultAsync(ServerMatchResult matchResult);

    #endregion

    #region Display Name

    /// <summary>
    /// 표시 이름을 변경합니다.
    /// </summary>
    Task<bool> UpdateDisplayNameAsync(string uid, string newName);

    /// <summary>
    /// 표시 이름 사용 가능 여부를 반환합니다.
    /// </summary>
    Task<bool> IsDisplayNameAvailableAsync(string name);

    #endregion
}
