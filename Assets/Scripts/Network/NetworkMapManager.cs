using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectVoid.Map;

namespace ProjectVoid.Network
{
    public class NetworkMapManager : NetworkBehaviour
    {
        private const int MAX_CHUNK_CAPACITY = 500;
        private const int MAX_WALL_SEGMENT_CAPACITY = 800;

        [Header("맵 생성 설정")]
        [SerializeField] private MapGenerationSettings _mapSettings;

        [Header("LootBox 관리")]
        [Tooltip("스폰할 LootBoxSpawnManager 프리팹 (NetworkObject 필요)")]
        [SerializeField] private NetworkPrefabRef _lootBoxSpawnManagerPrefab;

        [Header("산소 고갈 구역 관리")]
        [Tooltip("스폰할 OxygenDepletionManager 프리팹 (NetworkObject 필요)")]
        [SerializeField] private NetworkPrefabRef _oxygenDepletionManagerPrefab;

        [Header("몹 관리")]
        [Tooltip("스폰할 MobSpawnManager 프리팩 (NetworkObject 필요)")]
        [SerializeField] private NetworkPrefabRef _mobSpawnManagerPrefab;

        [Tooltip("스폰할 NavMeshBaker 프리팩 (NetworkObject 필요)")]
        [SerializeField] private NetworkPrefabRef _navMeshBakerPrefab;

        [Networked] public int MapSeed { get; set; }
        [Networked] public NetworkBool IsMapReady { get; set; }
        [Networked, Capacity(MAX_CHUNK_CAPACITY)] public NetworkArray<NetworkChunkData> ChunkArray => default;
        [Networked] public int ChunkCount { get; set; }
        [Networked, Capacity(MAX_WALL_SEGMENT_CAPACITY)] public NetworkArray<WallSegmentData> WallSegmentArray => default;
        [Networked] public int WallSegmentCount { get; set; }

        private MapGenerator _mapGenerator;
        private bool _hasGeneratedMap;
        private LootBoxSpawnManager _spawnedLootBoxManager;
        private ProjectVoid.Map.OxygenDepletionManager _spawnedOxygenDepletionManager;
        private MobSpawnManager _spawnedMobSpawnManager;
        private NavMeshBaker _spawnedNavMeshBaker;
        private GameObject _groundObject; // 바닥 충돌용 Ground 오브젝트
        private MapLayoutTemplate _selectedTemplate; // 선택된 맵 템플릿
        private GameMode _currentGameMode; // Why: 맵 생성 시 필요한 게임 모드 정보
        private int _currentPlayerCount; // Why: 맵 생성 시 필요한 플레이어 수 정보

        /// <summary>
        /// 맵 생성기 인스턴스를 반환합니다.
        /// NavMeshBaker와 MobSpawnManager에서 사용합니다.
        /// </summary>
        public MapGenerator MapGenerator => _mapGenerator;

        #region Fusion Lifecycle

        public override void Spawned()
        {
            // Why: NetworkObject를 씬 전환 시에도 유지하려면 Runner.MakeDontDestroyOnLoad 사용
            // Why: 서버에서만 호출 - 클라이언트에서는 assertion 경고가 발생할 수 있음
            if (HasStateAuthority)
            {
                Runner.MakeDontDestroyOnLoad(gameObject);
                
                // Why: 서버에서 각종 Manager 프리팩을 네트워크 스폰
                SpawnLootBoxManager();
                SpawnOxygenDepletionManager();
                SpawnNavMeshBaker();
                SpawnMobSpawnManager();
                // Why: 맵 생성은 클라이언트가 접속해서 RPC로 게임 모드 정보를 보낼 때만 수행
                // InitializeMap()이 호출되면 그때 GenerateMapOnServer() 실행
                Debug.Log("[NetworkMapManager] Managers spawned. Waiting for game mode info to generate map...");
            }
            else if (IsMapReady && ChunkCount > 0)
            {
                // Late Joiner: 이미 맵이 준비되어 있으면 즉시 렌더링
                OnMapDataReceived();
            }
            // 동시 접속자는 RPC_NotifyMapReady()를 받을 때까지 대기
        }

        #endregion

