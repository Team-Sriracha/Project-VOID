using System.Collections.Generic;
using FishNet.Object;
using ProjectVoid.Map;
using UnityEngine;

/// <summary>
/// 카메라와 로컬 플레이어 사이의 장애물을 감지하여 투명하게 만듭니다.
/// </summary>
public class PlayerOcclusionFader : NetworkBehaviour
{
    #region Serialized Fields

    [Header("레이캐스트 설정")]
    [SerializeField] private LayerMask _occluderLayers;
    [SerializeField] private float _checkFrequency = 30f;
    [SerializeField] private float _playerHeightOffset = 1f;

    [Header("청크 설정")]
    [SerializeField] private float _chunkSize = 30f;

    [Header("투명도 설정")]
    [Range(0f, 1f)]
    [SerializeField] private float _startAlpha = 0.9f;
    [Range(0f, 1f)]
    [SerializeField] private float _fadedAlpha = 0.3f;
    [SerializeField] private float _fadeSpeed = 8f;

    #endregion

    #region Private Fields

    private Camera _mainCamera;
    private float _checkTimer;
    private ChunkInstance _playerChunk;
    private float _playerChunkMinX;
    private float _playerChunkMaxX;
    
    private readonly List<(Renderer renderer, float posX)> _allInternalNorthWalls = new List<(Renderer, float)>();
    private bool _isWallDataCached = false;
    private ChunkInstance[] _cachedChunks;
    private bool _isChunksCached = false;
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

    #region Fishnet Lifecycle

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        if (!IsOwner)
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
        if (!IsOwner) return;

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
        }

        _checkTimer += Time.deltaTime;
        if (_checkTimer < 1f / _checkFrequency) return;
        _checkTimer = 0f;

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

    private void UpdatePlayerChunk()
    {
        Vector3 playerPos = transform.position;
        
        // Why: ChunkInstance 캐싱하여 FindObjectsByType 반복 호출 방지
        if (!_isChunksCached)
        {
            _cachedChunks = FindObjectsByType<ChunkInstance>(FindObjectsSortMode.None);
            _isChunksCached = true;
        }
        
        foreach (var chunk in _cachedChunks)
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
        
        _playerChunk = null;
        _playerChunkMinX = playerPos.x - _chunkSize / 2f;
        _playerChunkMaxX = playerPos.x + _chunkSize / 2f;
    }

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

    private void CalculateChunkXRange(ChunkInstance chunk)
    {
        Vector3 chunkPos = chunk.transform.position;
        int width = chunk.ChunkData?.ChunkWidth ?? 1;
        
        _playerChunkMinX = chunkPos.x;
        _playerChunkMaxX = chunkPos.x + width * _chunkSize;
        
        FilterNorthWallsByPlayerXRange();
    }

    private void CacheAllNorthWalls()
    {
        if (_isWallDataCached) return;
        
        _allInternalNorthWalls.Clear();
        
        // Why: 캐싱된 ChunkInstance 사용
        if (!_isChunksCached)
        {
            _cachedChunks = FindObjectsByType<ChunkInstance>(FindObjectsSortMode.None);
            _isChunksCached = true;
        }
        
        foreach (var chunk in _cachedChunks)
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
    
    private void FilterNorthWallsByPlayerXRange()
    {
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

        int hitCount = Physics.RaycastNonAlloc(
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

            if (IsNorthWall(hitObject))
            {
                float hitWallZ = hitObject.transform.position.z;
                FadeNorthWallsAtSameZ(hitWallZ);
                break;
            }
            else
            {
                AddRendererToFade(hitObject);
            }
        }
    }

    private bool IsNorthWall(GameObject obj)
    {
        return obj.name.Contains("North");
    }
    
    private void FadeNorthWallsAtSameZ(float targetZ)
    {
        foreach (var renderer in _cachedNorthWallRenderers)
        {
            if (renderer != null)
            {
                Vector3 wallPos = renderer.transform.position;
                
                if (Mathf.Abs(wallPos.z - targetZ) < 1f &&
                    wallPos.x >= _playerChunkMinX && wallPos.x <= _playerChunkMaxX)
                {
                    _renderersToFadeThisFrame.Add(renderer);
                }
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
        foreach (var renderer in _renderersToFadeThisFrame)
        {
            if (renderer == null) continue;

            if (!_fadedRenderers.ContainsKey(renderer))
            {
                InitializeFadedRenderer(renderer);
            }
        }

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
            SetMaterialAlpha(faded, _startAlpha);

            dataArray[i] = new MaterialData
            {
                OriginalMaterial = original,
                FadedMaterial = faded,
                CurrentAlpha = _startAlpha
            };
        }

        _fadedRenderers[renderer] = dataArray;
        
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
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
        }

        mat.renderQueue = 2450;
        
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");

        if (mat.HasProperty("_SrcBlend"))
        {
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

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

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(cameraPos, playerCenter);
        Gizmos.DrawWireSphere(playerCenter, 0.2f);

        Gizmos.color = Color.yellow;
        Vector3 minPos = new Vector3(_playerChunkMinX, playerCenter.y, playerCenter.z);
        Vector3 maxPos = new Vector3(_playerChunkMaxX, playerCenter.y, playerCenter.z);
        Gizmos.DrawLine(minPos, maxPos);
    }

    #endregion
}
