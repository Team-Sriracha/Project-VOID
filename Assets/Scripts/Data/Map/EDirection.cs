using UnityEngine;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 4방향을 나타내는 열거형
    /// </summary>
    public enum EDirection
    {
        None = 0,
        North = 1,
        East = 2,
        South = 4,
        West = 8
    }

    /// <summary>
    /// EDirection 확장 메서드
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
        public static Vector2Int ToOffset(this EDirection direction)
        {
            return direction switch
            {
                EDirection.North => NORTH_OFFSET,
                EDirection.East => EAST_OFFSET,
                EDirection.South => SOUTH_OFFSET,
                EDirection.West => WEST_OFFSET,
                _ => Vector2Int.zero
            };
        }

        /// <summary>
        /// 반대 방향을 반환합니다.
        /// </summary>
        public static EDirection GetOpposite(this EDirection direction)
        {
            return direction switch
            {
                EDirection.North => EDirection.South,
                EDirection.East => EDirection.West,
                EDirection.South => EDirection.North,
                EDirection.West => EDirection.East,
                _ => EDirection.None
            };
        }

        /// <summary>
        /// 모든 유효한 방향을 반환합니다.
        /// </summary>
        public static EDirection[] GetAllDirections()
        {
            return new[]
            {
                EDirection.North,
                EDirection.East,
                EDirection.South,
                EDirection.West
            };
        }

        /// <summary>
        /// 비트마스크에서 활성화된 방향들을 추출합니다.
        /// </summary>
        public static EDirection[] GetActiveDirections(byte doorMask)
        {
            var directions = new System.Collections.Generic.List<EDirection>();

            if ((doorMask & (byte)EDirection.North) != 0) directions.Add(EDirection.North);
            if ((doorMask & (byte)EDirection.East) != 0) directions.Add(EDirection.East);
            if ((doorMask & (byte)EDirection.South) != 0) directions.Add(EDirection.South);
            if ((doorMask & (byte)EDirection.West) != 0) directions.Add(EDirection.West);

            return directions.ToArray();
        }

        /// <summary>
        /// 특정 방향이 비트마스크에 포함되어 있는지 확인합니다.
        /// </summary>
        public static bool HasDirection(byte doorMask, EDirection direction)
        {
            return (doorMask & (byte)direction) != 0;
        }

        #endregion
    }
}
