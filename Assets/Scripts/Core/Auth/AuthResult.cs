/// <summary>
/// 인증 에러 코드입니다.
/// </summary>
public enum AuthErrorCode
{
    None = 0,
    InvalidEmail = 1,
    WrongPassword = 2,
    UserNotFound = 3,
    EmailAlreadyInUse = 4,
    WeakPassword = 5,
    NetworkError = 6,
    DuplicateDisplayName = 7,
    NotSupported = 8,
    Unknown = 999
}

/// <summary>
/// 인증 처리 결과입니다.
/// </summary>
public readonly struct AuthResult
{
    #region Properties

    /// <summary>
    /// 성공 여부입니다.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// UID입니다.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// 게스트 ID입니다.
    /// </summary>
    public string GuestId { get; }

    /// <summary>
    /// 표시 이름입니다.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 에러 메시지입니다.
    /// </summary>
    public string ErrorMessage { get; }

    /// <summary>
    /// 에러 코드입니다.
    /// </summary>
    public AuthErrorCode ErrorCode { get; }

    #endregion

    #region Constructor

    private AuthResult(
        bool isSuccess,
        string userId,
        string guestId,
        string displayName,
        string errorMessage,
        AuthErrorCode errorCode)
    {
        IsSuccess = isSuccess;
        UserId = userId ?? string.Empty;
        GuestId = guestId ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        ErrorMessage = errorMessage ?? string.Empty;
        ErrorCode = errorCode;
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// 성공 결과를 생성합니다.
    /// </summary>
    public static AuthResult Success(string userId, string guestId, string displayName)
    {
        return new AuthResult(true, userId, guestId, displayName, string.Empty, AuthErrorCode.None);
    }

    /// <summary>
    /// 실패 결과를 생성합니다.
    /// </summary>
    public static AuthResult Failure(AuthErrorCode errorCode, string errorMessage)
    {
        return new AuthResult(false, string.Empty, string.Empty, string.Empty, errorMessage, errorCode);
    }

    #endregion
}
