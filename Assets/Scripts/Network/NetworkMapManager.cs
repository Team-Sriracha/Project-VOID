using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using ProjectVoid.Map;
using System.Collections.Generic;
using System;

namespace ProjectVoid.Network
{
    public class NetworkMapManager : NetworkBehaviour
    {
        private const int MAX_CHUNK_CAPACITY = 500;
        private const int MAX_WALL_SEGMENT_CAPACITY = 800;
        private const float DEFAULT_SPAWN_HEIGHT = 0.6f;
        private const float NAVMESH_SPAWN_HEIGHT_OFFSET = 0.1f;
        private const float SPAWN_CHUNK_PADDING = 2f;
        private const float SPAWN_NAVMESH_SAMPLE_DISTANCE = 4f;

        private static readonly Vector2[] SPAWN_CHUNK_SEARCH_PATTERN =
        {
            Vector2.zero,
            new Vector2(0f, 0.35f),
            new Vector2(0.35f, 0f),
            new Vector2(0f, -0.35f),
            new Vector2(-0.35f, 0f),
            new Vector2(0.25f, 0.25f),
            new Vector2(0.25f, -0.25f),
            new Vector2(-0.25f, 0.25f),
            new Vector2(-0.25f, -0.25f),
            new Vector2(0.6f, 0f),
            new Vector2(0f, 0.6f),
            new Vector2(-0.6f, 0f),
            new Vector2(0f, -0.6f)
        };

        [Header("맵 생성 설정")]
        [SerializeField] private MapGenerationSettings _mapSettings;

        [Header("LootBox 관리")]
        [Tooltip("스폰할 LootBoxSpawnManager 프리팹 (NetworkObject 필요)")]
        [SerializeField] private NetworkObject _lootBoxSpawnManagerPrefab;

        [Header("산소 고갈 구역 관리")]
        [Tooltip("스폰할 OxygenDepletionManager 프리팹 (NetworkObject 필요)")]
        [SerializeField] private NetworkObject _oxygenDepletionManagerPrefab;

        [Header("몹 관리")]
        [Tooltip("스폰할 MobSpawnManager 프리팹 (NetworkObject 필요)")]
        [SerializeField] private NetworkObject _mobSpawnManagerPrefab;

        [Tooltip("스폰할 NavMeshBaker 프리팹 (NetworkObject 필요)")]
        [SerializeField] private NetworkObject _navMeshBakerPrefab;

        public readonly SyncVar<int> MapSeed = new();
        public readonly SyncVar<bool> IsMapReady = new();

        // SyncList for chunk data
        private readonly SyncList<NetworkChunkData> _chunkList = new SyncList<NetworkChunkData>();
        private readonly SyncList<WallSegmentData> _wallSegmentList = new SyncList<WallSegmentData>();

        private MapGenerator _mapGenerator;
        private bool _hasGeneratedMap;
        private LootBoxSpawnManager _spawnedLootBoxManager;
        private ProjectVoid.Map.OxygenDepletionManager _spawnedOxygenDepletionManager;
        private MobSpawnManager _spawnedMobSpawnManager;
        private NavMeshBaker _spawnedNavMeshBaker;
        private GameObject _groundObject;
        private MapLayoutTemplate _selectedTemplate;
        private GameMode _currentGameMode;
        private int _currentPlayerCount;

        public MapGenerator MapGenerator => _mapGenerator;
        public int ChunkCount => _chunkList.Count;
        public int WallSegmentCount => _wallSegmentList.Count;
        
        #region Fishnet Lifecycle

