using System.Threading.Tasks;

/// <summary>
/// 서버 전적 반영 인터페이스입니다.
/// </summary>
public interface IServerMatchResultService
{
    /// <summary>
    /// 매치 결과를 반영합니다.
    /// </summary>
    Task<bool> CommitMatchResultAsync(ServerMatchResult result);
}