        private void SpawnLootBoxManager()
        {
            if (!HasStateAuthority)
                return;

            if (!_lootBoxSpawnManagerPrefab.IsValid)
            {
                Debug.LogWarning("[NetworkMapManager] LootBoxSpawnManager 프리팹이 설정되지 않았습니다. LootBox가 스폰되지 않습니다.");
                return;
            }

            // Why: LootBoxSpawnManager를 네트워크 오브젝트로 스폰
            NetworkObject spawnedObj = Runner.Spawn(_lootBoxSpawnManagerPrefab, Vector3.zero, Quaternion.identity);

            if (spawnedObj != null)
            {
                _spawnedLootBoxManager = spawnedObj.GetComponent<LootBoxSpawnManager>();

                if (_spawnedLootBoxManager == null)
                {
                    Debug.LogError("[NetworkMapManager] 스폰된 프리팹에 LootBoxSpawnManager 컴포넌트가 없습니다!");
                }
                else
                {
                    Debug.Log("[NetworkMapManager] LootBoxSpawnManager 스폰 완료");
                }
            }
            else
            {
                Debug.LogError("[NetworkMapManager] LootBoxSpawnManager 스폰 실패!");
            }
        }

        private void SpawnOxygenDepletionManager()
        {
            if (!HasStateAuthority)
                return;

            if (!_oxygenDepletionManagerPrefab.IsValid)
            {
                Debug.LogWarning("[NetworkMapManager] OxygenDepletionManager 프리팹이 설정되지 않았습니다. 산소 고갈 시스템이 비활성화됩니다.");
                return;
            }

            // Why: OxygenDepletionManager를 네트워크 오브젝트로 스폰
            NetworkObject spawnedObj = Runner.Spawn(_oxygenDepletionManagerPrefab, Vector3.zero, Quaternion.identity);

            if (spawnedObj != null)
            {
                _spawnedOxygenDepletionManager = spawnedObj.GetComponent<ProjectVoid.Map.OxygenDepletionManager>();

                if (_spawnedOxygenDepletionManager == null)
                {
                    Debug.LogError("[NetworkMapManager] 스폰된 프리팹에 OxygenDepletionManager 컴포넌트가 없습니다!");
                }
                else
                {
                    Debug.Log("[NetworkMapManager] OxygenDepletionManager 스폰 완료");
                }
            }
            else
            {
                Debug.LogError("[NetworkMapManager] OxygenDepletionManager 스폰 실패!");
            }
        }

        private void SpawnMobSpawnManager()
        {
            if (!HasStateAuthority)
                return;

            if (!_mobSpawnManagerPrefab.IsValid)
            {
                Debug.LogWarning("[NetworkMapManager] MobSpawnManager 프리팹이 설정되지 않았습니다. 몹이 스폰되지 않습니다.");
                return;
            }

            // Why: MobSpawnManager를 네트워크 오브젝트로 스폰
            NetworkObject spawnedObj = Runner.Spawn(_mobSpawnManagerPrefab, Vector3.zero, Quaternion.identity);

            if (spawnedObj != null)
            {
                _spawnedMobSpawnManager = spawnedObj.GetComponent<MobSpawnManager>();

                if (_spawnedMobSpawnManager == null)
                {
                    Debug.LogError("[NetworkMapManager] 스폰된 프리팹에 MobSpawnManager 컴포넌트가 없습니다!");
                }
                else
                {
                    Debug.Log("[NetworkMapManager] MobSpawnManager 스폰 완료");
                }
            }
            else
            {
                Debug.LogError("[NetworkMapManager] MobSpawnManager 스폰 실패!");
            }
        }

        private void SpawnNavMeshBaker()
        {
            if (!HasStateAuthority)
                return;

            if (!_navMeshBakerPrefab.IsValid)
            {
                Debug.LogWarning("[NetworkMapManager] NavMeshBaker 프리팹이 설정되지 않았습니다. 몹 AI가 작동하지 않을 수 있습니다.");
                return;
            }

            // Why: NavMeshBaker를 네트워크 오브젝트로 스폰
            NetworkObject spawnedObj = Runner.Spawn(_navMeshBakerPrefab, Vector3.zero, Quaternion.identity);

            if (spawnedObj != null)
            {
                _spawnedNavMeshBaker = spawnedObj.GetComponent<NavMeshBaker>();

                if (_spawnedNavMeshBaker == null)
                {
                    Debug.LogError("[NetworkMapManager] 스폰된 프리팹에 NavMeshBaker 컴포넌트가 없습니다!");
                }
                else
                {
                    Debug.Log("[NetworkMapManager] NavMeshBaker 스폰 완료");
                }
            }
            else
            {
                Debug.LogError("[NetworkMapManager] NavMeshBaker 스폰 실패!");
            }
        }

