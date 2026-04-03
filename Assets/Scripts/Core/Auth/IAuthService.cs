using System;
using System.Threading.Tasks;

/// <summary>
/// 인증 서비스 추상화 인터페이스입니다.
/// </summary>
public interface IAuthService
{
    #region Properties

    /// <summary>
    /// 현재 로그인한 Firebase UID를 반환합니다.
    /// </summary>
    string UserId { get; }

    /// <summary>
    /// 게스트 사용자에게 부여된 표시용 ID를 반환합니다.
    /// </summary>
    string GuestId { get; }

    /// <summary>
    /// 표시 이름을 반환합니다.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// 이메일을 반환합니다.
    /// </summary>
    string Email { get; }

    /// <summary>
    /// 로그인 여부를 반환합니다.
    /// </summary>
    bool IsSignedIn { get; }

    /// <summary>
    /// 익명 계정 여부를 반환합니다.
    /// </summary>
    bool IsAnonymous { get; }

    /// <summary>
    /// 자동 로그인으로 바로 진입 가능한 Google 세션 여부를 반환합니다.
    /// </summary>
    bool IsGoogleSignedIn { get; }

    #endregion

    #region Events

    /// <summary>
    /// 인증 상태 변경 시 호출됩니다.
    /// </summary>
    event Action<AuthResult> OnAuthStateChanged;

    #endregion

    #region Sign In

    /// <summary>
    /// 익명 계정으로 로그인합니다.
    /// </summary>
    Task<AuthResult> SignInAnonymouslyAsync();

    /// <summary>
    /// Google 계정으로 로그인합니다.
    /// </summary>
    Task<AuthResult> SignInWithGoogleAsync();

    /// <summary>
    /// Apple 계정으로 로그인합니다.
    /// </summary>
    Task<AuthResult> SignInWithAppleAsync();

    #endregion

    #region Session

    /// <summary>
    /// 로그아웃합니다.
    /// </summary>
    Task SignOutAsync();

    /// <summary>
    /// 서버 검증용 ID Token을 반환합니다.
    /// </summary>
    Task<string> GetIdTokenAsync(bool forceRefresh = false);

    #endregion
}
