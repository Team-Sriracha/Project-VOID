using UnityEngine;
using System.Collections.Generic;
using Fusion;

namespace ProjectVoid.Map
{
    /// <summary>
    /// Chunk GameObject에 붙는 컴포넌트
    /// 3D 벽/문 GameObject를 관리합니다.
    /// </summary>
    public class ChunkInstance : MonoBehaviour
    {
        #region Private Fields

        private ChunkPrefabData _chunkData;
        private Vector2Int _gridPosition;
        private List<GameObject> _wallObjects = new List<GameObject>();

        /// <summary>
        /// 실제 생성된 문 정보 (절차적 생성 결과)
        /// </summary>
        private Dictionary<Direction, bool> _actualDoors = new Dictionary<Direction, bool>();

        /// <summary>
        /// 각 방향별 벽/문 구간 정보
        /// </summary>
        private Dictionary<Direction, List<WallSegmentData>> _wallSegments = new Dictionary<Direction, List<WallSegmentData>>();

        #endregion

        #region Properties

        /// <summary>
        /// 청크 데이터
        /// </summary>
        public ChunkPrefabData ChunkData => _chunkData;

        /// <summary>
        /// 그리드 위치
        /// </summary>
        public Vector2Int GridPosition => _gridPosition;

        #endregion

        #region Initialization

        /// <summary>
        /// 청크를 초기화합니다.
        /// </summary>
        public void Initialize(ChunkPrefabData chunkData, Vector2Int gridPosition)
        {
            _chunkData = chunkData;
            _gridPosition = gridPosition;
        }

        #endregion

        #region Wall Management (3D GameObject 배치)

        /// <summary>
        /// 특정 방향에 벽을 생성합니다 (GameObject 배치).
        /// </summary>
        /// <param name="startWallIndex">시작 벽 인덱스 (기본값: 0)</param>
        /// <param name="endWallIndex">종료 벽 인덱스 (기본값: -1, 전체)</param>
        public void SetWall(Direction direction, GameObject wallPrefab, int chunkSize, float wallSpacing, int startWallIndex = 0, int endWallIndex = -1)
        {
            if (wallPrefab == null)
            {
                Debug.LogWarning($"청크 {_gridPosition}: 벽 프리팹이 null입니다!");
                return;
            }

            // 청크 크기에 따라 배치할 벽 길이 계산 (멀티 칸 청크 고려)
            int effectiveLength = GetEffectiveLengthForDirection(direction, chunkSize);

            // 청크 크기에 따라 배치할 벽 개수 계산
            int totalWallCount = Mathf.FloorToInt(effectiveLength / wallSpacing);

            if (totalWallCount <= 0)
            {
                Debug.LogWarning($"청크 {_gridPosition}: 벽 간격({wallSpacing})이 청크 크기({chunkSize})보다 큽니다!");
                return;
            }

            // 범위 설정
            int start = Mathf.Max(0, startWallIndex);
            int end = (endWallIndex < 0) ? totalWallCount : Mathf.Min(endWallIndex, totalWallCount);

            // 벽 배치
            for (int i = start; i < end; i++)
            {
                Vector3 worldPosition = CalculateWallWorldPosition(direction, i, effectiveLength, chunkSize, wallSpacing);
                Quaternion rotation = CalculateWallRotation(direction);

                GameObject wallObj = Object.Instantiate(wallPrefab, worldPosition, rotation, transform);
                wallObj.name = $"Wall_{direction}_{i}";
                wallObj.isStatic = true;
                _wallObjects.Add(wallObj);
            }

            // 실제 생성 정보 기록: 벽 생성 = 문 없음 (부분 생성의 경우 나중에 덮어쓸 수 있음)
            if (startWallIndex == 0 && endWallIndex < 0)
            {
                _actualDoors[direction] = false;
            }

            // 벽 구간 정보 저장
            if (!_wallSegments.ContainsKey(direction))
            {
                _wallSegments[direction] = new List<WallSegmentData>();
            }
            _wallSegments[direction].Add(new WallSegmentData(direction, startWallIndex, endWallIndex < 0 ? totalWallCount - 1 : endWallIndex - 1, false));
        }