        public override void OnStartServer()
        {
            base.OnStartServer();
            
            SpawnLootBoxManager();
            SpawnOxygenDepletionManager();
            SpawnNavMeshBaker();
            SpawnMobSpawnManager();

            // OnChange 이벤트 구독
            IsMapReady.OnChange += OnMapReadyChanged;
            _chunkList.OnChange += OnChunkListChanged;
            
            Debug.Log("[NetworkMapManager] Managers spawned. Waiting for game mode info to generate map...");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // OnChange 이벤트 구독
            IsMapReady.OnChange += OnMapReadyChanged;
            _chunkList.OnChange += OnChunkListChanged;
            
            // 호스트 모드에서는 서버가 이미 맵을 생성했으므로 클라이언트 렌더링 스킵
            if (IsServerInitialized)
            {
                _hasGeneratedMap = true;
                Debug.Log("[NetworkMapManager] Host mode detected - skipping client render");
                return;
            }
            
            // 늦게 접속한 클라이언트: SyncList에서 데이터를 읽어 렌더링
            TryRenderMapFromSyncList();
        }

        private void OnMapReadyChanged(bool prev, bool next, bool asServer)
        {
            if (IsServerInitialized || asServer) return;
            if (!next || _hasGeneratedMap) return;
            
            TryRenderMapFromSyncList();
        }
        
        /// <summary>
        /// SyncList 변경 콜백 - 맵 데이터 수신 완료 시 렌더링
        /// </summary>
        private void OnChunkListChanged(SyncListOperation op, int index, 
            NetworkChunkData oldItem, NetworkChunkData newItem, bool asServer)
        {
            if (IsServerInitialized || asServer) return;
            if (_hasGeneratedMap) return;
            
            // Add 작업일 때만 체크 (Clear는 무시)
            if (op == SyncListOperation.Add)
            {
                TryRenderMapFromSyncList();
            }
        }
        
        /// <summary>
        /// 맵 렌더링 조건 확인 및 실행
        /// </summary>
        private void TryRenderMapFromSyncList()
        {
            // 조건: IsMapReady=true, 청크 데이터 있음, 아직 생성 안함
            if (!IsMapReady.Value || _chunkList.Count == 0 || _hasGeneratedMap) return;
            
            Debug.Log($"[Client] SyncList data ready - Chunks={_chunkList.Count}, Walls={_wallSegmentList.Count}");
            RenderMapFromSyncList();
        }
        
        /// <summary>
        /// SyncList 데이터 기반 맵 렌더링
        /// </summary>
        private void RenderMapFromSyncList()
        {
            _hasGeneratedMap = true;
            
            NetworkChunkData[] chunks = new NetworkChunkData[_chunkList.Count];
            for (int i = 0; i < _chunkList.Count; i++)
            {
                chunks[i] = _chunkList[i];
            }

            WallSegmentData[] wallSegments = new WallSegmentData[_wallSegmentList.Count];
            for (int i = 0; i < _wallSegmentList.Count; i++)
            {
                wallSegments[i] = _wallSegmentList[i];
            }

            RenderMapOnClient(MapSeed.Value, chunks, wallSegments);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            IsMapReady.OnChange -= OnMapReadyChanged;
            
            if (_spawnedLootBoxManager != null)
            {
                NetworkObject managerObj = _spawnedLootBoxManager.GetComponent<NetworkObject>();
                if (managerObj != null && managerObj.IsSpawned)
                {
                    ServerManager.Despawn(managerObj);
                    Debug.Log("[NetworkMapManager] LootBoxSpawnManager 제거 완료");
                }
            }

            if (_spawnedOxygenDepletionManager != null)
            {
                NetworkObject oxygenManagerObj = _spawnedOxygenDepletionManager.GetComponent<NetworkObject>();
                if (oxygenManagerObj != null && oxygenManagerObj.IsSpawned)
                {
                    ServerManager.Despawn(oxygenManagerObj);
                    Debug.Log("[NetworkMapManager] OxygenDepletionManager 제거 완료");
                }
            }

            if (_spawnedMobSpawnManager != null)
            {
                NetworkObject mobManagerObj = _spawnedMobSpawnManager.GetComponent<NetworkObject>();
                if (mobManagerObj != null && mobManagerObj.IsSpawned)
                {
                    ServerManager.Despawn(mobManagerObj);
                    Debug.Log("[NetworkMapManager] MobSpawnManager 제거 완료");
                }
            }

            if (_spawnedNavMeshBaker != null)
            {
                NetworkObject navMeshBakerObj = _spawnedNavMeshBaker.GetComponent<NetworkObject>();
                if (navMeshBakerObj != null && navMeshBakerObj.IsSpawned)
                {
                    ServerManager.Despawn(navMeshBakerObj);
                    Debug.Log("[NetworkMapManager] NavMeshBaker 제거 완료");
                }
            }
        }

