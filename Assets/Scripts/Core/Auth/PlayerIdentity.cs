using System;

/// <summary>
/// 서버에서 확정한 플레이어 신원 정보입니다.
/// </summary>
[Serializable]
public class PlayerIdentity
{
    #region Properties

    /// <summary>
    /// Firebase UID입니다.
    /// </summary>
    public string FirebaseUid { get; private set; }

    /// <summary>
    /// 게스트 표시용 ID입니다.
    /// </summary>
    public string GuestId { get; private set; }

    /// <summary>
    /// 표시 이름입니다.
    /// </summary>
    public string DisplayName { get; private set; }

    /// <summary>
    /// 익명 계정 여부입니다.
    /// </summary>
    public bool IsAnonymous { get; private set; }

    /// <summary>
    /// 검증 완료 UTC 시각입니다.
    /// </summary>
    public DateTime VerifiedAtUtc { get; private set; }

    #endregion

    #region Constructor

    /// <summary>
    /// 기본 생성자입니다.
    /// </summary>
    public PlayerIdentity()
    {
        FirebaseUid = string.Empty;
        GuestId = string.Empty;
        DisplayName = string.Empty;
        VerifiedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// 신원 객체를 생성합니다.
    /// </summary>
    public PlayerIdentity(string firebaseUid, string displayName, string guestId, bool isAnonymous, DateTime verifiedAtUtc)
    {
        FirebaseUid = firebaseUid ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        GuestId = guestId ?? string.Empty;
        IsAnonymous = isAnonymous;
        VerifiedAtUtc = verifiedAtUtc;
    }

    #endregion
}
