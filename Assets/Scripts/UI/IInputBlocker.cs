/// <summary>
/// 플레이어의 입력(조준, 발사 등)을 차단해야 하는 UI 요소가 구현합니다.
/// </summary>
public interface IInputBlocker
{
    /// <summary>
    /// 현재 입력 차단 여부를 반환합니다.
    /// </summary>
    bool ShouldBlockInput();
}
