using System.Threading.Tasks;

/// <summary>
/// 온라인 인증이 준비되지 않았을 때 빈 결과만 반환하는 데이터 서비스입니다.
/// </summary>
public sealed class UnavailablePlayerDataService : IPlayerDataService
{
    #region Profile

    /// <inheritdoc />
    public Task<PlayerProfile> GetPlayerProfileAsync(string uid)
    {
        return Task.FromResult<PlayerProfile>(null);
    }

    /// <inheritdoc />
    public Task<PlayerProfile> CreatePlayerProfileAsync(string uid, string displayName, string email, bool isGuest, string guestId = "")
    {
        return Task.FromResult<PlayerProfile>(null);
    }

    /// <inheritdoc />
    public Task<PlayerProfile> EnsureGuestProfileAsync(string uid)
    {
        return Task.FromResult<PlayerProfile>(null);
    }

    /// <inheritdoc />
    public Task UpdateLastLoginAtAsync(string uid)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ApplyMatchResultAsync(ServerMatchResult matchResult)
    {
        return Task.FromResult(false);
    }

    #endregion

    #region Display Name

    /// <inheritdoc />
    public Task<bool> UpdateDisplayNameAsync(string uid, string newName)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> IsDisplayNameAvailableAsync(string name)
    {
        return Task.FromResult(false);
    }

    #endregion
}
