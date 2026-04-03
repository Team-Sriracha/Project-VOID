using System;
using System.Collections.Generic;

/// <summary>
/// 모드별 전적 데이터입니다.
/// </summary>
[Serializable]
public class PlayerModeStats
{
    #region Properties

    /// <summary>
    /// 누적 매치 수입니다.
    /// </summary>
    public int TotalMatches { get; set; }

    /// <summary>
    /// 누적 승리 수입니다.
    /// </summary>
    public int Wins { get; set; }

    /// <summary>
    /// 누적 무승부 수입니다.
    /// </summary>
    public int Draws { get; set; }

    /// <summary>
    /// 누적 킬 수입니다.
    /// </summary>
    public int Kills { get; set; }

    /// <summary>
    /// 누적 데스 수입니다.
    /// </summary>
    public int Deaths { get; set; }

    #endregion
}

/// <summary>
/// 최근 경기 요약 데이터입니다.
/// </summary>
[Serializable]
public class RecentMatchSummary
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
    /// 순위입니다.
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// 킬 수입니다.
    /// </summary>
    public int Kills { get; set; }

    /// <summary>
    /// 데스 수입니다.
    /// </summary>
    public int Deaths { get; set; }

    /// <summary>
    /// 랭크 점수 증감값입니다.
    /// </summary>
    public int MmrDelta { get; set; }

    /// <summary>
    /// 승리 여부입니다.
    /// </summary>
    public bool IsWin { get; set; }

    /// <summary>
    /// 무승부 여부입니다.
    /// </summary>
    public bool IsDraw { get; set; }

    /// <summary>
    /// 경기 종료 시각(UTC)입니다.
    /// </summary>
    public DateTime EndedAtUtc { get; set; } = DateTime.UtcNow;

    #endregion
}

/// <summary>
/// 플레이어 프로필 데이터입니다.
/// </summary>
[Serializable]
public class PlayerProfile
{
    #region Properties

    /// <summary>
    /// UID입니다.
    /// </summary>
    public string Uid { get; set; } = string.Empty;

    /// <summary>
    /// 게스트 표시용 ID입니다. (게스트 세션 런타임 정보)
    /// </summary>
    public string GuestId { get; set; } = string.Empty;

    /// <summary>
    /// 게스트 계정 여부입니다. (게스트 세션 런타임 정보)
    /// </summary>
    public bool IsGuest { get; set; }

    /// <summary>
    /// 표시 이름입니다.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 표시 이름 확정 여부입니다.
    /// </summary>
    public bool IsDisplayNameConfirmed { get; set; }

    /// <summary>
    /// 이메일입니다.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// 생성 시각(UTC)입니다.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 마지막 로그인 시각(UTC)입니다.
    /// </summary>
    public DateTime LastLoginAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 누적 매치 수입니다.
    /// </summary>
    public int TotalMatches { get; set; }

    /// <summary>
    /// 누적 승리 수입니다.
    /// </summary>
    public int Wins { get; set; }

    /// <summary>
    /// 누적 무승부 수입니다.
    /// </summary>
    public int Draws { get; set; }

    /// <summary>
    /// 누적 킬 수입니다.
    /// </summary>
    public int Kills { get; set; }

    /// <summary>
    /// 누적 데스 수입니다.
    /// </summary>
    public int Deaths { get; set; }

    /// <summary>
    /// 일반 모드 전적입니다.
    /// </summary>
    public PlayerModeStats NormalModeStats { get; set; } = new PlayerModeStats();

    /// <summary>
    /// 랭크 모드 전적입니다.
    /// </summary>
    public PlayerModeStats RankedModeStats { get; set; } = new PlayerModeStats();

    /// <summary>
    /// 현재 랭크 MMR입니다.
    /// </summary>
    public int RankedMmr { get; set; } = 0;

    /// <summary>
    /// 랭크 시즌 최고 MMR입니다.
    /// </summary>
    public int RankedSeasonBestMmr { get; set; } = 0;

    /// <summary>
    /// 랭크 티어입니다.
    /// </summary>
    public string RankedTier { get; set; } = "Rookie";

    /// <summary>
    /// 최근 경기 목록입니다.
    /// </summary>
    public List<RecentMatchSummary> RecentMatches { get; set; } = new List<RecentMatchSummary>();

    /// <summary>
    /// 중복 반영 방지용 최근 처리 매치 ID 목록입니다.
    /// </summary>
    public List<string> ProcessedMatchIds { get; set; } = new List<string>();

    /// <summary>
    /// 마지막 경기 반영 시각(UTC)입니다.
    /// </summary>
    public DateTime LastMatchAtUtc { get; set; } = DateTime.UtcNow;

    #endregion
}
