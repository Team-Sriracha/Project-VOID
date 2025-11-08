using UnityEngine;
using System.Collections.Generic;

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
        private Grid _grid;
        private Transform _groundTransform;

        /// <summary>
        /// 실제 생성된 문 정보 (절차적 생성 결과)
        /// </summary>
        private Dictionary<EDirection, bool> _actualDoors = new Dictionary<EDirection, bool>();

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

            // Grid 컴포넌트 찾기 (Grid 좌표계 사용을 위해)
            _grid = GetComponentInChildren<Grid>();
            if (_grid == null)
            {
                Debug.LogWarning($"청크 {_gridPosition}: Grid 컴포넌트를 찾을 수 없습니다!");
            }

            // Ground Transform 찾기 (벽 배치 기준점)
            // Chunk/Ground 구조를 고려하여 재귀적으로 찾기
            Transform[] allChildren = GetComponentsInChildren<Transform>();
            foreach (Transform child in allChildren)
            {
                if (child.name == "Ground")
                {
                    _groundTransform = child;
                    break;
                }
            }

            if (_groundTransform == null)
            {
                Debug.LogWarning($"청크 {_gridPosition}: Ground를 찾을 수 없습니다! Chunk 기준으로 배치됩니다.");
                _groundTransform = transform; // Fallback
            }
        }

        #endregion

        #region Wall Management (3D GameObject 배치)

        /// <summary>
        /// 특정 방향에 벽을 생성합니다 (GameObject 배치).
        /// </summary>
        /// <param name="startWallIndex">시작 벽 인덱스 (기본값: 0)</param>
        /// <param name="endWallIndex">종료 벽 인덱스 (기본값: -1, 전체)</param>
        public void SetWall(EDirection direction, GameObject wallPrefab, int chunkSize, float wallSpacing, int startWallIndex = 0, int endWallIndex = -1)
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
        }

        /// <summary>
        /// 특정 방향에 문을 생성합니다 (GameObject 배치).
        /// 패턴: 벽-문-벽 (중앙에 문, 양쪽에 벽)
        /// </summary>
        /// <param name="startWallIndex">시작 벽 인덱스 (기본값: 0)</param>
        /// <param name="endWallIndex">종료 벽 인덱스 (기본값: -1, 전체)</param>
        public void SetDoor(EDirection direction, GameObject doorPrefab, int chunkSize, float wallSpacing, int startWallIndex = 0, int endWallIndex = -1)
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

            // 실제 생성 정보 기록: 문 생성 = 문 있음 (부분 생성의 경우 나중에 덮어쓸 수 있음)
            if (startWallIndex == 0 && endWallIndex < 0)
            {
                _actualDoors[direction] = true;
            }
        }

        /// <summary>
        /// 벽/문의 World Position을 계산합니다.
        /// Ground Transform을 기준으로 벽 위치 계산
        /// effectiveLength: 해당 방향의 실제 벽 길이 (멀티 칸 청크 고려)
        /// chunkSize: 단일 칸의 크기 (설정값, 기본 27)
        /// </summary>
        private Vector3 CalculateWallWorldPosition(EDirection direction, int index, int effectiveLength, int chunkSize, float wallSpacing)
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

            // North/East: 바깥쪽 (+0.5, 이웃과 겹침)
            // South/West: 안쪽 (-0.5, 외벽용)
            float wallOffsetZ_North = height * chunkSize / 2f + 0.5f;  // North: 바깥쪽
            float wallOffsetZ_South = height * chunkSize / 2f - 0.5f;  // South: 안쪽
            float wallOffsetX_East = width * chunkSize / 2f + 0.5f;    // East: 바깥쪽
            float wallOffsetX_West = width * chunkSize / 2f - 0.5f;    // West: 안쪽

            Vector3 localOffset = direction switch
            {
                EDirection.North => new Vector3(floorCenterX + positionAlongEdge, 0, floorCenterZ + wallOffsetZ_North),
                EDirection.South => new Vector3(floorCenterX + positionAlongEdge, 0, floorCenterZ - wallOffsetZ_South),
                EDirection.East => new Vector3(floorCenterX + wallOffsetX_East, 0, floorCenterZ + positionAlongEdge),
                EDirection.West => new Vector3(floorCenterX - wallOffsetX_West, 0, floorCenterZ + positionAlongEdge),
                _ => Vector3.zero
            };

            return transform.position + localOffset;
        }

        /// <summary>
        /// 벽/문의 회전을 계산합니다.
        /// </summary>
        private Quaternion CalculateWallRotation(EDirection direction)
        {
            return direction switch
            {
                EDirection.North => Quaternion.Euler(0, 0, 0),     // Z+
                EDirection.South => Quaternion.Euler(0, 180, 0),   // Z-
                EDirection.East => Quaternion.Euler(0, 90, 0),     // X+
                EDirection.West => Quaternion.Euler(0, -90, 0),    // X-
                _ => Quaternion.identity
            };
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 특정 방향에 문이 있는지 확인합니다 (실제 생성 기준).
        /// </summary>
        public bool HasDoor(EDirection direction)
        {
            if (_actualDoors.TryGetValue(direction, out bool hasDoor))
            {
                return hasDoor;
            }

            // Fallback: 생성 전이면 ChunkData 기준 (레거시)
            return _chunkData != null && _chunkData.HasDoor(direction);
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
        /// 방향에 따른 실제 벽 길이를 계산합니다 (멀티 칸 청크 고려).
        /// </summary>
        private int GetEffectiveLengthForDirection(EDirection direction, int chunkSize)
        {
            if (_chunkData == null)
                return chunkSize;

            // North/South: 청크의 가로 길이 (ChunkWidth)
            // East/West: 청크의 세로 길이 (ChunkHeight)
            int effectiveLength = direction switch
            {
                EDirection.North => _chunkData.ChunkWidth * chunkSize,
                EDirection.South => _chunkData.ChunkWidth * chunkSize,
                EDirection.East => _chunkData.ChunkHeight * chunkSize,
                EDirection.West => _chunkData.ChunkHeight * chunkSize,
                _ => chunkSize
            };

            return effectiveLength;
        }

        #endregion
    }
}
