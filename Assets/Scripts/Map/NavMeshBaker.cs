using Fusion;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ProjectVoid.Map;

/// <summary>
/// 런타임에 NavMesh를 베이킹합니다.
/// Ground 오브젝트(바닥)와 청크의 벽 Collider를 사용합니다.
/// 맵 생성 완료 후 호출하여 몹이 길찾기를 할 수 있도록 합니다.
/// </summary>
public class NavMeshBaker : NetworkBehaviour
{
    #region Serialized Fields

    [Header("NavMesh 설정")]
    [Tooltip("에이전트 반지름")]
    [SerializeField] private float _agentRadius = 0.5f;

    [Tooltip("에이전트 높이")]
    [SerializeField] private float _agentHeight = 2f;

    [Tooltip("에이전트가 오를 수 있는 최대 경사")]
    [SerializeField] private float _agentMaxSlope = 45f;

    [Tooltip("에이전트가 오를 수 있는 최대 계단 높이")]
    [SerializeField] private float _agentMaxStepHeight = 0.4f;

    [Header("바닥 설정")]
    [Tooltip("바닥 Y 좌표 (NavMesh가 생성될 높이)")]
    [SerializeField] private float _groundY = 0f;

    [Tooltip("바닥 여유 공간 (맵 바운드에 추가)")]
    [SerializeField] private float _groundPadding = 10f;

    [Header("벽 설정")]
    [Tooltip("벽 Collider를 NavMesh 장애물로 사용할지 여부 (디버그용)")]
    [SerializeField] private bool _includeWalls = true;

    [Tooltip("벽으로 인식할 레이어")]
    [SerializeField] private LayerMask _wallLayer;

    [Header("디버그")]
    [Tooltip("베이킹 로그 출력")]
    [SerializeField] private bool _enableDebugLogs = true;

    #endregion

    #region Private Fields

    private NavMeshData _navMeshData;
    private NavMeshDataInstance _navMeshDataInstance;
    private bool _isNavMeshBaked = false;
    private Mesh _groundMesh; // 런타임 생성 바닥 메시 (NavMesh용)

    #endregion

    #region Properties

    /// <summary>
    /// NavMesh가 베이킹되었는지 여부를 반환합니다.
    /// </summary>
    public bool IsNavMeshBaked => _isNavMeshBaked;

    #endregion