        #endregion

        private void SpawnLootBoxManager()
        {
            if (!IsServerInitialized) return;

            if (_lootBoxSpawnManagerPrefab == null)
            {
                Debug.LogWarning("[NetworkMapManager] LootBoxSpawnManager 프리팹이 설정되지 않았습니다.");
                return;
            }

            NetworkObject spawnedObj = Instantiate(_lootBoxSpawnManagerPrefab);
            ServerManager.Spawn(spawnedObj);

            if (spawnedObj != null)
            {
                _spawnedLootBoxManager = spawnedObj.GetComponent<LootBoxSpawnManager>();
                Debug.Log("[NetworkMapManager] LootBoxSpawnManager 스폰 완료");
            }
        }

        private void SpawnOxygenDepletionManager()
        {
            if (!IsServerInitialized) return;

            if (_oxygenDepletionManagerPrefab == null)
            {
                Debug.LogWarning("[NetworkMapManager] OxygenDepletionManager 프리팹이 설정되지 않았습니다.");
                return;
            }

            NetworkObject spawnedObj = Instantiate(_oxygenDepletionManagerPrefab);
            ServerManager.Spawn(spawnedObj);

            if (spawnedObj != null)
            {
                _spawnedOxygenDepletionManager = spawnedObj.GetComponent<ProjectVoid.Map.OxygenDepletionManager>();
                Debug.Log("[NetworkMapManager] OxygenDepletionManager 스폰 완료");
            }
        }

        private void SpawnMobSpawnManager()
        {
            if (!IsServerInitialized) return;

            if (_mobSpawnManagerPrefab == null)
            {
                Debug.LogWarning("[NetworkMapManager] MobSpawnManager 프리팹이 설정되지 않았습니다.");
                return;
            }

            NetworkObject spawnedObj = Instantiate(_mobSpawnManagerPrefab);
            ServerManager.Spawn(spawnedObj);

            if (spawnedObj != null)
            {
                _spawnedMobSpawnManager = spawnedObj.GetComponent<MobSpawnManager>();
                Debug.Log("[NetworkMapManager] MobSpawnManager 스폰 완료");
            }
        }

        private void SpawnNavMeshBaker()
        {
            if (!IsServerInitialized) return;

            if (_navMeshBakerPrefab == null)
            {
                Debug.LogWarning("[NetworkMapManager] NavMeshBaker 프리팹이 설정되지 않았습니다.");
                return;
            }

            NetworkObject spawnedObj = Instantiate(_navMeshBakerPrefab);
            ServerManager.Spawn(spawnedObj);

            if (spawnedObj != null)
            {
                _spawnedNavMeshBaker = spawnedObj.GetComponent<NavMeshBaker>();
                Debug.Log("[NetworkMapManager] NavMeshBaker 스폰 완료");
            }
        }

