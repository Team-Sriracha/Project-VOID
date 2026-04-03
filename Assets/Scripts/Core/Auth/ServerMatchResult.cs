using System;
using System.Collections.Generic;

/// <summary>
/// 서버 전적 집계 데이터입니다.
/// </summary>
[Serializable]
public class ServerMatchResult
{
    #region Properties

    /// <summary>
    /// 매치 ID입니다.
    /// </summary>
    public string MatchId { get; set; } = string.Empty;

    /// <summary>
    /// 모드 ID입니다.
    /// </summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>
    /// 시작 시각(UTC)입니다.
    /// </summary>
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 종료 시각(UTC)입니다.
    /// </summary>
    public DateTime EndedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 우승자 UID입니다.
    /// </summary>
    public string WinnerUid { get; set; } = string.Empty;

    /// <summary>
    /// 플레이어 결과 목록입니다.
    /// </summary>
    public List<ServerMatchPlayerResult> Players { get; } = new();

    #endregion
}

/// <summary>
/// 플레이어 단위 매치 결과입니다.
/// </summary>
[Serializable]
public class ServerMatchPlayerResult
{
    #region Properties

    /// <summary>
    /// UID입니다.
    /// </summary>
    public string Uid { get; set; } = string.Empty;

    /// <summary>
    /// 킬 수입니다.
    /// </summary>
    public int Kills { get; set; }

    /// <summary>
    /// 데스 수입니다.
    /// </summary>
    public int Deaths { get; set; }

    /// <summary>
    /// 등수입니다.
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// 게스트 여부입니다.
    /// </summary>
    public bool IsGuest { get; set; }

    /// <summary>
    /// 무승부 판정 여부입니다.
    /// </summary>
    public bool IsDraw { get; set; }

    #endregion
}
