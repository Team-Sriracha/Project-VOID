using Fusion;
using System.Collections.Generic;
using UnityEngine;
using ProjectVoid.Map;

/// <summary>
/// 맵의 모든 몹 스폰을 중앙에서 관리하는 네트워크 매니저입니다.
/// 각 청크의 MobSpawnPoint 컴포넌트를 찾아서 몹을 스폰하고 관리합니다.
/// LootBoxSpawnManager와 동일한 패턴을 사용합니다.
/// </summary>
public class MobSpawnManager : NetworkBehaviour
{
    #region Serialized Fields

    [Header("UI")]
    [Tooltip("몹 오버헤드 UI 프리팹")]
    [SerializeField] private GameObject _mobOverheadUIPrefab;

    // Why: UI Canvas는 런타임에 자동 검색 (프리팹에서 씬 참조 불가)
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
    private float _lastUICheckTime = 0f;
    private const float UI_CHECK_INTERVAL = 0.5f; // 0.5초마다 체크

    #endregion

    #region Properties

    /// <summary>
    /// 현재 스폰된 몹 개수
    /// </summary>
    public int SpawnedMobCount => _spawnedMobs.Count;

    /// <summary>
    /// 발견된 총 스폰 포인트 개수
    /// </summary>
    public int TotalSpawnPointsFound => _totalSpawnPointsFound;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            SpawnMobParent();
        }

        // Why: 클라이언트도 UI Canvas 참조 필요
        if (_uiCanvas == null)
        {
            _uiCanvas = FindUICanvas();
        }

        // Why: 늦게 접속한 클라이언트를 위해 기존 몹들의 UI 생성
        CreateUIForExistingMobs();
    }

    public override void Render()
    {
        // Why: 성능 최적화 - 0.5초마다 새로 스폰된 몹 체크
        if (Time.time - _lastUICheckTime >= UI_CHECK_INTERVAL)
        {
            CheckForNewMobs();
            _lastUICheckTime = Time.time;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (hasState && HasStateAuthority)
        {
            DespawnAllMobs();

            if (_mobParent != null)
            {
                Destroy(_mobParent.gameObject);
            }
        }

        // Why: UI 정리
        ClearAllMobUI();
    }

    #endregion

    #region Initialization

    /// <summary>
    /// MobParent를 일반 GameObject로 생성합니다.
    /// </summary>
    private void SpawnMobParent()
    {
        if (!HasStateAuthority) return;
        if (_mobParent != null) return;

        GameObject parentObj = new GameObject("MobParent");
        _mobParent = parentObj.transform;

        LogDebug("MobParent 생성 완료");
    }

    /// <summary>
    /// 몹 오버헤드 UI 전용 Canvas를 찾거나 생성합니다.
    /// Sort Order를 낮게 설정하여 다른 UI 아래에 표시되도록 합니다.
    /// </summary>
    /// <returns>몹 UI 전용 Canvas</returns>
    private Canvas FindUICanvas()
    {
        // Why: 기존에 생성된 MobOverheadCanvas가 있는지 먼저 확인
        Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var canvas in canvases)
        {
            if (canvas.gameObject.name == "MobOverheadCanvas")
            {
                return canvas;
            }
        }

        // Why: 없으면 새로 생성 - 다른 UI보다 낮은 Sort Order로 설정
        GameObject canvasObj = new GameObject("MobOverheadCanvas");
        Canvas mobCanvas = canvasObj.AddComponent<Canvas>();
        mobCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        mobCanvas.sortingOrder = -10; // Why: 메인 UI(기본값 0)보다 낮게 설정하여 아래에 표시

        // Why: Canvas Scaler 추가 (UI 스케일링용)
        UnityEngine.UI.CanvasScaler scaler = canvasObj.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Why: Graphic Raycaster 추가 (UI 상호작용용, 필요시)
        canvasObj.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        LogDebug("MobOverheadCanvas 생성 완료 (Sort Order: -10)");
        return mobCanvas;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// MapGenerator의 모든 청크에서 몹을 스폰합니다.
    /// 서버(State Authority)만 호출해야 합니다.
    /// </summary>
    /// <param name="mapGenerator">맵 생성기 인스턴스</param>
    public void SpawnMobsForAllChunks(MapGenerator mapGenerator)
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("[MobSpawnManager] SpawnMobsForAllChunks는 서버만 호출할 수 있습니다!");
            return;
        }

        if (mapGenerator == null)
        {
            Debug.LogError("[MobSpawnManager] MapGenerator가 null입니다!");
            return;
        }

        // Why: 기존 몹 제거
        DespawnAllMobs();

        _totalSpawnPointsFound = 0;
        int spawnedCount = 0;
        int invalidCount = 0;
        var processedChunks = new HashSet<ChunkInstance>();

        // Why: 모든 청크를 순회하면서 MobSpawnPoint 컴포넌트 찾기
        foreach (var kvp in mapGenerator.PlacedChunks)
        {
            ChunkInstance chunk = kvp.Value;

            // Why: 그룹 청크는 여러 칸에 같은 ChunkInstance가 있으므로 중복 방지
            if (processedChunks.Contains(chunk))
                continue;

            processedChunks.Add(chunk);

            // Why: 청크 내 모든 MobSpawnPoint 컴포넌트 검색
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
                    Debug.LogWarning($"[MobSpawnManager] 유효하지 않은 스폰 포인트: {spawnPoint.gameObject.name} (MobData 또는 MobPrefab이 없음)");
                }
            }
        }

        if (_totalSpawnPointsFound == 0)
        {
            Debug.LogWarning("[MobSpawnManager] 맵에서 MobSpawnPoint 컴포넌트를 가진 오브젝트를 찾을 수 없습니다!");
        }
        else
        {
            LogDebug($"몹 스폰 완료! 스폰 포인트: {_totalSpawnPointsFound}개, 스폰됨: {spawnedCount}개, 유효하지 않음: {invalidCount}개");
        }
    }

    /// <summary>
    /// 스폰된 모든 몹을 제거합니다.
    /// 서버(State Authority)만 호출해야 합니다.
    /// </summary>
    public void DespawnAllMobs()
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("[MobSpawnManager] DespawnAllMobs는 서버만 호출할 수 있습니다!");
            return;
        }

        if (Runner == null)
        {
            Debug.LogWarning("[MobSpawnManager] Runner가 null입니다. 몹을 제거할 수 없습니다.");
            return;
        }

        foreach (NetworkObject mob in _spawnedMobs)
        {
            if (mob != null && mob.IsValid)
            {
                Runner.Despawn(mob);
            }
        }

        _spawnedMobs.Clear();
        ClearAllMobUI();

        LogDebug("모든 몹 제거됨.");
    }

    #endregion

    #region Spawn Methods

    /// <summary>
    /// MobSpawnPoint에서 지정된 몹을 스폰합니다.
    /// </summary>
    /// <param name="spawnPoint">스폰 포인트 컴포넌트</param>
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

        // Why: Runner.Spawn으로 네트워크 오브젝트 생성
        NetworkObject mob = Runner.Spawn(
            prefab,
            worldPosition,
            worldRotation,
            onBeforeSpawned: (runner, obj) =>
            {
                // Why: MobCombat 초기화 (스폰 전에 데이터 설정)
                MobCombat combat = obj.GetComponent<MobCombat>();
                if (combat != null)
                {
                    combat.InitializeWithData(mobData);
                }
            }
        );

        if (mob != null)
        {
            // Why: 스폰 후에 부모 설정 (worldPositionStays=true로 월드 좌표 유지)
            if (_mobParent != null)
            {
                mob.transform.SetParent(_mobParent, true);
            }

            _spawnedMobs.Add(mob);

            // Why: 서버에서도 UI 생성 (Render에서 클라이언트도 자동 생성)
            CreateOverheadUI(mob);

            LogDebug($"몹 스폰 완료: {mobData.MobName} at {mob.transform.position} (목표: {worldPosition})");
        }
        else
        {
            Debug.LogError($"[MobSpawnManager] 몹 스폰 실패: {spawnPoint.gameObject.name}");
        }
    }

    #endregion

    #region UI Methods

    /// <summary>
    /// 기존에 스폰된 몹들의 UI를 생성합니다.
    /// 늦게 접속한 클라이언트를 위함.
    /// </summary>
    private void CreateUIForExistingMobs()
    {
        // Why: 씬의 모든 MobCombat을 찾아서 UI 생성
        MobCombat[] allMobs = FindObjectsByType<MobCombat>(FindObjectsSortMode.None);
        
        foreach (MobCombat mob in allMobs)
        {
            NetworkObject netObj = mob.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsValid)
            {
                CreateOverheadUI(netObj);
            }
        }

        LogDebug($"기존 몹 UI 생성 완료: {allMobs.Length}개");
    }

    /// <summary>
    /// 새로 스폰된 몹이 있는지 확인하여 UI 생성.
    /// Render에서 호출됨.
    /// </summary>
    private void CheckForNewMobs()
    {
        // Why: 씬의 모든 MobCombat을 찾아서 UI가 없는 몹에 대해 생성
        MobCombat[] allMobs = FindObjectsByType<MobCombat>(FindObjectsSortMode.None);
        
        foreach (MobCombat mob in allMobs)
        {
            NetworkObject netObj = mob.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsValid && !_mobUIMap.ContainsKey(netObj))
            {
                CreateOverheadUI(netObj);
            }
        }
    }

    /// <summary>
    /// 몹의 오버헤드 UI를 생성합니다.
    /// </summary>
    /// <param name="mob">몹 NetworkObject</param>
    private void CreateOverheadUI(NetworkObject mob)
    {
        // Why: Canvas 다시 검색 시도
        if (_uiCanvas == null)
        {
            _uiCanvas = FindUICanvas();
        }

        if (_mobOverheadUIPrefab == null)
        {
            Debug.LogError("[MobSpawnManager] MobOverheadUI 프리팹이 설정되지 않았습니다! Inspector에서 할당해주세요.");
            return;
        }

        if (_uiCanvas == null)
        {
            Debug.LogError("[MobSpawnManager] Screen Space Overlay Canvas를 찾을 수 없습니다! 씬에 Canvas가 있는지 확인해주세요.");
            return;
        }

        // Why: 이미 UI가 있으면 생성하지 않음
        if (_mobUIMap.ContainsKey(mob))
        {
            return;
        }

        GameObject uiObj = Instantiate(_mobOverheadUIPrefab, _uiCanvas.transform);
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

    /// <summary>
    /// 모든 몹 UI를 제거합니다.
    /// </summary>
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