        public void InitializeMap(GameMode mode, int playerCount, string preferredTemplateName = "")
        {
            string normalizedPreferredTemplate = string.IsNullOrWhiteSpace(preferredTemplateName)
                ? string.Empty
                : preferredTemplateName.Trim();
            Debug.Log($"[NetworkMapManager] InitializeMap called - Mode: {mode}, Players: {playerCount}, PreferredTemplate: {normalizedPreferredTemplate}");

            if (!IsServerInitialized)
            {
                Debug.LogWarning("[NetworkMapManager] InitializeMap은 서버에서만 호출할 수 있습니다.");
                return;
            }

            if (_mapSettings == null)
            {
                Debug.LogError("[NetworkMapManager] MapGenerationSettings가 설정되지 않았습니다!");
                return;
            }

            if (IsReady())
            {
                Debug.LogWarning($"[NetworkMapManager] Map already initialized!");
                return;
            }

            _currentGameMode = mode;
            _currentPlayerCount = playerCount;

            MapLayoutTemplate[] templates = _mapSettings.GetTemplatesForMode(mode, playerCount);

            if (templates == null || templates.Length == 0)
            {
                Debug.LogWarning($"[NetworkMapManager] {mode} ({playerCount}명)에 맞는 템플릿이 없습니다. 기본 템플릿 사용.");
                GenerateMapOnServer();
                return;
            }

            _selectedTemplate = SelectTemplate(templates, normalizedPreferredTemplate);
            if (_selectedTemplate == null)
            {
                _selectedTemplate = templates[UnityEngine.Random.Range(0, templates.Length)];
            }
            Debug.Log($"[NetworkMapManager] InitializeMap: Mode={mode}, Players={playerCount}, Template={_selectedTemplate.TemplateName}");

            GenerateMapOnServer();
        }

        private static MapLayoutTemplate SelectTemplate(MapLayoutTemplate[] templates, string preferredTemplateName)
        {
            if (templates == null || templates.Length == 0)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(preferredTemplateName))
            {
                return null;
            }

            for (int i = 0; i < templates.Length; i++)
            {
                MapLayoutTemplate template = templates[i];
                if (template == null)
                {
                    continue;
                }

                if (string.Equals(template.TemplateName, preferredTemplateName, StringComparison.OrdinalIgnoreCase))
                {
                    return template;
                }
            }

            return null;
        }

        private void GenerateMapOnServer()
        {
            if (_mapSettings == null)
            {
                Debug.LogError("[NetworkMapManager] MapGenerationSettings가 설정되지 않았습니다!");
                return;
            }

            MapSeed.Value = _mapSettings.GenerateSeed();

            Debug.Log($"[NetworkMapManager] Creating new map | Seed: {MapSeed.Value}");
            _mapGenerator = new MapGenerator(_mapSettings, this.transform, IsServerInitialized);

            if (!_mapGenerator.GenerateMap(MapSeed.Value, _selectedTemplate))
            {
                Debug.LogError("[Server] 맵 생성 실패!");
                return;
            }

            if (_spawnedLootBoxManager != null)
            {
                _spawnedLootBoxManager.SpawnLootBoxesForAllChunks(_mapGenerator);
            }
            else
            {
                Debug.LogError("[NetworkMapManager] _spawnedLootBoxManager is null during GenerateMapOnServer!");
            }

            if (_spawnedOxygenDepletionManager != null)
            {
                var gameStateManager = FindFirstObjectByType<GameStateManager>();
                _spawnedOxygenDepletionManager.Initialize(_mapGenerator, gameStateManager, _mapSettings);
            }

            CreateGroundObject();

            if (_spawnedNavMeshBaker != null)
            {
                _spawnedNavMeshBaker.BakeNavMeshForMap(_mapGenerator);
                Debug.Log("[NetworkMapManager] NavMesh 베이킹 완료");
            }

            if (_spawnedMobSpawnManager != null)
            {
                _spawnedMobSpawnManager.SpawnMobsForAllChunks(_mapGenerator);
                Debug.Log("[NetworkMapManager] 몹 스폰 완료");
            }
            else
            {
                Debug.LogError("[NetworkMapManager] _spawnedMobSpawnManager is null during GenerateMapOnServer!");
            }

            SyncMapDataToClients();
        }

        private void SyncMapDataToClients()
        {
            NetworkChunkData[] chunks = _mapGenerator.ToNetworkArray();
            WallSegmentData[] wallSegments = _mapGenerator.ToWallSegmentArray();
            
            // SyncList에도 저장 (추후 늦게 접속하는 클라이언트용)
            _chunkList.Clear();
            foreach (var chunk in chunks)
            {
                _chunkList.Add(chunk);
            }
            
            _wallSegmentList.Clear();
            foreach (var segment in wallSegments)
            {
                _wallSegmentList.Add(segment);
            }

            Debug.Log($"[Server] 맵 동기화: Chunks={chunks.Length}, WallSegments={wallSegments.Length}");

            // SyncList가 클라이언트에 동기화된 후 IsMapReady를 true로 설정
            // 클라이언트는 OnChunkListChanged 또는 OnMapReadyChanged 콜백에서 렌더링
            _hasGeneratedMap = true;
            IsMapReady.Value = true;
        }