        /// <summary>
        /// 게임 모드와 플레이어 수에 맞는 맵 초기화
        /// MatchmakingManager에서 호출됩니다.
        /// </summary>
        /// <param name="mode">게임 모드</param>
        /// <param name="playerCount">플레이어 수</param>
        public void InitializeMap(GameMode mode, int playerCount)
        {
            Debug.Log($"[NetworkMapManager] InitializeMap called - Mode: {mode}, Players: {playerCount}, HasStateAuthority: {HasStateAuthority}");

            if (!HasStateAuthority)
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
                Debug.LogWarning($"[NetworkMapManager] Map already initialized! IsMapReady: {IsMapReady}, HasGeneratedMap: {_hasGeneratedMap}");
                return;
            }

            // Why: 게임 모드와 플레이어 수 저장 (맵 풀링용)
            _currentGameMode = mode;
            _currentPlayerCount = playerCount;

            // 모드에 맞는 템플릿 리스트 가져오기
            MapLayoutTemplate[] templates = _mapSettings.GetTemplatesForMode(mode, playerCount);

            if (templates == null || templates.Length == 0)
            {
                Debug.LogWarning($"[NetworkMapManager] {mode} ({playerCount}명)에 맞는 템플릿이 없습니다. 기본 템플릿 사용.");
                // 기본 GenerateMapOnServer 사용
                GenerateMapOnServer();
                return;
            }

            // 템플릿 중 하나를 랜덤 선택
            _selectedTemplate = templates[UnityEngine.Random.Range(0, templates.Length)];

            Debug.Log($"[NetworkMapManager] InitializeMap: Mode={mode}, Players={playerCount}, Template={_selectedTemplate.TemplateName}");

            // 선택된 템플릿으로 맵 생성
            GenerateMapOnServer();
        }

        private void GenerateMapOnServer()
        {
            if (_mapSettings == null)
            {
                Debug.LogError("[NetworkMapManager] MapGenerationSettings가 설정되지 않았습니다!");
                return;
            }

            MapSeed = _mapSettings.GenerateSeed();

            // Why: 매 게임마다 새 맵 생성 (MapPool 제거됨)
            Debug.Log($"[NetworkMapManager] Creating new map | Seed: {MapSeed}");
            _mapGenerator = new MapGenerator(_mapSettings, this.transform, HasStateAuthority);

            if (!_mapGenerator.GenerateMap(MapSeed, _selectedTemplate))
            {
                Debug.LogError("[Server] 맵 생성 실패!");
                return;
            }

            // Why: 맵 생성 후 모든 청크의 LootBox 스폰
            if (_spawnedLootBoxManager != null)
            {
                _spawnedLootBoxManager.SpawnLootBoxesForAllChunks(_mapGenerator);
            }
            else
            {
                Debug.LogWarning("[NetworkMapManager] LootBoxSpawnManager가 스폰되지 않았습니다. LootBox가 스폰되지 않습니다.");
            }

            // Why: 맵 생성 후 OxygenDepletionManager 초기화
            if (_spawnedOxygenDepletionManager != null)
            {
                var gameStateManager = NetworkManager.GetManager(Runner)?.GameStateManager;
                _spawnedOxygenDepletionManager.Initialize(_mapGenerator, gameStateManager, _mapSettings);
            }
            else
            {
                Debug.LogWarning("[NetworkMapManager] OxygenDepletionManager가 스폰되지 않았습니다. 산소 고갈 시스템이 비활성화됩니다.");
            }

            // Why: 맵 생성 후 Ground 오브젝트 생성 (서버/클라이언트 공통)
            CreateGroundObject();

            // Why: 맵 생성 후 NavMesh 베이킹 (서버에서만)
            if (_spawnedNavMeshBaker != null)
            {
                _spawnedNavMeshBaker.BakeNavMeshForMap(_mapGenerator);
                Debug.Log("[NetworkMapManager] NavMesh 베이킹 완료");
            }
            else
            {
                Debug.LogWarning("[NetworkMapManager] NavMeshBaker가 스폰되지 않았습니다. 몹 AI가 작동하지 않을 수 있습니다.");
            }

            if (_spawnedMobSpawnManager != null)
            {
                _spawnedMobSpawnManager.SpawnMobsForAllChunks(_mapGenerator);
                Debug.Log("[NetworkMapManager] 몹 스폰 완료");
            }
            else
            {
                Debug.LogWarning("[NetworkMapManager] MobSpawnManager가 스폰되지 않았습니다. 몹이 스폰되지 않습니다.");
            }

            SyncMapDataToClients();
        }

