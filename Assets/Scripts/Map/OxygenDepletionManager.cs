using Fusion;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 산소 고갈 구역(Oxygen Depletion Zone) 시스템을 관리합니다.
    /// 페이즈마다 맵 가장자리부터 자동으로 축소하며, 최종적으로 시작 청크만 남깁니다.
    /// </summary>
    public class OxygenDepletionManager : NetworkBehaviour
    {
        #region Constants

        private const int MAX_CHUNK_CAPACITY = 500;
        private const float DAMAGE_INTERVAL = 1f;
        private const float RED_LIGHT_HEIGHT = 2f;
        private const float CHUNK_CENTER_OFFSET = 0.5f;

        #endregion

        #region Serialized Fields

        [Header("데미지 설정")]
        [Tooltip("산소 고갈 구역에서 초당 입는 데미지")]
        [SerializeField] private float _damagePerSecond = 5f;

        [Header("경고 시스템")]
        [Tooltip("청크 비활성화 몇 초 전부터 경고를 시작할지 (빨간 조명 깜빡임)")]
        [SerializeField] private float _warningDuration = 5f;

        [Header("시각 효과")]
        [Tooltip("산소 고갈 구역에 배치할 붉은 조명 프리팹")]
        [SerializeField] private GameObject _redLightPrefab;

        [Tooltip("경고 시 조명 깜빡임 속도 (초당 깜빡임 횟수)")]
        [SerializeField] private float _blinkSpeed = 2f;

        #endregion

        #region Networked Properties

        /// <summary>
        /// 각 청크의 산소 고갈 상태를 저장하는 마스크 (true = 고갈됨)
        /// </summary>
        [Networked, Capacity(MAX_CHUNK_CAPACITY)]
        public NetworkArray<NetworkBool> DepletedZoneMask => default;

        /// <summary>
        /// 각 청크의 경고 상태를 저장하는 마스크 (true = 경고 중, 곧 고갈됨)
        /// </summary>
        [Networked, Capacity(MAX_CHUNK_CAPACITY)]
        public NetworkArray<NetworkBool> WarningZoneMask => default;

        /// <summary>
        /// 마지막으로 처리한 Phase (중복 처리 방지)
        /// </summary>
        [Networked]
        public int LastProcessedPhase { get; set; }

        /// <summary>
        /// 시작 청크의 위치 (최종 페이즈에 남을 청크)
        /// </summary>
        [Networked]
        public Vector2Int StartChunkPosition { get; set; }

        /// <summary>
        /// 다음 Phase까지 남은 시간 (경고 시스템용)
        /// </summary>
        [Networked]
        public TickTimer PhaseTimer { get; set; }

        #endregion

        #region Private Fields

        private GameStateManager _gameStateManager;
        private MapGenerator _mapGenerator;
        private MapGenerationSettings _mapSettings;
        private TickTimer _damageTimer;
        private Dictionary<Vector2Int, List<GameObject>> _redLights;
        private Dictionary<Vector2Int, int> _chunkToIndexMap; // 청크 위치 → NetworkArray 인덱스 매핑
        private HashSet<Vector2Int> _warningChunks; // 경고 중인 청크 (깜빡임 효과용)
        private int _lastWarningPhase; // 마지막으로 경고를 표시한 Phase (중복 방지)

        #endregion

        #region Initialization

        /// <summary>
        /// OxygenDepletionManager를 초기화합니다.
        /// </summary>
        public void Initialize(MapGenerator mapGenerator, GameStateManager gameStateManager, MapGenerationSettings mapSettings)
        {
            _mapGenerator = mapGenerator;
            _gameStateManager = gameStateManager;
            _mapSettings = mapSettings;
            _redLights = new Dictionary<Vector2Int, List<GameObject>>();
            _chunkToIndexMap = new Dictionary<Vector2Int, int>();

            if (HasStateAuthority)
            {
                StartChunkPosition = FindStartChunk();
                Debug.Log($"[OxygenDepletionManager] Start Chunk: {StartChunkPosition}");

                ResetAllChunksToActive();

                // 청크 위치 → 인덱스 매핑 초기화
                InitializeChunkIndexMap();
            }
        }

        /// <summary>
        /// 청크 위치를 NetworkArray 인덱스로 매핑합니다.
        /// </summary>
        public void InitializeChunkIndexMap()
        {
            if (_chunkToIndexMap == null)
            {
                _chunkToIndexMap = new Dictionary<Vector2Int, int>();
            }

            _chunkToIndexMap.Clear();

            if (_mapGenerator == null || _mapGenerator.PlacedChunks == null)
            {
                Debug.LogWarning("[OxygenDepletionManager] MapGenerator 또는 PlacedChunks가 null입니다!");
                return;
            }

            // Why: 그룹 청크 중복 제거 (ToNetworkArray()와 동일한 로직)
            var sortedChunks = _mapGenerator.PlacedChunks
                .OrderBy(kvp => kvp.Key.y)
                .ThenBy(kvp => kvp.Key.x)
                .ToList();

            var processedChunks = new HashSet<ChunkInstance>();
            var uniqueChunkPositions = new List<Vector2Int>();

            foreach (var kvp in sortedChunks)
            {
                ChunkInstance chunk = kvp.Value;

                if (processedChunks.Contains(chunk))
                    continue;

                processedChunks.Add(chunk);
                uniqueChunkPositions.Add(kvp.Key);
            }

            // 그룹 청크의 모든 그리드 셀을 같은 인덱스로 매핑
            for (int i = 0; i < uniqueChunkPositions.Count && i < MAX_CHUNK_CAPACITY; i++)
            {
                Vector2Int representativePos = uniqueChunkPositions[i];

                if (_mapGenerator.PlacedChunks.TryGetValue(representativePos, out ChunkInstance chunk))
                {
                    int width = chunk.ChunkData.ChunkWidth;
                    int height = chunk.ChunkData.ChunkHeight;

                    // 청크가 차지하는 모든 그리드 셀을 같은 인덱스로 매핑
                    for (int dy = 0; dy < height; dy++)
                    {
                        for (int dx = 0; dx < width; dx++)
                        {
                            Vector2Int cellPos = representativePos + new Vector2Int(dx, dy);
                            _chunkToIndexMap[cellPos] = i;
                        }
                    }
                }
            }

            Debug.Log($"[OxygenDepletionManager] 청크 인덱스 매핑 완료: {_chunkToIndexMap.Count}개");

            // 서버는 매핑 정보를 클라이언트에 전송
            if (HasStateAuthority)
            {
                RPC_SyncChunkMapping(uniqueChunkPositions.ToArray(), uniqueChunkPositions.Count);
            }
        }

        /// <summary>
        /// 서버의 청크 매핑 정보를 클라이언트에 전송합니다.
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_SyncChunkMapping(Vector2Int[] chunkPositions, int count)
        {
            if (HasStateAuthority) return; // 서버는 이미 매핑함

            Debug.Log($"[OxygenDepletionManager] 서버로부터 청크 매핑 수신: {count}개");

            if (_chunkToIndexMap == null)
            {
                _chunkToIndexMap = new Dictionary<Vector2Int, int>();
            }

            _chunkToIndexMap.Clear();

            for (int i = 0; i < count && i < chunkPositions.Length; i++)
            {
                _chunkToIndexMap[chunkPositions[i]] = i;
            }

            Debug.Log($"[OxygenDepletionManager] 클라이언트 매핑 완료: {_chunkToIndexMap.Count}개");
        }

        /// <summary>
        /// 게임 시작 시 모든 청크를 활성화 상태로 리셋합니다.
        /// </summary>
        private void ResetAllChunksToActive()
        {
            if (_mapGenerator == null || _mapGenerator.PlacedChunks == null)
            {
                Debug.LogWarning("[OxygenDepletionManager] MapGenerator 또는 PlacedChunks가 null입니다!");
                return;
            }

            int activatedCount = 0;
            foreach (var chunkPos in _mapGenerator.PlacedChunks.Keys)
            {
                if (!_mapGenerator.IsChunkActive(chunkPos))
                {
                    _mapGenerator.ActivateChunk(chunkPos);
                    activatedCount++;
                }
            }

            for (int i = 0; i < MAX_CHUNK_CAPACITY; i++)
            {
                DepletedZoneMask.Set(i, false);
            }

            Debug.Log($"[OxygenDepletionManager] 모든 청크 활성화 완료: {activatedCount}개 재활성화, 총 {_mapGenerator.ActiveChunkCount}개 활성");
        }

        /// <summary>
        /// 시작 청크를 찾습니다 (Central 타입 또는 (0,0)에 가장 가까운 청크).
        /// </summary>
        private Vector2Int FindStartChunk()
        {
            // 1. Central 타입 청크 우선 검색
            foreach (var kvp in _mapGenerator.PlacedChunks)
            {
                if (kvp.Value.ChunkData.ChunkType == EChunkType.Central)
                {
                    Debug.Log($"[OxygenDepletionManager] Central 청크 발견: {kvp.Key}");
                    return kvp.Key;
                }
            }

            // 2. Central이 없으면 (0,0)에 가장 가까운 청크
            Vector2Int closest = Vector2Int.zero;
            int minDist = int.MaxValue;

            foreach (var pos in _mapGenerator.PlacedChunks.Keys)
            {
                int dist = Mathf.Abs(pos.x) + Mathf.Abs(pos.y); // Manhattan Distance
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = pos;
                }
            }

            Debug.Log($"[OxygenDepletionManager] (0,0)에 가장 가까운 청크: {closest}");
            return closest;
        }

        #endregion

        #region Fusion Lifecycle

        public override void Spawned()
        {
            if (_redLights == null)
            {
                _redLights = new Dictionary<Vector2Int, List<GameObject>>();
            }

            if (_chunkToIndexMap == null)
            {
                _chunkToIndexMap = new Dictionary<Vector2Int, int>();
            }

            if (_warningChunks == null)
            {
                _warningChunks = new HashSet<Vector2Int>();
            }

            Debug.Log($"[OxygenDepletionManager] Spawned - HasStateAuthority={HasStateAuthority}");

            // Why: 클라이언트는 나중에 맵이 렌더링되면 InitializeForClient()가 호출됨
        }

        /// <summary>
        /// 클라이언트에서 맵 렌더링 후 호출되어 초기화합니다.
        /// </summary>
        public void InitializeForClient(MapGenerator mapGenerator, MapGenerationSettings mapSettings)
        {
            _mapGenerator = mapGenerator;
            _mapSettings = mapSettings;

            InitializeChunkIndexMap();

            Debug.Log($"[OxygenDepletionManager] 클라이언트 초기화 완료");
        }

        public override void FixedUpdateNetwork()
        {
            // Why: Runner가 종료 중이거나 실행 중이 아니면 처리하지 않음
            if (Runner == null || !Runner.IsRunning) return;

            if (!HasStateAuthority) return;

            int currentPhase = _gameStateManager.CurrentPhase;

            // Phase가 변경되고 Phase 2 이상일 때만 맵 축소
            if (currentPhase > LastProcessedPhase && currentPhase >= 2)
            {
                ShrinkMap(currentPhase);
                LastProcessedPhase = currentPhase;

                // Phase 변경 시 모든 경고 제거
                _warningChunks.Clear();
                for (int i = 0; i < MAX_CHUNK_CAPACITY; i++)
                {
                    WarningZoneMask.Set(i, false);
                }
                _lastWarningPhase = 0;
                RPC_SyncWarningZone();
            }

            // 다음 Phase까지 _warningDuration 초 남았을 때 경고 표시
            float timeToNext = _gameStateManager.TimeToNextPhase;
            int nextPhase = currentPhase + 1;

            if (timeToNext > 0f && timeToNext <= _warningDuration && _lastWarningPhase != nextPhase)
            {
                PreviewNextPhaseWarning(nextPhase);
                _lastWarningPhase = nextPhase;
                Debug.Log($"[OxygenDepletionManager] Phase {nextPhase} 경고 시작 (남은 시간: {timeToNext:F1}초)");
            }

            ApplyDamageToPlayersInDepletedZone();
        }

        public override void Render()
        {
            UpdateWarningLightBlink();
        }

        #endregion

        #region Map Shrinking

        /// <summary>
        /// 다음 Phase에서 비활성화될 청크들에 경고를 표시합니다.
        /// </summary>
        private void PreviewNextPhaseWarning(int nextPhase)
        {
            if (!HasStateAuthority) return;

            if (_gameStateManager == null)
            {
                Debug.LogWarning("[OxygenDepletionManager] GameStateManager가 null입니다!");
                return;
            }

            Debug.Log($"[OxygenDepletionManager] Phase {nextPhase} 경고 시작");

            var warningChunkInstances = CalculateChunksToDeactivateForPhase(nextPhase);

            if (warningChunkInstances.Count == 0)
            {
                Debug.Log($"[OxygenDepletionManager] Phase {nextPhase}에서 비활성화될 청크가 없습니다. 경고 스킵.");
                return;
            }

            Debug.Log($"[OxygenDepletionManager] 경고 대상 청크: {warningChunkInstances.Count}개 ChunkInstance");

            _warningChunks.Clear();

            foreach (var chunk in warningChunkInstances)
            {
                for (int dy = 0; dy < chunk.ChunkData.ChunkHeight; dy++)
                {
                    for (int dx = 0; dx < chunk.ChunkData.ChunkWidth; dx++)
                    {
                        Vector2Int cellPos = chunk.GridPosition + new Vector2Int(dx, dy);
                        _warningChunks.Add(cellPos);

                        if (_chunkToIndexMap.TryGetValue(cellPos, out int index))
                        {
                            WarningZoneMask.Set(index, true);

                            if (!_redLights.ContainsKey(cellPos))
                            {
                                SpawnRedLightsForChunk(cellPos);
                                Debug.Log($"[OxygenDepletionManager] 경고 Red Light 스폰: Chunk {cellPos}");
                            }
                        }
                    }
                }
            }

            RPC_SyncWarningZone();
            Debug.Log($"[OxygenDepletionManager] Phase {nextPhase} 경고 완료: {_warningChunks.Count}개 그리드 셀");
        }

        /// <summary>
        /// 특정 Phase에서 비활성화될 ChunkInstance 목록을 계산합니다.
        /// </summary>
        private HashSet<ChunkInstance> CalculateChunksToDeactivateForPhase(int phase)
        {
            var result = new HashSet<ChunkInstance>();

            // 1. 축소할 청크 개수 계산
            int targetCount = CalculateChunksToDeactivate(phase);
            if (targetCount == 0) return result;

            // 2. ChunkInstance 거리 계산
            var chunkInstanceDistances = CalculateChunkInstanceDistances();
            if (chunkInstanceDistances.Count == 0) return result;

            // 3. 거리별로 그룹화
            var layersByDistance = GetChunkInstancesByDistance(chunkInstanceDistances);
            int maxDistance = layersByDistance.Keys.Max();

            // 4. 가장 먼 레이어부터 방향별로 균등하게 선택
            int selectedCount = 0;

            for (int distance = maxDistance; distance >= 1 && selectedCount < targetCount; distance--)
            {
                if (!layersByDistance.TryGetValue(distance, out var chunksAtDistance))
                    continue;

                // 방향별로 그룹화
                var chunksByDirection = GroupChunksByDirection(chunksAtDistance);
                int remainingTarget = targetCount - selectedCount;

                // 라운드 로빈 방식으로 각 방향에서 균등하게 선택
                foreach (var directionChunks in chunksByDirection.Values)
                {
                    // Start Chunk에서 먼 순서로 정렬
                    directionChunks.Sort((a, b) =>
                    {
                        int distA = GetManhattanDistance(GetChunkCenter(a));
                        int distB = GetManhattanDistance(GetChunkCenter(b));
                        return distB.CompareTo(distA);
                    });
                }

                // 라운드 로빈 선택
                var activeDirections = chunksByDirection.Where(kvp => kvp.Value.Count > 0).ToList();
                if (activeDirections.Count == 0) break;

                int directionIndex = 0;
                int consecutiveFailures = 0;

                while (selectedCount < targetCount && consecutiveFailures < activeDirections.Count)
                {
                    var direction = activeDirections[directionIndex];
                    if (direction.Value.Count > 0)
                    {
                        var chunk = direction.Value[0];
                        direction.Value.RemoveAt(0);

                        // Start Chunk는 선택 안 함
                        if (_mapGenerator.PlacedChunks.TryGetValue(StartChunkPosition, out var startChunkInstance))
                        {
                            if (chunk != startChunkInstance)
                            {
                                result.Add(chunk);
                                selectedCount++;
                                consecutiveFailures = 0;
                            }
                        }
                    }
                    else
                    {
                        consecutiveFailures++;
                    }

                    directionIndex = (directionIndex + 1) % activeDirections.Count;
                }
            }

            return result;
        }

        /// <summary>
        /// 현재 Phase에 맞춰 맵을 축소합니다.
        /// Start Chunk로부터 거리 기반으로 바깥쪽부터 안쪽으로, 모든 방향 균등하게 축소합니다.
        /// </summary>
        private void ShrinkMap(int phase)
        {
            Debug.Log($"[OxygenDepletionManager] Phase {phase} 맵 축소 시작");

            // 1. 축소할 청크 개수 계산
            int targetCount = CalculateChunksToDeactivate(phase);

            if (targetCount == 0)
            {
                Debug.Log($"[OxygenDepletionManager] Phase {phase}의 축소 비율이 0%입니다. 맵 축소 스킵.");
                return;
            }

            // 마지막 Phase(100%)는 연결성 검증 없이 강제 제거
            bool isFinalPhase = _gameStateManager.GetShrinkRateForPhase(phase) >= 1.0f;

            // ChunkInstance 단위로 거리 계산
            var chunkInstanceDistances = CalculateChunkInstanceDistances();

            if (chunkInstanceDistances.Count == 0)
            {
                Debug.LogWarning($"[OxygenDepletionManager] Start Chunk에서 도달 가능한 청크가 없습니다!");
                return;
            }

            var layersByDistance = GetChunkInstancesByDistance(chunkInstanceDistances);
            int maxDistance = layersByDistance.Keys.Max();

            Debug.Log($"[OxygenDepletionManager] 최대 거리: {maxDistance}, 레이어 개수: {layersByDistance.Count}");

            int deactivatedCount = 0;

            for (int distance = maxDistance; distance >= 1 && deactivatedCount < targetCount; distance--)
            {
                if (!layersByDistance.TryGetValue(distance, out var chunksAtDistance))
                    continue;

                Debug.Log($"[OxygenDepletionManager] 거리 {distance} 레이어 처리 시작 ({chunksAtDistance.Count}개 ChunkInstance)");

                var chunksByDirection = GroupChunksByDirection(chunksAtDistance);
                int remainingTarget = targetCount - deactivatedCount;

                // 라운드 로빈 방식으로 각 방향에서 균등하게 제거
                int removedInThisLayer = DeactivateChunksRoundRobin(chunksByDirection, remainingTarget, distance, isFinalPhase);
                deactivatedCount += removedInThisLayer;
            }

            if (deactivatedCount > 0)
            {
                UpdateDepletedZoneMask();
                ClearWarningForDeactivatedChunks();

                RPC_SyncDepletedZone();
                int remainingChunkInstances = CalculateActiveChunkInstanceCount();
                Debug.Log($"[OxygenDepletionManager] Phase {phase} 맵 축소 완료. 비활성화: {deactivatedCount}개 ChunkInstance, 남은 ChunkInstance: {remainingChunkInstances}개 (그리드 칸: {_mapGenerator.ActiveChunkCount})");
            }
            else
            {
                Debug.LogWarning($"[OxygenDepletionManager] Phase {phase} 맵 축소 실패");
            }
        }


        /// <summary>
        /// 현재 Phase의 축소 비율에 맞춰 비활성화할 ChunkInstance 개수를 계산합니다.
        /// </summary>
        private int CalculateChunksToDeactivate(int phase)
        {
            int activeChunkInstanceCount = CalculateActiveChunkInstanceCount();
            int deactivatableCount = activeChunkInstanceCount - 1; // Start ChunkInstance 1개 제외

            if (deactivatableCount <= 0)
            {
                Debug.Log($"[OxygenDepletionManager] 비활성화 가능한 청크가 없습니다. (Start Chunk만 남음)");
                return 0;
            }

            float shrinkRate = _gameStateManager.GetShrinkRateForPhase(phase);
            int targetCount = Mathf.CeilToInt(deactivatableCount * shrinkRate);

            Debug.Log($"[OxygenDepletionManager] Phase {phase}: 활성 ChunkInstance={activeChunkInstanceCount}, 축소 비율={shrinkRate:P0}, 목표={targetCount}개");

            return targetCount;
        }

        /// <summary>
        /// 현재 활성화된 ChunkInstance 개수를 계산합니다 (중복 제거).
        /// </summary>
        private int CalculateActiveChunkInstanceCount()
        {
            var activeChunkInstances = new HashSet<ChunkInstance>();

            foreach (var kvp in _mapGenerator.PlacedChunks)
            {
                if (_mapGenerator.IsChunkActive(kvp.Key))
                {
                    activeChunkInstances.Add(kvp.Value);
                }
            }

            return activeChunkInstances.Count;
        }

        /// <summary>
        /// Start Chunk로부터 모든 ChunkInstance까지의 최단 거리를 BFS로 계산합니다.
        /// 멀티 칸 청크는 대표 위치(GridPosition) 기준으로 계산합니다.
        /// </summary>
        private Dictionary<ChunkInstance, int> CalculateChunkInstanceDistances()
        {
            var distances = new Dictionary<ChunkInstance, int>();
            var visited = new HashSet<ChunkInstance>();
            var queue = new Queue<(ChunkInstance chunk, int distance)>();

            // Start Chunk Instance 찾기
            if (!_mapGenerator.PlacedChunks.TryGetValue(StartChunkPosition, out var startChunk))
            {
                Debug.LogError($"[OxygenDepletionManager] Start Chunk({StartChunkPosition})를 PlacedChunks에서 찾을 수 없습니다!");
                return distances;
            }

            distances[startChunk] = 0;
            visited.Add(startChunk);
            queue.Enqueue((startChunk, 0));

            while (queue.Count > 0)
            {
                var (currentChunk, currentDistance) = queue.Dequeue();

                // 현재 청크가 차지하는 모든 그리드 위치의 이웃 확인
                for (int dy = 0; dy < currentChunk.ChunkData.ChunkHeight; dy++)
                {
                    for (int dx = 0; dx < currentChunk.ChunkData.ChunkWidth; dx++)
                    {
                        Vector2Int cellPos = currentChunk.GridPosition + new Vector2Int(dx, dy);

                        // 4방향 이웃 확인
                        foreach (var direction in DirectionExtensions.GetAllDirections())
                        {
                            Vector2Int neighborPos = cellPos + direction.ToOffset();

                            if (_mapGenerator.PlacedChunks.TryGetValue(neighborPos, out var neighborChunk))
                            {
                                // 활성 청크이면서 아직 방문 안한 ChunkInstance만
                                if (!visited.Contains(neighborChunk) && _mapGenerator.IsChunkActive(neighborPos))
                                {
                                    visited.Add(neighborChunk);
                                    distances[neighborChunk] = currentDistance + 1;
                                    queue.Enqueue((neighborChunk, currentDistance + 1));
                                }
                            }
                        }
                    }
                }
            }

            Debug.Log($"[OxygenDepletionManager] Start Chunk로부터 {distances.Count}개 ChunkInstance까지 거리 계산 완료");
            return distances;
        }

        /// <summary>
        /// 거리별로 ChunkInstance를 그룹화합니다.
        /// </summary>
        private Dictionary<int, List<ChunkInstance>> GetChunkInstancesByDistance(Dictionary<ChunkInstance, int> chunkDistances)
        {
            var layersByDistance = new Dictionary<int, List<ChunkInstance>>();

            foreach (var kvp in chunkDistances)
            {
                ChunkInstance chunk = kvp.Key;
                int distance = kvp.Value;

                if (!layersByDistance.ContainsKey(distance))
                {
                    layersByDistance[distance] = new List<ChunkInstance>();
                }

                layersByDistance[distance].Add(chunk);
            }

            return layersByDistance;
        }

        /// <summary>
        /// ChunkInstance를 Start Chunk 기준 방향별로 그룹화합니다.
        /// </summary>
        private Dictionary<string, List<ChunkInstance>> GroupChunksByDirection(List<ChunkInstance> chunks)
        {
            var grouped = new Dictionary<string, List<ChunkInstance>>
            {
                { "North", new List<ChunkInstance>() },
                { "East", new List<ChunkInstance>() },
                { "South", new List<ChunkInstance>() },
                { "West", new List<ChunkInstance>() }
            };

            foreach (var chunk in chunks)
            {
                // Start Chunk와의 상대 위치로 방향 결정
                Vector2Int center = GetChunkCenter(chunk);
                Vector2Int diff = center - StartChunkPosition;

                // 절대값이 큰 축의 방향으로 분류
                if (Mathf.Abs(diff.x) > Mathf.Abs(diff.y))
                {
                    // 가로 방향
                    grouped[diff.x > 0 ? "East" : "West"].Add(chunk);
                }
                else
                {
                    // 세로 방향
                    grouped[diff.y > 0 ? "North" : "South"].Add(chunk);
                }
            }

            return grouped;
        }

        /// <summary>
        /// 라운드 로빈 방식으로 각 방향에서 균등하게 ChunkInstance를 제거합니다.
        /// </summary>
        private int DeactivateChunksRoundRobin(Dictionary<string, List<ChunkInstance>> chunksByDirection, int targetCount, int distance, bool skipConnectivityCheck)
        {
            int deactivatedCount = 0;

            // 빈 방향 제거
            var activeDirections = chunksByDirection.Where(kvp => kvp.Value.Count > 0).ToList();

            if (activeDirections.Count == 0)
                return 0;

            // 각 방향별로 Start Chunk에서 먼 순서로 정렬
            foreach (var kvp in activeDirections)
            {
                kvp.Value.Sort((a, b) =>
                {
                    int distA = GetManhattanDistance(GetChunkCenter(a));
                    int distB = GetManhattanDistance(GetChunkCenter(b));
                    return distB.CompareTo(distA);
                });
            }

            // 라운드 로빈: 각 방향에서 번갈아가며 제거
            int directionIndex = 0;
            int consecutiveFailures = 0; // 연속 실패 횟수 추적

            while (deactivatedCount < targetCount)
            {
                bool removed = false;

                // 현재 방향에서 제거 시도
                var direction = activeDirections[directionIndex];
                if (direction.Value.Count > 0)
                {
                    var chunk = direction.Value[0];
                    direction.Value.RemoveAt(0);

                    if (DeactivateChunkInstance(chunk, skipConnectivityCheck))
                    {
                        deactivatedCount++;
                        removed = true;
                        consecutiveFailures = 0; // 성공 시 카운터 리셋
                        Debug.Log($"[OxygenDepletionManager] {direction.Key} 방향 청크 {chunk.GridPosition} (거리 {distance}) 비활성화 성공 ({deactivatedCount}/{targetCount})");
                    }
                    else
                    {
                        Debug.LogWarning($"[OxygenDepletionManager] {direction.Key} 방향 청크 {chunk.GridPosition} (거리 {distance}) 비활성화 실패 - 스킵됨");
                    }
                }

                // 제거 실패 시 카운터 증가
                if (!removed)
                {
                    consecutiveFailures++;
                }

                // 다음 방향으로 이동
                directionIndex = (directionIndex + 1) % activeDirections.Count;

                // 모든 방향을 한 바퀴 돌았는데 아무것도 제거 못했으면 종료
                if (consecutiveFailures >= activeDirections.Count)
                {
                    Debug.LogWarning($"[OxygenDepletionManager] 더 이상 제거할 청크가 없습니다. ({deactivatedCount}/{targetCount})");
                    break;
                }
            }

            return deactivatedCount;
        }

        /// <summary>
        /// ChunkInstance 전체를 비활성화합니다 (멀티 칸 청크의 모든 그리드 위치 포함).
        /// </summary>
        /// <param name="chunk">비활성화할 ChunkInstance</param>
        /// <param name="skipConnectivityCheck">연결성 검증 생략 여부 (최종 Phase용)</param>
        private bool DeactivateChunkInstance(ChunkInstance chunk, bool skipConnectivityCheck = false)
        {
            if (chunk == null)
                return false;

            // StartChunk ChunkInstance와 동일한지 직접 비교
            if (_mapGenerator.PlacedChunks.TryGetValue(StartChunkPosition, out var startChunkInstance))
            {
                if (chunk == startChunkInstance)
                {
                    return false;
                }
            }

            var gridPositions = GetGridPositions(chunk);

            // 최종 Phase는 연결성 검증 없이 강제 비활성화
            if (skipConnectivityCheck)
            {
                foreach (var pos in gridPositions)
                {
                    _mapGenerator.DeactivateChunk(pos);
                }
                return true;
            }

            // 연결성 검증 후 비활성화
            return _mapGenerator.DeactivateChunksAndValidateFromStart(gridPositions, StartChunkPosition);
        }

        /// <summary>
        /// 비활성화된 청크의 경고 상태를 제거합니다 (깜빡임 중지).
        /// </summary>
        private void ClearWarningForDeactivatedChunks()
        {
            if (!HasStateAuthority) return;

            var deactivatedChunks = new List<Vector2Int>();

            // 비활성화된 청크를 _warningChunks에서 제거
            foreach (var chunkPos in _warningChunks.ToList())
            {
                if (!_mapGenerator.IsChunkActive(chunkPos))
                {
                    deactivatedChunks.Add(chunkPos);

                    // WarningZoneMask 업데이트
                    if (_chunkToIndexMap.TryGetValue(chunkPos, out int index))
                    {
                        WarningZoneMask.Set(index, false);
                    }
                }
            }

            // _warningChunks에서 제거
            foreach (var chunkPos in deactivatedChunks)
            {
                _warningChunks.Remove(chunkPos);
            }

            if (deactivatedChunks.Count > 0)
            {
                Debug.Log($"[OxygenDepletionManager] 경고 상태 제거 (깜빡임 중지): {deactivatedChunks.Count}개 청크");
                RPC_SyncWarningZone();
            }
        }

        #endregion

        #region Network Synchronization

        /// <summary>
        /// 비활성화된 청크 정보를 NetworkArray에 업데이트합니다.
        /// </summary>
        private void UpdateDepletedZoneMask()
        {
            if (!HasStateAuthority) return;

            int depletedCount = 0;
            var depletedChunks = new List<Vector2Int>();

            // 모든 청크를 순회하면서 비활성 상태 확인
            foreach (var kvp in _chunkToIndexMap)
            {
                Vector2Int chunkPos = kvp.Key;
                int index = kvp.Value;

                // Why: MapGenerator.IsChunkActive()로 정확한 활성 상태 확인
                bool isDepleted = !_mapGenerator.IsChunkActive(chunkPos);

                DepletedZoneMask.Set(index, isDepleted);

                if (isDepleted)
                {
                    depletedCount++;
                    depletedChunks.Add(chunkPos);
                }
            }

            Debug.Log($"[OxygenDepletionManager] DepletedZoneMask 업데이트: {depletedCount}개 비활성 청크 - {string.Join(", ", depletedChunks)}");
        }

        /// <summary>
        /// 모든 클라이언트에 산소 고갈 구역 동기화
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_SyncDepletedZone(RpcInfo info = default)
        {
            Debug.Log($"[OxygenDepletionManager] 산소 고갈 구역 동기화 RPC 수신 (HasStateAuthority={HasStateAuthority})");

            // Why: 클라이언트는 서버의 매핑 정보 필요
            if (!HasStateAuthority && _chunkToIndexMap.Count == 0)
            {
                Debug.LogWarning("[OxygenDepletionManager] 클라이언트의 ChunkToIndexMap이 비어있습니다. 매핑 요청 중...");
                return;
            }

            // Why: 클라이언트는 Red Light 시각 효과만 동기화 (청크 비활성화는 서버만)
            UpdateRedLightVisuals();
        }

        /// <summary>
        /// 모든 클라이언트에 경고 구역 동기화
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_SyncWarningZone(RpcInfo info = default)
        {
            Debug.Log($"[OxygenDepletionManager] 경고 구역 동기화 RPC 수신 (HasStateAuthority={HasStateAuthority})");

            // Why: 클라이언트는 WarningZoneMask를 기반으로 _warningChunks 재구성
            if (!HasStateAuthority)
            {
                _warningChunks.Clear();

                foreach (var kvp in _chunkToIndexMap)
                {
                    Vector2Int chunkPos = kvp.Key;
                    int index = kvp.Value;

                    if (index >= 0 && index < MAX_CHUNK_CAPACITY && WarningZoneMask[index])
                    {
                        _warningChunks.Add(chunkPos);

                        // Why: 클라이언트도 경고 Red Light 스폰 (깜빡임 효과용)
                        if (!_redLights.ContainsKey(chunkPos))
                        {
                            SpawnRedLightsForChunk(chunkPos);
                            Debug.Log($"[OxygenDepletionManager] 클라이언트 경고 Red Light 스폰: Chunk {chunkPos}");
                        }
                    }
                    else if (!WarningZoneMask[index])
                    {
                        // Why: 경고 상태가 해제된 청크는 _warningChunks에서 제거 (깜빡임 중지)
                        _warningChunks.Remove(chunkPos);
                    }
                }

                Debug.Log($"[OxygenDepletionManager] 클라이언트 경고 상태 업데이트: {_warningChunks.Count}개 그리드 셀");
            }
        }

        #endregion

        #region Damage System

        /// <summary>
        /// 산소 고갈 구역에 있는 플레이어에게 데미지를 적용합니다.
        /// </summary>
        private void ApplyDamageToPlayersInDepletedZone()
        {
            if (!HasStateAuthority) return;

            if (!_damageTimer.ExpiredOrNotRunning(Runner))
                return;

            foreach (var player in Runner.ActivePlayers)
            {
                // Why: Server 모드에서는 서버 자신을 제외
                if (Runner.GameMode == GameMode.Server && player == Runner.LocalPlayer)
                {
                    continue;
                }

                var netObj = Runner.GetPlayerObject(player);
                if (netObj == null) continue;

                Vector3 playerPos = netObj.transform.position;
                Vector2Int chunkPos = WorldToChunkPosition(playerPos);

                if (IsChunkDepleted(chunkPos))
                {
                    var combat = netObj.GetComponent<PlayerCombat>();
                    if (combat != null && combat.IsAlive)
                    {
                        combat.TakeDamage(_damagePerSecond, PlayerRef.None);
                        Debug.Log($"[OxygenDepletionManager] Player{player.PlayerId}가 산소 고갈 구역에서 {_damagePerSecond} 데미지 받음");
                    }
                }
            }

            // 다음 데미지 타이머 시작
            _damageTimer = TickTimer.CreateFromSeconds(Runner, DAMAGE_INTERVAL);
        }

        /// <summary>
        /// 월드 좌표를 청크 그리드 좌표로 변환합니다.
        /// </summary>
        private Vector2Int WorldToChunkPosition(Vector3 worldPos)
        {
            if (_mapSettings == null)
            {
                Debug.LogError("[OxygenDepletionManager] MapSettings가 null입니다!");
                return Vector2Int.zero;
            }

            int chunkSize = _mapSettings.ChunkSize;

            int x = Mathf.FloorToInt(worldPos.x / chunkSize);
            int z = Mathf.FloorToInt(worldPos.z / chunkSize);

            return new Vector2Int(x, z);
        }

        /// <summary>
        /// 청크가 산소 고갈 구역인지 확인합니다.
        /// </summary>
        private bool IsChunkDepleted(Vector2Int chunkPos)
        {
            if (!_chunkToIndexMap.TryGetValue(chunkPos, out int index))
            {
                // 매핑되지 않은 청크 = 맵 밖 = 고갈된 것으로 간주
                return true;
            }

            if (index < 0 || index >= MAX_CHUNK_CAPACITY)
            {
                Debug.LogWarning($"[OxygenDepletionManager] 유효하지 않은 청크 인덱스: {index}");
                return true;
            }

            return DepletedZoneMask[index];
        }

        #endregion

        #region Visual Effects

        /// <summary>
        /// 경고 중인 Red Light의 깜빡임 효과를 업데이트합니다.
        /// </summary>
        private void UpdateWarningLightBlink()
        {
            if (_redLightPrefab == null || _warningChunks.Count == 0)
                return;

            float blinkValue = Mathf.PingPong(Time.time * _blinkSpeed, 1f);
            bool isLightOn = blinkValue > 0.5f;

            // 경고 중인 청크의 Red Light만 깜빡임
            foreach (var chunkPos in _warningChunks)
            {
                if (_redLights.TryGetValue(chunkPos, out var lights))
                {
                    foreach (var lightObj in lights)
                    {
                        if (lightObj != null)
                        {
                            var lightComponent = lightObj.GetComponent<Light>();
                            if (lightComponent != null)
                            {
                                lightComponent.enabled = isLightOn;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 산소 고갈 구역의 붉은 조명 시각 효과를 업데이트합니다.
        /// </summary>
        private void UpdateRedLightVisuals()
        {
            if (_redLightPrefab == null)
            {
                // 붉은 조명 프리팹이 없으면 시각 효과 스킵
                return;
            }

            int depletedCount = 0;

            // 고갈된 청크에 붉은 조명 배치 (기존 조명이 없는 경우만)
            foreach (var kvp in _chunkToIndexMap)
            {
                Vector2Int chunkPos = kvp.Key;
                int index = kvp.Value;

                if (index >= 0 && index < MAX_CHUNK_CAPACITY && DepletedZoneMask[index])
                {
                    // 이미 경고 단계에서 조명이 스폰되었으면 스킵 (깜빡임만 중지)
                    if (!_redLights.ContainsKey(chunkPos))
                    {
                        SpawnRedLightsForChunk(chunkPos);
                    }

                    depletedCount++;

                    // 고갈된 청크의 조명은 항상 켜짐 (깜빡임 없음)
                    EnsureLightsEnabled(chunkPos);
                }
            }

            // 더 이상 고갈되지 않은 청크의 조명 제거 (재활성화된 경우)
            var chunksToRemove = new List<Vector2Int>();
            foreach (var kvp in _redLights)
            {
                Vector2Int chunkPos = kvp.Key;

                // 경고 중인 청크는 제거하지 않음
                if (_warningChunks.Contains(chunkPos))
                    continue;

                // 고갈되지 않은 청크의 조명 제거
                if (_chunkToIndexMap.TryGetValue(chunkPos, out int index))
                {
                    if (index >= 0 && index < MAX_CHUNK_CAPACITY && !DepletedZoneMask[index])
                    {
                        chunksToRemove.Add(chunkPos);
                    }
                }
            }

            // 제거 대상 조명 삭제
            foreach (var chunkPos in chunksToRemove)
            {
                if (_redLights.TryGetValue(chunkPos, out var lights))
                {
                    foreach (var lightObj in lights)
                    {
                        if (lightObj != null)
                        {
                            Destroy(lightObj);
                        }
                    }
                    _redLights.Remove(chunkPos);
                }
            }
        }

        /// <summary>
        /// 특정 청크의 모든 Red Light를 항상 켜진 상태로 설정합니다.
        /// </summary>
        private void EnsureLightsEnabled(Vector2Int chunkPos)
        {
            if (_redLights.TryGetValue(chunkPos, out var lights))
            {
                foreach (var lightObj in lights)
                {
                    if (lightObj != null)
                    {
                        var lightComponent = lightObj.GetComponent<Light>();
                        if (lightComponent != null)
                        {
                            lightComponent.enabled = true;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 특정 청크에 붉은 조명을 배치합니다.
        /// 각 그리드 칸의 중앙에 하나씩 배치 (1x1 청크 = 1개, 2x2 청크 = 4개)
        /// </summary>
        private void SpawnRedLightsForChunk(Vector2Int chunkPos)
        {
            if (_mapSettings == null || _redLightPrefab == null) return;

            // ChunkInstance에서 실제 청크 크기 확인
            if (!_mapGenerator.PlacedChunks.TryGetValue(chunkPos, out var chunkInstance))
            {
                Debug.LogWarning($"[OxygenDepletionManager] 청크 {chunkPos}를 찾을 수 없습니다.");
                return;
            }

            int chunkSize = _mapSettings.ChunkSize;
            int chunkWidth = chunkInstance.ChunkData.ChunkWidth;
            int chunkHeight = chunkInstance.ChunkData.ChunkHeight;

            // 청크의 왼쪽 아래 월드 좌표 (피벗)
            Vector3 chunkWorldPos = new Vector3(chunkPos.x * chunkSize, 0, chunkPos.y * chunkSize);

            var lights = new List<GameObject>();

            // 각 그리드 칸의 중앙에 조명 배치
            for (int gridY = 0; gridY < chunkHeight; gridY++)
            {
                for (int gridX = 0; gridX < chunkWidth; gridX++)
                {
                    // 각 칸의 중앙 위치 계산
                    float centerX = (gridX + CHUNK_CENTER_OFFSET) * chunkSize;
                    float centerZ = (gridY + CHUNK_CENTER_OFFSET) * chunkSize;

                    Vector3 lightPos = chunkWorldPos + new Vector3(centerX, RED_LIGHT_HEIGHT, centerZ);
                    GameObject lightObj = Instantiate(_redLightPrefab, lightPos, Quaternion.identity, transform);
                    lightObj.name = $"RedLight_Chunk{chunkPos}_Grid{gridX}x{gridY}";
                    lights.Add(lightObj);
                }
            }

            _redLights[chunkPos] = lights;
        }

        /// <summary>
        /// 모든 붉은 조명을 제거합니다.
        /// </summary>
        private void ClearAllRedLights()
        {
            foreach (var kvp in _redLights)
            {
                foreach (var light in kvp.Value)
                {
                    if (light != null)
                    {
                        Destroy(light);
                    }
                }
            }

            _redLights.Clear();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// ChunkInstance의 중심 좌표를 반환합니다.
        /// </summary>
        private Vector2Int GetChunkCenter(ChunkInstance chunk)
        {
            return chunk.GridPosition + new Vector2Int(chunk.ChunkData.ChunkWidth / 2, chunk.ChunkData.ChunkHeight / 2);
        }

        /// <summary>
        /// Start Chunk로부터의 Manhattan 거리를 계산합니다.
        /// </summary>
        private int GetManhattanDistance(Vector2Int position)
        {
            return Mathf.Abs(position.x - StartChunkPosition.x) + Mathf.Abs(position.y - StartChunkPosition.y);
        }

        /// <summary>
        /// ChunkInstance가 차지하는 모든 그리드 위치를 수집합니다.
        /// </summary>
        private HashSet<Vector2Int> GetGridPositions(ChunkInstance chunk)
        {
            var positions = new HashSet<Vector2Int>();
            for (int dy = 0; dy < chunk.ChunkData.ChunkHeight; dy++)
            {
                for (int dx = 0; dx < chunk.ChunkData.ChunkWidth; dx++)
                {
                    positions.Add(chunk.GridPosition + new Vector2Int(dx, dy));
                }
            }
            return positions;
        }

        #endregion

        #region Unity Lifecycle

        private void OnDestroy()
        {
            ClearAllRedLights();
        }

        #endregion
    }
}
