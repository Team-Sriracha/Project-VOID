using System.Threading.Tasks;

/// <summary>
/// 서버 토큰 검증 인터페이스입니다.
/// </summary>
public interface IIdentityVerificationService
{
    /// <summary>
    /// ID Token을 검증합니다.
    /// </summary>
    Task<VerifiedIdentity> VerifyIdTokenAsync(string idToken);
}
