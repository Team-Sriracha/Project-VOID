using FishNet.Object;
using FishNet.Object.Synchronizing;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using ProjectVoid.Map;

/// <summary>
/// 맵의 모든 몹 스폰 중앙 관리
/// 각 청크 MobSpawnPoint 탐색 및 몹 스폰/관리
/// </summary>
public class MobSpawnManager : NetworkBehaviour
{
    public static MobSpawnManager Instance { get; private set; }
    #region Serialized Fields

    [Header("UI")]
    [Tooltip("몹 오버헤드 UI 프리팹")]
    [SerializeField] private GameObject _mobOverheadUIPrefab;

    private const string OVERHEAD_CANVAS_NAME = "MobOverheadCanvas";
    private Canvas _uiCanvas;

    [Header("디버그")]
    [Tooltip("스폰 정보 로그 출력")]
    [SerializeField] private bool _enableDebugLogs = true;

    #endregion

    #region Private Fields

    private List<NetworkObject> _spawnedMobs = new List<NetworkObject>();
    private Dictionary<NetworkObject, GameObject> _mobUIMap = new Dictionary<NetworkObject, GameObject>();
    private int _totalSpawnPointsFound = 0;
    private Transform _mobParent;

    #endregion

    #region Properties

    public int SpawnedMobCount => _spawnedMobs.Count;
    public int TotalSpawnPointsFound => _totalSpawnPointsFound;

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartServer()
    {
        base.OnStartServer();
        
        // _spawnedMobs.Clear(); // 서버는 리스트만 관리, 클라이언트는 등록 요청 받음
        // _mobUIMap.Clear();

        if (_uiCanvas == null)
        {
            _uiCanvas = FindUICanvas();
        }
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        if (_uiCanvas == null)
        {
            _uiCanvas = FindUICanvas();
        }

        if (!IsServerInitialized)
        {
            // 클라이언트는 개별 몹의 Register 요청으로 처리되므로 별도 캐싱 불필요
            // CacheExistingMobs(); 
        }
        
        // 클라이언트는 개별 몹의 Register 요청으로 처리되므로 별도 캐싱 및 UI 생성 호출 불필요
    }

