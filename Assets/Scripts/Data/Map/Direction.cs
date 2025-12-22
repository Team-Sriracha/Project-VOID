using UnityEngine;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 4방향을 나타내는 열거형
    /// </summary>
    public enum Direction
    {
        None = 0,
        North = 1,
        East = 2,
        South = 4,
        West = 8
    }

    /// <summary>
    /// Direction 확장 메서드
    /// </summary>
    public static class DirectionExtensions
    {
        #region Constants

        private static readonly Vector2Int NORTH_OFFSET = new Vector2Int(0, 1);
        private static readonly Vector2Int EAST_OFFSET = new Vector2Int(1, 0);
        private static readonly Vector2Int SOUTH_OFFSET = new Vector2Int(0, -1);
        private static readonly Vector2Int WEST_OFFSET = new Vector2Int(-1, 0);

        #endregion

        #region Public Methods

        /// <summary>
        /// 방향을 Vector2Int 오프셋으로 변환합니다.
        /// </summary>
        public static Vector2Int ToOffset(this Direction direction)
        {
            return direction switch
            {
                Direction.North => NORTH_OFFSET,
                Direction.East => EAST_OFFSET,
                Direction.South => SOUTH_OFFSET,
                Direction.West => WEST_OFFSET,
                _ => Vector2Int.zero
            };
        }

        /// <summary>
        /// 반대 방향을 반환합니다.
        /// </summary>
        public static Direction GetOpposite(this Direction direction)
        {
            return direction switch
            {
                Direction.North => Direction.South,
                Direction.East => Direction.West,
                Direction.South => Direction.North,
                Direction.West => Direction.East,
                _ => Direction.None
            };
        }

        /// <summary>
        /// 모든 유효한 방향을 반환합니다.
        /// </summary>
        public static Direction[] GetAllDirections()
        {
            return new[]
            {
                Direction.North,
                Direction.East,
                Direction.South,
                Direction.West
            };
        }

        /// <summary>
        /// 비트마스크에서 활성화된 방향들을 추출합니다.
        /// </summary>
        public static Direction[] GetActiveDirections(byte doorMask)
        {
            var directions = new System.Collections.Generic.List<Direction>();

            if ((doorMask & (byte)Direction.North) != 0) directions.Add(Direction.North);
            if ((doorMask & (byte)Direction.East) != 0) directions.Add(Direction.East);
            if ((doorMask & (byte)Direction.South) != 0) directions.Add(Direction.South);
            if ((doorMask & (byte)Direction.West) != 0) directions.Add(Direction.West);

            return directions.ToArray();
        }

        /// <summary>
        /// 특정 방향이 비트마스크에 포함되어 있는지 확인합니다.
        /// </summary>
        public static bool HasDirection(byte doorMask, Direction direction)
        {
            return (doorMask & (byte)direction) != 0;
        }

        #endregion
    }
}
