using System;
using System.Threading.Tasks;

/// <summary>
/// 온라인 인증이 준비되지 않았을 때 실패만 반환하는 인증 서비스입니다.
/// </summary>
public sealed class UnavailableAuthService : IAuthService
{
    #region Constants

    private const string AUTH_UNAVAILABLE_MESSAGE = "인터넷 연결 또는 Firebase 초기화가 준비되지 않아 로그인할 수 없습니다.";

    #endregion

    #region Properties

    private readonly string _unavailableMessage;

    /// <inheritdoc />
    public string UserId => string.Empty;

    /// <inheritdoc />
    public string GuestId => string.Empty;

    /// <inheritdoc />
    public string DisplayName => string.Empty;

    /// <inheritdoc />
    public string Email => string.Empty;

    /// <inheritdoc />
    public bool IsSignedIn => false;

    /// <inheritdoc />
    public bool IsAnonymous => false;

    /// <inheritdoc />
    public bool IsGoogleSignedIn => false;

    #endregion

    #region Events

    /// <inheritdoc />
    public event Action<AuthResult> OnAuthStateChanged;

    #endregion

    #region Constructor

    public UnavailableAuthService()
        : this(AUTH_UNAVAILABLE_MESSAGE)
    {
    }

    public UnavailableAuthService(string unavailableMessage)
    {
        _unavailableMessage = string.IsNullOrWhiteSpace(unavailableMessage)
            ? AUTH_UNAVAILABLE_MESSAGE
            : unavailableMessage.Trim();
    }

    #endregion

    #region Sign In

    /// <inheritdoc />
    public Task<AuthResult> SignInAnonymouslyAsync()
    {
        return Task.FromResult(AuthResult.Failure(AuthErrorCode.NetworkError, _unavailableMessage));
    }

    /// <inheritdoc />
    public Task<AuthResult> SignInWithGoogleAsync()
    {
        return Task.FromResult(AuthResult.Failure(AuthErrorCode.NetworkError, _unavailableMessage));
    }

    /// <inheritdoc />
    public Task<AuthResult> SignInWithAppleAsync()
    {
        return Task.FromResult(AuthResult.Failure(AuthErrorCode.NetworkError, _unavailableMessage));
    }

    #endregion

    #region Session

    /// <inheritdoc />
    public Task SignOutAsync()
    {
        OnAuthStateChanged?.Invoke(AuthResult.Failure(AuthErrorCode.None, string.Empty));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> GetIdTokenAsync(bool forceRefresh = false)
    {
        return Task.FromResult(string.Empty);
    }

    #endregion
}