    #region Fusion Lifecycle

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        ClearNavMesh();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 맵 전체에 대한 NavMesh를 베이킹합니다.
    /// 서버에서만 실행됩니다 (몬스터 AI는 서버에서만 동작).
    /// </summary>
    /// <param name="mapGenerator">맵 생성기 인스턴스</param>
    public void BakeNavMeshForMap(MapGenerator mapGenerator)
    {
        // Why: NavMesh는 서버에서만 필요 (몬스터 AI가 서버에서만 실행됨)
        if (Runner != null && !Runner.IsServer)
        {
            LogDebug("클라이언트에서는 NavMesh를 베이킹하지 않습니다.");
            return;
        }

        if (mapGenerator == null)
        {
            Debug.LogError("[NavMeshBaker] MapGenerator가 null입니다!");
            return;
        }

        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();

        // Why: 기존 NavMesh 데이터 제거
        ClearNavMesh();

        // Why: NavMesh 빌드 설정 - GetSettingsByID(0)가 실패할 수 있으므로 직접 설정
        NavMeshBuildSettings buildSettings = NavMesh.GetSettingsByID(0);
        
        // Why: Unity 6에서는 기본 Agent가 없을 수 있으므로 검증
        string[] validationErrors = buildSettings.ValidationReport(new Bounds(Vector3.zero, Vector3.one * 1000));
        if (validationErrors != null && validationErrors.Length > 0)
        {
            LogDebug($"NavMesh 설정 검증 실패, 직접 생성: {string.Join(", ", validationErrors)}");
            // Why: 기본 설정으로 직접 생성
            buildSettings = new NavMeshBuildSettings
            {
                agentTypeID = 0,
                agentRadius = _agentRadius,
                agentHeight = _agentHeight,
                agentSlope = _agentMaxSlope,
                agentClimb = _agentMaxStepHeight,
                minRegionArea = 0.1f,
                overrideVoxelSize = true,
                voxelSize = _agentRadius / 3f,
                overrideTileSize = false,
                tileSize = 256
            };
        }
        else
        {
            buildSettings.agentRadius = _agentRadius;
            buildSettings.agentHeight = _agentHeight;
            buildSettings.agentSlope = _agentMaxSlope;
            buildSettings.agentClimb = _agentMaxStepHeight;
        }
        
        LogDebug($"NavMesh 빌드 설정: Radius={buildSettings.agentRadius}, Height={buildSettings.agentHeight}, VoxelSize={buildSettings.voxelSize}");

        List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();

        // ========== 1. 맵 크기 계산 (MapLayout 기반) ==========
        // Why: MapLayout의 Width/Height와 ChunkSize를 사용하여 Ground 크기 결정
        int chunkSize = mapGenerator.Settings.ChunkSize;
        float mapWidth = mapGenerator.MapWidth * chunkSize;
        float mapHeight = mapGenerator.MapHeight * chunkSize;
        
        // Why: 맵 중심 계산 (청크 그리드는 중앙 기준으로 배치됨)
        Vector3 mapCenter = new Vector3(0f, _groundY + 0.5f, 0f);
        
        LogDebug($"맵 크기: {mapGenerator.MapWidth}x{mapGenerator.MapHeight} 청크, {mapWidth}x{mapHeight} 유닛");

        // ========== 2. 바닥 Mesh 소스 생성 (MapLayout 크기 기반) ==========
        // Why: Box shape는 장애물로만 인식됨. Mesh를 생성해서 walkable 표면으로 사용
        float groundWidth = mapWidth + _groundPadding * 2;
        float groundDepth = mapHeight + _groundPadding * 2;
        Vector3 groundCenter = mapCenter;
        
        // Why: NavMesh용 Mesh 생성 (Ground GameObject는 NetworkMapManager에서 생성)
        _groundMesh = CreatePlaneMesh(groundWidth, groundDepth);
        _groundMesh.name = "NavMesh_Ground_Plane";

        NavMeshBuildSource groundSource = new NavMeshBuildSource();
        groundSource.shape = NavMeshBuildSourceShape.Mesh;
        groundSource.sourceObject = _groundMesh;
        groundSource.transform = Matrix4x4.TRS(groundCenter, Quaternion.identity, Vector3.one);
        groundSource.area = 0; // Walkable
        sources.Add(groundSource);
        LogDebug($"NavMesh Ground Mesh 생성: Size=({groundWidth}, {groundDepth}), Position={groundCenter}");

        // ========== 3. 청크의 벽 Collider 소스 추가 ==========
        int wallSourceCount = 0;
        int wallColliderCount = 0;

        if (_includeWalls)
        {
            HashSet<ChunkInstance> processedChunks = new HashSet<ChunkInstance>();

            foreach (var kvp in mapGenerator.PlacedChunks)
            {
                ChunkInstance chunk = kvp.Value;

                // Why: 그룹 청크는 여러 칸에 같은 ChunkInstance가 있으므로 한 번만 처리
                if (processedChunks.Contains(chunk))
                    continue;

                processedChunks.Add(chunk);

                // Why: Wall 레이어에 해당하는 자식 오브젝트의 Collider만 수집
                Collider[] wallColliders = chunk.GetComponentsInChildren<Collider>();
                foreach (var collider in wallColliders)
                {
                    // Why: 벽 레이어만 필터링
                    if ((_wallLayer.value & (1 << collider.gameObject.layer)) == 0)
                        continue;

                    NavMeshBuildSource wallSource = CreateNavMeshSourceFromCollider(collider);
                    if (wallSource.shape != NavMeshBuildSourceShape.Box && 
                        wallSource.shape != NavMeshBuildSourceShape.Mesh)
                        continue;

                    wallSource.area = 1; // Not Walkable (장애물)
                    sources.Add(wallSource);
                    wallColliderCount++;
                }
                wallSourceCount++;
            }

            LogDebug($"벽 소스 추가: {wallSourceCount}개 청크에서 {wallColliderCount}개 벽 Collider 수집");
        }
        else
        {
            LogDebug("벽 소스 수집 비활성화됨 (_includeWalls = false)");
        }

        if (sources.Count == 0)
        {
            Debug.LogError("[NavMeshBaker] NavMesh 소스가 없습니다!");
            return;
        }

        // Why: 전체 바운드 설정 (Ground 크기 기반)
        Bounds totalBounds = new Bounds(
            groundCenter,
            new Vector3(groundWidth, _agentHeight * 2 + 10f, groundDepth)
        );

        LogDebug($"NavMesh 베이킹 시작. 총 소스: {sources.Count}개, 바운드: {totalBounds}");

        // Why: NavMesh 베이킹
        _navMeshData = NavMeshBuilder.BuildNavMeshData(
            buildSettings,
            sources,
            totalBounds,
            Vector3.zero,
            Quaternion.identity
        );

        if (_navMeshData != null)
        {
            // Why: NavMesh 데이터 등록
            _navMeshDataInstance = NavMesh.AddNavMeshData(_navMeshData);
            _isNavMeshBaked = true;

            stopwatch.Stop();
            
            // Why: 결과 확인
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            LogDebug($"NavMesh 베이킹 완료! 소요 시간: {stopwatch.ElapsedMilliseconds}ms, 버텍스: {triangulation.vertices.Length}, 삼각형: {triangulation.indices.Length / 3}");
        }
        else
        {
            Debug.LogError("[NavMeshBaker] NavMesh 베이킹 실패!");
        }
    }

