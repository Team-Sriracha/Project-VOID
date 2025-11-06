using UnityEngine;

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
    private MeshRenderer _lineRenderer;
    private MeshRenderer _fanRenderer;

    #endregion

    #region Public Methods

    public void ShowAimIndicator(WeaponData weaponData, Vector3 aimDirection)
    {
        if (weaponData is GunData)
        {
            ShowLineIndicator(weaponData.Range, aimDirection);
            HideFanIndicator();
        }
        else if (weaponData is MeleeWeaponData meleeData)
        {
            ShowFanIndicator(meleeData.Range, meleeData.AttackAngle, aimDirection);
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
        float actualRange = range;
        Vector3 rayStart = startPos + Vector3.up * _raycastOffset;

        // Why: 장애물 감지 - 벽에 맞으면 거리 단축
        if (Physics.Raycast(rayStart, direction, out RaycastHit hit, range, _obstacleLayers))
        {
            float hitDistance = Vector3.Distance(startPos, hit.point);
            actualRange = Mathf.Min(range, hitDistance);
        }

        float halfRange = actualRange / 2f;
        _lineQuad.transform.position = startPos + direction * halfRange;
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
        _lineRenderer = _lineQuad.GetComponent<MeshRenderer>();
        _lineRenderer.material = _aimMaterial;
    }

    #endregion

    #region Fan Indicator (근거리)

    private void ShowFanIndicator(float range, float angle, Vector3 direction)
    {
        if (_fanMesh == null) CreateFanMesh();

        _fanMesh.SetActive(true);

        Vector3 startPos = _aimPoint.position + Vector3.up * _fanHeightOffset;
        int segments = _fanSegments;
        float[] segmentRanges = new float[segments + 1];
        float halfAngle = angle / 2f;
        Quaternion rotation = Quaternion.LookRotation(direction);
        Vector3 rayStart = startPos + Vector3.up * _raycastOffset;

        // Why: 각 세그먼트마다 개별 Raycast
        for (int i = 0; i <= segments; i++)
        {
            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
            Vector3 segmentDirection = rotation * Quaternion.Euler(0, currentAngle, 0) * Vector3.forward;

            // Why: 각 방향마다 Raycast로 장애물까지의 거리 계산
            if (Physics.Raycast(rayStart, segmentDirection, out RaycastHit hit, range, _obstacleLayers))
            {
                segmentRanges[i] = Vector3.Distance(startPos, hit.point);
            }
            else
            {
                segmentRanges[i] = range;
            }
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
        _fanRenderer = _fanMesh.AddComponent<MeshRenderer>();
        _fanRenderer.material = _aimMaterial;
    }

    private void UpdateFanMesh(float[] segmentRanges, float angle, Vector3 forward, Vector3 origin)
    {
        _fanMesh.transform.position = origin;
        _fanMesh.transform.rotation = Quaternion.identity;

        Mesh mesh = new Mesh();
        int segments = segmentRanges.Length - 1;
        Vector3[] vertices = new Vector3[segments + 2];
        int[] triangles = new int[segments * 3];
        Vector2[] uvs = new Vector2[segments + 2];

        // Why: 중심점은 로컬 좌표 (0, 0, 0)
        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0);

        float halfAngle = angle / 2f;
        Quaternion rotation = Quaternion.LookRotation(forward);

        // Why: 각 세그먼트의 정점을 로컬 좌표로 생성
        for (int i = 0; i <= segments; i++)
        {
            float currentAngle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / segments);
            Vector3 direction = rotation * Quaternion.Euler(0, currentAngle, 0) * Vector3.forward;
            vertices[i + 1] = direction * segmentRanges[i];
            uvs[i + 1] = new Vector2((float)i / segments, 1);
        }

        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();

        _fanMeshFilter.mesh = mesh;
    }

    #endregion
}
