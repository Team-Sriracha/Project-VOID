using System;
using System.Threading.Tasks;

/// <summary>
/// Google 토큰 제공자가 연결되지 않았을 때 사용하는 기본 구현체입니다.
/// </summary>
public class UnsupportedGoogleAuthTokenProvider : IGoogleAuthTokenProvider
{
    /// <inheritdoc />
    public Task<SocialAuthToken> RequestGoogleTokenAsync()
    {
        throw new NotSupportedException("Google 로그인 토큰 제공자가 연결되지 않았습니다. 플러그인 구현체를 ServiceLocator에 등록해 주세요.");
    }
}

/// <summary>
/// Apple 토큰 제공자가 연결되지 않았을 때 사용하는 기본 구현체입니다.
/// </summary>
public class UnsupportedAppleAuthTokenProvider : IAppleAuthTokenProvider
{
    /// <inheritdoc />
    public Task<SocialAuthToken> RequestAppleTokenAsync()
    {
        throw new NotSupportedException("Apple 로그인 토큰 제공자가 연결되지 않았습니다. 플러그인 구현체를 ServiceLocator에 등록해 주세요.");
    }
}
