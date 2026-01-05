using UnityEngine;

/// <summary>
/// FOV 메시를 동적으로 생성합니다. RenderPass에 메시 데이터를 전달하여 Stencil 기반 렌더링을 지원합니다.
/// </summary>
public class FOVMeshGenerator
{
    #region Constants

    private const int MAX_SEGMENTS = 180;

    #endregion

    #region Private Fields

    private Mesh _mesh;
    private Vector3[] _vertices;
    private Vector2[] _uvs;
    private int[] _triangles;

    // GC 할당 방지를 위한 재사용 배열
    private Vector3[] _usedVertices;
    private Vector2[] _usedUVs;
    private int[] _usedTriangles;
    private int _lastVertexCount;
    private int _lastTriangleCount;

    #endregion

    #region Initialization

    public void Initialize(Transform parent, Material material)
    {
        _mesh = new Mesh
        {
            name = "FOV_Mesh"
        };
        _mesh.MarkDynamic();

        // 중심(1) + 내부 링(MAX+1) + 외부 링(MAX+1)
        int maxVertices = 1 + (MAX_SEGMENTS + 1) * 2;
        // 내부 삼각형(MAX) + 외부 링 삼각형(MAX*2)
        int maxTriangles = MAX_SEGMENTS * 3 + MAX_SEGMENTS * 6;

        _vertices = new Vector3[maxVertices];
        _uvs = new Vector2[maxVertices];
        _triangles = new int[maxTriangles];

        _usedVertices = new Vector3[maxVertices];
        _usedUVs = new Vector2[maxVertices];
        _usedTriangles = new int[maxTriangles];
    }

    #endregion

    #region Mesh Generation

    public void UpdateMesh(float[] hitDistances, float startAngle, float endAngle,
        Vector3 origin, Vector3 forward, bool enableSoftEdge = true, float edgeSoftness = 0.5f)
    {
        if (_mesh == null) return;

        int segmentCount = hitDistances.Length - 1;
        int vertexCount = 0;
        int triangleCount = 0;

        Quaternion rotation = Quaternion.LookRotation(forward);
        float angleStep = (endAngle - startAngle) / segmentCount;
        bool is360 = Mathf.Abs(endAngle - startAngle) >= 359f;

        // 중심점
        _vertices[vertexCount] = Vector3.zero;
        _uvs[vertexCount] = new Vector2(0f, 0.5f);
        int centerIndex = vertexCount++;

        // 내부 링 (Gradient 시작점)
        int innerRingStart = vertexCount;
        for (int i = 0; i <= segmentCount; i++)
        {
            float currentAngle = startAngle + (angleStep * i);
            Vector3 direction = rotation * Quaternion.Euler(0, currentAngle, 0) * Vector3.forward;
            float innerDistance = Mathf.Max(0.1f, hitDistances[i] - edgeSoftness);

            _vertices[vertexCount] = direction * innerDistance;
            _uvs[vertexCount] = new Vector2(0f, is360 ? 0.5f : (float)i / segmentCount);
            vertexCount++;
        }

        // 외부 링 (Gradient 끝점 = 장애물 위치)
        int outerRingStart = vertexCount;
        for (int i = 0; i <= segmentCount; i++)
        {
            float currentAngle = startAngle + (angleStep * i);
            Vector3 direction = rotation * Quaternion.Euler(0, currentAngle, 0) * Vector3.forward;

            _vertices[vertexCount] = direction * hitDistances[i];
            _uvs[vertexCount] = new Vector2(enableSoftEdge ? 1f : 0f, is360 ? 0.5f : (float)i / segmentCount);
            vertexCount++;
        }

        // 내부 삼각형 (중심 → 내부 링)
        for (int i = 0; i < segmentCount; i++)
        {
            _triangles[triangleCount++] = centerIndex;
            _triangles[triangleCount++] = innerRingStart + i;
            _triangles[triangleCount++] = innerRingStart + i + 1;
        }

        // 외부 링 삼각형 (내부 링 → 외부 링)
        for (int i = 0; i < segmentCount; i++)
        {
            int inner0 = innerRingStart + i;
            int inner1 = innerRingStart + i + 1;
            int outer0 = outerRingStart + i;
            int outer1 = outerRingStart + i + 1;

            _triangles[triangleCount++] = inner0;
            _triangles[triangleCount++] = outer0;
            _triangles[triangleCount++] = outer1;

            _triangles[triangleCount++] = inner0;
            _triangles[triangleCount++] = outer1;
            _triangles[triangleCount++] = inner1;
        }

        ApplyMesh(vertexCount, triangleCount);

        Matrix4x4 matrix = Matrix4x4.TRS(origin, Quaternion.identity, Vector3.one);
        FOVRenderPass.SetFOVMeshData(_mesh, matrix);
    }

    private void ApplyMesh(int vertexCount, int triangleCount)
    {
        // 배열 크기가 변경된 경우에만 재할당
        if (_lastVertexCount != vertexCount)
        {
            if (_usedVertices.Length < vertexCount)
            {
                _usedVertices = new Vector3[vertexCount];
                _usedUVs = new Vector2[vertexCount];
            }
            _lastVertexCount = vertexCount;
        }

        if (_lastTriangleCount != triangleCount)
        {
            if (_usedTriangles.Length < triangleCount)
            {
                _usedTriangles = new int[triangleCount];
            }
            _lastTriangleCount = triangleCount;
        }

        System.Array.Copy(_vertices, _usedVertices, vertexCount);
        System.Array.Copy(_uvs, _usedUVs, vertexCount);
        System.Array.Copy(_triangles, _usedTriangles, triangleCount);

        _mesh.Clear();
        _mesh.SetVertices(_usedVertices, 0, vertexCount);
        _mesh.SetUVs(0, _usedUVs, 0, vertexCount);
        _mesh.SetTriangles(_usedTriangles, 0, triangleCount, 0);
        _mesh.RecalculateBounds();
    }

    #endregion

    #region Public Methods

    public void Show()
    {
        // RenderPass 방식에서는 별도 처리 불필요
    }

    public void Hide()
    {
        FOVRenderPass.ClearFOVMeshData();
    }

    public void Destroy()
    {
        FOVRenderPass.ClearFOVMeshData();
        if (_mesh != null)
        {
            Object.Destroy(_mesh);
        }
    }

    #endregion
}