        /// <summary>
        /// 특정 방향에 문을 생성합니다 (GameObject 배치).
        /// 패턴: 벽-문-벽 (중앙에 문, 양쪽에 벽)
        /// </summary>
        /// <param name="startWallIndex">시작 벽 인덱스 (기본값: 0)</param>
        /// <param name="endWallIndex">종료 벽 인덱스 (기본값: -1, 전체)</param>
        public void SetDoor(Direction direction, GameObject doorPrefab, int chunkSize, float wallSpacing, int startWallIndex = 0, int endWallIndex = -1)
        {
            // 청크 크기에 따라 배치할 벽 길이 계산 (멀티 칸 청크 고려)
            int effectiveLength = GetEffectiveLengthForDirection(direction, chunkSize);

            // 총 개수 계산
            int totalCount = Mathf.FloorToInt(effectiveLength / wallSpacing);

            if (totalCount <= 0)
            {
                Debug.LogWarning($"청크 {_gridPosition}: 간격({wallSpacing})이 청크 크기({chunkSize})보다 큽니다!");
                return;
            }

            // 범위 설정
            int start = Mathf.Max(0, startWallIndex);
            int end = (endWallIndex < 0) ? totalCount : Mathf.Min(endWallIndex, totalCount);

            // 중앙 인덱스 계산 (부분 범위 내에서)
            int rangeCount = end - start;
            int centerIndex = start + rangeCount / 2;

            for (int i = start; i < end; i++)
            {
                Vector3 worldPosition = CalculateWallWorldPosition(direction, i, effectiveLength, chunkSize, wallSpacing);
                Quaternion rotation = CalculateWallRotation(direction);

                // 중앙에는 문, 양쪽에는 벽 배치
                if (i == centerIndex && doorPrefab != null)
                {
                    // 문 배치 (구멍 뚫린 벽)
                    GameObject doorObj = Object.Instantiate(doorPrefab, worldPosition, rotation, transform);
                    doorObj.name = $"Door_{direction}";
                    doorObj.isStatic = true;
                    _wallObjects.Add(doorObj);
                }
                else if (_chunkData.WallPrefab != null)
                {
                    // 벽 배치
                    GameObject wallObj = Object.Instantiate(_chunkData.WallPrefab, worldPosition, rotation, transform);
                    wallObj.name = $"Wall_{direction}_{i}";
                    wallObj.isStatic = true;
                    _wallObjects.Add(wallObj);
                }
            }

            // 실제 생성 정보 기록: 문 생성 = 문 있음
            // 부분 벽 생성이더라도 문이 포함되어 있으면 기록
            _actualDoors[direction] = true;

            // 문 구간 정보 저장
            if (!_wallSegments.ContainsKey(direction))
            {
                _wallSegments[direction] = new List<WallSegmentData>();
            }
            _wallSegments[direction].Add(new WallSegmentData(direction, startWallIndex, endWallIndex < 0 ? totalCount - 1 : endWallIndex - 1, true));
        }

        /// <summary>
        /// 벽/문의 World Position을 계산합니다.
        /// effectiveLength: 해당 방향의 실제 벽 길이 (멀티 칸 청크 고려)
        /// chunkSize: 단일 칸의 크기 (설정값, 기본 27)
        /// </summary>
        private Vector3 CalculateWallWorldPosition(Direction direction, int index, int effectiveLength, int chunkSize, float wallSpacing)
        {
            int totalCount = Mathf.FloorToInt(effectiveLength / wallSpacing);

            // 벽을 한 변을 따라 배치할 위치 계산
            // 예: 길이 54, wallSpacing 9 -> 6개 배치
            // positionAlongEdge: -22.5, -13.5, -4.5, 4.5, 13.5, 22.5
            float startPosition = -(totalCount - 1) * wallSpacing / 2f;
            float positionAlongEdge = startPosition + (index * wallSpacing);

            // 멀티 칸 청크 크기 계산 (공통)
            int width = _chunkData != null ? _chunkData.ChunkWidth : 1;
            int height = _chunkData != null ? _chunkData.ChunkHeight : 1;

            // 청크 Transform 기준으로 직접 계산
            // 청크 프리팹의 Floor는 0 ~ (width*chunkSize), 0 ~ (height*chunkSize) 로컬 좌표
            // 청크 Transform의 로컬 (0,0,0)은 Floor의 왼쪽 아래 모서리
            // Floor의 로컬 중심: (width * chunkSize / 2, 0, height * chunkSize / 2)

            float floorCenterX = width * chunkSize / 2f;
            float floorCenterZ = height * chunkSize / 2f;

            // 벽을 청크 경계에 정확히 배치
            // North/East만 생성하는 로직이므로 경계에 딱 맞춰 배치
            // 이웃 청크가 있으면 South/West는 생성 안 함 → 겹침 없음
            Vector3 localOffset = direction switch
            {
                Direction.North => new Vector3(floorCenterX + positionAlongEdge, 0, height * chunkSize),
                Direction.South => new Vector3(floorCenterX + positionAlongEdge, 0, 0),
                Direction.East => new Vector3(width * chunkSize, 0, floorCenterZ + positionAlongEdge),
                Direction.West => new Vector3(0, 0, floorCenterZ + positionAlongEdge),
                _ => Vector3.zero
            };

            return transform.position + localOffset;
        }

