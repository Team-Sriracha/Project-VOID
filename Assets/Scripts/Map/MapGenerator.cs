using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 템플릿 기반 프로시저럴 맵 생성 클래스
    /// </summary>
    public class MapGenerator
    {
        #region Private Fields

        private MapGenerationSettings _settings;
        private Dictionary<Vector2Int, ChunkInstance> _placedChunks;
        private Dictionary<Vector2Int, int> _chunkGroupIds;
        private Dictionary<Vector2Int, byte> _chunkIndexMap; // 각 위치의 청크가 풀에서 몇 번째 인덱스인지 저장
        private HashSet<Vector2Int> _activeChunks; // 현재 활성화된 청크 목록 (구역 축소용)
        private Transform _parentTransform;
        private System.Random _random;
        private System.Random _doorRandom;
        private int _currentChunkCount;
        private Stopwatch _generationTimer;

        #endregion

        #region Properties

        public IReadOnlyDictionary<Vector2Int, ChunkInstance> PlacedChunks => _placedChunks;
        public int CurrentChunkCount => _currentChunkCount;

        #endregion

        #region Constructor

        public MapGenerator(MapGenerationSettings settings, Transform parentTransform)
        {
            _settings = settings;
            _parentTransform = parentTransform;
            _placedChunks = new Dictionary<Vector2Int, ChunkInstance>();
            _chunkGroupIds = new Dictionary<Vector2Int, int>();
            _chunkIndexMap = new Dictionary<Vector2Int, byte>();
            _activeChunks = new HashSet<Vector2Int>();
            _generationTimer = new Stopwatch();
        }

        #endregion

        #region Public Methods

        public bool GenerateMap(int seed)
        {
            return TryGenerateMap(seed);
        }

        public NetworkChunkData[] ToNetworkArray()
        {
            // Position 기준으로 정렬하여 순서 보장 (클라이언트와 동일한 순서)
            var sortedChunks = _placedChunks
                .OrderBy(kvp => kvp.Key.y)
                .ThenBy(kvp => kvp.Key.x)
                .ToList();

            var networkChunks = new NetworkChunkData[sortedChunks.Count];
            int index = 0;

            foreach (var kvp in sortedChunks)
            {
                Vector2Int position = kvp.Key;
                ChunkInstance chunk = kvp.Value;
                byte actualDoorMask = chunk.GetActualDoorMask();
                int groupId = _chunkGroupIds.ContainsKey(position) ? _chunkGroupIds[position] : 0;
                byte chunkIndex = _chunkIndexMap.ContainsKey(position) ? _chunkIndexMap[position] : (byte)0;

                networkChunks[index] = new NetworkChunkData(
                    position,
                    chunk.ChunkData.ChunkType,
                    actualDoorMask,
                    groupId,
                    chunk.ChunkData.ChunkWidth,
                    chunk.ChunkData.ChunkHeight,
                    chunkIndex
                );
                index++;
            }

            return networkChunks;
        }

        /// <summary>
        /// 청크를 비활성화합니다 (구역 축소용).
        /// </summary>
        public void DeactivateChunk(Vector2Int gridPosition)
        {
            if (_activeChunks.Remove(gridPosition))
            {
                Debug.Log($"청크 {gridPosition} 비활성화");
            }
        }

        /// <summary>
        /// 청크를 다시 활성화합니다.
        /// </summary>
        public void ActivateChunk(Vector2Int gridPosition)
        {
            if (_placedChunks.ContainsKey(gridPosition) && _activeChunks.Add(gridPosition))
            {
                Debug.Log($"청크 {gridPosition} 활성화");
            }
        }

        /// <summary>
        /// 여러 청크를 한 번에 비활성화하고 연결성을 검증합니다.
        /// </summary>
        /// <returns>비활성화 후에도 맵이 연결되어 있으면 true</returns>
        public bool DeactivateChunksAndValidate(HashSet<Vector2Int> chunksToDeactivate)
        {
            // 백업
            var backup = new HashSet<Vector2Int>(_activeChunks);

            // 비활성화
            foreach (var pos in chunksToDeactivate)
            {
                _activeChunks.Remove(pos);
            }

            // 연결성 검증
            if (ValidateConnectivity())
            {
                Debug.Log($"{chunksToDeactivate.Count}개 청크 비활성화 성공. 남은 활성 청크: {_activeChunks.Count}");
                return true;
            }
            else
            {
                // 실패 시 복원
                _activeChunks = backup;
                Debug.LogWarning($"청크 비활성화 실패: 맵이 분리됩니다. 복원됨.");
                return false;
            }
        }

        /// <summary>
        /// 현재 활성 청크 개수를 반환합니다.
        /// </summary>
        public int ActiveChunkCount => _activeChunks.Count;

        public bool RenderMapFromNetworkData(NetworkChunkData[] networkChunks, int seed)
        {
            ClearExistingChunks();
            _placedChunks.Clear();
            _chunkGroupIds.Clear();
            _chunkIndexMap.Clear();
            _activeChunks.Clear();
            _currentChunkCount = 0;

            // Random 초기화 (서버와 같은 시드 사용)
            _random = new System.Random(seed);
            Debug.Log($"[Client-RenderMapFromNetworkData] Seed={seed}로 Random 초기화, ChunkCount={networkChunks.Length}");

            // Position 기준으로 정렬 (서버와 같은 순서로 처리)
            var sortedNetworkChunks = networkChunks
                .OrderBy(chunk => chunk.Position.y)
                .ThenBy(chunk => chunk.Position.x)
                .ToArray();

            // 1단계: 청크 프리팹 인스턴스화
            var networkDataDict = new Dictionary<Vector2Int, NetworkChunkData>();
            int chunkIndex = 0;
            foreach (var networkData in sortedNetworkChunks)
            {
                chunkIndex++;
                networkDataDict[networkData.Position] = networkData;

                Debug.Log($"[Client] 청크 {chunkIndex}/{sortedNetworkChunks.Length}: Position={networkData.Position}, " +
                         $"Type={networkData.Type}, Size={networkData.ChunkWidth}x{networkData.ChunkHeight}, GroupId={networkData.GroupId}");

                // EChunkType을 EGridCell로 변환
                EGridCell gridCellType = ConvertChunkTypeToGridCell(networkData.Type);
                ChunkPrefabData[] chunkPool = GetChunkPoolForCell(gridCellType);

                if (chunkPool == null || chunkPool.Length == 0)
                {
                    Debug.LogError($"[Client] 청크 타입 {networkData.Type}에 대한 청크 풀이 없습니다!");
                    continue;
                }

                // 서버가 선택한 정확한 청크를 ChunkIndex로 선택
                if (networkData.ChunkIndex >= chunkPool.Length)
                {
                    Debug.LogError($"[Client] ChunkIndex({networkData.ChunkIndex})가 청크 풀 크기({chunkPool.Length})를 초과합니다!");
                    continue;
                }

                ChunkPrefabData chunkData = chunkPool[networkData.ChunkIndex];
                if (chunkData == null || chunkData.ChunkPrefab == null)
                {
                    Debug.LogError($"[Client] ChunkIndex {networkData.ChunkIndex}의 청크가 null입니다!");
                    continue;
                }

                Debug.Log($"[Client] 선택된 청크: ChunkIndex={networkData.ChunkIndex}, ChunkId={chunkData.ChunkId}, Size={chunkData.ChunkWidth}x{chunkData.ChunkHeight}");

                InstantiateChunk(networkData.Position, chunkData);

                // 청크가 차지하는 모든 영역에 그룹 ID 저장
                for (int dy = 0; dy < networkData.ChunkHeight; dy++)
                {
                    for (int dx = 0; dx < networkData.ChunkWidth; dx++)
                    {
                        Vector2Int cellPos = networkData.Position + new Vector2Int(dx, dy);
                        _chunkGroupIds[cellPos] = networkData.GroupId;
                    }
                }
            }

            // 2단계: NetworkChunkData의 DoorMask에 따라 벽/문 생성
            foreach (var kvp in _placedChunks)
            {
                Vector2Int position = kvp.Key;
                ChunkInstance chunkInstance = kvp.Value;
                NetworkChunkData networkData = networkDataDict[position];

                foreach (EDirection direction in DirectionExtensions.GetAllDirections())
                {
                    Vector2Int neighborPos = position + direction.ToOffset();
                    bool hasNeighbor = networkDataDict.ContainsKey(neighborPos);

                    if (hasNeighbor)
                    {
                        // 중복 방지: North/East만 생성
                        if (direction == EDirection.North || direction == EDirection.East)
                        {
                            // 청크 그룹 내부 벽 제거: 같은 그룹 ID를 가진 청크끼리는 벽 생성 안 함
                            bool isSameGroup = IsChunkGroupBoundary(position, neighborPos);
                            if (isSameGroup)
                            {
                                continue; // 벽 생성하지 않음
                            }

                            bool hasDoor = networkData.HasDoor(direction);
                            GenerateWallOrDoor(chunkInstance, direction, hasDoor);
                        }
                    }
                    else
                    {
                        GenerateWallOrDoor(chunkInstance, direction, false);
                    }
                }
            }

            return _currentChunkCount > 0;
        }

        #endregion

        #region Private Methods

        private bool TryGenerateMap(int seed)
        {
            ClearExistingChunks();
            _placedChunks.Clear();
            _chunkGroupIds.Clear();
            _activeChunks.Clear();
            _currentChunkCount = 0;
            _random = new System.Random(seed);
            _doorRandom = new System.Random(seed + 1000);
            _generationTimer.Restart();

            MapLayoutTemplate template = SelectRandomTemplate();
            if (template == null)
            {
                return false;
            }

            if (!PlaceChunksFromTemplate(template))
            {
                return false;
            }

            GenerateWallsAndDoors(template);

            if (!ValidateConnectivity())
            {
                return false;
            }

            _generationTimer.Stop();
            return true;
        }

        private void ClearExistingChunks()
        {
            foreach (var chunk in _placedChunks.Values)
            {
                if (chunk != null && chunk.gameObject != null)
                {
                    Object.Destroy(chunk.gameObject);
                }
            }
        }

        private MapLayoutTemplate SelectRandomTemplate()
        {
            var templates = _settings.MapTemplates;
            if (templates == null || templates.Length == 0)
            {
                Debug.LogError("MapGenerationSettings에 템플릿이 없습니다!");
                return null;
            }

            var validTemplates = new List<MapLayoutTemplate>();
            foreach (var template in templates)
            {
                if (template != null)
                    validTemplates.Add(template);
            }

            if (validTemplates.Count == 0)
            {
                Debug.LogError("유효한 템플릿이 없습니다!");
                return null;
            }

            int index = _random.Next(0, validTemplates.Count);
            return validTemplates[index];
        }

        private bool PlaceChunksFromTemplate(MapLayoutTemplate template)
        {
            // 템플릿 레이아웃 출력 (디버깅용)
            Debug.Log($"[PlaceChunksFromTemplate] 템플릿 레이아웃:\n{template.ToDebugString()}");

            Vector2Int center = template.GetCenterPosition();
            HashSet<Vector2Int> processedPositions = new HashSet<Vector2Int>(); // 이미 처리된 위치

            for (int y = 0; y < template.Height; y++)
            {
                for (int x = 0; x < template.Width; x++)
                {
                    EGridCell cellType = template.GetCell(x, y);

                    if (cellType == EGridCell.Empty)
                        continue;

                    int groupId = template.GetChunkGroupId(x, y);
                    Vector2Int templatePos = new Vector2Int(x, y);

                    // 이미 처리된 위치면 스킵
                    if (processedPositions.Contains(templatePos))
                        continue;

                    // 그룹이 있는 경우
                    if (groupId > 0)
                    {
                        // 그룹 처리 (인접한 같은 그룹 ID 셀만 찾기)
                        List<Vector2Int> groupCells;
                        if (!PlaceGroupChunk(template, templatePos, groupId, center, cellType, out groupCells))
                        {
                            return false;
                        }

                        // 처리된 모든 셀 위치 저장
                        foreach (var cellPos in groupCells)
                        {
                            processedPositions.Add(cellPos);
                        }
                    }
                    else
                    {
                        // 그룹이 없는 경우: 기존 방식대로 1칸 청크 생성
                        Vector2Int gridPos = new Vector2Int(x - center.x, y - center.y);
                        ChunkPrefabData[] chunkPool = GetChunkPoolForCell(cellType);

                        if (chunkPool == null || chunkPool.Length == 0)
                        {
                            Debug.LogError($"셀 타입 {cellType}에 대한 청크 풀이 없습니다!");
                            return false;
                        }

                        ChunkPrefabData chunkData = GetRandomChunkBySize(chunkPool, 1, 1, out byte chunkIndex);
                        if (chunkData != null)
                        {
                            InstantiateChunk(gridPos, chunkData);
                            _chunkGroupIds[gridPos] = 0;
                            _chunkIndexMap[gridPos] = chunkIndex;
                            processedPositions.Add(templatePos);
                        }
                    }
                }
            }

            return _currentChunkCount > 0;
        }

        private void GenerateWallsAndDoors(MapLayoutTemplate template)
        {
            Vector2Int center = template.GetCenterPosition();
            HashSet<(Vector2Int, Vector2Int)> essentialConnections = CreateSpanningTree();
            HashSet<(ChunkInstance, EDirection)> processedWalls = new HashSet<(ChunkInstance, EDirection)>();
            HashSet<ChunkInstance> processedChunks = new HashSet<ChunkInstance>(); // 이미 처리한 청크 추적

            foreach (var kvp in _placedChunks)
            {
                ChunkInstance chunk = kvp.Value;

                // 그룹 청크는 여러 칸에 같은 ChunkInstance가 있으므로 한 번만 처리
                if (processedChunks.Contains(chunk))
                    continue;

                processedChunks.Add(chunk);

                // 청크의 대표 키 사용 (그룹 청크의 경우 왼쪽 아래 칸)
                Vector2Int representativeKey = chunk.GridPosition;

                foreach (EDirection direction in DirectionExtensions.GetAllDirections())
                {
                    Vector2Int offset = direction.ToOffset();

                    // 같은 ChunkInstance의 같은 방향 벽을 중복 생성하지 않도록 체크
                    if (processedWalls.Contains((chunk, direction)))
                        continue;

                    // 그룹 청크의 경우 해당 방향의 모든 칸의 이웃을 확인 (셀별 정보 포함)
                    var (hasAnyNeighbor, cellHasNeighbor) = CheckGroupChunkNeighborDetailed(template, chunk, representativeKey, center, direction);

                    if (hasAnyNeighbor)
                    {
                        // 중복 방지: North/East만 생성
                        if (direction == EDirection.North || direction == EDirection.East)
                        {
                            // 셀별 이웃 정보를 기반으로 부분 벽 생성
                            GeneratePartialWalls(chunk, direction, cellHasNeighbor, representativeKey, offset, essentialConnections);
                            processedWalls.Add((chunk, direction));
                        }
                        else
                        {
                            // South/West: 이웃 없는 부분만 외벽 생성 (중복 방지)
                            GenerateOuterWallsOnly(chunk, direction, cellHasNeighbor);
                            processedWalls.Add((chunk, direction));
                        }
                    }
                    else
                    {
                        // 모든 셀에 이웃이 없음 → 전체 외벽 생성
                        GenerateWallOrDoor(chunk, direction, false);
                        processedWalls.Add((chunk, direction));
                    }
                }
            }
        }

        /// <summary>
        /// South/West 방향에서 이웃이 없는 부분만 외벽을 생성합니다 (중복 방지용).
        /// </summary>
        private void GenerateOuterWallsOnly(ChunkInstance chunk, EDirection direction, bool[] cellHasNeighbor)
        {
            if (cellHasNeighbor == null || cellHasNeighbor.Length == 0)
                return;

            // 1칸당 벽 개수 계산
            int wallsPerCell = Mathf.FloorToInt(_settings.ChunkSize / chunk.ChunkData.WallSpacing);

            // 연속된 범위 찾기
            List<(int startCell, int endCell, bool hasNeighbor)> ranges = GetConsecutiveRanges(cellHasNeighbor);

            // 이웃 없는 범위만 외벽 생성
            foreach (var (startCell, endCell, hasNeighbor) in ranges)
            {
                if (!hasNeighbor)  // 이웃 없는 부분만
                {
                    int startWallIndex = startCell * wallsPerCell;
                    int endWallIndex = (endCell + 1) * wallsPerCell;

                    chunk.SetWall(direction, chunk.ChunkData.WallPrefab, _settings.ChunkSize,
                                chunk.ChunkData.WallSpacing, startWallIndex, endWallIndex);
                }
                // 이웃 있는 부분은 스킵 (이웃 청크의 North/East가 생성함)
            }
        }

        /// <summary>
        /// 셀별 이웃 정보를 기반으로 부분 벽을 생성합니다.
        /// </summary>
        private void GeneratePartialWalls(ChunkInstance chunk, EDirection direction, bool[] cellHasNeighbor,
                                          Vector2Int representativeKey, Vector2Int offset,
                                          HashSet<(Vector2Int, Vector2Int)> essentialConnections)
        {
            if (cellHasNeighbor == null || cellHasNeighbor.Length == 0)
                return;

            // 1칸당 벽 개수 계산
            int wallsPerCell = Mathf.FloorToInt(_settings.ChunkSize / chunk.ChunkData.WallSpacing);

            // 연속된 범위 찾기
            List<(int startCell, int endCell, bool hasNeighbor)> ranges = GetConsecutiveRanges(cellHasNeighbor);

            // 각 범위에 대해 벽/문 생성
            foreach (var (startCell, endCell, hasNeighbor) in ranges)
            {
                int startWallIndex = startCell * wallsPerCell;
                int endWallIndex = (endCell + 1) * wallsPerCell;

                if (hasNeighbor)
                {
                    // 내부 벽/문: spanning tree 기반 문 생성 확률
                    Vector2Int neighborGridPos = representativeKey + offset;
                    bool isEssential = essentialConnections.Contains((representativeKey, neighborGridPos)) ||
                                     essentialConnections.Contains((neighborGridPos, representativeKey));

                    bool generateDoor = isEssential || _doorRandom.NextDouble() < _settings.DoorGenerationProbability;

                    if (generateDoor)
                    {
                        chunk.SetDoor(direction, chunk.ChunkData.DoorPrefab, _settings.ChunkSize,
                                    chunk.ChunkData.WallSpacing, startWallIndex, endWallIndex);
                    }
                    else
                    {
                        chunk.SetWall(direction, chunk.ChunkData.WallPrefab, _settings.ChunkSize,
                                    chunk.ChunkData.WallSpacing, startWallIndex, endWallIndex);
                    }
                }
                else
                {
                    // 외벽
                    chunk.SetWall(direction, chunk.ChunkData.WallPrefab, _settings.ChunkSize,
                                chunk.ChunkData.WallSpacing, startWallIndex, endWallIndex);
                }
            }
        }

        /// <summary>
        /// bool 배열에서 연속된 true/false 범위를 찾습니다.
        /// </summary>
        /// <returns>(시작 셀 인덱스, 종료 셀 인덱스, 값) 리스트</returns>
        private List<(int startCell, int endCell, bool value)> GetConsecutiveRanges(bool[] array)
        {
            var ranges = new List<(int, int, bool)>();

            if (array.Length == 0)
                return ranges;

            int start = 0;
            bool currentValue = array[0];

            for (int i = 1; i < array.Length; i++)
            {
                if (array[i] != currentValue)
                {
                    // 범위 종료
                    ranges.Add((start, i - 1, currentValue));
                    start = i;
                    currentValue = array[i];
                }
            }

            // 마지막 범위 추가
            ranges.Add((start, array.Length - 1, currentValue));

            return ranges;
        }

        private HashSet<(Vector2Int, Vector2Int)> CreateSpanningTree()
        {
            HashSet<(Vector2Int, Vector2Int)> connections = new HashSet<(Vector2Int, Vector2Int)>();

            if (_placedChunks.Count == 0)
                return connections;

            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            Queue<Vector2Int> queue = new Queue<Vector2Int>();

            Vector2Int start = _placedChunks.Keys.First();
            queue.Enqueue(start);
            visited.Add(start);

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();

                foreach (EDirection direction in DirectionExtensions.GetAllDirections())
                {
                    Vector2Int neighbor = current + direction.ToOffset();

                    if (_placedChunks.ContainsKey(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                        connections.Add((current, neighbor));
                    }
                }
            }

            return connections;
        }

        private void GenerateWallOrDoor(ChunkInstance chunk, EDirection direction, bool hasDoor)
        {
            if (hasDoor)
            {
                chunk.SetDoor(direction, chunk.ChunkData.DoorPrefab, _settings.ChunkSize, chunk.ChunkData.WallSpacing);
            }
            else
            {
                chunk.SetWall(direction, chunk.ChunkData.WallPrefab, _settings.ChunkSize, chunk.ChunkData.WallSpacing);
            }
        }

        /// <summary>
        /// 두 청크가 같은 그룹에 속하는지 확인 (같은 그룹이면 내부 벽 제거)
        /// </summary>
        private bool IsChunkGroupBoundary(Vector2Int pos1, Vector2Int pos2)
        {
            // 두 위치 모두 그룹 ID가 있는지 확인
            if (!_chunkGroupIds.TryGetValue(pos1, out int groupId1) ||
                !_chunkGroupIds.TryGetValue(pos2, out int groupId2))
            {
                return false;
            }

            // 그룹 ID가 0이면 독립적인 청크 (그룹 없음)
            if (groupId1 == 0 || groupId2 == 0)
            {
                return false;
            }

            // 같은 그룹 ID를 가지면 true 반환
            return groupId1 == groupId2;
        }

        /// <summary>
        /// 그룹 청크의 특정 방향에 외부 이웃이 있는지 확인하고, 각 셀별 이웃 정보를 반환
        /// </summary>
        /// <returns>(이웃 존재 여부, 셀별 이웃 마스크)</returns>
        private (bool hasAnyNeighbor, bool[] cellHasNeighbor) CheckGroupChunkNeighborDetailed(MapLayoutTemplate template, ChunkInstance chunk, Vector2Int representativeKey, Vector2Int center, EDirection direction)
        {
            int width = chunk.ChunkData != null ? chunk.ChunkData.ChunkWidth : 1;
            int height = chunk.ChunkData != null ? chunk.ChunkData.ChunkHeight : 1;

            Vector2Int offset = direction.ToOffset();

            // 해당 방향의 셀 개수
            int cellCount = (direction == EDirection.North || direction == EDirection.South) ? width : height;
            bool[] cellHasNeighbor = new bool[cellCount];
            bool hasAnyNeighbor = false;

            // 해당 방향의 모든 경계 칸에 대해 이웃 확인
            for (int i = 0; i < cellCount; i++)
            {
                Vector2Int cellOffset = direction switch
                {
                    EDirection.North => new Vector2Int(i, height - 1),  // 윗줄
                    EDirection.South => new Vector2Int(i, 0),            // 아랫줄
                    EDirection.East => new Vector2Int(width - 1, i),     // 오른쪽 열
                    EDirection.West => new Vector2Int(0, i),             // 왼쪽 열
                    _ => Vector2Int.zero
                };

                Vector2Int currentCell = representativeKey + cellOffset;
                Vector2Int neighborCell = currentCell + offset;

                int neighborTemplateX = neighborCell.x + center.x;
                int neighborTemplateY = neighborCell.y + center.y;

                // 템플릿 범위 체크
                if (!template.IsValidPosition(neighborTemplateX, neighborTemplateY))
                {
                    cellHasNeighbor[i] = false;
                    continue;
                }

                EGridCell neighborType = template.GetCell(neighborTemplateX, neighborTemplateY);
                if (neighborType != EGridCell.Empty)
                {
                    // 그룹 내부인지 확인
                    if (_chunkGroupIds.TryGetValue(currentCell, out int currentGroupId) &&
                        _chunkGroupIds.TryGetValue(neighborCell, out int neighborGroupId) &&
                        currentGroupId > 0 && currentGroupId == neighborGroupId)
                    {
                        // 같은 그룹 내부 → 이웃 없음으로 처리
                        cellHasNeighbor[i] = false;
                        continue;
                    }

                    // 외부 이웃 발견
                    cellHasNeighbor[i] = true;
                    hasAnyNeighbor = true;
                }
                else
                {
                    cellHasNeighbor[i] = false;
                }
            }

            return (hasAnyNeighbor, cellHasNeighbor);
        }

        private EGridCell ConvertChunkTypeToGridCell(EChunkType chunkType)
        {
            return chunkType switch
            {
                EChunkType.Central => EGridCell.Central,
                EChunkType.Normal => EGridCell.Normal,
                EChunkType.Special => EGridCell.Special,
                _ => EGridCell.Empty
            };
        }

        private ChunkPrefabData[] GetChunkPoolForCell(EGridCell cellType)
        {
            return cellType switch
            {
                EGridCell.Central => _settings.CentralChunks,
                EGridCell.Normal => _settings.NormalChunks,
                EGridCell.Special => _settings.SpecialChunks,
                _ => null
            };
        }

        private ChunkPrefabData GetRandomChunk(ChunkPrefabData[] chunks)
        {
            if (chunks == null || chunks.Length == 0)
                return null;

            var validChunks = new List<ChunkPrefabData>();
            foreach (var chunk in chunks)
            {
                if (chunk != null && chunk.ChunkPrefab != null)
                    validChunks.Add(chunk);
            }

            if (validChunks.Count == 0)
                return null;

            float totalWeight = 0f;
            foreach (var chunk in validChunks)
            {
                totalWeight += chunk.SpawnWeight;
            }

            if (totalWeight <= 0f)
            {
                Debug.LogWarning("GetRandomChunk: 모든 청크의 가중치가 0입니다. 첫 번째 청크를 반환합니다.");
                return validChunks[0];
            }

            float randomValue = (float)(_random.NextDouble() * totalWeight);
            float currentWeight = 0f;

            foreach (var chunk in validChunks)
            {
                currentWeight += chunk.SpawnWeight;
                if (randomValue <= currentWeight)
                {
                    return chunk;
                }
            }

            return validChunks[0];
        }

        private ChunkPrefabData GetRandomChunkBySize(ChunkPrefabData[] chunks, int width, int height, out byte selectedIndex)
        {
            selectedIndex = 0;

            if (chunks == null || chunks.Length == 0)
                return null;

            // 정확한 크기가 맞는 청크 찾기 (원본 인덱스와 함께 저장)
            var exactMatchIndices = new List<int>();
            for (int i = 0; i < chunks.Length; i++)
            {
                var chunk = chunks[i];
                if (chunk != null && chunk.ChunkPrefab != null &&
                    chunk.ChunkWidth == width && chunk.ChunkHeight == height)
                {
                    exactMatchIndices.Add(i);
                }
            }

            // 정확한 크기가 있으면 반환
            if (exactMatchIndices.Count > 0)
            {
                int chosenIndex = SelectWeightedRandomIndex(chunks, exactMatchIndices);
                selectedIndex = (byte)chosenIndex;
                return chunks[chosenIndex];
            }

            // 아무것도 없으면 Fallback
            Debug.LogWarning($"크기 {width}x{height}에 맞는 청크를 찾을 수 없습니다. 임의의 청크를 사용합니다.");
            ChunkPrefabData fallbackChunk = GetRandomChunk(chunks);

            // Fallback 청크의 인덱스 찾기
            for (int i = 0; i < chunks.Length; i++)
            {
                if (chunks[i] == fallbackChunk)
                {
                    selectedIndex = (byte)i;
                    break;
                }
            }

            return fallbackChunk;
        }

        private ChunkPrefabData SelectWeightedRandom(List<ChunkPrefabData> chunks)
        {
            if (chunks == null || chunks.Count == 0)
                return null;

            float totalWeight = 0f;
            foreach (var chunk in chunks)
            {
                if (chunk != null)
                {
                    totalWeight += chunk.SpawnWeight;
                }
            }

            if (totalWeight <= 0f)
            {
                Debug.LogWarning("SelectWeightedRandom: 유효한 청크가 없거나 총 가중치가 0입니다!");
                return chunks.FirstOrDefault(c => c != null);
            }

            float randomValue = (float)(_random.NextDouble() * totalWeight);
            float currentWeight = 0f;

            foreach (var chunk in chunks)
            {
                if (chunk == null)
                    continue;

                currentWeight += chunk.SpawnWeight;
                if (randomValue <= currentWeight)
                {
                    return chunk;
                }
            }

            return chunks.FirstOrDefault(c => c != null);
        }

        /// <summary>
        /// 가중치 기반으로 청크를 선택하고 원본 배열에서의 인덱스를 반환합니다.
        /// </summary>
        private int SelectWeightedRandomIndex(ChunkPrefabData[] allChunks, List<int> validIndices)
        {
            if (allChunks == null || validIndices == null || validIndices.Count == 0)
                return 0;

            // 유효한 인덱스들의 총 가중치 계산
            float totalWeight = 0f;
            foreach (int index in validIndices)
            {
                var chunk = allChunks[index];
                if (chunk != null)
                {
                    totalWeight += chunk.SpawnWeight;
                }
            }

            if (totalWeight <= 0f)
            {
                Debug.LogWarning("SelectWeightedRandomIndex: 유효한 청크가 없거나 총 가중치가 0입니다!");
                return validIndices[0];
            }

            // 가중치 기반 랜덤 선택
            float randomValue = (float)(_random.NextDouble() * totalWeight);
            float currentWeight = 0f;

            foreach (int index in validIndices)
            {
                var chunk = allChunks[index];
                if (chunk == null)
                    continue;

                currentWeight += chunk.SpawnWeight;
                if (randomValue <= currentWeight)
                {
                    return index;
                }
            }

            return validIndices[0];
        }

        private bool PlaceGroupChunk(MapLayoutTemplate template, Vector2Int startPos, int groupId, Vector2Int center, EGridCell cellType, out List<Vector2Int> groupCells)
        {
            // 1. 인접한 같은 그룹 ID 셀만 찾기 (flood fill)
            groupCells = GetConnectedGroupCells(template, startPos, groupId);

            if (groupCells.Count == 0)
            {
                Debug.LogError($"그룹 {groupId}의 셀을 찾을 수 없습니다!");
                return false;
            }

            // 2. 그룹 경계 정보 계산
            var (minPos, width, height) = GetGroupBounds(groupCells);
            Debug.Log($"[PlaceGroupChunk] 그룹 {groupId}: minPos={minPos}, width={width}, height={height}, 셀 개수={groupCells.Count}");

            // 3. 크기에 맞는 청크 선택
            ChunkPrefabData[] chunkPool = GetChunkPoolForCell(cellType);

            if (chunkPool == null || chunkPool.Length == 0)
            {
                Debug.LogError($"셀 타입 {cellType}에 대한 청크 풀이 없습니다!");
                return false;
            }

            ChunkPrefabData chunkData = GetRandomChunkBySize(chunkPool, width, height, out byte chunkIndex);
            if (chunkData == null)
            {
                Debug.LogError($"그룹 {groupId} ({width}x{height})에 맞는 청크를 찾을 수 없습니다!");
                return false;
            }
            Debug.Log($"[PlaceGroupChunk] 그룹 {groupId}: 선택된 청크={chunkData.ChunkId}, 크기={chunkData.ChunkWidth}x{chunkData.ChunkHeight}");

            // 4. 대표 키 (왼쪽 아래)를 그리드 좌표로 변환
            Vector2Int representativeKey = new Vector2Int(minPos.x - center.x, minPos.y - center.y);
            Debug.Log($"[PlaceGroupChunk] 그룹 {groupId}: representativeKey={representativeKey}");

            // 5. 청크 생성
            if (!InstantiateChunk(representativeKey, chunkData))
            {
                Debug.LogError($"그룹 {groupId} 청크 생성 실패!");
                return false;
            }

            // 청크 인덱스 저장 (대표 키에만 저장)
            _chunkIndexMap[representativeKey] = chunkIndex;

            // 6. 생성된 ChunkInstance를 그룹의 모든 셀에 추가
            if (_placedChunks.TryGetValue(representativeKey, out ChunkInstance chunkInstance))
            {
                foreach (var cellPos in groupCells)
                {
                    Vector2Int cellGridPos = new Vector2Int(cellPos.x - center.x, cellPos.y - center.y);

                    // 모든 셀에 같은 ChunkInstance 참조 추가
                    if (!_placedChunks.ContainsKey(cellGridPos))
                    {
                        _placedChunks[cellGridPos] = chunkInstance;
                    }

                    _chunkGroupIds[cellGridPos] = groupId;
                }
            }

            return true;
        }

        private List<Vector2Int> GetGroupCells(MapLayoutTemplate template, int groupId)
        {
            List<Vector2Int> cells = new List<Vector2Int>();

            for (int y = 0; y < template.Height; y++)
            {
                for (int x = 0; x < template.Width; x++)
                {
                    if (template.GetChunkGroupId(x, y) == groupId)
                    {
                        cells.Add(new Vector2Int(x, y));
                    }
                }
            }

            return cells;
        }

        /// <summary>
        /// 시작 위치로부터 인접한(연결된) 같은 그룹 ID 셀들을 flood fill로 찾습니다.
        /// </summary>
        private List<Vector2Int> GetConnectedGroupCells(MapLayoutTemplate template, Vector2Int startPos, int groupId)
        {
            List<Vector2Int> connectedCells = new List<Vector2Int>();
            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            Queue<Vector2Int> queue = new Queue<Vector2Int>();

            queue.Enqueue(startPos);
            visited.Add(startPos);

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                connectedCells.Add(current);

                // 4방향 인접 셀 확인
                foreach (EDirection direction in DirectionExtensions.GetAllDirections())
                {
                    Vector2Int offset = direction.ToOffset();
                    Vector2Int neighbor = new Vector2Int(current.x + offset.x, current.y + offset.y);

                    // 이미 방문했거나 유효하지 않은 위치면 스킵
                    if (visited.Contains(neighbor) || !template.IsValidPosition(neighbor.x, neighbor.y))
                        continue;

                    // 같은 그룹 ID를 가진 셀만 추가
                    if (template.GetChunkGroupId(neighbor.x, neighbor.y) == groupId)
                    {
                        queue.Enqueue(neighbor);
                        visited.Add(neighbor);
                    }
                }
            }

            return connectedCells;
        }

        private (int width, int height) CalculateGroupSize(List<Vector2Int> groupCells)
        {
            if (groupCells.Count == 0)
                return (1, 1);

            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;

            foreach (var cell in groupCells)
            {
                if (cell.x < minX) minX = cell.x;
                if (cell.x > maxX) maxX = cell.x;
                if (cell.y < minY) minY = cell.y;
                if (cell.y > maxY) maxY = cell.y;
            }

            int width = maxX - minX + 1;
            int height = maxY - minY + 1;

            return (width, height);
        }

        /// <summary>
        /// 그룹의 경계 정보를 반환합니다.
        /// </summary>
        private (Vector2Int min, int width, int height) GetGroupBounds(List<Vector2Int> groupCells)
        {
            if (groupCells.Count == 0)
                return (Vector2Int.zero, 1, 1);

            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;

            foreach (var cell in groupCells)
            {
                if (cell.x < minX) minX = cell.x;
                if (cell.x > maxX) maxX = cell.x;
                if (cell.y < minY) minY = cell.y;
                if (cell.y > maxY) maxY = cell.y;
            }

            int width = maxX - minX + 1;
            int height = maxY - minY + 1;

            return (new Vector2Int(minX, minY), width, height);
        }

        /// <summary>
        /// 그룹의 중심 위치를 계산합니다 (피벗 위치 = 두 칸 사이 경계 중앙).
        /// 예: (0,0), (1,0) 그룹 → 중심 = (0.5, 0.5) (각 칸의 중심값 평균)
        /// </summary>
        private Vector2 GetGroupCenterPosition(List<Vector2Int> groupCells)
        {
            if (groupCells.Count == 0)
                return Vector2.zero;

            float sumX = 0;
            float sumY = 0;

            foreach (var cell in groupCells)
            {
                sumX += cell.x + 0.5f; // 각 칸의 중심
                sumY += cell.y + 0.5f;
            }

            return new Vector2(sumX / groupCells.Count, sumY / groupCells.Count);
        }

        /// <summary>
        /// 활성 청크들의 연결성을 검증합니다.
        /// 맵 생성 시 및 구역 축소 시 사용됩니다.
        /// </summary>
        private bool ValidateConnectivity()
        {
            if (_activeChunks.Count == 0)
            {
                Debug.LogError("연결성 검증: 활성 청크가 없습니다!");
                return false;
            }

            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            Queue<Vector2Int> queue = new Queue<Vector2Int>();

            // 활성 청크 중 아무거나 시작점으로
            Vector2Int startPos = _activeChunks.First();
            queue.Enqueue(startPos);
            visited.Add(startPos);

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();

                foreach (EDirection direction in DirectionExtensions.GetAllDirections())
                {
                    Vector2Int neighborPos = current + direction.ToOffset();

                    // 활성 청크이면서 아직 방문 안한 경우만
                    if (_activeChunks.Contains(neighborPos) && !visited.Contains(neighborPos))
                    {
                        visited.Add(neighborPos);
                        queue.Enqueue(neighborPos);
                    }
                }
            }

            // 모든 활성 청크가 연결되었는지 확인
            return visited.Count == _activeChunks.Count;
        }

        /// <summary>
        /// 청크를 생성합니다 (gridPosition = 청크의 왼쪽 아래 그리드 위치).
        /// </summary>
        private bool InstantiateChunk(Vector2Int gridPosition, ChunkPrefabData chunkData)
        {
            if (chunkData == null || chunkData.ChunkPrefab == null)
            {
                Debug.LogError($"[InstantiateChunk] ChunkData 또는 ChunkPrefab이 null입니다! gridPos={gridPosition}");
                return false;
            }

            if (_placedChunks.ContainsKey(gridPosition))
            {
                Debug.LogWarning($"[InstantiateChunk] gridPosition {gridPosition}에 이미 청크가 존재합니다!");
                return false;
            }

            GameObject chunkObj = Object.Instantiate(chunkData.ChunkPrefab, _parentTransform);
            chunkObj.name = $"Chunk_{chunkData.ChunkWidth}x{chunkData.ChunkHeight}_{gridPosition.x}_{gridPosition.y}_{chunkData.ChunkId}";

            // 청크 프리팹의 피벗이 왼쪽 아래 (0,0,0)에 있으므로
            // gridPosition을 그대로 ChunkSize로 곱하면 월드 위치
            Vector3 worldPosition = new Vector3(
                gridPosition.x * _settings.ChunkSize,
                0,
                gridPosition.y * _settings.ChunkSize
            );

            chunkObj.transform.position = worldPosition;

            Debug.Log($"[InstantiateChunk] ChunkId={chunkData.ChunkId}, gridPos={gridPosition}, worldPos={worldPosition}, " +
                     $"size={chunkData.ChunkWidth}x{chunkData.ChunkHeight}, ChunkSize={_settings.ChunkSize}");

            ChunkInstance chunkInstance = chunkObj.GetComponent<ChunkInstance>();
            if (chunkInstance == null)
            {
                chunkInstance = chunkObj.AddComponent<ChunkInstance>();
            }

            chunkInstance.Initialize(chunkData, gridPosition);

            _placedChunks[gridPosition] = chunkInstance;
            _activeChunks.Add(gridPosition); // 생성 시 활성 상태로 추가
            _currentChunkCount++;

            return true;
        }

        #endregion
    }
}
