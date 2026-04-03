/// <summary>
/// 백엔드 토큰 검증 결과 DTO입니다.
/// </summary>
public class VerifiedIdentity
{
    #region Properties

    /// <summary>
    /// UID입니다.
    /// </summary>
    public string FirebaseUid { get; set; } = string.Empty;

    /// <summary>
    /// 게스트 표시 ID입니다.
    /// </summary>
    public string GuestId { get; set; } = string.Empty;

    /// <summary>
    /// 표시 이름입니다.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 이메일입니다.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 익명 계정 여부입니다.
    /// </summary>
    public bool IsAnonymous { get; set; }

    /// <summary>
    /// 유효성 여부입니다.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 실패 메시지입니다.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    #endregion
}
