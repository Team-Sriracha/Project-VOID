using System.Collections.Generic;
using UnityEngine;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 맵 레이아웃의 제약 조건을 분석하고 적합한 청크를 선택하는 클래스
    /// </summary>
    public class ConstraintSolver
    {
        #region Private Fields

        private MapLayoutTemplate _template;
        private System.Random _random;

        #endregion

        #region Constructor

        public ConstraintSolver(MapLayoutTemplate template, System.Random random)
        {
            _template = template;
            _random = random;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 주어진 위치에 필요한 문 방향을 계산합니다.
        /// </summary>
        public byte CalculateRequiredDoors(int x, int y)
        {
            byte doorMask = 0;

            // 4방향 이웃 확인
            foreach (EDirection direction in DirectionExtensions.GetAllDirections())
            {
                Vector2Int offset = direction.ToOffset();
                int neighborX = x + offset.x;
                int neighborY = y + offset.y;

                // 이웃 위치가 유효하고 청크가 있으면 해당 방향에 문 필요
                if (_template.IsValidPosition(neighborX, neighborY))
                {
                    EGridCell neighborCell = _template.GetCell(neighborX, neighborY);
                    if (neighborCell != EGridCell.Empty)
                    {
                        // 해당 방향에 문 필요
                        doorMask |= (byte)(1 << (int)direction);
                    }
                }
            }

            return doorMask;
        }

        /// <summary>
        /// 필요한 문 조건을 만족하는 청크를 찾습니다.
        /// </summary>
        public ChunkPrefabData FindMatchingChunk(
            int x,
            int y,
            byte requiredDoors,
            ChunkPrefabData[] chunkPool)
        {
            if (chunkPool == null || chunkPool.Length == 0)
            {
                Debug.LogError("청크 풀이 비어있습니다!");
                return null;
            }

            // 조건을 만족하는 청크 목록
            List<ChunkPrefabData> matchingChunks = new List<ChunkPrefabData>();

            foreach (var chunk in chunkPool)
            {
                if (chunk == null || chunk.ChunkPrefab == null)
                    continue;

                // 청크가 모든 필수 문을 가지고 있는지 확인
                if (HasRequiredDoors(chunk.DoorMask, requiredDoors))
                {
                    matchingChunks.Add(chunk);
                }
            }

            if (matchingChunks.Count == 0)
            {
                Debug.LogWarning($"위치 ({x}, {y})에 맞는 청크를 찾을 수 없습니다! " +
                    $"필요한 문: {DoorMaskToString(requiredDoors)}");
                return null;
            }

            // 가중치를 고려하여 랜덤 선택
            return SelectWeightedRandom(matchingChunks);
        }

        /// <summary>
        /// 그리드 셀 타입에 따라 적절한 청크 풀을 반환합니다.
        /// </summary>
        public ChunkPrefabData[] GetChunkPoolForCell(
            EGridCell cellType,
            MapGenerationSettings settings)
        {
            return cellType switch
            {
                EGridCell.Central => settings.CentralChunks,
                EGridCell.Normal => settings.NormalChunks,
                EGridCell.Special => settings.SpecialChunks,
                _ => null
            };
        }

        /// <summary>
        /// 외곽 위치인지 확인합니다.
        /// </summary>
        public bool IsEdgePosition(int x, int y)
        {
            return x == 0 || x == _template.Width - 1 ||
                   y == 0 || y == _template.Height - 1;
        }

        /// <summary>
        /// 외곽에서 바깥을 향하는 방향을 반환합니다.
        /// </summary>
        public List<EDirection> GetOutwardDirections(int x, int y)
        {
            List<EDirection> outwardDirs = new List<EDirection>();

            if (x == 0) outwardDirs.Add(EDirection.West);
            if (x == _template.Width - 1) outwardDirs.Add(EDirection.East);
            if (y == 0) outwardDirs.Add(EDirection.South);
            if (y == _template.Height - 1) outwardDirs.Add(EDirection.North);

            return outwardDirs;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 청크가 필요한 모든 문을 가지고 있는지 확인합니다.
        /// </summary>
        private bool HasRequiredDoors(byte chunkDoors, byte requiredDoors)
        {
            // 비트 AND 연산: 필요한 모든 문이 청크에 있는지 확인
            return (chunkDoors & requiredDoors) == requiredDoors;
        }

        /// <summary>
        /// 가중치를 고려하여 랜덤 선택합니다.
        /// </summary>
        private ChunkPrefabData SelectWeightedRandom(List<ChunkPrefabData> chunks)
        {
            // 전체 가중치 계산
            float totalWeight = 0f;
            foreach (var chunk in chunks)
            {
                totalWeight += chunk.SpawnWeight;
            }

            // 랜덤 값 생성
            float randomValue = (float)(_random.NextDouble() * totalWeight);

            // 가중치에 따라 선택
            float currentWeight = 0f;
            foreach (var chunk in chunks)
            {
                currentWeight += chunk.SpawnWeight;
                if (randomValue <= currentWeight)
                {
                    return chunk;
                }
            }

            // Fallback
            return chunks[0];
        }

        /// <summary>
        /// 문 마스크를 문자열로 변환합니다 (디버깅용)
        /// </summary>
        private string DoorMaskToString(byte doorMask)
        {
            List<string> doors = new List<string>();

            if ((doorMask & (1 << (int)EDirection.North)) != 0) doors.Add("North");
            if ((doorMask & (1 << (int)EDirection.East)) != 0) doors.Add("East");
            if ((doorMask & (1 << (int)EDirection.South)) != 0) doors.Add("South");
            if ((doorMask & (1 << (int)EDirection.West)) != 0) doors.Add("West");

            return string.Join(", ", doors);
        }

        #endregion
    }
}
