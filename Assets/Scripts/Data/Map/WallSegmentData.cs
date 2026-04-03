namespace ProjectVoid.Map
{
    /// <summary>
    /// 벽/문 구간 정보를 담는 구조체 (총 6 bytes)
    /// </summary>
    public struct WallSegmentData
    {
        /// <summary>
        /// 청크 인덱스 (ChunkArray에서의 인덱스, 0~65535)
        /// </summary>
        public ushort ChunkIndex;

        /// <summary>
        /// 방향 (0=None, 1=North, 2=East, 4=South, 8=West)
        /// </summary>
        public byte Direction;

        /// <summary>
        /// 시작 벽 인덱스 (0~255)
        /// </summary>
        public byte StartIndex;

        /// <summary>
        /// 종료 벽 인덱스 (0~255)
        /// </summary>
        public byte EndIndex;

        /// <summary>
        /// 구간 타입 (0=없음, 1=벽, 2=문)
        /// </summary>
        public byte SegmentType;

        public WallSegmentData(int chunkIndex, Direction direction, int startIndex, int endIndex, bool isDoor)
        {
            ChunkIndex = (ushort)chunkIndex;
            Direction = (byte)direction;
            StartIndex = (byte)startIndex;
            EndIndex = (byte)endIndex;
            SegmentType = (byte)(isDoor ? 2 : 1);
        }

        /// <summary>
        /// ChunkIndex 없이 생성 (나중에 MapGenerator에서 설정)
        /// </summary>
        public WallSegmentData(Direction direction, int startIndex, int endIndex, bool isDoor) : this(0, direction, startIndex, endIndex, isDoor)
        {
        }

        /// <summary>
        /// 빈 구간인지 확인
        /// </summary>
        public bool IsEmpty => SegmentType == 0;

        /// <summary>
        /// 문 구간인지 확인
        /// </summary>
        public bool IsDoor => SegmentType == 2;

        /// <summary>
        /// 방향을 enum으로 반환
        /// </summary>
        public Direction GetDirection() => (Direction)Direction;
    }
}
