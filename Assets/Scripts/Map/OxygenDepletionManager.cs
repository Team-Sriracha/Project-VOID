using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 산소 고갈 구역(Oxygen Depletion Zone) 시스템 관리
    /// 페이즈마다 맵 가장자리부터 자동으로 축소하며, 최종적으로 시작 청크만 남김
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

        #region SyncVars

        public readonly SyncVar<int> LastProcessedPhase = new();
        public readonly SyncVar<Vector2Int> StartChunkPosition = new();

        private readonly SyncList<bool> _depletedZoneMask = new SyncList<bool>();
        private readonly SyncList<bool> _warningZoneMask = new SyncList<bool>();

        #endregion

        #region Private Fields

        private GameStateManager _gameStateManager;
        private MapGenerator _mapGenerator;
        private MapGenerationSettings _mapSettings;
        private float _lastDamageTime;
        private Dictionary<Vector2Int, List<GameObject>> _redLights;
        private Dictionary<Vector2Int, int> _chunkToIndexMap;
        private HashSet<Vector2Int> _warningChunks;
        private int _lastWarningPhase;
        private Dictionary<int, Dictionary<ChunkInstance, int>> _cachedDistancesByPhase;
        
        private Vector2Int[] _pendingChunkPositions;
        private int _pendingChunkCount;

        #endregion

        #region Initialization

        /// <summary>
        /// OxygenDepletionManager 초기화
        /// </summary>
        public void Initialize(MapGenerator mapGenerator, GameStateManager gameStateManager, MapGenerationSettings mapSettings)
        {
            _mapGenerator = mapGenerator;
            _gameStateManager = gameStateManager;
            _mapSettings = mapSettings;
            _redLights = new Dictionary<Vector2Int, List<GameObject>>();
            _chunkToIndexMap = new Dictionary<Vector2Int, int>();
            _cachedDistancesByPhase = new Dictionary<int, Dictionary<ChunkInstance, int>>();

            if (IsServerInitialized)
            {
                StartChunkPosition.Value = FindStartChunk();
                Debug.Log($"[OxygenDepletionManager] Start Chunk: {StartChunkPosition.Value}");

                ResetAllChunksToActive();

                // 청크 위치 → 인덱스 매핑 초기화
                InitializeChunkIndexMap();
            }
        }

        /// <summary>
        /// 청크 위치를 NetworkArray 인덱스로 매핑
        /// </summary>
        public void InitializeChunkIndexMap()
        {
            if (_chunkToIndexMap == null)
            {
                _chunkToIndexMap = new Dictionary<Vector2Int, int>();
            }
            ClearDistanceCache();

            _chunkToIndexMap.Clear();

            if (_mapGenerator == null || _mapGenerator.PlacedChunks == null)
            {
                Debug.LogWarning("[OxygenDepletionManager] MapGenerator 또는 PlacedChunks가 null입니다!");
                return;
            }

            // 그룹 청크 중복 제거 (ToNetworkArray()와 동일한 로직)
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
            if (IsServerInitialized)
            {
                _depletedZoneMask.Clear();
                _warningZoneMask.Clear();
                for (int i = 0; i < MAX_CHUNK_CAPACITY; i++)
                {
                    _depletedZoneMask.Add(false);
                    _warningZoneMask.Add(false);
                }
            }

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

            if (IsServerInitialized)
            {
                RPC_SyncChunkMapping(uniqueChunkPositions.ToArray(), uniqueChunkPositions.Count);
            }
        }

        [ObserversRpc]
        private void RPC_SyncChunkMapping(Vector2Int[] chunkPositions, int count)
        {
            if (IsServerInitialized) return;

            Debug.Log($"[OxygenDepletionManager] 서버로부터 청크 매핑 수신: {count}개");

            if (_mapGenerator == null || _mapGenerator.PlacedChunks == null)
            {
                Debug.LogWarning("[OxygenDepletionManager] MapGenerator가 준비되지 않아 청크 매핑을 보류합니다.");
                _pendingChunkPositions = chunkPositions;
                _pendingChunkCount = count;
                return;
            }

            ApplyChunkMapping(chunkPositions, count);
        }

        private void ApplyChunkMapping(Vector2Int[] chunkPositions, int count)
        {
            if (_chunkToIndexMap == null)
            {
                _chunkToIndexMap = new Dictionary<Vector2Int, int>();
            }

            ClearDistanceCache();
            _chunkToIndexMap.Clear();

            var processedChunks = new HashSet<ChunkInstance>();

            for (int i = 0; i < count && i < chunkPositions.Length; i++)
            {
                Vector2Int representativePos = chunkPositions[i];

                if (!_mapGenerator.PlacedChunks.TryGetValue(representativePos, out ChunkInstance chunk))
                {
                    Debug.LogWarning($"[OxygenDepletionManager] 청크 {representativePos}를 찾을 수 없습니다.");
                    continue;
                }

                if (!processedChunks.Add(chunk))
                {
                    continue;
                }

                int width = chunk.ChunkData.ChunkWidth;
                int height = chunk.ChunkData.ChunkHeight;

                for (int dy = 0; dy < height; dy++)
                {
                    for (int dx = 0; dx < width; dx++)
                    {
                        Vector2Int cellPos = representativePos + new Vector2Int(dx, dy);
                        _chunkToIndexMap[cellPos] = i;
                    }
                }
            }

            Debug.Log($"[OxygenDepletionManager] 클라이언트 매핑 적용 완료: {_chunkToIndexMap.Count}개");
        }

        /// <summary>
        /// 게임 시작 시 모든 청크를 활성화 상태로 리셋
        /// </summary>
        private void ResetAllChunksToActive()
        {
            if (_mapGenerator == null || _mapGenerator.PlacedChunks == null)
            {
                Debug.LogWarning("[OxygenDepletionManager] MapGenerator 또는 PlacedChunks가 null입니다!");
                return;
            }
            ClearDistanceCache();

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
                _depletedZoneMask.Set(i, false);
            }

            Debug.Log($"[OxygenDepletionManager] 모든 청크 활성화 완료: {activatedCount}개 재활성화, 총 {_mapGenerator.ActiveChunkCount}개 활성");
        }

        /// <summary>
        /// 시작 청크 찾기 (Central 타입 또는 (0,0)에 가장 가까운 청크)
        /// </summary>
        private Vector2Int FindStartChunk()
        {
            // 1. Central 타입 청크 우선 검색
            foreach (var kvp in _mapGenerator.PlacedChunks)
            {
                if (kvp.Value.ChunkData.ChunkType == ChunkType.Central)
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
        #region Fishnet Lifecycle

        public override void OnStartServer()
        {
            base.OnStartServer();
            
            if (_redLights == null)
                _redLights = new Dictionary<Vector2Int, List<GameObject>>();
            if (_chunkToIndexMap == null)
                _chunkToIndexMap = new Dictionary<Vector2Int, int>();
            if (_warningChunks == null)
                _warningChunks = new HashSet<Vector2Int>();
            if (_cachedDistancesByPhase == null)
                _cachedDistancesByPhase = new Dictionary<int, Dictionary<ChunkInstance, int>>();

            // SyncList 초기화 - 빈 리스트에 Set을 호출하면 IndexOutOfRange 발생하므로 미리 초기화
            if (_depletedZoneMask.Count == 0)
            {
                for (int i = 0; i < MAX_CHUNK_CAPACITY; i++)
                {
                    _depletedZoneMask.Add(false);
                }
            }
            if (_warningZoneMask.Count == 0)
            {
                for (int i = 0; i < MAX_CHUNK_CAPACITY; i++)
                {
                    _warningZoneMask.Add(false);
                }
            }

            Debug.Log($"[OxygenDepletionManager] OnStartServer - SyncList initialized with {MAX_CHUNK_CAPACITY} elements");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            
            if (_redLights == null)
                _redLights = new Dictionary<Vector2Int, List<GameObject>>();
            if (_chunkToIndexMap == null)
                _chunkToIndexMap = new Dictionary<Vector2Int, int>();
            if (_warningChunks == null)
                _warningChunks = new HashSet<Vector2Int>();
            if (_cachedDistancesByPhase == null)
                _cachedDistancesByPhase = new Dictionary<int, Dictionary<ChunkInstance, int>>();

            // 클라이언트에서 _warningZoneMask SyncList 변경 감지하여 자동 업데이트
            _warningZoneMask.OnChange += OnWarningZoneMaskChanged;

            // Late Joiner를 위한 초기 상태 동기화
            if (!IsServerInitialized)
            {
                InitializeClientWarnings();
            }

            Debug.Log($"[OxygenDepletionManager] OnStartClient");
        }
        
        /// <summary>
        /// SyncList 변경 시 클라이언트에서 경고 상태 자동 업데이트
        /// </summary>
        private void OnWarningZoneMaskChanged(SyncListOperation op, int index, bool oldValue, bool newValue, bool asServer)
        {
            // 서버는 직접 관리하므로 스킵
            if (asServer) return;
            
            // chunkToIndexMap이 아직 초기화되지 않았으면 스킵
            if (_chunkToIndexMap == null || _chunkToIndexMap.Count == 0) return;
            
            // Debug.Log($"[OxygenDepletionManager] 클라이언트 SyncList 변경 감지: index={index}, newValue={newValue}");
            
            // 해당 인덱스에 해당하는 청크 위치 찾기
            foreach (var kvp in _chunkToIndexMap)
            {
                if (kvp.Value == index)
                {
                    Vector2Int chunkPos = kvp.Key;
                    
                    if (newValue)
                    {
                        _warningChunks.Add(chunkPos);
                        
                        if (!_redLights.ContainsKey(chunkPos))
                        {
                            SpawnRedLightsForChunk(chunkPos);
                        }
                    }
                    else
                    {
                        _warningChunks.Remove(chunkPos);
                    }
                }
            }
        }
        
        public override void OnStopClient()
        {
            base.OnStopClient();
            
            // 콜백 해제
            _warningZoneMask.OnChange -= OnWarningZoneMaskChanged;
        }

        public void InitializeForClient(MapGenerator mapGenerator, MapGenerationSettings mapSettings)
        {
            _mapGenerator = mapGenerator;
            _mapSettings = mapSettings;

            if (_pendingChunkPositions != null)
            {
                Debug.Log("[OxygenDepletionManager] 보류된 청크 매핑 정보를 적용합니다.");
                ApplyChunkMapping(_pendingChunkPositions, _pendingChunkCount);
                _pendingChunkPositions = null;
                _pendingChunkCount = 0;
                
                // 매핑 적용 후 초기 경고 상태 확인
                InitializeClientWarnings();
            }
            else
            {
                InitializeChunkIndexMap();
            }

            Debug.Log($"[OxygenDepletionManager] 클라이언트 초기화 완료");
        }


        private void Update()
        {
            if (!IsServerInitialized) return;
            if (_gameStateManager == null || _mapGenerator == null) return;

            int currentPhase = _gameStateManager.CurrentPhase.Value;
            bool phaseAdvanced = false;

            if (currentPhase > LastProcessedPhase.Value && currentPhase >= 2)
            {
                ShrinkMap(currentPhase);
                LastProcessedPhase.Value = currentPhase;
                phaseAdvanced = true;

                // 경고 조명을 켜진 상태로 고정한 후 경고 상태 해제
                FixWarningLightsOn();

                _warningChunks.Clear();
                for (int i = 0; i < _warningZoneMask.Count && i < MAX_CHUNK_CAPACITY; i++)
                {
                    _warningZoneMask[i] = false;
                }
                _lastWarningPhase = 0;
                ClearDistanceCache();
            }

            float timeToNext = _gameStateManager.TimeToNextPhase;
            int nextPhase = currentPhase + 1;

            if (!phaseAdvanced && timeToNext > 0f && timeToNext <= _warningDuration && _lastWarningPhase != nextPhase)
            {
                PreviewNextPhaseWarning(nextPhase);
                _lastWarningPhase = nextPhase;
                Debug.Log($"[OxygenDepletionManager] Phase {nextPhase} 경고 시작 (남은 시간: {timeToNext:F1}초)");
            }

            ApplyDamageToPlayersInDepletedZone();
        }

        private void LateUpdate()
        {
            UpdateWarningLightBlink();
        }

        #endregion

        #region Map Shrinking

        /// <summary>
        /// 다음 Phase에서 비활성화될 청크들에 경고 표시
        /// </summary>
        private void PreviewNextPhaseWarning(int nextPhase)
        {
            if (!IsServerInitialized) return;

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
                            _warningZoneMask.Set(index, true);

                            if (!_redLights.ContainsKey(cellPos))
                            {
                                SpawnRedLightsForChunk(cellPos);
                                Debug.Log($"[OxygenDepletionManager] 경고 Red Light 스폰: Chunk {cellPos}");
                            }
                        }
                    }
                }
            }



            Debug.Log($"[OxygenDepletionManager] Phase {nextPhase} 경고 완료: {_warningChunks.Count}개 그리드 셀");
        }

        /// <summary>
        /// 특정 Phase에서 비활성화될 ChunkInstance 목록 계산
        /// </summary>
        private HashSet<ChunkInstance> CalculateChunksToDeactivateForPhase(int phase)
        {
            var result = new HashSet<ChunkInstance>();

            // 1. 축소할 청크 개수 계산
            int targetCount = CalculateChunksToDeactivate(phase);
            if (targetCount == 0) return result;

            // 2. ChunkInstance 거리 계산
            var chunkInstanceDistances = GetOrCalculateDistances(phase);
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
                        if (_mapGenerator.PlacedChunks.TryGetValue(StartChunkPosition.Value, out var startChunkInstance))
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
        /// 현재 Phase에 맞춰 맵 축소
        /// Start Chunk로부터 거리 기반으로 바깥쪽부터 안쪽으로, 모든 방향 균등하게 축소
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
            var chunkInstanceDistances = GetOrCalculateDistances(phase);

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
        /// 현재 Phase의 축소 비율에 맞춰 비활성화할 ChunkInstance 개수 계산
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
        /// 현재 활성화된 ChunkInstance 개수 계산 (중복 제거)
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
        /// Start Chunk로부터 모든 ChunkInstance까지의 최단 거리를 BFS로 계산
        /// 멀티 칸 청크는 대표 위치(GridPosition) 기준으로 계산
        /// </summary>
        private Dictionary<ChunkInstance, int> CalculateChunkInstanceDistances()
        {
            var distances = new Dictionary<ChunkInstance, int>();
            var visited = new HashSet<ChunkInstance>();
            var queue = new Queue<(ChunkInstance chunk, int distance)>();

            // Start Chunk Instance 찾기
            if (!_mapGenerator.PlacedChunks.TryGetValue(StartChunkPosition.Value, out var startChunk))
            {
                Debug.LogError($"[OxygenDepletionManager] Start Chunk({StartChunkPosition.Value})를 PlacedChunks에서 찾을 수 없습니다!");
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
        /// Phase별 BFS 결과를 캐싱하여 반복 계산 최소화
        /// </summary>
        private Dictionary<ChunkInstance, int> GetOrCalculateDistances(int phase)
        {
            if (_cachedDistancesByPhase == null)
            {
                _cachedDistancesByPhase = new Dictionary<int, Dictionary<ChunkInstance, int>>();
            }

            if (_cachedDistancesByPhase.TryGetValue(phase, out var cached))
            {
                return cached;
            }

            var distances = CalculateChunkInstanceDistances();
            _cachedDistancesByPhase[phase] = distances;
            return distances;
        }

        /// <summary>
        /// BFS 캐시를 초기화합니다 (맵 상태 변경 시 호출).
        /// </summary>
        private void ClearDistanceCache()
        {
            _cachedDistancesByPhase?.Clear();
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
                Vector2Int diff = center - StartChunkPosition.Value;

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
            if (_mapGenerator.PlacedChunks.TryGetValue(StartChunkPosition.Value, out var startChunkInstance))
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
            return _mapGenerator.DeactivateChunksAndValidateFromStart(gridPositions, StartChunkPosition.Value);
        }

        /// <summary>
        /// 비활성화된 청크의 경고 상태를 제거합니다 (깜빡임 중지).
        /// </summary>
        private void ClearWarningForDeactivatedChunks()
        {
            if (!IsServerInitialized) return;

            var deactivatedChunks = new List<Vector2Int>();

            // 비활성화된 청크를 _warningChunks에서 제거
            foreach (var chunkPos in _warningChunks.ToList())
            {
                if (!_mapGenerator.IsChunkActive(chunkPos))
                {
                    deactivatedChunks.Add(chunkPos);

                    // _warningZoneMask 업데이트
                    if (_chunkToIndexMap.TryGetValue(chunkPos, out int index))
                    {
                        _warningZoneMask.Set(index, false);
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
            }
        }

        #endregion

        #region Network Synchronization

        /// <summary>
        /// 비활성화된 청크 정보를 NetworkArray에 업데이트합니다.
        /// </summary>
        private void UpdateDepletedZoneMask()
        {
            if (!IsServerInitialized) return;

            int depletedCount = 0;
            var depletedChunks = new List<Vector2Int>();

            // 모든 청크를 순회하면서 비활성 상태 확인
            foreach (var kvp in _chunkToIndexMap)
            {
                Vector2Int chunkPos = kvp.Key;
                int index = kvp.Value;

                // MapGenerator.IsChunkActive()로 정확한 활성 상태 확인
                bool isDepleted = !_mapGenerator.IsChunkActive(chunkPos);

                _depletedZoneMask.Set(index, isDepleted);

                if (isDepleted)
                {
                    depletedCount++;
                    depletedChunks.Add(chunkPos);
                }
            }

            Debug.Log($"[OxygenDepletionManager] _depletedZoneMask 업데이트: {depletedCount}개 비활성 청크 - {string.Join(", ", depletedChunks)}");
        }

        /// <summary>
        /// 모든 클라이언트에 산소 고갈 구역 동기화
        /// </summary>
        [ObserversRpc]
        private void RPC_SyncDepletedZone()
        {
            Debug.Log($"[OxygenDepletionManager] 산소 고갈 구역 동기화 RPC 수신 (IsServerInitialized={IsServerInitialized})");

            // 클라이언트는 서버의 매핑 정보 필요
            if (!IsServerInitialized && _chunkToIndexMap.Count == 0)
            {
                Debug.LogWarning("[OxygenDepletionManager] 클라이언트의 ChunkToIndexMap이 비어있습니다. 매핑 요청 중...");
                return;
            }

            // 클라이언트는 Red Light 시각 효과만 동기화 (청크 비활성화는 서버만)
            UpdateRedLightVisuals();
        }



        #endregion

        #region Damage System

        /// <summary>
        /// 산소 고갈 구역에 있는 플레이어에게 데미지를 적용합니다.
        /// </summary>
        private void ApplyDamageToPlayersInDepletedZone()
        {
            if (!IsServerInitialized) return;

            if (Time.time < _lastDamageTime + DAMAGE_INTERVAL)
                return;

            _lastDamageTime = Time.time;

            var playerCombats = FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None);
            
            foreach (var combat in playerCombats)
            {
                if (combat == null) continue;
                
                FishNet.Object.NetworkObject netObj = combat.GetComponent<FishNet.Object.NetworkObject>();
                if (netObj == null || !netObj.IsSpawned) continue;

                Vector3 playerPos = combat.transform.position;
                Vector2Int chunkPos = WorldToChunkPosition(playerPos);

                if (IsChunkDepleted(chunkPos))
                {
                    if (combat.IsAlive)
                    {
                        combat.TakeDamage(_damagePerSecond, null);
                        Debug.Log($"[OxygenDepletionManager] 플레이어가 산소 고갈 구역에서 {_damagePerSecond} 데미지 받음");
                    }
                }
            }
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

            return _depletedZoneMask[index];
        }

        #endregion

        #region Visual Effects

        /// <summary>
        /// 경고 중인 Red Light의 깜빡임 효과를 업데이트합니다.
        /// </summary>
        private void UpdateWarningLightBlink()
        {
            if (_redLightPrefab == null || _warningChunks == null || _warningChunks.Count == 0)
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
        /// 경고 조명을 켜진 상태로 고정합니다 (Phase 진행 시 호출).
        /// </summary>
        private void FixWarningLightsOn()
        {
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
                                lightComponent.enabled = true;
                            }
                        }
                    }
                }
            }

            // 클라이언트에도 동기화
            if (IsServerInitialized)
            {
                RPC_FixWarningLightsOn();
            }
        }

        [ObserversRpc]
        private void RPC_FixWarningLightsOn()
        {
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
                                lightComponent.enabled = true;
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

                if (index >= 0 && index < MAX_CHUNK_CAPACITY && _depletedZoneMask[index])
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
                    if (index >= 0 && index < MAX_CHUNK_CAPACITY && !_depletedZoneMask[index])
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
            // OnDestroy는 객체가 같은 틱에서 Spawn/Despawn될 때도 호출될 수 있음
            // 이 경우 _redLights가 아직 초기화되지 않았을 수 있음
            if (_redLights == null)
                return;

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
            return Mathf.Abs(position.x - StartChunkPosition.Value.x) + Mathf.Abs(position.y - StartChunkPosition.Value.y);
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
        
        /// <summary>
        /// 클라이언트 진입 시 기존 경고 상태를 시각화합니다.
        /// </summary>
        private void InitializeClientWarnings()
        {
            if (_chunkToIndexMap == null || _chunkToIndexMap.Count == 0) return;

            // _warningZoneMask를 순회하며 true인 항목에 대해 시각 효과 적용
            for (int i = 0; i < _warningZoneMask.Count; i++)
            {
                if (_warningZoneMask[i])
                {
                    // 해당 인덱스의 청크 위치 찾기
                    foreach (var kvp in _chunkToIndexMap)
                    {
                        if (kvp.Value == i)
                        {
                            Vector2Int chunkPos = kvp.Key;
                            if (!_warningChunks.Contains(chunkPos))
                            {
                                _warningChunks.Add(chunkPos);
                                if (!_redLights.ContainsKey(chunkPos))
                                {
                                    SpawnRedLightsForChunk(chunkPos);
                                }
                            }
                        }
                    }
                }
            }
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