    /// <summary>
    /// 기존 NavMesh 데이터를 제거합니다.
    /// </summary>
    public void ClearNavMesh()
    {
        if (_navMeshDataInstance.valid)
        {
            NavMesh.RemoveNavMeshData(_navMeshDataInstance);
        }

        if (_navMeshData != null)
        {
            _navMeshData = null;
        }

        // Why: 런타임 생성 메시 정리
        if (_groundMesh != null)
        {
            UnityEngine.Object.Destroy(_groundMesh);
            _groundMesh = null;
        }

        _isNavMeshBaked = false;

        LogDebug("NavMesh 데이터 제거됨.");
    }

    #endregion

    #region Debug Methods

    /// <summary>
    /// 특정 위치에서 NavMesh가 유효한지 테스트합니다.
    /// </summary>
    /// <param name="position">테스트할 월드 좌표</param>
    /// <param name="maxDistance">NavMesh 검색 거리</param>
    /// <returns>해당 위치에서 NavMesh가 유효한지 여부</returns>
    public bool TestNavMeshAtPosition(Vector3 position, float maxDistance = 2f)
    {
        if (!_isNavMeshBaked)
        {
            Debug.LogWarning("[NavMeshBaker] NavMesh가 아직 베이킹되지 않았습니다.");
            return false;
        }

        NavMeshHit hit;
        bool isValid = NavMesh.SamplePosition(position, out hit, maxDistance, NavMesh.AllAreas);

        if (_enableDebugLogs)
        {
            if (isValid)
            {
                Debug.Log($"[NavMeshBaker] 위치 {position}에서 NavMesh 유효. 가장 가까운 점: {hit.position}, 거리: {hit.distance}");
            }
            else
            {
                Debug.LogWarning($"[NavMeshBaker] 위치 {position}에서 NavMesh를 찾을 수 없음 (검색 거리: {maxDistance})");
            }
        }

        return isValid;
    }

    /// <summary>
    /// 두 위치 사이에 경로가 존재하는지 테스트합니다.
    /// </summary>
    public bool TestPathBetween(Vector3 from, Vector3 to)
    {
        if (!_isNavMeshBaked)
        {
            Debug.LogWarning("[NavMeshBaker] NavMesh가 아직 베이킹되지 않았습니다.");
            return false;
        }

        NavMeshPath path = new NavMeshPath();
        bool hasPath = NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path);

        if (_enableDebugLogs)
        {
            Debug.Log($"[NavMeshBaker] 경로 테스트 {from} → {to}: {(hasPath && path.status == NavMeshPathStatus.PathComplete ? "성공" : "실패")} (상태: {path.status})");
        }

