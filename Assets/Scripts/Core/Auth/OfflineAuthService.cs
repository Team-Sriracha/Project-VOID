using System;
using System.Threading.Tasks;

/// <summary>
/// 더 이상 오프라인 로그인을 허용하지 않는 비활성 인증 서비스입니다.
/// </summary>
public class OfflineAuthService : IAuthService
{
    #region Constants

    private const string AUTH_UNAVAILABLE_MESSAGE = "오프라인 로그인은 지원되지 않습니다. 인터넷 연결 후 다시 시도해 주세요.";

    #endregion

    #region Private Fields

    private string _userId = string.Empty;
    private string _guestId = string.Empty;
    private string _displayName = string.Empty;
    private string _email = string.Empty;
    private bool _isSignedIn;
    private bool _isAnonymous;

    #endregion

    #region Properties

    /// <inheritdoc />
    public string UserId => _userId;

    /// <inheritdoc />
    public string GuestId => _guestId;

    /// <inheritdoc />
    public string DisplayName => _displayName;

    /// <inheritdoc />
    public string Email => _email;

    /// <inheritdoc />
    public bool IsSignedIn => _isSignedIn;

    /// <inheritdoc />
    public bool IsAnonymous => _isAnonymous;

    /// <inheritdoc />
    public bool IsGoogleSignedIn => false;

    #endregion

    #region Events

    /// <inheritdoc />
    public event Action<AuthResult> OnAuthStateChanged;

    #endregion

    #region Constructor

    /// <summary>
    /// 오프라인 인증 서비스를 생성합니다.
    /// </summary>
    public OfflineAuthService()
    {
    }

    #endregion

    #region Sign In

    /// <inheritdoc />
    public Task<AuthResult> SignInAnonymouslyAsync()
    {
        return Task.FromResult(AuthResult.Failure(AuthErrorCode.NetworkError, AUTH_UNAVAILABLE_MESSAGE));
    }

    /// <inheritdoc />
    public Task<AuthResult> SignInWithGoogleAsync()
    {
        return Task.FromResult(AuthResult.Failure(AuthErrorCode.NotSupported, "Google 로그인은 현재 빌드에서 지원되지 않습니다."));
    }

    /// <inheritdoc />
    public Task<AuthResult> SignInWithAppleAsync()
    {
        return Task.FromResult(AuthResult.Failure(AuthErrorCode.NotSupported, "Apple 로그인은 현재 빌드에서 지원되지 않습니다."));
    }

    #endregion

    #region Session

    /// <inheritdoc />
    public Task SignOutAsync()
    {
        _userId = string.Empty;
        _guestId = string.Empty;
        _displayName = string.Empty;
        _email = string.Empty;
        _isSignedIn = false;
        _isAnonymous = false;

        OnAuthStateChanged?.Invoke(AuthResult.Failure(AuthErrorCode.None, string.Empty));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> GetIdTokenAsync(bool forceRefresh = false)
    {
        return Task.FromResult(string.Empty);
    }

    #endregion

    #region Helper Methods

    private AuthResult ApplySignedInState(string uid, string guestId, string displayName, string email, bool isAnonymous)
    {
        _userId = uid ?? string.Empty;
        _guestId = guestId ?? string.Empty;
        _displayName = displayName ?? string.Empty;
        _email = email ?? string.Empty;
        _isSignedIn = true;
        _isAnonymous = isAnonymous;

        AuthResult result = AuthResult.Success(_userId, _guestId, _displayName);
        OnAuthStateChanged?.Invoke(result);
        return result;
    }

    private static string ResolveGuestIdFromUid(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return "G-00000000";
        }

        return $"G-{GetSafeSuffix(uid, 8)}";
    }

    private static string GetSafeSuffix(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0000";
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed.ToUpperInvariant()
            : trimmed[^maxLength..].ToUpperInvariant();
    }

    #endregion
}
