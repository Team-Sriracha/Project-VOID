using UnityEngine;
using FishNet.Object;

/// <summary>
/// 조준선을 시각화합니다.
/// 세그먼트별 장애물 감지를 구현합니다.
/// </summary>
public class AimVisualizer : MonoBehaviour
{
    #region Serialized Fields

    [Header("조준선 설정")]
    [SerializeField] private Material _aimMaterial;
    [SerializeField] private Transform _aimPoint;
    [SerializeField] private float _lineWidth = 0.2f;
    [SerializeField] private float _lineHeightOffset = 0.1f;

    [Header("부채꼴 설정")]
    [SerializeField] private int _fanSegments = 15;
    [SerializeField] private float _fanHeightOffset = 0.1f;

    [Header("장애물 감지")]
    [SerializeField] private LayerMask _obstacleLayers;
    [SerializeField] private float _raycastOffset = 0.5f;

    #endregion

    #region Private Fields

    private GameObject _lineQuad;
    private GameObject _fanMesh;
    private MeshFilter _fanMeshFilter;

    #endregion

    #region Properties

    /// <summary>
    /// AimPoint Transform을 반환합니다.
    /// </summary>
    public Transform AimPoint => _aimPoint;

    #endregion

    #region Raycast Helper

    /// <summary>
    /// Raycast를 수행합니다.
    /// </summary>
    private bool DoRaycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance, LayerMask layerMask)
    {
        // Fishnet: 세션당 별도 프로세스이므로 기본 Physics 사용
        return Physics.Raycast(origin, direction, out hit, maxDistance, layerMask);
    }

    #endregion

    #region Public Methods

    public void ShowAimIndicator(WeaponData weaponData, Vector3 aimDirection)
    {
        // AimPoint의 회전을 반영한 방향 사용
        Vector3 direction = _aimPoint.forward;

        if (weaponData is GunData gunData)
        {
            ShowLineIndicator(weaponData.Range, direction);
            HideFanIndicator();
        }
        else if (weaponData is MeleeWeaponData meleeData)
        {
            ShowFanIndicator(meleeData.Range, meleeData.AttackAngle, direction);
            HideLineIndicator();
        }
    }

    public void HideAimIndicator()
    {
        HideLineIndicator();
        HideFanIndicator();
    }

    #endregion

    #region Line Indicator (원거리)

    private void ShowLineIndicator(float range, Vector3 direction)
    {
        if (_lineQuad == null) CreateLineQuad();

        _lineQuad.SetActive(true);

        Vector3 startPos = _aimPoint.position + Vector3.up * _lineHeightOffset;
        Vector3 rayStart = startPos + Vector3.up * _raycastOffset;

        // 장애물 감지 - 벽에 맞으면 거리 단축
        float actualRange = range;
        if (DoRaycast(rayStart, direction, out RaycastHit hit, range, _obstacleLayers))
        {
            actualRange = Mathf.Min(range, Vector3.Distance(startPos, hit.point));
        }

        // 조준선을 중앙 위치에 배치 (Quad 피벗이 중앙)
        _lineQuad.transform.position = startPos + direction * (actualRange * 0.5f);
        _lineQuad.transform.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(90, 0, 0);
        _lineQuad.transform.localScale = new Vector3(_lineWidth, actualRange, 1);
    }

    private void HideLineIndicator()
    {
        if (_lineQuad != null) _lineQuad.SetActive(false);
    }

    private void CreateLineQuad()
    {
        _lineQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _lineQuad.name = "AimLine";
        _lineQuad.transform.SetParent(transform);
        Destroy(_lineQuad.GetComponent<Collider>());
        _lineQuad.GetComponent<MeshRenderer>().material = _aimMaterial;
    }

    #endregion

    #region Fan Indicator (근거리)

    private void ShowFanIndicator(float range, float angle, Vector3 direction)
    {
        if (_fanMesh == null) CreateFanMesh();

        _fanMesh.SetActive(true);

        Vector3 startPos = _aimPoint.position + Vector3.up * _fanHeightOffset;
        Vector3 rayStart = startPos + Vector3.up * _raycastOffset;
        float[] segmentRanges = new float[_fanSegments + 1];
        float halfAngle = angle * 0.5f;
        Quaternion rotation = Quaternion.LookRotation(direction);

        // 각 세그먼트마다 개별 레이캐스트로 장애물까지의 거리 계산
        for (int i = 0; i <= _fanSegments; i++)
        {
            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / _fanSegments);
            Vector3 segmentDirection = rotation * Quaternion.Euler(0, currentAngle, 0) * Vector3.forward;

            segmentRanges[i] = DoRaycast(rayStart, segmentDirection, out RaycastHit hit, range, _obstacleLayers)
                ? Vector3.Distance(startPos, hit.point)
                : range;
        }

        UpdateFanMesh(segmentRanges, angle, direction, startPos);
    }

    private void HideFanIndicator()
    {
        if (_fanMesh != null) _fanMesh.SetActive(false);
    }

    private void CreateFanMesh()
    {
        _fanMesh = new GameObject("AimFan");
        _fanMesh.transform.SetParent(transform);
        _fanMeshFilter = _fanMesh.AddComponent<MeshFilter>();
        _fanMesh.AddComponent<MeshRenderer>().material = _aimMaterial;
    }

    private void UpdateFanMesh(float[] segmentRanges, float angle, Vector3 forward, Vector3 origin)
    {
        _fanMesh.transform.SetPositionAndRotation(origin, Quaternion.identity);

        int segments = segmentRanges.Length - 1;
        Vector3[] vertices = new Vector3[segments + 2];
        int[] triangles = new int[segments * 3];
        Vector2[] uvs = new Vector2[segments + 2];

        // 중심점 (부채꼴의 꼭짓점)
        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0);

        float halfAngle = angle * 0.5f;
        Quaternion rotation = Quaternion.LookRotation(forward);

        // 각 세그먼트의 끝점 정점 생성
        for (int i = 0; i <= segments; i++)
        {
            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
            Vector3 direction = rotation * Quaternion.Euler(0, currentAngle, 0) * Vector3.forward;
            vertices[i + 1] = direction * segmentRanges[i];
            uvs[i + 1] = new Vector2((float)i / segments, 1);
        }

        // 삼각형 생성 (중심점 → 현재 끝점 → 다음 끝점)
        for (int i = 0; i < segments; i++)
        {
            int baseIndex = i * 3;
            triangles[baseIndex] = 0;
            triangles[baseIndex + 1] = i + 1;
            triangles[baseIndex + 2] = i + 2;
        }

        Mesh mesh = new Mesh
        {
            vertices = vertices,
            triangles = triangles,
            uv = uvs
        };
        mesh.RecalculateNormals();

        _fanMeshFilter.mesh = mesh;
    }

    #endregion
}
