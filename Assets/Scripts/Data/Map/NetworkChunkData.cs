using UnityEngine;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 네트워크로 전송되는 청크 데이터 구조체
    /// 총 15 bytes로 최적화됨
    /// </summary>
    public struct NetworkChunkData
    {
        #region Fields

        /// <summary>
        /// 청크의 그리드 위치 (8 bytes)
        /// </summary>
        public Vector2Int Position;

        /// <summary>
        /// 청크 타입 (1 byte)
        /// </summary>
        public byte ChunkType;

        /// <summary>
        /// 문/벽 정보 비트마스크 (1 byte)
        /// 비트 1: North 문
        /// 비트 2: East 문
        /// 비트 4: South 문
        /// 비트 8: West 문
        /// </summary>
        public byte DoorMask;

        /// <summary>
        /// 청크 그룹 ID (2 bytes)
        /// 같은 그룹 ID를 가진 청크들은 하나의 큰 청크로 취급 (내부 벽 제거)
        /// 0 = 그룹 없음 (독립적인 청크)
        /// </summary>
        public ushort GroupId;

        /// <summary>
        /// 청크가 차지하는 그리드 너비 (1 byte)
        /// </summary>
        public byte ChunkWidth;

        /// <summary>
        /// 청크가 차지하는 그리드 높이 (1 byte)
        /// </summary>
        public byte ChunkHeight;

        /// <summary>
        /// 청크 풀 내에서의 인덱스 (1 byte)
        /// 서버가 선택한 청크를 클라이언트가 정확히 재현하기 위해 사용
        /// </summary>
        public byte ChunkIndex;

        #endregion

        #region Properties

        /// <summary>
        /// 청크 타입을 enum으로 반환합니다.
        /// </summary>
        public ChunkType Type => (ChunkType)ChunkType;

        #endregion

        #region Constructor

        /// <summary>
        /// NetworkChunkData를 생성합니다.
        /// </summary>
        public NetworkChunkData(Vector2Int position, ChunkType type, byte doorMask, int groupId = 0, int chunkWidth = 1, int chunkHeight = 1, int chunkIndex = 0)
        {
            Position = position;
            ChunkType = (byte)type;
            DoorMask = doorMask;
            GroupId = (ushort)groupId;
            ChunkWidth = (byte)chunkWidth;
            ChunkHeight = (byte)chunkHeight;
            ChunkIndex = (byte)chunkIndex;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 특정 방향에 문이 있는지 확인합니다.
        /// </summary>
        public bool HasDoor(Direction direction)
        {
            return DirectionExtensions.HasDirection(DoorMask, direction);
        }

        /// <summary>
        /// 활성화된 모든 문 방향을 반환합니다.
        /// </summary>
        public Direction[] GetDoorDirections()
        {
            return DirectionExtensions.GetActiveDirections(DoorMask);
        }

        #endregion
    }
}
