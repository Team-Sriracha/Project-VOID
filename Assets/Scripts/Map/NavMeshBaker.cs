using FishNet.Object;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using ProjectVoid.Map;

/// <summary>
/// 런타임 NavMesh 베이킹
/// </summary>
public class NavMeshBaker : NetworkBehaviour
{
    #region Serialized Fields

    [Header("NavMesh 설정")]
    [SerializeField] private float _agentRadius = 0.5f;
    [SerializeField] private float _agentHeight = 2f;
    [SerializeField] private float _agentMaxSlope = 45f;
    [SerializeField] private float _agentMaxStepHeight = 0.4f;

    [Header("바닥 설정")]
    [SerializeField] private float _groundY = 0f;
    [SerializeField] private float _groundPadding = 10f;

    [Header("벽 설정")]
    [SerializeField] private bool _includeWalls = true;
    [SerializeField] private LayerMask _wallLayer;

    [Header("디버그")]
    [SerializeField] private bool _enableDebugLogs = true;

    #endregion

    #region Private Fields

    private NavMeshData _navMeshData;
    private NavMeshDataInstance _navMeshDataInstance;
    private bool _isNavMeshBaked = false;
    private Mesh _groundMesh;

    #endregion

    #region Properties

    public bool IsNavMeshBaked => _isNavMeshBaked;

    #endregion

    #region Fishnet Lifecycle

    public override void OnStopServer()
    {
        base.OnStopServer();
        ClearNavMesh();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 맵용 NavMesh 베이킹
    /// </summary>
    public void BakeNavMeshForMap(MapGenerator mapGenerator)
    {
        if (!IsServerInitialized)
        {
            _isNavMeshBaked = false; // 클라이언트는 베이킹 상태 false
            return;
        }


        if (mapGenerator == null)
        {
            Debug.LogError("[NavMeshBaker] MapGenerator가 null입니다!");
            return;
        }

        System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
        stopwatch.Start();

        ClearNavMesh();

        NavMeshBuildSettings buildSettings = NavMesh.GetSettingsByID(0);
        
        string[] validationErrors = buildSettings.ValidationReport(new Bounds(Vector3.zero, Vector3.one * 1000));
        if (validationErrors != null && validationErrors.Length > 0)
        {
            LogDebug($"NavMesh 설정 검증 실패, 직접 생성: {string.Join(", ", validationErrors)}");
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

        List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();

        int chunkSize = mapGenerator.Settings.ChunkSize;
        float mapWidth = mapGenerator.MapWidth * chunkSize;
        float mapHeight = mapGenerator.MapHeight * chunkSize;
        
        // Why: 맵 중심을 기준으로 Ground 생성 (0,0,0 하드코딩 대신)
        Vector3 mapCenter = mapGenerator.GetMapCenter();
        mapCenter.y = _groundY + 0.5f;

        float groundWidth = mapWidth + _groundPadding * 2;
        float groundDepth = mapHeight + _groundPadding * 2;
        Vector3 groundCenter = mapCenter;
        
        _groundMesh = CreatePlaneMesh(groundWidth, groundDepth);
        _groundMesh.name = "NavMesh_Ground_Plane";

        NavMeshBuildSource groundSource = new NavMeshBuildSource();
        groundSource.shape = NavMeshBuildSourceShape.Mesh;
        groundSource.sourceObject = _groundMesh;
        groundSource.transform = Matrix4x4.TRS(groundCenter, Quaternion.identity, Vector3.one);
        groundSource.area = 0;
        sources.Add(groundSource);

        int wallColliderCount = 0;

        if (_includeWalls)
        {
            HashSet<ChunkInstance> processedChunks = new HashSet<ChunkInstance>();

            foreach (var kvp in mapGenerator.PlacedChunks)
            {
                ChunkInstance chunk = kvp.Value;

                if (processedChunks.Contains(chunk))
                    continue;

                processedChunks.Add(chunk);

                Collider[] wallColliders = chunk.GetComponentsInChildren<Collider>();
                foreach (var collider in wallColliders)
                {
                    if ((_wallLayer.value & (1 << collider.gameObject.layer)) == 0)
                        continue;

                    NavMeshBuildSource wallSource = CreateNavMeshSourceFromCollider(collider);
                    if (wallSource.shape != NavMeshBuildSourceShape.Box && 
                        wallSource.shape != NavMeshBuildSourceShape.Mesh)
                        continue;

                    wallSource.area = 1;
                    sources.Add(wallSource);
                    wallColliderCount++;
                }
            }

            LogDebug($"벽 소스 추가: {wallColliderCount}개 벽 Collider 수집");
        }

        if (sources.Count == 0)
        {
            Debug.LogError("[NavMeshBaker] NavMesh 소스가 없습니다!");
            return;
        }

        Bounds totalBounds = new Bounds(
            groundCenter,
            new Vector3(groundWidth, _agentHeight * 2 + 10f, groundDepth)
        );

        _navMeshData = NavMeshBuilder.BuildNavMeshData(
            buildSettings,
            sources,
            totalBounds,
            Vector3.zero,
            Quaternion.identity
        );

        if (_navMeshData != null)
        {
            _navMeshDataInstance = NavMesh.AddNavMeshData(_navMeshData);
            _isNavMeshBaked = true;

            stopwatch.Stop();
            
            NavMeshTriangulation triangulation = NavMesh.CalculateTriangulation();
            LogDebug($"NavMesh 베이킹 완료! 소요 시간: {stopwatch.ElapsedMilliseconds}ms");
        }
        else
        {
            Debug.LogError("[NavMeshBaker] NavMesh 베이킹 실패!");
        }
    }

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

        if (_groundMesh != null)
        {
            Object.Destroy(_groundMesh);
            _groundMesh = null;
        }

        _isNavMeshBaked = false;
        LogDebug("NavMesh 데이터 제거됨.");
    }

    #endregion

    #region Helper Methods

    private void LogDebug(string message)
    {
        if (_enableDebugLogs)
        {
            Debug.Log($"[NavMeshBaker] {message}");
        }
    }

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
            source.shape = (NavMeshBuildSourceShape)(-1);
        }

        return source;
    }

    private Mesh CreatePlaneMesh(float width, float depth)
    {
        Mesh mesh = new Mesh();
        mesh.name = "NavMesh_Ground_Plane";

        float halfWidth = width / 2f;
        float halfDepth = depth / 2f;

        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-halfWidth, 0, -halfDepth),
            new Vector3(halfWidth, 0, -halfDepth),
            new Vector3(-halfWidth, 0, halfDepth),
            new Vector3(halfWidth, 0, halfDepth)
        };

        int[] triangles = new int[6] { 0, 2, 1, 2, 3, 1 };

        Vector3[] normals = new Vector3[4]
        {
            Vector3.up, Vector3.up, Vector3.up, Vector3.up
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;
        mesh.RecalculateBounds();

        return mesh;
    }

    #endregion
}