        private void RenderMapOnClient(int seed, NetworkChunkData[] networkChunks, WallSegmentData[] wallSegments)
        {
            if (_mapSettings == null)
            {
                Debug.LogError("[Client] MapGenerationSettings가 설정되지 않았습니다!");
                return;
            }

            Debug.Log($"[Client] 맵 렌더링 시작: Seed={seed}, ChunkCount={networkChunks.Length}, WallSegmentCount={wallSegments.Length}");

            _mapGenerator = new MapGenerator(_mapSettings, this.transform, IsServerInitialized);

            if (!_mapGenerator.RenderMapFromNetworkData(networkChunks, wallSegments, seed))
            {
                Debug.LogError("[Client] 맵 렌더링 실패!");
                return;
            }

            var oxygenManager = FindFirstObjectByType<ProjectVoid.Map.OxygenDepletionManager>();
            if (oxygenManager != null)
            {
                oxygenManager.InitializeForClient(_mapGenerator, _mapSettings);
            }

            CreateGroundObject();
            Debug.Log("[Client] Ground 오브젝트 생성 완료");
            Debug.Log("[Client] 맵 준비 완료 - 로컬 렌더링 상태 갱신 완료");
        }

        private void CreateGroundObject()
        {
            if (_groundObject != null)
            {
                Destroy(_groundObject);
            }

            int chunkSize = _mapSettings.ChunkSize;
            float mapWidth = _mapGenerator.MapWidth * chunkSize;
            float mapHeight = _mapGenerator.MapHeight * chunkSize;
            float padding = 10f;

            float groundWidth = mapWidth + padding * 2;
            float groundDepth = mapHeight + padding * 2;

            Vector3 mapCenter = _mapGenerator.GetMapCenter();
            Vector3 groundPosition = new Vector3(mapCenter.x, 0.5f, mapCenter.z);

            _groundObject = new GameObject("Ground");
            _groundObject.transform.position = groundPosition;
            _groundObject.layer = LayerMask.NameToLayer("Ground");

            BoxCollider groundCollider = _groundObject.AddComponent<BoxCollider>();
            groundCollider.size = new Vector3(groundWidth, 0.1f, groundDepth);
            groundCollider.center = Vector3.zero;

            Debug.Log($"[NetworkMapManager] Ground 생성: Size=({groundWidth}, 0.1, {groundDepth}), Position={groundPosition}");
        }

        public bool IsReady()
        {
            return IsMapReady.Value && _hasGeneratedMap;
        }

        public MapLayoutTemplate CurrentTemplate => _selectedTemplate != null
            ? _selectedTemplate
            : (_mapSettings?.MapTemplates?.Length > 0 ? _mapSettings.MapTemplates[0] : null);

        public int ChunkSize => _mapSettings?.ChunkSize ?? 50;

        public Vector3 GetPlayerSpawnPosition(int playerIndex)
        {
            if (!TryGetSpawnChunkGridPosition(playerIndex, out Vector2Int gridPosition))
            {
                return GetDefaultSpawnPosition(playerIndex);
            }

            if (TryGetNavMeshSpawnPosition(gridPosition, out Vector3 navMeshSpawnPosition))
            {
                Debug.Log($"[NetworkMapManager] Player {playerIndex} NavMesh 스폰 위치: {navMeshSpawnPosition}");
                return navMeshSpawnPosition;
            }

            Vector3 fallbackSpawnPosition = GetDoorFrontWorldPosition(gridPosition);
            Debug.LogWarning($"[NetworkMapManager] Player {playerIndex} NavMesh 스폰 탐색 실패. 문 앞 위치로 폴백: {fallbackSpawnPosition}");
            return fallbackSpawnPosition;
        }