        return hasPath && path.status == NavMeshPathStatus.PathComplete;
    }

    /// <summary>
    /// NavMesh 베이킹 상태를 콘솔에 출력합니다.
    /// </summary>
    [ContextMenu("Print NavMesh Status")]
    public void PrintNavMeshStatus()
    {
        Debug.Log("========== NavMesh 상태 ==========");
        Debug.Log($"베이킹 완료: {_isNavMeshBaked}");
        Debug.Log($"NavMeshData 유효: {_navMeshData != null}");
        Debug.Log($"NavMeshDataInstance 유효: {_navMeshDataInstance.valid}");

        // NavMesh 삼각형 정보
        NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
        Debug.Log($"NavMesh 버텍스 수: {triangulation.vertices.Length}");
        Debug.Log($"NavMesh 삼각형 수: {triangulation.indices.Length / 3}");
        Debug.Log("===================================");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 맵 전체의 바운드를 계산합니다.
    /// </summary>
    /// <param name="mapGenerator">맵 생성기</param>
    /// <returns>맵 전체 바운드</returns>
    private Bounds CalculateMapBounds(MapGenerator mapGenerator)
    {
        if (mapGenerator.PlacedChunks == null || mapGenerator.PlacedChunks.Count == 0)
        {
            Debug.LogWarning("[NavMeshBaker] PlacedChunks가 비어있습니다. 기본 바운드 사용.");
            return new Bounds(Vector3.zero, new Vector3(100f, 10f, 100f));
        }

        Bounds bounds = new Bounds();
        bool initialized = false;

        foreach (var kvp in mapGenerator.PlacedChunks)
        {
            ChunkInstance chunk = kvp.Value;
            Bounds chunkBounds = CalculateChunkBounds(chunk);

            if (!initialized)
            {
                bounds = chunkBounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(chunkBounds);
            }
        }

        return bounds;
    }

    /// <summary>
    /// 청크의 바운드를 계산합니다.
    /// </summary>
    /// <param name="chunk">청크 인스턴스</param>
    /// <returns>청크의 바운드</returns>
    private Bounds CalculateChunkBounds(ChunkInstance chunk)
    {
        Bounds bounds = new Bounds(chunk.transform.position, Vector3.zero);

        // Why: 청크 내 모든 Renderer의 바운드 합산
        Renderer[] renderers = chunk.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }

        // Why: Renderer가 없으면 Collider로 계산
        if (renderers.Length == 0)
        {
            Collider[] colliders = chunk.GetComponentsInChildren<Collider>();
            foreach (var col in colliders)
            {
                bounds.Encapsulate(col.bounds);
            }
        }

        return bounds;
    }

    private void LogDebug(string message)
    {
        if (_enableDebugLogs)
        {
            Debug.Log($"[NavMeshBaker] {message}");
        }
    }

    /// <summary>
    /// Collider에서 NavMeshBuildSource를 생성합니다.
    /// </summary>
    /// <param name="collider">변환할 Collider</param>
    /// <returns>생성된 NavMeshBuildSource</returns>
    private NavMeshBuildSource CreateNavMeshSourceFromCollider(Collider collider)
    {
        NavMeshBuildSource source = new NavMeshBuildSource();

        if (collider is BoxCollider box)
        {
            source.shape = NavMeshBuildSourceShape.Box;
            source.size = box.size;
            source.transform = Matrix4x4.TRS(
                collider.transform.TransformPoint(box.center),
                collider.transform.rotation,
                collider.transform.lossyScale
            );
        }
        else if (collider is SphereCollider sphere)
        {
            // Why: Sphere는 Box로 근사
            source.shape = NavMeshBuildSourceShape.Box;
            float diameter = sphere.radius * 2f;
            source.size = new Vector3(diameter, diameter, diameter);
            source.transform = Matrix4x4.TRS(
                collider.transform.TransformPoint(sphere.center),
                collider.transform.rotation,
                collider.transform.lossyScale
            );
        }
        else if (collider is CapsuleCollider capsule)
        {
            // Why: Capsule은 Box로 근사
            source.shape = NavMeshBuildSourceShape.Box;
            float diameter = capsule.radius * 2f;
            float height = capsule.height;
            source.size = new Vector3(diameter, height, diameter);
            source.transform = Matrix4x4.TRS(
                collider.transform.TransformPoint(capsule.center),
                collider.transform.rotation,
                collider.transform.lossyScale
            );
        }
        else if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
        {
            source.shape = NavMeshBuildSourceShape.Mesh;
            source.sourceObject = meshCollider.sharedMesh;
            source.transform = collider.transform.localToWorldMatrix;
        }
        else
        {
            // Why: 지원하지 않는 Collider 타입
            source.shape = (NavMeshBuildSourceShape)(-1); // Invalid
        }

        return source;
    }

    /// <summary>
    /// 런타임에 평면 메시를 생성합니다.
    /// </summary>
    /// <param name="width">X축 크기</param>
    /// <param name="depth">Z축 크기</param>
    /// <returns>생성된 평면 메시</returns>
    private Mesh CreatePlaneMesh(float width, float depth)
    {
        Mesh mesh = new Mesh();
        mesh.name = "NavMesh_Ground_Plane";

        float halfWidth = width / 2f;
        float halfDepth = depth / 2f;

        // Why: 평면의 4개 꼭짓점 (Y=0, XZ 평면)
        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-halfWidth, 0, -halfDepth), // 좌하단
            new Vector3(halfWidth, 0, -halfDepth),  // 우하단
            new Vector3(-halfWidth, 0, halfDepth),  // 좌상단
            new Vector3(halfWidth, 0, halfDepth)    // 우상단
        };

        // Why: 삼각형 인덱스 (시계 방향으로 위를 향함)
        int[] triangles = new int[6]
        {
            0, 2, 1, // 첫 번째 삼각형
            2, 3, 1  // 두 번째 삼각형
        };

        // Why: 노멀 (위쪽 방향)
        Vector3[] normals = new Vector3[4]
        {
            Vector3.up,
            Vector3.up,
            Vector3.up,
            Vector3.up
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;
        mesh.RecalculateBounds();

        return mesh;
    }

    #endregion
}
