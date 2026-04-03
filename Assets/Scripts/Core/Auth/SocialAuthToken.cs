using System.Threading.Tasks;

/// <summary>
/// 소셜 로그인 토큰 데이터입니다.
/// </summary>
public readonly struct SocialAuthToken
{
    #region Properties

    /// <summary>
    /// ID 토큰입니다.
    /// </summary>
    public string IdToken { get; }

    /// <summary>
    /// 액세스 토큰입니다.
    /// </summary>
    public string AccessToken { get; }

    /// <summary>
    /// Apple 로그인 raw nonce입니다.
    /// </summary>
    public string RawNonce { get; }

    #endregion

    #region Constructor

    /// <summary>
    /// 토큰 구조체를 생성합니다.
    /// </summary>
    public SocialAuthToken(string idToken, string accessToken, string rawNonce = "")
    {
        IdToken = idToken ?? string.Empty;
        AccessToken = accessToken ?? string.Empty;
        RawNonce = rawNonce ?? string.Empty;
    }

    #endregion
}

/// <summary>
/// Google 로그인 토큰 제공자 인터페이스입니다.
/// </summary>
public interface IGoogleAuthTokenProvider
{
    /// <summary>
    /// Google 로그인 토큰을 요청합니다.
    /// </summary>
    Task<SocialAuthToken> RequestGoogleTokenAsync();
}

/// <summary>
/// Apple 로그인 토큰 제공자 인터페이스입니다.
/// </summary>
public interface IAppleAuthTokenProvider
{
    /// <summary>
    /// Apple 로그인 토큰을 요청합니다.
    /// </summary>
    Task<SocialAuthToken> RequestAppleTokenAsync();
}
