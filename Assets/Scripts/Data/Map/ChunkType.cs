namespace ProjectVoid.Map
{
    /// <summary>
    /// 청크 타입을 나타내는 열거형
    /// 문/벽 배치 절차적 생성. 타입은 시각적/게임플레이 차별화 목적
    /// </summary>
    public enum ChunkType : byte
    {
        /// <summary>
        /// 중앙 허브 청크 - 주요 전투 지역, 넓고 화려한 디자인
        /// (조명, 장식물, 특수 바닥 타일 등)
        /// </summary>
        Central = 0,

        /// <summary>
        /// 일반 청크 - 표준 디자인
        /// (기본 바닥 타일, 일반 조명, 통로/방)
        /// </summary>
        Normal = 1,

        /// <summary>
        /// 스페셜 청크 - 특별한 용도의 방
        /// (보물 상자, 특수 아이템, 은신처, 보스방 등)
        /// </summary>
        Special = 2
    }
}
