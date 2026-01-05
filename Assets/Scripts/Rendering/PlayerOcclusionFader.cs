using System.Collections.Generic;
using Fusion;
using ProjectVoid.Map;
using UnityEngine;

/// <summary>
/// 카메라와 로컬 플레이어 사이의 장애물을 감지하여 투명하게 만듭니다.
/// - 북쪽 벽: 플레이어 청크의 X 범위 내 벽만 투명화
/// - 동/서쪽 벽, 일반 오브젝트: 해당 오브젝트만 투명화
/// </summary>
public class PlayerOcclusionFader : NetworkBehaviour
{
    #region Serialized Fields

    [Header("레이캐스트 설정")]
    [Tooltip("감지할 레이어 (벽, 장애물 등)")]
    [SerializeField] private LayerMask _occluderLayers;

    [Tooltip("레이캐스트 체크 빈도 (초당 횟수)")]
    [SerializeField] private float _checkFrequency = 30f;

    [Tooltip("플레이어 중심점 오프셋 (Y 높이)")]
    [SerializeField] private float _playerHeightOffset = 1f;

    [Header("청크 설정")]
    [Tooltip("청크 사이즈 (MapGenerationSettings의 ChunkSize와 동일하게)")]
    [SerializeField] private float _chunkSize = 30f;

    [Header("투명도 설정")]
    [Tooltip("시작 투명도 (1 = 불투명에서 시작, 낮을수록 바로 투명)")]
    [Range(0f, 1f)]
    [SerializeField] private float _startAlpha = 0.9f;

    [Tooltip("목표 투명도 (0 = 완전 투명, 1 = 불투명)")]
    [Range(0f, 1f)]
    [SerializeField] private float _fadedAlpha = 0.3f;

    [Tooltip("페이드 전환 속도")]
    [SerializeField] private float _fadeSpeed = 8f;

    #endregion

    #region Private Fields

    private Camera _mainCamera;
    private float _checkTimer;
    private ChunkInstance _playerChunk;
    private float _playerChunkMinX;
    private float _playerChunkMaxX;
    
    // 게임 시작 시 한 번만 캐싱 (모든 내부 북쪽 벽과 X 좌표)
    private readonly List<(Renderer renderer, float posX)> _allInternalNorthWalls = new List<(Renderer, float)>();
    private bool _isWallDataCached = false;
    
    // 플레이어 X 범위 내 벽만 필터링 (플레이어 청크 변경 시 업데이트)
    private readonly List<Renderer> _cachedNorthWallRenderers = new List<Renderer>();
    
    private readonly Dictionary<Renderer, MaterialData[]> _fadedRenderers = new Dictionary<Renderer, MaterialData[]>();
    private readonly HashSet<Renderer> _renderersToFadeThisFrame = new HashSet<Renderer>();
    private readonly RaycastHit[] _hitResults = new RaycastHit[20];

    #endregion

    #region Data Structures

    private struct MaterialData
    {
        public Material OriginalMaterial;
        public Material FadedMaterial;
        public float CurrentAlpha;
    }

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        if (!HasInputAuthority)
        {
            enabled = false;
            return;
        }