        private Vector2 GetDoorFrontOffset(Vector2Int gridPosition)
        {
            if (_mapGenerator == null) return Vector2.zero;

            if (!_mapGenerator.PlacedChunks.TryGetValue(gridPosition, out var chunkInstance))
            {
                return Vector2.zero;
            }

            int chunkSize = ChunkSize;
            float offsetDistance = chunkSize * 0.25f;

            var doorDirections = chunkInstance.GetDoorDirections();

            if (doorDirections.Count == 0) return Vector2.zero;

            Direction[] priorityOrder = { Direction.North, Direction.East, Direction.South, Direction.West };

            foreach (var direction in priorityOrder)
            {
                if (doorDirections.Contains(direction))
                {
                    return direction switch
                    {
                        Direction.North => new Vector2(0, -offsetDistance),
                        Direction.South => new Vector2(0, offsetDistance),
                        Direction.East => new Vector2(-offsetDistance, 0),
                        Direction.West => new Vector2(offsetDistance, 0),
                        _ => Vector2.zero
                    };
                }
            }

            return Vector2.zero;
        }

        public float GetPlayerSpawnRotation(int playerIndex)
        {
            if (!TryGetSpawnChunkGridPosition(playerIndex, out Vector2Int gridPosition))
            {
                return 0f;
            }

            return GetDoorFacingRotation(gridPosition);
        }

        private bool TryGetSpawnChunkGridPosition(int playerIndex, out Vector2Int gridPosition)
        {
            gridPosition = default;

            MapLayoutTemplate template = CurrentTemplate;

            if (template == null)
            {
                Debug.LogWarning("[NetworkMapManager] MapLayoutTemplate이 없습니다. 기본 위치 반환.");
                return false;
            }

            if (template.ValidSpawnPointCount == 0)
            {
                Debug.LogWarning("[NetworkMapManager] 스폰 포인트가 설정되지 않았습니다. 기본 위치 반환.");
                return false;
            }

            PlayerSpawnPoint[] allSpawnPoints = template.PlayerSpawnPoints;
            if (allSpawnPoints == null || allSpawnPoints.Length == 0)
            {
                Debug.LogWarning("[NetworkMapManager] 스폰 포인트 배열이 비어있습니다. 기본 위치 반환.");
                return false;
            }

            Vector2Int templateCenter = template.GetCenterPosition();
            int validIndex = 0;
            int targetValidIndex = playerIndex % template.ValidSpawnPointCount;

            for (int i = 0; i < allSpawnPoints.Length; i++)
            {
                if (!allSpawnPoints[i].IsValid)
                {
                    continue;
                }

                if (validIndex != targetValidIndex)
                {
                    validIndex++;
                    continue;
                }

                gridPosition = new Vector2Int(
                    allSpawnPoints[i].GridPosition.x - templateCenter.x,
                    allSpawnPoints[i].GridPosition.y - templateCenter.y
                );

                if (_mapGenerator == null || !_mapGenerator.PlacedChunks.ContainsKey(gridPosition))
                {
                    Debug.LogError($"[NetworkMapManager] Player {playerIndex} 스폰 포인트에 청크가 없습니다!");
                    return false;
                }

                return true;
            }

            return false;
        }

