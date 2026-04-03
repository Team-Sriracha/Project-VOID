using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 템플릿 기반 절차적 맵 생성기
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
        private readonly bool _isServer;

        #endregion

        #region Properties

        public IReadOnlyDictionary<Vector2Int, ChunkInstance> PlacedChunks => _placedChunks;
        public int CurrentChunkCount => _currentChunkCount;
        
        /// <summary>
        /// 맵 생성 설정 반환
        /// </summary>
        public MapGenerationSettings Settings => _settings;
        
        /// <summary>
        /// 맵 가로 크기 (청크 단위)
        /// </summary>
        public int MapWidth { get; private set; }
        
        /// <summary>
        /// 맵 세로 크기 (청크 단위)
        /// </summary>
        public int MapHeight { get; private set; }

        /// <summary>
        /// 맵 중심 월드 좌표 반환
        /// </summary>
        public Vector3 GetMapCenter()
        {
            if (_placedChunks == null || _placedChunks.Count == 0)
            {
                return Vector3.zero;
            }

            // 모든 청크의 그리드 위치에서 최소/최대 값 계산
            Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
            Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);

            foreach (var gridPos in _placedChunks.Keys)
            {
                min.x = Mathf.Min(min.x, gridPos.x);
                min.y = Mathf.Min(min.y, gridPos.y);
                max.x = Mathf.Max(max.x, gridPos.x);
                max.y = Mathf.Max(max.y, gridPos.y);
            }

            // 그리드 중심을 월드 좌표로 변환
            Vector2 gridCenter = new Vector2(
                (min.x + max.x) / 2f,
                (min.y + max.y) / 2f
            );

            int chunkSize = _settings.ChunkSize;
            return new Vector3(
                gridCenter.x * chunkSize,
                0f,
                gridCenter.y * chunkSize
            );
        }

        #endregion

        #region Constructor

        public MapGenerator(MapGenerationSettings settings, Transform parentTransform, bool isServer)
        {
            _settings = settings;
            _parentTransform = parentTransform;
            _placedChunks = new Dictionary<Vector2Int, ChunkInstance>();
            _chunkGroupIds = new Dictionary<Vector2Int, int>();
            _chunkIndexMap = new Dictionary<Vector2Int, byte>();
            _activeChunks = new HashSet<Vector2Int>();
            _generationTimer = new Stopwatch();
            _isServer = isServer;
        }

        #endregion

        #region Public Methods

        public bool GenerateMap(int seed, MapLayoutTemplate templateToUse)
        {
            if (!_isServer)
            {
                Debug.LogWarning("[MapGenerator] GenerateMap은 서버에서만 호출할 수 있습니다.");
                return false;
            }

            return TryGenerateMap(seed, templateToUse);
        }

        public NetworkChunkData[] ToNetworkArray()
        {
            if (!_isServer)
            {
                Debug.LogWarning("[MapGenerator] ToNetworkArray는 서버에서만 호출할 수 있습니다.");
                return System.Array.Empty<NetworkChunkData>();
            }

            // Position 기준으로 정렬하여 순서 보장 (클라이언트와 동일한 순서)
            var sortedChunks = _placedChunks
                .OrderBy(kvp => kvp.Key.y)
                .ThenBy(kvp => kvp.Key.x)
                .ToList();

            var networkChunks = new List<NetworkChunkData>();
            var processedChunks = new HashSet<ChunkInstance>();

            foreach (var kvp in sortedChunks)
            {
                Vector2Int position = kvp.Key;
                ChunkInstance chunk = kvp.Value;

                // 그룹 청크는 여러 칸에 같은 ChunkInstance가 있으므로 한 번만 처리
                if (processedChunks.Contains(chunk))
                    continue;

                processedChunks.Add(chunk);

                byte actualDoorMask = chunk.GetActualDoorMask();
                int groupId = _chunkGroupIds.ContainsKey(position) ? _chunkGroupIds[position] : 0;
                byte chunkIndex = _chunkIndexMap.ContainsKey(position) ? _chunkIndexMap[position] : (byte)0;

                networkChunks.Add(new NetworkChunkData(
                    position,
                    chunk.ChunkData.ChunkType,
                    actualDoorMask,
                    groupId,
                    chunk.ChunkData.ChunkWidth,
                    chunk.ChunkData.ChunkHeight,
                    chunkIndex
                ));
            }

            return networkChunks.ToArray();
        }

        /// <summary>
        /// 전체 청크 벽/문 구간 정보 배열 반환
        /// </summary>
        public WallSegmentData[] ToWallSegmentArray()
        {
            if (!_isServer)
            {
                Debug.LogWarning("[MapGenerator] ToWallSegmentArray는 서버에서만 호출할 수 있습니다.");
                return System.Array.Empty<WallSegmentData>();
            }

            var allSegments = new List<WallSegmentData>();
            var processedChunks = new HashSet<ChunkInstance>();

            // Position 기준으로 정렬하여 순서 보장 (ChunkArray와 동일한 순서)
            var sortedChunks = _placedChunks
                .OrderBy(kvp => kvp.Key.y)
                .ThenBy(kvp => kvp.Key.x)
                .ToList();

            int chunkIndex = 0;
            foreach (var kvp in sortedChunks)
            {
                ChunkInstance chunk = kvp.Value;

                // 그룹 청크는 여러 칸에 같은 ChunkInstance가 있으므로 한 번만 처리
                if (processedChunks.Contains(chunk))
                    continue;

                processedChunks.Add(chunk);

                WallSegmentData[] segments = chunk.GetAllWallSegments();

                // ChunkIndex 설정
                for (int i = 0; i < segments.Length; i++)
                {
                    segments[i].ChunkIndex = (ushort)chunkIndex;
                }

                allSegments.AddRange(segments);
                chunkIndex++;
            }

            return allSegments.ToArray();
        }


        /// <summary>
        /// 청크 비활성화 (구역 축소)
        /// </summary>
        public void DeactivateChunk(Vector2Int gridPosition)
        {
            if (_activeChunks.Remove(gridPosition))
            {
                Debug.Log($"청크 {gridPosition} 비활성화");
            }
        }

        /// <summary>
        /// 청크 재활성화
        /// </summary>
        public void ActivateChunk(Vector2Int gridPosition)
        {
            if (_placedChunks.ContainsKey(gridPosition) && _activeChunks.Add(gridPosition))
            {
                Debug.Log($"청크 {gridPosition} 활성화");
            }
        }

        /// <summary>
        /// 다수 청크 비활성화 및 연결성 검증
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
        /// 다수 청크 비활성화 및 Start Chunk 연결성 검증
        /// </summary>
        /// <param name="chunksToDeactivate">비활성화할 청크 목록</param>
        /// <param name="startChunkPosition">연결성 검증 기준점 (Start Chunk)</param>
        /// <returns>비활성화 후에도 모든 청크가 Start Chunk와 연결되어 있으면 true</returns>
        public bool DeactivateChunksAndValidateFromStart(HashSet<Vector2Int> chunksToDeactivate, Vector2Int startChunkPosition)
        {
            // 백업
            var backup = new HashSet<Vector2Int>(_activeChunks);

            // 비활성화
            foreach (var pos in chunksToDeactivate)
            {
                _activeChunks.Remove(pos);
            }

            // Start Chunk 기준 연결성 검증
            if (ValidateConnectivityFromStart(startChunkPosition))
            {
                Debug.Log($"{chunksToDeactivate.Count}개 청크 비활성화 성공. 남은 활성 청크: {_activeChunks.Count}");
                return true;
            }
            else
            {
                // 실패 시 복원
                _activeChunks = backup;
                Debug.LogWarning($"청크 비활성화 실패: Start Chunk와의 연결성이 끊어집니다. 복원됨.");
                return false;
            }
        }

        /// <summary>
        /// 현재 활성 청크 개수 반환
        /// </summary>
        public int ActiveChunkCount => _activeChunks.Count;

        /// <summary>
        /// 청크가 활성 상태인지 확인합니다.
        /// </summary>
        public bool IsChunkActive(Vector2Int gridPosition)
        {
            return _activeChunks.Contains(gridPosition);
        }

        /// <summary>
        /// 가장자리 청크들을 반환합니다 (맵 외부와 접하는 청크).
        /// </summary>
        public HashSet<Vector2Int> GetEdgeChunks()
        {
            var edgeChunks = new HashSet<Vector2Int>();

            if (_activeChunks.Count == 0) return edgeChunks;

            // 맵 외부(PlacedChunks에 없는 공간)와 접하는 청크가 진짜 가장자리
            foreach (var chunkPos in _activeChunks)
            {
                bool touchesExterior = false;

                // 4방향 이웃 확인
                foreach (var direction in DirectionExtensions.GetAllDirections())
                {
                    Vector2Int neighbor = chunkPos + direction.ToOffset();

                    // PlacedChunks에 없음 = 처음부터 생성되지 않은 맵 외부
                    if (!_placedChunks.ContainsKey(neighbor))
                    {
                        touchesExterior = true;
                        break;
                    }
                }

                if (touchesExterior)
                {
                    edgeChunks.Add(chunkPos);
                }
            }

            return edgeChunks;
        }

        public bool RenderMapFromNetworkData(NetworkChunkData[] networkChunks, WallSegmentData[] wallSegments, int seed)
        {
            ClearExistingChunks();
            _placedChunks.Clear();
            _chunkGroupIds.Clear();
            _chunkIndexMap.Clear();
            _activeChunks.Clear();
            _currentChunkCount = 0;

            Debug.Log($"[Client-RenderMapFromNetworkData] Seed={seed}, ChunkCount={networkChunks.Length}, WallSegmentCount={wallSegments.Length}");

            // Position 기준으로 정렬 (서버와 같은 순서로 처리)
            var sortedNetworkChunks = networkChunks
                .OrderBy(chunk => chunk.Position.y)
                .ThenBy(chunk => chunk.Position.x)
                .ToArray();

            // 맵 크기 계산 (청크 위치에서 min/max 찾아서 계산)
            if (sortedNetworkChunks.Length > 0)
            {
                int minX = int.MaxValue, minY = int.MaxValue;
                int maxX = int.MinValue, maxY = int.MinValue;

                foreach (var chunk in sortedNetworkChunks)
                {
                    minX = Mathf.Min(minX, chunk.Position.x);
                    minY = Mathf.Min(minY, chunk.Position.y);
                    maxX = Mathf.Max(maxX, chunk.Position.x + chunk.ChunkWidth - 1);
                    maxY = Mathf.Max(maxY, chunk.Position.y + chunk.ChunkHeight - 1);
                }

                MapWidth = maxX - minX + 1;
                MapHeight = maxY - minY + 1;
                Debug.Log($"[Client] 맵 크기 계산: MapWidth={MapWidth}, MapHeight={MapHeight}");
            }

            // 청크 프리팹 인스턴스화
            var chunkInstances = new List<ChunkInstance>();
            int chunkIndex = 0;
            foreach (var networkData in sortedNetworkChunks)
            {
                chunkIndex++;

                Debug.Log($"[Client] 청크 {chunkIndex}/{sortedNetworkChunks.Length}: Position={networkData.Position}, " +
                         $"Type={networkData.Type}, Size={networkData.ChunkWidth}x{networkData.ChunkHeight}, GroupId={networkData.GroupId}");

                // EChunkType을 EGridCell로 변환
                GridCell gridCellType = ConvertChunkTypeToGridCell(networkData.Type);
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

                // 청크가 차지하는 모든 영역에 그룹 ID 저장 및 _placedChunks, _activeChunks에 추가
                if (_placedChunks.TryGetValue(networkData.Position, out ChunkInstance chunkInstance))
                {
                    for (int dy = 0; dy < networkData.ChunkHeight; dy++)
                    {
                        for (int dx = 0; dx < networkData.ChunkWidth; dx++)
                        {
                            Vector2Int cellPos = networkData.Position + new Vector2Int(dx, dy);
                            _chunkGroupIds[cellPos] = networkData.GroupId;
                            
                            // 그룹 청크의 모든 셀을 _placedChunks와 _activeChunks에 추가
                            if (!_placedChunks.ContainsKey(cellPos))
                            {
                                _placedChunks[cellPos] = chunkInstance;
                            }
                            if (!_activeChunks.Contains(cellPos))
                            {
                                _activeChunks.Add(cellPos);
                            }
                        }
                    }
                }

                // 청크 인스턴스 저장 (중복 제거)
                if (_placedChunks.TryGetValue(networkData.Position, out ChunkInstance instance))
                {
                    if (!chunkInstances.Contains(instance))
                    {
                        chunkInstances.Add(instance);
                    }
                }
            }

            // 서버에서 보낸 벽/문 구간 정보 기반으로 생성
            foreach (var segment in wallSegments)
            {
                if (segment.ChunkIndex >= chunkInstances.Count)
                {
                    Debug.LogWarning($"[Client] WallSegment ChunkIndex({segment.ChunkIndex})가 범위를 초과합니다!");
                    continue;
                }

                ChunkInstance chunk = chunkInstances[segment.ChunkIndex];
                Direction direction = segment.GetDirection();

                if (segment.IsDoor)
                {
                    chunk.SetDoor(direction, _settings.DoorPrefab, _settings.WallPrefab, _settings.ChunkSize,
                                _settings.WallSpacing, segment.StartIndex, segment.EndIndex + 1);
                }
                else
                {
                    chunk.SetWall(direction, _settings.WallPrefab, _settings.ChunkSize,
                                _settings.WallSpacing, segment.StartIndex, segment.EndIndex + 1);
                }
            }

            Debug.Log($"[Client] 맵 렌더링 완료: {chunkInstances.Count}개 청크, {wallSegments.Length}개 벽 구간");

            return _currentChunkCount > 0;
        }

        #endregion

        #region Private Methods

        private bool TryGenerateMap(int seed, MapLayoutTemplate templateToUse)
        {
            ClearExistingChunks();
            _placedChunks.Clear();
            _chunkGroupIds.Clear();
            _activeChunks.Clear();
            _currentChunkCount = 0;
            _random = new System.Random(seed);
            _doorRandom = new System.Random(seed + 1000);
            _generationTimer.Restart();

            MapLayoutTemplate template = templateToUse; // Use the provided template
            if (template == null)
            {
                Debug.LogError("[MapGenerator] 제공된 템플릿이 null입니다!");
                return false;
            }
            
            // NavMeshBaker에서 맵 크기를 알 수 있도록 저장
            MapWidth = template.Width;
            MapHeight = template.Height;

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

        public void ResetAllChunks()
        {
            if (_placedChunks == null) return;

            foreach (var kvp in _placedChunks)
            {
                var chunk = kvp.Value;
                if (chunk != null && chunk.gameObject != null)
                {
                    chunk.gameObject.SetActive(true);
                }
            }

            // 활성 청크 목록도 초기화 (모든 청크 활성화)
            if (_activeChunks == null) _activeChunks = new HashSet<Vector2Int>();
            _activeChunks.Clear();
            foreach (var pos in _placedChunks.Keys)
            {
                _activeChunks.Add(pos);
            }

            Debug.Log($"[MapGenerator] 모든 청크 리셋 및 활성화 완료 ({_activeChunks.Count}/{_placedChunks.Count})");
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
                    GridCell cellType = template.GetCell(x, y);

                    if (cellType == GridCell.Empty)
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
            HashSet<(ChunkInstance, Direction)> processedWalls = new HashSet<(ChunkInstance, Direction)>();
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

                foreach (Direction direction in DirectionExtensions.GetAllDirections())
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
                        if (direction == Direction.North || direction == Direction.East)
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
        private void GenerateOuterWallsOnly(ChunkInstance chunk, Direction direction, bool[] cellHasNeighbor)
        {
            if (cellHasNeighbor == null || cellHasNeighbor.Length == 0)
                return;

            // 1칸당 벽 개수 계산
            int wallsPerCell = Mathf.FloorToInt(_settings.ChunkSize / _settings.WallSpacing);

            // 연속된 범위 찾기
            List<(int startCell, int endCell, bool hasNeighbor)> ranges = GetConsecutiveRanges(cellHasNeighbor);

            // 이웃 없는 범위만 외벽 생성
            foreach (var (startCell, endCell, hasNeighbor) in ranges)
            {
                if (!hasNeighbor)  // 이웃 없는 부분만
                {
                    int startWallIndex = startCell * wallsPerCell;
                    int endWallIndex = (endCell + 1) * wallsPerCell;

                    chunk.SetWall(direction, _settings.WallPrefab, _settings.ChunkSize,
                                _settings.WallSpacing, startWallIndex, endWallIndex);
                }
                // 이웃 있는 부분은 스킵 (이웃 청크의 North/East가 생성함)
            }
        }

        /// <summary>
        /// 셀별 이웃 정보를 기반으로 부분 벽을 생성합니다.
        /// </summary>
        private void GeneratePartialWalls(ChunkInstance chunk, Direction direction, bool[] cellHasNeighbor,
                                          Vector2Int representativeKey, Vector2Int offset,
                                          HashSet<(Vector2Int, Vector2Int)> essentialConnections)
        {
            if (cellHasNeighbor == null || cellHasNeighbor.Length == 0)
                return;

            // 1칸당 벽 개수 계산
            int wallsPerCell = Mathf.FloorToInt(_settings.ChunkSize / _settings.WallSpacing);

            // 각 셀별로 개별 처리 (문은 셀 가운데에만 생성)
            for (int cellIndex = 0; cellIndex < cellHasNeighbor.Length; cellIndex++)
            {
                int startWallIndex = cellIndex * wallsPerCell;
                int endWallIndex = (cellIndex + 1) * wallsPerCell;

                if (cellHasNeighbor[cellIndex])
                {
                    // 이웃이 있는 셀: spanning tree 기반 문 생성 확률
                    int width = chunk.ChunkData != null ? chunk.ChunkData.ChunkWidth : 1;
                    int height = chunk.ChunkData != null ? chunk.ChunkData.ChunkHeight : 1;

                    // 현재 경계 셀의 위치 계산 (방향에 따라 다름)
                    Vector2Int boundaryCellOffset = direction switch
                    {
                        Direction.North => new Vector2Int(cellIndex, height - 1),  // 윗줄
                        Direction.South => new Vector2Int(cellIndex, 0),            // 아랫줄
                        Direction.East => new Vector2Int(width - 1, cellIndex),     // 오른쪽 열
                        Direction.West => new Vector2Int(0, cellIndex),             // 왼쪽 열
                        _ => Vector2Int.zero
                    };

                    Vector2Int currentCellPos = representativeKey + boundaryCellOffset;
                    Vector2Int neighborGridPos = currentCellPos + offset;
                    
                    // spanning tree에서 현재 셀과 이웃 셀 사이에 연결이 있는지 확인
                    bool isEssential = essentialConnections.Contains((currentCellPos, neighborGridPos)) ||
                                     essentialConnections.Contains((neighborGridPos, currentCellPos));

                    bool generateDoor = isEssential || _doorRandom.NextDouble() < _settings.DoorGenerationProbability;

                    if (generateDoor)
                    {
                        // 문은 셀의 가운데에만 생성 (예: 벽-문-벽)
                        int midIndex = (startWallIndex + endWallIndex) / 2;

                        // 문 앞쪽 벽
                        if (startWallIndex < midIndex)
                        {
                            chunk.SetWall(direction, _settings.WallPrefab, _settings.ChunkSize,
                                        _settings.WallSpacing, startWallIndex, midIndex);
                        }

                        // 가운데 문 (1개)
                        chunk.SetDoor(direction, _settings.DoorPrefab, _settings.WallPrefab, _settings.ChunkSize,
                                    _settings.WallSpacing, midIndex, midIndex + 1);

                        // 문 뒤쪽 벽
                        if (midIndex + 1 < endWallIndex)
                        {
                            chunk.SetWall(direction, _settings.WallPrefab, _settings.ChunkSize,
                                        _settings.WallSpacing, midIndex + 1, endWallIndex);
                        }
                    }
                    else
                    {
                        // 문 생성 안함: 전체 벽
                        chunk.SetWall(direction, _settings.WallPrefab, _settings.ChunkSize,
                                    _settings.WallSpacing, startWallIndex, endWallIndex);
                    }
                }
                else
                {
                    // 이웃 없는 셀: 외벽
                    chunk.SetWall(direction, _settings.WallPrefab, _settings.ChunkSize,
                                _settings.WallSpacing, startWallIndex, endWallIndex);
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

                foreach (Direction direction in DirectionExtensions.GetAllDirections())
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

        private void GenerateWallOrDoor(ChunkInstance chunk, Direction direction, bool hasDoor)
        {
            if (hasDoor)
            {
                // 전체 범위에서 문+벽 배치
                int chunkSize = _settings.ChunkSize;
                float wallSpacing = _settings.WallSpacing;
                int effectiveLength = chunk.GetEffectiveLengthForDirection(direction, chunkSize);
                int totalCount = Mathf.FloorToInt(effectiveLength / wallSpacing);
                int midIndex = totalCount / 2;

                // 문 앞쪽 벽
                if (midIndex > 0)
                {
                    chunk.SetWall(direction, _settings.WallPrefab, chunkSize, wallSpacing, 0, midIndex);
                }

                // 문
                chunk.SetDoor(direction, _settings.DoorPrefab, _settings.WallPrefab, chunkSize, wallSpacing, midIndex, midIndex + 1);

                // 문 뒤쪽 벽
                if (midIndex + 1 < totalCount)
                {
                    chunk.SetWall(direction, _settings.WallPrefab, chunkSize, wallSpacing, midIndex + 1, totalCount);
                }
            }
            else
            {
                chunk.SetWall(direction, _settings.WallPrefab, _settings.ChunkSize, _settings.WallSpacing);
            }
        }

        /// <summary>
        /// 그룹 청크의 특정 방향에 외부 이웃이 있는지 확인하고, 각 셀별 이웃 정보를 반환
        /// </summary>
        /// <returns>(이웃 존재 여부, 셀별 이웃 마스크)</returns>
        private (bool hasAnyNeighbor, bool[] cellHasNeighbor) CheckGroupChunkNeighborDetailed(MapLayoutTemplate template, ChunkInstance chunk, Vector2Int representativeKey, Vector2Int center, Direction direction)
        {
            int width = chunk.ChunkData != null ? chunk.ChunkData.ChunkWidth : 1;
            int height = chunk.ChunkData != null ? chunk.ChunkData.ChunkHeight : 1;

            Vector2Int offset = direction.ToOffset();

            // 해당 방향의 셀 개수
            int cellCount = (direction == Direction.North || direction == Direction.South) ? width : height;
            bool[] cellHasNeighbor = new bool[cellCount];
            bool hasAnyNeighbor = false;

            // 해당 방향의 모든 경계 칸에 대해 이웃 확인
            for (int i = 0; i < cellCount; i++)
            {
                Vector2Int cellOffset = direction switch
                {
                    Direction.North => new Vector2Int(i, height - 1),  // 윗줄
                    Direction.South => new Vector2Int(i, 0),            // 아랫줄
                    Direction.East => new Vector2Int(width - 1, i),     // 오른쪽 열
                    Direction.West => new Vector2Int(0, i),             // 왼쪽 열
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

                GridCell neighborType = template.GetCell(neighborTemplateX, neighborTemplateY);
                if (neighborType != GridCell.Empty)
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

        private GridCell ConvertChunkTypeToGridCell(ChunkType chunkType)
        {
            return chunkType switch
            {
                ChunkType.Central => GridCell.Central,
                ChunkType.Normal => GridCell.Normal,
                ChunkType.Special => GridCell.Special,
                _ => GridCell.Empty
            };
        }

        private ChunkPrefabData[] GetChunkPoolForCell(GridCell cellType)
        {
            return cellType switch
            {
                GridCell.Central => _settings.CentralChunks,
                GridCell.Normal => _settings.NormalChunks,
                GridCell.Special => _settings.SpecialChunks,
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

        private bool PlaceGroupChunk(MapLayoutTemplate template, Vector2Int startPos, int groupId, Vector2Int center, GridCell cellType, out List<Vector2Int> groupCells)
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

            // 4. 대표 키 (왼쪽 아래)를 그리드 좌표로 변환
            Vector2Int representativeKey = new Vector2Int(minPos.x - center.x, minPos.y - center.y);

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

                    // 그룹 청크의 모든 셀을 _activeChunks에도 추가 (연결성 검증용)
                    if (!_activeChunks.Contains(cellGridPos))
                    {
                        _activeChunks.Add(cellGridPos);
                    }

                    _chunkGroupIds[cellGridPos] = groupId;
                }
            }

            return true;
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
                foreach (Direction direction in DirectionExtensions.GetAllDirections())
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

                foreach (Direction direction in DirectionExtensions.GetAllDirections())
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
        /// 특정 Start Chunk로부터 모든 활성 청크가 연결되어 있는지 검증합니다.
        /// </summary>
        /// <param name="startChunkPosition">연결성 검증 기준점</param>
        /// <returns>모든 활성 청크가 Start Chunk와 연결되어 있으면 true</returns>
        private bool ValidateConnectivityFromStart(Vector2Int startChunkPosition)
        {
            if (_activeChunks.Count == 0)
            {
                Debug.LogError($"[ValidateConnectivityFromStart] 활성 청크가 없습니다!");
                return false;
            }

            if (!_activeChunks.Contains(startChunkPosition))
            {
                Debug.LogError($"[ValidateConnectivityFromStart] Start Chunk({startChunkPosition})가 활성 청크가 아닙니다!");
                return false;
            }

            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            Queue<Vector2Int> queue = new Queue<Vector2Int>();

            // Start Chunk부터 시작
            queue.Enqueue(startChunkPosition);
            visited.Add(startChunkPosition);

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();

                // 4방향 이웃 확인
                foreach (Direction direction in DirectionExtensions.GetAllDirections())
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

            // 모든 활성 청크가 Start Chunk와 연결되었는지 확인
            bool isConnected = visited.Count == _activeChunks.Count;

            if (!isConnected)
            {
                Debug.LogWarning($"[ValidateConnectivityFromStart] Start Chunk({startChunkPosition})와 연결 안 된 청크 발견! " +
                                $"방문: {visited.Count}개, 활성: {_activeChunks.Count}개");
            }

            return isConnected;
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