        _mainCamera = Camera.main;
    }

    #endregion

    #region Unity Lifecycle

    private void LateUpdate()
    {
        if (!HasInputAuthority) return;

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
        }

        _checkTimer += Time.deltaTime;
        if (_checkTimer < 1f / _checkFrequency) return;
        _checkTimer = 0f;

        // 플레이어 청크 업데이트
        UpdatePlayerChunk();
        
        PerformOcclusionCheck();
        UpdateFading();
    }

    private void OnDestroy()
    {
        foreach (var kvp in _fadedRenderers)
        {
            foreach (var data in kvp.Value)
            {
                if (data.FadedMaterial != null)
                    Destroy(data.FadedMaterial);
            }
        }
        _fadedRenderers.Clear();
    }

    #endregion

    #region Player Chunk Detection

    /// <summary>
    /// 플레이어가 있는 청크를 찾고 X 범위를 계산합니다.
    /// </summary>
    private void UpdatePlayerChunk()
    {
        Vector3 playerPos = transform.position;
        
        // 모든 청크 중에서 플레이어 위치를 포함하는 청크 찾기
        var allChunks = FindObjectsOfType<ChunkInstance>();
        
        foreach (var chunk in allChunks)
        {
            if (IsPlayerInChunk(playerPos, chunk))
            {
                if (_playerChunk != chunk)
                {
                    _playerChunk = chunk;
                    CalculateChunkXRange(chunk);
                }
                return;
            }
        }
        
        // 청크를 찾지 못한 경우 기본값 사용
        _playerChunk = null;
        _playerChunkMinX = playerPos.x - _chunkSize / 2f;
        _playerChunkMaxX = playerPos.x + _chunkSize / 2f;
    }

    /// <summary>
    /// 플레이어가 해당 청크 안에 있는지 확인합니다.
    /// </summary>
    private bool IsPlayerInChunk(Vector3 playerPos, ChunkInstance chunk)
    {
        Vector3 chunkPos = chunk.transform.position;
        int width = chunk.ChunkData?.ChunkWidth ?? 1;
        int height = chunk.ChunkData?.ChunkHeight ?? 1;
        
        float minX = chunkPos.x;
        float maxX = chunkPos.x + width * _chunkSize;
        float minZ = chunkPos.z;
        float maxZ = chunkPos.z + height * _chunkSize;
        
        return playerPos.x >= minX && playerPos.x <= maxX &&
               playerPos.z >= minZ && playerPos.z <= maxZ;
    }

    /// <summary>
    /// 청크의 X 범위를 계산하고 해당 범위 내 북쪽 벽 렌더러를 필터링합니다.
    /// </summary>
    private void CalculateChunkXRange(ChunkInstance chunk)
    {
        Vector3 chunkPos = chunk.transform.position;
        int width = chunk.ChunkData?.ChunkWidth ?? 1;
        
        _playerChunkMinX = chunkPos.x;
        _playerChunkMaxX = chunkPos.x + width * _chunkSize;
        
        // 캐싱된 데이터에서 X 범위만 필터링 (O(n) 단순 필터링)
        FilterNorthWallsByPlayerXRange();
    }

    /// <summary>
    /// 게임 시작 시 모든 북쪽 벽 데이터를 한 번만 캐싱합니다.
    /// 레이캐스트에서 감지된 벽 기준으로 같은 Z의 벽만 투명화하므로 경계 체크 불필요.
    /// </summary>
    private void CacheAllNorthWalls()
    {
        if (_isWallDataCached) return;
        
        _allInternalNorthWalls.Clear();
        
        var allChunks = FindObjectsOfType<ChunkInstance>();
        
        foreach (var chunk in allChunks)
        {
            var allChildren = chunk.GetComponentsInChildren<Transform>(true);
            foreach (var child in allChildren)
            {
                if (child.name.Contains("North"))
                {
                    float wallX = child.position.x;
                    
                    var renderer = child.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        _allInternalNorthWalls.Add((renderer, wallX));
                    }
                    
                    var childRenderers = child.GetComponentsInChildren<Renderer>(true);
                    foreach (var r in childRenderers)
                    {
                        if (!_allInternalNorthWalls.Exists(item => item.renderer == r))
                            _allInternalNorthWalls.Add((r, wallX));
                    }
                }
            }
        }
        
        _isWallDataCached = true;
    }
    
    /// <summary>
    /// 플레이어 X 범위 내 북쪽 벽만 필터링합니다. (O(n) 단순 필터링)
    /// </summary>
    private void FilterNorthWallsByPlayerXRange()
    {
        // 아직 캐싱 안됐으면 캐싱
        if (!_isWallDataCached)
        {
            CacheAllNorthWalls();
        }
        
        _cachedNorthWallRenderers.Clear();
        
        foreach (var (renderer, posX) in _allInternalNorthWalls)
        {
            if (renderer != null && posX >= _playerChunkMinX && posX <= _playerChunkMaxX)
            {
                _cachedNorthWallRenderers.Add(renderer);
            }
        }
    }

    #endregion

    #region Occlusion Check

    private void PerformOcclusionCheck()
    {
        _renderersToFadeThisFrame.Clear();

        Vector3 playerCenter = transform.position + Vector3.up * _playerHeightOffset;
        Vector3 cameraPos = _mainCamera.transform.position;
        Vector3 direction = playerCenter - cameraPos;
        float distance = direction.magnitude;

        PhysicsScene physicsScene = Runner != null ? Runner.GetPhysicsScene() : Physics.defaultPhysicsScene;

        int hitCount = physicsScene.Raycast(
            cameraPos,
            direction.normalized,
            _hitResults,
            distance,
            _occluderLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            var hit = _hitResults[i];
            GameObject hitObject = hit.collider.gameObject;

            // 북쪽 벽인지 확인
            if (IsNorthWall(hitObject))
            {
                // 히트된 벽의 Z 위치 기준으로 같은 Z의 북쪽 벽들만 투명화
                float hitWallZ = hitObject.transform.position.z;
                FadeNorthWallsAtSameZ(hitWallZ);
                break; // 한 번만 호출하면 됨
            }
            else
            {
                // 개별 오브젝트만 투명화
                AddRendererToFade(hitObject);
            }
        }
    }

    private bool IsNorthWall(GameObject obj)
    {
        return obj.name.Contains("North");
    }
    
    /// <summary>
    /// 특정 Z 위치 + 플레이어 청크 X 범위 내의 북쪽 벽들을 투명화합니다.
    /// </summary>
    private void FadeNorthWallsAtSameZ(float targetZ)
    {
        // 캐싱된 렌더러에서 같은 Z + 플레이어 X 범위 내인 벽만 투명화
        foreach (var renderer in _cachedNorthWallRenderers)
        {
            if (renderer != null)
            {
                Vector3 wallPos = renderer.transform.position;
                
                // 같은 Z 위치 + 플레이어 청크 X 범위 내
                if (Mathf.Abs(wallPos.z - targetZ) < 1f &&
                    wallPos.x >= _playerChunkMinX && wallPos.x <= _playerChunkMaxX)
                {
                    _renderersToFadeThisFrame.Add(renderer);
                }
            }
        }
    }

    /// <summary>
    /// 캐싱된 북쪽 벽 렌더러를 투명화합니다. (성능 최적화)
    /// </summary>
    private void FadeAllNorthWallsInPlayerXRange()
    {
        // 캐싱된 렌더러 사용 (매 프레임 검색하지 않음)
        foreach (var renderer in _cachedNorthWallRenderers)
        {
            if (renderer != null)
            {
                _renderersToFadeThisFrame.Add(renderer);
            }
        }
    }

    private void AddRendererToFade(GameObject obj)
    {
        var selfRenderer = obj.GetComponent<Renderer>();
        if (selfRenderer != null)
        {
            _renderersToFadeThisFrame.Add(selfRenderer);
        }

        var childRenderers = obj.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in childRenderers)
        {
            _renderersToFadeThisFrame.Add(renderer);
        }
    }

    #endregion

    #region Fading Logic

    private void UpdateFading()
    {
        // 새로 투명화할 렌더러 추가
        foreach (var renderer in _renderersToFadeThisFrame)
        {
            if (renderer == null) continue;

            if (!_fadedRenderers.ContainsKey(renderer))
            {
                InitializeFadedRenderer(renderer);
            }
        }

        // 모든 페이드 렌더러 업데이트
        var renderersToRemove = new List<Renderer>();
        foreach (var kvp in _fadedRenderers)
        {
            var renderer = kvp.Key;
            if (renderer == null)
            {
                renderersToRemove.Add(renderer);
                continue;
            }

            bool shouldBeFaded = _renderersToFadeThisFrame.Contains(renderer);
            float targetAlpha = shouldBeFaded ? _fadedAlpha : 1f;

            var dataArray = kvp.Value;
            bool allRestored = true;

            for (int i = 0; i < dataArray.Length; i++)
            {
                ref var data = ref dataArray[i];
                data.CurrentAlpha = Mathf.MoveTowards(data.CurrentAlpha, targetAlpha, _fadeSpeed * Time.deltaTime);

                if (data.CurrentAlpha < 1f)
                {
                    allRestored = false;
                    ApplyFadedMaterial(renderer, i, data);
                }
                else
                {
                    RestoreOriginalMaterial(renderer, i, data);
                }
            }

            if (allRestored && !shouldBeFaded)
            {
                CleanupRenderer(renderer, dataArray);
                renderersToRemove.Add(renderer);
            }
        }

        foreach (var renderer in renderersToRemove)
        {
            _fadedRenderers.Remove(renderer);
        }
    }

    private void InitializeFadedRenderer(Renderer renderer)
    {
        var materials = renderer.materials;
        var dataArray = new MaterialData[materials.Length];

        for (int i = 0; i < materials.Length; i++)
        {
            var original = materials[i];
            var faded = new Material(original);
            SetupTransparentMaterial(faded);
            
            // 시작 투명도로 초기화 (하얀 반짝임 방지)
            SetMaterialAlpha(faded, _startAlpha);

            dataArray[i] = new MaterialData
            {
                OriginalMaterial = original,
                FadedMaterial = faded,
                CurrentAlpha = _startAlpha // 시작 투명도에서 서서히 _fadedAlpha로 전환
            };
        }

        _fadedRenderers[renderer] = dataArray;
        
        // 머티리얼 즉시 적용 (시작 투명도)
        var mats = renderer.materials;
        for (int i = 0; i < dataArray.Length && i < mats.Length; i++)
        {
            mats[i] = dataArray[i].FadedMaterial;
        }
        renderer.materials = mats;
    }

    private void ApplyFadedMaterial(Renderer renderer, int index, MaterialData data)
    {
        var materials = renderer.materials;
        if (index >= materials.Length) return;

        materials[index] = data.FadedMaterial;
        SetMaterialAlpha(data.FadedMaterial, data.CurrentAlpha);
        renderer.materials = materials;
    }

    private void RestoreOriginalMaterial(Renderer renderer, int index, MaterialData data)
    {
        var materials = renderer.materials;
        if (index >= materials.Length) return;

        materials[index] = data.OriginalMaterial;
        renderer.materials = materials;
    }

    private void CleanupRenderer(Renderer renderer, MaterialData[] dataArray)
    {
        foreach (var data in dataArray)
        {
            if (data.FadedMaterial != null)
                Destroy(data.FadedMaterial);
        }
    }

    private void SetupTransparentMaterial(Material mat)
    {
        if (mat.HasProperty("_Surface"))
        {
            mat.SetFloat("_Surface", 1);  // Transparent
            mat.SetFloat("_Blend", 0);    // Alpha blend
        }

        // 불투명 큐(2450)에서 렌더링하여 스텐실 효과 적용
        // ZWrite = 0이므로 뒤 오브젝트가 보임
        mat.renderQueue = 2450;
        
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");

        if (mat.HasProperty("_SrcBlend"))
        {
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        // ZWrite = 0으로 설정 (뒤 오브젝트가 보이도록)
        if (mat.HasProperty("_ZWrite"))
        {
            mat.SetFloat("_ZWrite", 0);
        }
    }

    private void SetMaterialAlpha(Material mat, float alpha)
    {
        if (mat.HasProperty("_BaseColor"))
        {
            Color c = mat.GetColor("_BaseColor");
            c.a = alpha;
            mat.SetColor("_BaseColor", c);
        }
        else if (mat.HasProperty("_Color"))
        {
            Color c = mat.color;
            c.a = alpha;
            mat.color = c;
        }
    }

    #endregion

    #region Public Methods

    public void SetFadedAlpha(float alpha)
    {
        _fadedAlpha = Mathf.Clamp01(alpha);
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        if (_mainCamera == null) return;

        Vector3 playerCenter = transform.position + Vector3.up * _playerHeightOffset;
        Vector3 cameraPos = _mainCamera.transform.position;

        // 레이캐스트 라인
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(cameraPos, playerCenter);
        Gizmos.DrawWireSphere(playerCenter, 0.2f);

        // 플레이어 청크 X 범위
        Gizmos.color = Color.yellow;
        Vector3 minPos = new Vector3(_playerChunkMinX, playerCenter.y, playerCenter.z);
        Vector3 maxPos = new Vector3(_playerChunkMaxX, playerCenter.y, playerCenter.z);
        Gizmos.DrawLine(minPos, maxPos);
    }

    #endregion
}