    private void Update()
    {
        // 씬 전환 후 Canvas 참조 무효화 대비 매번 체크
        if (_uiCanvas == null)
        {
            _uiCanvas = FindUICanvas();
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        
        DespawnAllMobs();

        if (_mobParent != null)
        {
            Destroy(_mobParent.gameObject);
        }

        ClearAllMobUI();
    }

    #endregion

    #region Initialization

    private void SpawnMobParent()
    {
        if (!IsServerInitialized) return;
        if (_mobParent != null) return;

        GameObject parentObj = new GameObject("MobParent");
        _mobParent = parentObj.transform;

        LogDebug("MobParent 생성 완료");
    }

    private Canvas FindUICanvas()
    {
        if (_uiCanvas != null)
        {
            return _uiCanvas;
        }

        if (UIManager.Instance != null && UIManager.Instance.OverheadUIContainer != null)
        {
            _uiCanvas = UIManager.Instance.OverheadUIContainer.GetComponentInParent<Canvas>();
            if (_uiCanvas != null)
            {
                return _uiCanvas;
            }
        }

        GameObject canvasObj = GameObject.Find(OVERHEAD_CANVAS_NAME);
        if (canvasObj != null)
        {
            _uiCanvas = canvasObj.GetComponent<Canvas>();
            if (_uiCanvas != null)
            {
                return _uiCanvas;
            }
        }

        canvasObj = new GameObject(OVERHEAD_CANVAS_NAME);
        Canvas mobCanvas = canvasObj.AddComponent<Canvas>();
        mobCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        mobCanvas.sortingOrder = -10;

        UnityEngine.UI.CanvasScaler scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        LogDebug("MobOverheadCanvas 생성 완료 (Sort Order: -10)");
        return mobCanvas;
    }

    #endregion

    #region Public Methods

    public void SpawnMobsForAllChunks(MapGenerator mapGenerator)
    {
        if (!IsServerInitialized)
        {
            Debug.LogWarning("[MobSpawnManager] SpawnMobsForAllChunks는 서버만 호출할 수 있습니다!");
            return;
        }

        if (mapGenerator == null)
        {
            Debug.LogError("[MobSpawnManager] MapGenerator가 null입니다!");
            return;
        }

        DespawnAllMobs();
        SpawnMobParent();

        _totalSpawnPointsFound = 0;
        int spawnedCount = 0;
        int invalidCount = 0;
        var processedChunks = new HashSet<ChunkInstance>();

        foreach (var kvp in mapGenerator.PlacedChunks)
        {
            ChunkInstance chunk = kvp.Value;

            if (processedChunks.Contains(chunk))
                continue;

            processedChunks.Add(chunk);

            MobSpawnPoint[] spawnPoints = chunk.GetComponentsInChildren<MobSpawnPoint>();

            foreach (MobSpawnPoint spawnPoint in spawnPoints)
            {
                _totalSpawnPointsFound++;

                if (spawnPoint.IsValid)
                {
                    SpawnMobAtPoint(spawnPoint);
                    spawnedCount++;
                }
                else
                {
                    invalidCount++;
                    Debug.LogWarning($"[MobSpawnManager] 유효하지 않은 스폰 포인트: {spawnPoint.gameObject.name}");
                }
            }
        }

        if (_totalSpawnPointsFound == 0)
        {
            Debug.LogWarning("[MobSpawnManager] 맵에서 MobSpawnPoint 컴포넌트를 가진 오브젝트를 찾을 수 없습니다!");
        }
        else
        {
            LogDebug($"몹 스폰 완료! 스폰 포인트: {_totalSpawnPointsFound}개, 스폰됨: {spawnedCount}개");
        }
    }

    public void DespawnAllMobs()
    {
        if (!IsServerInitialized)
        {
            Debug.LogWarning("[MobSpawnManager] DespawnAllMobs는 서버만 호출할 수 있습니다!");
            return;
        }

        foreach (NetworkObject mob in _spawnedMobs)
        {
            if (mob != null && mob.IsSpawned)
            {
                ServerManager.Despawn(mob);
            }
        }

        _spawnedMobs.Clear();
        ClearAllMobUI();

        LogDebug("모든 몹 제거됨.");
    }

    #endregion

    #region Spawn Methods

    private void SpawnMobAtPoint(MobSpawnPoint spawnPoint)
    {
        if (!spawnPoint.IsValid)
        {
            Debug.LogError($"[MobSpawnManager] 유효하지 않은 스폰 포인트: {spawnPoint.gameObject.name}");
            return;
        }

        Vector3 worldPosition = spawnPoint.transform.position;
        Quaternion worldRotation = spawnPoint.transform.rotation;
        MobData mobData = spawnPoint.MobData;
        GameObject prefab = spawnPoint.MobPrefab;

        LogDebug($"스폰 시도: {mobData.MobName} at {worldPosition}");

        // Fishnet: Instantiate 후 Spawn
        NetworkObject mobPrefabNetObj = prefab.GetComponent<NetworkObject>();
        if (mobPrefabNetObj == null)
        {
            Debug.LogError($"[MobSpawnManager] {prefab.name}에 NetworkObject가 없습니다!");
            return;
        }

        NetworkObject mob = Instantiate(mobPrefabNetObj, worldPosition, worldRotation);
        
        // MobCombat 초기화 (스폰 전 데이터 설정)
        MobCombat combat = mob.GetComponent<MobCombat>();
        if (combat != null)
        {
            combat.InitializeWithData(mobData);
        }

        ServerManager.Spawn(mob);

        if (mob != null)
        {
            // Ensure correct layer for FOV system
            int mobLayer = LayerMask.NameToLayer("Mob");
            if (mobLayer != -1)
            {
                SetLayerRecursively(mob.gameObject, mobLayer);
            }
            else
            {
                Debug.LogWarning("[MobSpawnManager] 'Mob' layer not found! Mobs might be invisible.");
            }

            UnityEngine.AI.NavMeshAgent agent = mob.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent != null)
            {
                agent.Warp(worldPosition);
            }
            
            if (_mobParent != null)
            {
                mob.transform.SetParent(_mobParent, true);
            }

            _spawnedMobs.Add(mob);
            CreateOverheadUI(mob);

            LogDebug($"몹 스폰 완료: {mobData.MobName} at {mob.transform.position}");
        }
        else
        {
            Debug.LogError($"[MobSpawnManager] 몹 스폰 실패: {spawnPoint.gameObject.name}");
        }
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    #endregion

    #region UI Methods

    public void RegisterMob(NetworkObject mob)
    {
        if (mob == null) return;

        if (!_spawnedMobs.Contains(mob))
        {
            _spawnedMobs.Add(mob);
            
            // 클라이언트라면 UI 생성
            if (IsClientInitialized)
            {
                CreateOverheadUI(mob);
            }
        }
    }

    public void UnregisterMob(NetworkObject mob)
    {
        if (mob == null) return;

        if (_spawnedMobs.Contains(mob))
        {
            _spawnedMobs.Remove(mob);
            
            // UI 제거
            if (_mobUIMap.TryGetValue(mob, out GameObject uiObj))
            {
                if (uiObj != null) Destroy(uiObj);
                _mobUIMap.Remove(mob);
            }
        }
    }

    private void CreateOverheadUI(NetworkObject mob)
    {
        if (mob == null || !mob.IsSpawned)
        {
            return;
        }

        if (_uiCanvas == null)
        {
            _uiCanvas = FindUICanvas();
        }

        if (_mobOverheadUIPrefab == null)
        {
            Debug.LogError("[MobSpawnManager] MobOverheadUI 프리팹이 설정되지 않았습니다!");
            return;
        }

        if (_uiCanvas == null)
        {
            Debug.LogError("[MobSpawnManager] Canvas를 찾을 수 없습니다!");
            return;
        }

        if (_mobUIMap.ContainsKey(mob))
        {
            return;
        }

        Transform parent = ResolveOverheadUIParent();
        if (parent == null)
        {
            Debug.LogError("[MobSpawnManager] 오버헤드 UI 부모를 찾을 수 없습니다!");
            return;
        }

        GameObject uiObj = Instantiate(_mobOverheadUIPrefab, parent);
        MobOverheadUI overheadUI = uiObj.GetComponent<MobOverheadUI>();

        if (overheadUI != null)
        {
            overheadUI.SetTargetMob(mob.transform);
            _mobUIMap[mob] = uiObj;
            LogDebug($"HP 바 UI 생성 완료: {mob.name}");
        }
        else
        {
            Debug.LogError($"[MobSpawnManager] MobOverheadUI 프리팹에 MobOverheadUI 컴포넌트가 없습니다!");
            Destroy(uiObj);
        }
    }

    private Transform ResolveOverheadUIParent()
    {
        if (UIManager.Instance != null && UIManager.Instance.OverheadUIContainer != null)
        {
            return UIManager.Instance.OverheadUIContainer;
        }

        if (_uiCanvas == null)
        {
            _uiCanvas = FindUICanvas();
        }

        return _uiCanvas != null ? _uiCanvas.transform : null;
    }

    private void ClearAllMobUI()
    {
        foreach (var uiObj in _mobUIMap.Values)
        {
            if (uiObj != null)
            {
                Destroy(uiObj);
            }
        }

        _mobUIMap.Clear();
    }

    #endregion

    #region Helper Methods

    private void LogDebug(string message)
    {
        if (_enableDebugLogs)
        {
            Debug.Log($"[MobSpawnManager] {message}");
        }
    }

    #endregion
}