        /// <summary>
        /// 벽/문의 회전을 계산합니다.
        /// </summary>
        private Quaternion CalculateWallRotation(Direction direction)
        {
            return direction switch
            {
                Direction.North => Quaternion.Euler(0, 0, 0),     // Z+
                Direction.South => Quaternion.Euler(0, 180, 0),   // Z-
                Direction.East => Quaternion.Euler(0, 90, 0),     // X+
                Direction.West => Quaternion.Euler(0, -90, 0),    // X-
                _ => Quaternion.identity
            };
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 특정 방향에 문이 있는지 확인합니다 (실제 생성 기준).
        /// </summary>
        public bool HasDoor(Direction direction)
        {
            return _actualDoors.TryGetValue(direction, out bool hasDoor) && hasDoor;
        }

        /// <summary>
        /// 문이 있는 모든 방향을 반환합니다.
        /// </summary>
        public List<Direction> GetDoorDirections()
        {
            var doorDirections = new List<Direction>();
            foreach (var kvp in _actualDoors)
            {
                if (kvp.Value)
                {
                    doorDirections.Add(kvp.Key);
                }
            }
            return doorDirections;
        }

        /// <summary>
        /// 실제 생성된 문 정보를 비트마스크로 반환합니다.
        /// NetworkChunkData 동기화용
        /// </summary>
        public byte GetActualDoorMask()
        {
            byte mask = 0;
            foreach (var kvp in _actualDoors)
            {
                if (kvp.Value)
                {
                    mask |= (byte)kvp.Key;
                }
            }
            return mask;
        }

        /// <summary>
        /// 특정 방향의 모든 벽/문 구간 정보를 반환합니다.
        /// </summary>
        public WallSegmentData[] GetWallSegments(Direction direction)
        {
            if (_wallSegments.TryGetValue(direction, out List<WallSegmentData> segments))
            {
                return segments.ToArray();
            }
            return new WallSegmentData[0];
        }

        /// <summary>
        /// 모든 방향의 벽/문 구간 정보를 반환합니다.
        /// </summary>
        public WallSegmentData[] GetAllWallSegments()
        {
            var allSegments = new System.Collections.Generic.List<WallSegmentData>();
            foreach (var kvp in _wallSegments)
            {
                allSegments.AddRange(kvp.Value);
            }
            return allSegments.ToArray();
        }

        /// <summary>
        /// 방향에 따른 실제 벽 길이를 계산합니다 (멀티 칸 청크 고려).
        /// </summary>
        private int GetEffectiveLengthForDirection(Direction direction, int chunkSize)
        {
            if (_chunkData == null)
                return chunkSize;

            // North/South: 청크의 가로 길이 (ChunkWidth)
            // East/West: 청크의 세로 길이 (ChunkHeight)
            int effectiveLength = direction switch
            {
                Direction.North => _chunkData.ChunkWidth * chunkSize,
                Direction.South => _chunkData.ChunkWidth * chunkSize,
                Direction.East => _chunkData.ChunkHeight * chunkSize,
                Direction.West => _chunkData.ChunkHeight * chunkSize,
                _ => chunkSize
            };

            return effectiveLength;
        }

        #endregion
    }
}
