namespace ProjectVoid.Map
{
    /// <summary>
    /// 맵 레이아웃 그리드 셀 타입
    /// </summary>
    public enum GridCell
    {
        /// <summary>
        /// 빈 공간 (청크 생성 안 함)
        /// </summary>
        Empty = 0,

        /// <summary>
        /// 중앙 청크
        /// </summary>
        Central = 1,

        /// <summary>
        /// 일반 청크
        /// </summary>
        Normal = 2,

        /// <summary>
        /// 스페셜 청크
        /// </summary>
        Special = 3
    }
}