        private void SyncMapDataToClients()
        {
            NetworkChunkData[] chunks = _mapGenerator.ToNetworkArray();
            ChunkCount = chunks.Length;

            if (chunks.Length > MAX_CHUNK_CAPACITY)
            {
                Debug.LogWarning($"[Server] 청크 수({chunks.Length})가 최대 용량({MAX_CHUNK_CAPACITY})을 초과합니다!");
            }

            for (int i = 0; i < chunks.Length && i < MAX_CHUNK_CAPACITY; i++)
            {
                ChunkArray.Set(i, chunks[i]);
            }

            // 벽 구간 정보 동기화
            WallSegmentData[] wallSegments = _mapGenerator.ToWallSegmentArray();
            WallSegmentCount = wallSegments.Length;

            if (wallSegments.Length > MAX_WALL_SEGMENT_CAPACITY)
            {
                Debug.LogWarning($"[Server] 벽 구간 수({wallSegments.Length})가 최대 용량({MAX_WALL_SEGMENT_CAPACITY})을 초과합니다!");
            }

            for (int i = 0; i < wallSegments.Length && i < MAX_WALL_SEGMENT_CAPACITY; i++)
            {
                WallSegmentArray.Set(i, wallSegments[i]);
            }

            Debug.Log($"[Server] 맵 동기화: Chunks={ChunkCount}, WallSegments={WallSegmentCount}");

            IsMapReady = true;
            _hasGeneratedMap = true;

            // 모든 클라이언트에게 맵 준비 완료 알림 (한 번만)
            RPC_NotifyMapReady();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_NotifyMapReady()
        {
            // 클라이언트만 실행 (서버는 이미 생성함)
            if (!HasStateAuthority && !_hasGeneratedMap)
            {
                OnMapDataReceived();
            }
        }

        private void OnMapDataReceived()
        {
            _hasGeneratedMap = true;
            RenderMapOnClient();
        }

        private void RenderMapOnClient()
        {
            if (_mapSettings == null)
            {
                Debug.LogError("[Client] MapGenerationSettings가 설정되지 않았습니다!");
                return;
            }

            NetworkChunkData[] networkChunks = new NetworkChunkData[ChunkCount];
            for (int i = 0; i < ChunkCount; i++)
            {
                networkChunks[i] = ChunkArray[i];
            }

            WallSegmentData[] wallSegments = new WallSegmentData[WallSegmentCount];
            for (int i = 0; i < WallSegmentCount; i++)
            {
                wallSegments[i] = WallSegmentArray[i];
            }

            Debug.Log($"[Client] 맵 렌더링 시작: Seed={MapSeed}, ChunkCount={ChunkCount}, WallSegmentCount={WallSegmentCount}");

            _mapGenerator = new MapGenerator(_mapSettings, this.transform, HasStateAuthority);

            // 서버에서 보낸 벽 구간 정보 기반으로 렌더링
            if (!_mapGenerator.RenderMapFromNetworkData(networkChunks, wallSegments, MapSeed))
            {
                Debug.LogError("[Client] 맵 렌더링 실패!");
                return;
            }

            // Why: 클라이언트에서도 OxygenDepletionManager 초기화
            var oxygenManager = FindFirstObjectByType<ProjectVoid.Map.OxygenDepletionManager>();
            if (oxygenManager != null)
            {
                oxygenManager.InitializeForClient(_mapGenerator, _mapSettings);
            }
            else
            {
                Debug.LogWarning("[Client] OxygenDepletionManager를 찾을 수 없습니다.");
            }

            // Why: 클라이언트에서도 Ground 오브젝트 생성 (바닥 충돌용)
            CreateGroundObject();
            Debug.Log("[Client] Ground 오브젝트 생성 완료");

            // Why: 클라이언트에서도 맵 준비 완료 플래그 설정
            IsMapReady = true;
            Debug.Log("[Client] 맵 준비 완료 - IsMapReady = true");

            // Why: 클라이언트에서는 NavMesh 베이킹 불필요 (몹 AI는 서버에서만 실행)
        }

        /// <summary>
        /// Ground 오브젝트를 생성합니다 (서버/클라이언트 공통).
        /// 플레이어가 바닥으로 떨어지지 않도록 BoxCollider를 포함합니다.
        /// </summary>
        private void CreateGroundObject()
        {
            // Why: 기존 Ground가 있으면 제거
            if (_groundObject != null)
            {
                Destroy(_groundObject);
            }

            // Why: MapLayout 기반으로 Ground 크기 계산
            int chunkSize = _mapSettings.ChunkSize;
            float mapWidth = _mapGenerator.MapWidth * chunkSize;
            float mapHeight = _mapGenerator.MapHeight * chunkSize;
            float padding = 10f; // 맵 바운드 여유 공간

            float groundWidth = mapWidth + padding * 2;
            float groundDepth = mapHeight + padding * 2;

            // Why: 맵의 실제 중심 위치 계산 (청크들의 중심)
            Vector3 mapCenter = _mapGenerator.GetMapCenter();
            Vector3 groundPosition = new Vector3(mapCenter.x, 0.5f, mapCenter.z); // Y = 0.5 (바닥보다 살짝 위)

            // Why: Ground GameObject 생성
            _groundObject = new GameObject("Ground");
            _groundObject.transform.position = groundPosition;
            _groundObject.layer = LayerMask.NameToLayer("Ground"); // Ground 레이어 설정 (있다면)

            // Why: Multi-Peer 환경에서 Ground를 Runner의 씬으로 이동하여 Physics 충돌 보장
            MoveToRunnerScene(_groundObject);

            // Why: BoxCollider 추가 (플레이어 충돌용)
            BoxCollider groundCollider = _groundObject.AddComponent<BoxCollider>();
            groundCollider.size = new Vector3(groundWidth, 0.1f, groundDepth);
            groundCollider.center = Vector3.zero;

            Debug.Log($"[NetworkMapManager] Ground 생성: Size=({groundWidth}, 0.1, {groundDepth}), Position={groundPosition}");
        }

        /// <summary>
        /// Multi-Peer 환경에서 GameObject를 Runner의 씬으로 이동합니다.
        /// </summary>
        private void MoveToRunnerScene(GameObject obj)
        {
            if (obj == null) return;
            if (Runner == null || !Runner.SimulationUnityScene.IsValid()) return;

            SceneManager.MoveGameObjectToScene(obj, Runner.SimulationUnityScene);
            Debug.Log($"[NetworkMapManager] '{obj.name}'을 Runner 씬으로 이동 완료");
        }

        public bool IsReady()
        {
            return IsMapReady && _hasGeneratedMap;
        }

        /// <summary>
        /// 현재 사용 중인 MapLayoutTemplate을 반환합니다.
        /// </summary>
        public MapLayoutTemplate CurrentTemplate => _selectedTemplate != null
            ? _selectedTemplate
            : (_mapSettings?.MapTemplates?.Length > 0 ? _mapSettings.MapTemplates[0] : null);

        /// <summary>
        /// 청크 크기를 반환합니다.
        /// </summary>
        public int ChunkSize => _mapSettings?.ChunkSize ?? 50;

        /// <summary>
        /// 플레이어 인덱스에 해당하는 스폰 위치를 월드 좌표로 반환합니다.
        /// 청크 내 문 앞 위치에 스폰되어 다른 오브젝트와 충돌을 방지합니다.
        /// </summary>
        /// <param name="playerIndex">플레이어 인덱스 (0부터 시작)</param>
        /// <returns>월드 좌표 스폰 위치</returns>
        public Vector3 GetPlayerSpawnPosition(int playerIndex)
        {
            MapLayoutTemplate template = CurrentTemplate;

            if (template == null)
            {
                Debug.LogWarning("[NetworkMapManager] MapLayoutTemplate이 없습니다. 기본 위치 반환.");
                return GetDefaultSpawnPosition(playerIndex);
            }

            Debug.Log($"[NetworkMapManager] GetPlayerSpawnPosition - PlayerIndex: {playerIndex}, Template: {template.TemplateName}, ValidSpawnPointCount: {template.ValidSpawnPointCount}");

            if (template.ValidSpawnPointCount == 0)
            {
                Debug.LogWarning("[NetworkMapManager] 스폰 포인트가 설정되지 않았습니다. 기본 위치 반환.");
                return GetDefaultSpawnPosition(playerIndex);
            }

            // 유효한 스폰 포인트만 수집
            var allSpawnPoints = template.PlayerSpawnPoints;
            if (allSpawnPoints == null || allSpawnPoints.Length == 0)
            {
                Debug.LogWarning("[NetworkMapManager] 스폰 포인트 배열이 비어있습니다. 기본 위치 반환.");
                return GetDefaultSpawnPosition(playerIndex);
            }

            // Why: 템플릿 중심점 가져오기 (템플릿 좌표 → 그리드 좌표 변환에 필요)
            Vector2Int templateCenter = template.GetCenterPosition();
            Debug.Log($"[NetworkMapManager] Template center: ({templateCenter.x}, {templateCenter.y})");

            // Why: 모든 스폰 포인트 정보 출력 (디버깅용)
            for (int i = 0; i < allSpawnPoints.Length; i++)
            {
                if (allSpawnPoints[i].IsValid)
                {
                    // Why: 템플릿 좌표를 그리드 좌표로 변환하여 확인
                    Vector2Int gridPos = new Vector2Int(
                        allSpawnPoints[i].GridPosition.x - templateCenter.x,
                        allSpawnPoints[i].GridPosition.y - templateCenter.y
                    );
                    Debug.Log($"[NetworkMapManager] SpawnPoint[{i}]: Template({allSpawnPoints[i].GridPosition.x}, {allSpawnPoints[i].GridPosition.y}) -> Grid({gridPos.x}, {gridPos.y}), ChunkExists: {_mapGenerator?.PlacedChunks.ContainsKey(gridPos)}");
                }
            }

            // Why: 유효한 스폰 포인트 인덱스 찾기 (원래 순서 유지)
            int validIndex = 0;
            int targetValidIndex = playerIndex % template.ValidSpawnPointCount;
            Debug.Log($"[NetworkMapManager] Looking for spawn point - validIndex target: {targetValidIndex}");

            for (int i = 0; i < allSpawnPoints.Length; i++)
            {
                if (allSpawnPoints[i].IsValid)
                {
                    if (validIndex == targetValidIndex)
                    {
                        var spawnPoint = allSpawnPoints[i];

                        // Why: PlayerSpawnPoint.GridPosition은 템플릿 좌표이므로 그리드 좌표로 변환 필요
                        // 템플릿 좌표 (x, y) → 그리드 좌표 (x - center.x, y - center.y)
                        Vector2Int gridPosition = new Vector2Int(
                            spawnPoint.GridPosition.x - templateCenter.x,
                            spawnPoint.GridPosition.y - templateCenter.y
                        );

                        Debug.Log($"[NetworkMapManager] Selected SpawnPoint[{i}] for Player {playerIndex}: Template({spawnPoint.GridPosition.x}, {spawnPoint.GridPosition.y}) -> Grid({gridPosition.x}, {gridPosition.y})");

                        // Why: 스폰 포인트의 청크가 실제로 생성되었는지 확인
                        if (_mapGenerator == null || !_mapGenerator.PlacedChunks.ContainsKey(gridPosition))
                        {
                            Debug.LogError($"[NetworkMapManager] Player {playerIndex} 스폰 포인트 Grid({gridPosition})에 청크가 생성되지 않았습니다! 기본 위치 사용.");
                            return GetDefaultSpawnPosition(playerIndex);
                        }

                        // 청크의 문 방향을 확인하여 문 앞 위치 계산
                        Vector2 doorOffset = GetDoorFrontOffset(gridPosition);

                        // 그리드 좌표를 월드 좌표로 변환 (문 앞 오프셋 적용)
                        Vector3 worldPosition = GridToWorldPosition(
                            gridPosition.x,
                            gridPosition.y,
                            doorOffset
                        );

                        Debug.Log($"[NetworkMapManager] Player {playerIndex} 최종 스폰 위치: Template({spawnPoint.GridPosition.x}, {spawnPoint.GridPosition.y}) -> Grid({gridPosition.x}, {gridPosition.y}) + DoorOffset({doorOffset.x:F2}, {doorOffset.y:F2}) -> World({worldPosition.x:F2}, {worldPosition.y:F2}, {worldPosition.z:F2})");

                        return worldPosition;
                    }
                    validIndex++;
                }
            }

            // Why: 여기까지 왔다면 스폰 포인트를 찾지 못한 경우
            Debug.LogError($"[NetworkMapManager] Player {playerIndex}에 해당하는 스폰 포인트를 찾을 수 없습니다! 기본 위치 사용.");
            return GetDefaultSpawnPosition(playerIndex);
        }

        /// <summary>
        /// 청크의 문 방향을 확인하여 문 앞 오프셋을 계산합니다.
        /// 문이 여러 개인 경우 우선순위: North > East > South > West
        /// </summary>
        private Vector2 GetDoorFrontOffset(Vector2Int gridPosition)
        {
            if (_mapGenerator == null)
            {
                Debug.LogWarning("[NetworkMapManager] MapGenerator가 없습니다. 기본 오프셋 반환.");
                return Vector2.zero;
            }

            // 해당 그리드 위치의 청크 인스턴스 가져오기
            if (!_mapGenerator.PlacedChunks.TryGetValue(gridPosition, out var chunkInstance))
            {
                Debug.LogWarning($"[NetworkMapManager] 청크가 없습니다: {gridPosition}. 기본 오프셋 반환.");
                return Vector2.zero;
            }

            int chunkSize = ChunkSize;
            float offsetDistance = chunkSize * 0.25f; // 청크 중심에서 문 앞까지 거리 (25% - 청크 내부 보장)

            // 문이 있는 방향들 확인
            var doorDirections = chunkInstance.GetDoorDirections();

            if (doorDirections.Count == 0)
            {
                // 문이 없는 경우 (닫힌 청크) - 청크 중앙
                Debug.LogWarning($"[NetworkMapManager] 청크 {gridPosition}에 문이 없습니다. 중앙 위치 반환.");
                return Vector2.zero;
            }

            // Why: 문 방향에 따른 오프셋 계산
            // 문이 있는 쪽에서 청크 **안쪽**으로 이동하여 스폰
            // 예: 북쪽에 문이 있으면 → 남쪽(청크 안쪽)으로 이동
            // X: East(+), West(-)
            // Y(Z): North(+), South(-)

            // 우선순위: North > East > South > West (일반적인 진입 방향)
            Direction[] priorityOrder = { Direction.North, Direction.East, Direction.South, Direction.West };

            foreach (var direction in priorityOrder)
            {
                if (doorDirections.Contains(direction))
                {
                    return direction switch
                    {
                        Direction.North => new Vector2(0, -offsetDistance),  // 북쪽 문 → 청크 안쪽(남쪽)으로
                        Direction.South => new Vector2(0, offsetDistance),   // 남쪽 문 → 청크 안쪽(북쪽)으로
                        Direction.East => new Vector2(-offsetDistance, 0),   // 동쪽 문 → 청크 안쪽(서쪽)으로
                        Direction.West => new Vector2(offsetDistance, 0),    // 서쪽 문 → 청크 안쪽(동쪽)으로
                        _ => Vector2.zero
                    };
                }
            }

            return Vector2.zero;
        }

        /// <summary>
        /// 플레이어 인덱스에 해당하는 스폰 회전값을 반환합니다.
        /// 문 방향을 바라보도록 회전합니다.
        /// </summary>
        /// <param name="playerIndex">플레이어 인덱스 (0부터 시작)</param>
        /// <returns>Y축 회전값 (도)</returns>
        public float GetPlayerSpawnRotation(int playerIndex)
        {
            MapLayoutTemplate template = CurrentTemplate;
            
            if (template == null || template.ValidSpawnPointCount == 0)
            {
                return 0f;
            }

            var allSpawnPoints = template.PlayerSpawnPoints;
            if (allSpawnPoints == null || allSpawnPoints.Length == 0)
            {
                return 0f;
            }

            // 유효한 스폰 포인트 인덱스 찾기
            int validIndex = 0;
            int targetValidIndex = playerIndex % template.ValidSpawnPointCount;

            // Why: 템플릿 중심점 계산 (템플릿 좌표 → 그리드 좌표 변환에 필요)
            Vector2Int center = template.GetCenterPosition();

            for (int i = 0; i < allSpawnPoints.Length; i++)
            {
                if (allSpawnPoints[i].IsValid)
                {
                    if (validIndex == targetValidIndex)
                    {
                        var spawnPoint = allSpawnPoints[i];

                        // Why: PlayerSpawnPoint.GridPosition은 템플릿 좌표이므로 그리드 좌표로 변환
                        Vector2Int gridPosition = new Vector2Int(
                            spawnPoint.GridPosition.x - center.x,
                            spawnPoint.GridPosition.y - center.y
                        );

                        return GetDoorFacingRotation(gridPosition);
                    }
                    validIndex++;
                }
            }

            return 0f;
        }

        /// <summary>
        /// 청크의 문 방향을 바라보는 회전값을 반환합니다.
        /// </summary>
        private float GetDoorFacingRotation(Vector2Int gridPosition)
        {
            if (_mapGenerator == null)
            {
                return 0f;
            }

            if (!_mapGenerator.PlacedChunks.TryGetValue(gridPosition, out var chunkInstance))
            {
                return 0f;
            }

            var doorDirections = chunkInstance.GetDoorDirections();
            
            if (doorDirections.Count == 0)
            {
                return 0f;
            }

            // 우선순위: North > East > South > West
            Direction[] priorityOrder = { Direction.North, Direction.East, Direction.South, Direction.West };
            
            foreach (var direction in priorityOrder)
            {
                if (doorDirections.Contains(direction))
                {
                    // 문 방향을 바라보도록 회전
                    return direction switch
                    {
                        Direction.North => 0f,     // 북쪽 문 → 북쪽(0도) 바라봄
                        Direction.South => 180f,   // 남쪽 문 → 남쪽(180도) 바라봄
                        Direction.East => 90f,     // 동쪽 문 → 동쪽(90도) 바라봄
                        Direction.West => 270f,    // 서쪽 문 → 서쪽(270도) 바라봄
                        _ => 0f
                    };
                }
            }

            return 0f;
        }

        /// <summary>
        /// 그리드 좌표를 월드 좌표로 변환합니다.
        /// Why: gridX, gridY는 이미 중앙 정렬된 좌표이므로 centerOffset을 빼면 안 됨!
        /// </summary>
        private Vector3 GridToWorldPosition(int gridX, int gridY, Vector2 localOffset)
        {
            int chunkSize = ChunkSize;

            // Why: 청크 중앙 위치 + 로컬 오프셋
            // gridX, gridY는 MapGenerator에서 이미 center를 뺀 값이므로 그대로 사용
            float worldX = (gridX * chunkSize) + (chunkSize / 2f) + localOffset.x;
            float worldZ = (gridY * chunkSize) + (chunkSize / 2f) + localOffset.y;
            float worldY = 1.5f; // 플레이어 높이

            Debug.Log($"[NetworkMapManager] GridToWorld: Grid({gridX},{gridY}) ChunkSize({chunkSize}) + Offset({localOffset.x:F2},{localOffset.y:F2}) -> World({worldX:F2},{worldY:F2},{worldZ:F2})");

            return new Vector3(worldX, worldY, worldZ);
        }

        /// <summary>
        /// 스폰 포인트가 없을 때 기본 스폰 위치를 반환합니다.
        /// </summary>
        private Vector3 GetDefaultSpawnPosition(int playerIndex)
        {
            // 8방향으로 분산 배치
            float radius = 10f;
            float angle = (playerIndex * 45f) * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;
            return new Vector3(x, 1.5f, z);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (!hasState || !HasStateAuthority)
                return;

            // Why: NetworkMapManager가 제거될 때 스폰된 LootBoxSpawnManager도 제거
            if (_spawnedLootBoxManager != null)
            {
                NetworkObject managerObj = _spawnedLootBoxManager.GetComponent<NetworkObject>();
                if (managerObj != null && managerObj.IsValid)
                {
                    runner.Despawn(managerObj);
                    Debug.Log("[NetworkMapManager] LootBoxSpawnManager 제거 완료");
                }
            }

            // Why: NetworkMapManager가 제거될 때 스폰된 OxygenDepletionManager도 제거
            if (_spawnedOxygenDepletionManager != null)
            {
                NetworkObject oxygenManagerObj = _spawnedOxygenDepletionManager.GetComponent<NetworkObject>();
                if (oxygenManagerObj != null && oxygenManagerObj.IsValid)
                {
                    runner.Despawn(oxygenManagerObj);
                    Debug.Log("[NetworkMapManager] OxygenDepletionManager 제거 완료");
                }
            }

            // Why: NetworkMapManager가 제거될 때 스폰된 MobSpawnManager도 제거
            if (_spawnedMobSpawnManager != null)
            {
                NetworkObject mobManagerObj = _spawnedMobSpawnManager.GetComponent<NetworkObject>();
                if (mobManagerObj != null && mobManagerObj.IsValid)
                {
                    runner.Despawn(mobManagerObj);
                    Debug.Log("[NetworkMapManager] MobSpawnManager 제거 완료");
                }
            }

            // Why: NetworkMapManager가 제거될 때 스폰된 NavMeshBaker도 제거
            if (_spawnedNavMeshBaker != null)
            {
                NetworkObject navMeshBakerObj = _spawnedNavMeshBaker.GetComponent<NetworkObject>();
                if (navMeshBakerObj != null && navMeshBakerObj.IsValid)
                {
                    runner.Despawn(navMeshBakerObj);
                    Debug.Log("[NetworkMapManager] NavMeshBaker 제거 완료");
                }
            }
        }
    }
}