        private bool TryGetNavMeshSpawnPosition(Vector2Int gridPosition, out Vector3 spawnPosition)
        {
            spawnPosition = Vector3.zero;

            if (_spawnedNavMeshBaker == null || !_spawnedNavMeshBaker.IsNavMeshBaked)
            {
                return false;
            }

            Bounds chunkBounds = GetSpawnChunkBounds(gridPosition);
            Vector3 doorFrontCandidate = GetDoorFrontWorldPosition(gridPosition);
            if (TrySampleNavMeshPositionInChunk(doorFrontCandidate, chunkBounds, out spawnPosition))
            {
                return true;
            }

            Vector3 chunkCenter = GetChunkCenterWorldPosition(gridPosition);
            Vector3 boundsExtents = chunkBounds.extents;

            for (int i = 0; i < SPAWN_CHUNK_SEARCH_PATTERN.Length; i++)
            {
                Vector2 pattern = SPAWN_CHUNK_SEARCH_PATTERN[i];
                Vector3 candidatePosition = new Vector3(
                    chunkCenter.x + (boundsExtents.x * pattern.x),
                    chunkCenter.y,
                    chunkCenter.z + (boundsExtents.z * pattern.y));

                if (TrySampleNavMeshPositionInChunk(candidatePosition, chunkBounds, out spawnPosition))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TrySampleNavMeshPositionInChunk(Vector3 candidatePosition, Bounds chunkBounds, out Vector3 spawnPosition)
        {
            spawnPosition = Vector3.zero;

            if (!NavMesh.SamplePosition(candidatePosition, out NavMeshHit navMeshHit, SPAWN_NAVMESH_SAMPLE_DISTANCE, NavMesh.AllAreas))
            {
                return false;
            }

            Vector3 chunkCheckPosition = new Vector3(navMeshHit.position.x, chunkBounds.center.y, navMeshHit.position.z);
            if (!chunkBounds.Contains(chunkCheckPosition))
            {
                return false;
            }

            spawnPosition = navMeshHit.position + Vector3.up * NAVMESH_SPAWN_HEIGHT_OFFSET;
            return true;
        }

        private Bounds GetSpawnChunkBounds(Vector2Int gridPosition)
        {
            float paddedChunkSize = Mathf.Max(1f, ChunkSize - (SPAWN_CHUNK_PADDING * 2f));
            return new Bounds(
                GetChunkCenterWorldPosition(gridPosition),
                new Vector3(paddedChunkSize, 10f, paddedChunkSize));
        }

        private Vector3 GetChunkCenterWorldPosition(Vector2Int gridPosition)
        {
            int chunkSize = ChunkSize;
            float worldX = (gridPosition.x * chunkSize) + (chunkSize / 2f);
            float worldZ = (gridPosition.y * chunkSize) + (chunkSize / 2f);
            return new Vector3(worldX, DEFAULT_SPAWN_HEIGHT, worldZ);
        }

        private Vector3 GetDoorFrontWorldPosition(Vector2Int gridPosition)
        {
            Vector2 doorOffset = GetDoorFrontOffset(gridPosition);
            return GridToWorldPosition(gridPosition.x, gridPosition.y, doorOffset);
        }

        private float GetDoorFacingRotation(Vector2Int gridPosition)
        {
            if (_mapGenerator == null) return 0f;

            if (!_mapGenerator.PlacedChunks.TryGetValue(gridPosition, out var chunkInstance))
            {
                return 0f;
            }

            var doorDirections = chunkInstance.GetDoorDirections();
            
            if (doorDirections.Count == 0) return 0f;

            Direction[] priorityOrder = { Direction.North, Direction.East, Direction.South, Direction.West };
            
            foreach (var direction in priorityOrder)
            {
                if (doorDirections.Contains(direction))
                {
                    return direction switch
                    {
                        Direction.North => 0f,
                        Direction.South => 180f,
                        Direction.East => 90f,
                        Direction.West => 270f,
                        _ => 0f
                    };
                }
            }

            return 0f;
        }

        private Vector3 GridToWorldPosition(int gridX, int gridY, Vector2 localOffset)
        {
            int chunkSize = ChunkSize;

            float worldX = (gridX * chunkSize) + (chunkSize / 2f) + localOffset.x;
            float worldZ = (gridY * chunkSize) + (chunkSize / 2f) + localOffset.y;
            float worldY = DEFAULT_SPAWN_HEIGHT;

            return new Vector3(worldX, worldY, worldZ);
        }

        private Vector3 GetDefaultSpawnPosition(int playerIndex)
        {
            float radius = 10f;
            float angle = (playerIndex * 45f) * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            return new Vector3(x, DEFAULT_SPAWN_HEIGHT, z);
        }
    }
}
