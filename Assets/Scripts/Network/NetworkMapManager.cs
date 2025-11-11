using Fusion;
using UnityEngine;
using ProjectVoid.Map;

namespace ProjectVoid.Network
{
    public class NetworkMapManager : NetworkBehaviour
    {
        private const int MAX_CHUNK_CAPACITY = 500;
        private const int MAX_WALL_SEGMENT_CAPACITY = 800;

        [Header("맵 생성 설정")]
        [SerializeField] private MapGenerationSettings _mapSettings;

        [Networked] public int MapSeed { get; set; }
        [Networked] public NetworkBool IsMapReady { get; set; }
        [Networked, Capacity(MAX_CHUNK_CAPACITY)] public NetworkArray<NetworkChunkData> ChunkArray => default;
        [Networked] public int ChunkCount { get; set; }
        [Networked, Capacity(MAX_WALL_SEGMENT_CAPACITY)] public NetworkArray<WallSegmentData> WallSegmentArray => default;
        [Networked] public int WallSegmentCount { get; set; }

        private MapGenerator _mapGenerator;
        private bool _hasGeneratedMap;

        public override void Spawned()
        {
            if (HasStateAuthority)
            {
                GenerateMapOnServer();
            }
            else if (IsMapReady && ChunkCount > 0)
            {
                // Late Joiner: 이미 맵이 준비되어 있으면 즉시 렌더링
                OnMapDataReceived();
            }
            // 동시 접속자는 RPC_NotifyMapReady()를 받을 때까지 대기
        }

        private void GenerateMapOnServer()
        {
            if (_mapSettings == null)
            {
                Debug.LogError("[NetworkMapManager] MapGenerationSettings가 설정되지 않았습니다!");
                return;
            }

            MapSeed = _mapSettings.GenerateSeed();
            _mapGenerator = new MapGenerator(_mapSettings, this.transform);

            if (!_mapGenerator.GenerateMap(MapSeed))
            {
                Debug.LogError("[Server] 맵 생성 실패!");
                return;
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

            _mapGenerator = new MapGenerator(_mapSettings, this.transform);

            // 서버에서 보낸 벽 구간 정보 기반으로 렌더링
            if (!_mapGenerator.RenderMapFromNetworkData(networkChunks, wallSegments, MapSeed))
            {
                Debug.LogError("[Client] 맵 렌더링 실패!");
            }
        }

        public bool IsReady()
        {
            return IsMapReady && _hasGeneratedMap;
        }
    }
}
