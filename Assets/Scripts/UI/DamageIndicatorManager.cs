using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 데미지 인디케이터를 관리하는 싱글톤 매니저.
/// PlayerOverheadUI와 동일한 Screen Space Overlay 방식 사용.
/// 오브젝트 풀링과 FOV 기반 가시성 체크를 담당합니다.
/// 
/// Multi-Peer 호환성:
/// - 클라이언트 전용 (서버에서는 Camera.main이 null이므로 자동으로 무시됨)
/// - FOVController.LocalInstance가 없으면 FOV 체크 건너뜀
/// 
/// 데미지 합산:
/// - 50ms 내 같은 위치(0.5m 이내)에서 발생한 데미지를 합산하여 표시
/// - 샷건 펠렛은 합산되고, 라이플 연사는 각각 표시됨
/// </summary>
public class DamageIndicatorManager : MonoBehaviour
{
    #region Singleton

    public static DamageIndicatorManager Instance { get; private set; }

    #endregion

    #region Constants

    private const int INITIAL_POOL_SIZE = 20;
    private const float INDICATOR_HEIGHT_OFFSET = 1.5f;
    private const float STACK_TIME_WINDOW = 0.05f;  // 50ms - 샷건 펠렛 합산
    private const float STACK_DISTANCE_THRESHOLD = 1.5f;  // 1.5m 이내 피격은 같은 위치로 취급 (샷건 spread 고려)

    #endregion

    #region Serialized Fields

    [Header("프리팹")]
    [SerializeField] private DamageIndicator _indicatorPrefab;

    [Header("컨테이너")]
    [Tooltip("인디케이터가 생성될 부모 RectTransform (Screen Space Canvas 하위)")]
    [SerializeField] private RectTransform _indicatorContainer;

    #endregion

    #region Private Fields

    private readonly Queue<DamageIndicator> _pool = new Queue<DamageIndicator>();
    private readonly List<PendingDamage> _pendingDamages = new List<PendingDamage>();
    private Camera _mainCamera;
    private Coroutine _flushCoroutine;

    #endregion

    #region Nested Classes

    /// <summary>
    /// 합산 대기 중인 데미지 정보
    /// </summary>
    private class PendingDamage
    {
        public Vector3 Position;
        public float TotalDamage;
        public bool IsHeal;
        public bool IsLocalHit;
        public float SpawnTime;

        public PendingDamage(Vector3 position, float damage, bool isHeal, bool isLocalHit)
        {
            Position = position;
            TotalDamage = damage;
            IsHeal = isHeal;
            IsLocalHit = isLocalHit;
            SpawnTime = Time.time;
        }
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        _mainCamera = Camera.main;
        InitializePool();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    #endregion

    #region Pool Management

    private void InitializePool()
    {
        if (_indicatorPrefab == null)
        {
            Debug.LogError("[DamageIndicatorManager] Indicator prefab is not assigned!");
            return;
        }

        if (_indicatorContainer == null)
        {
            Debug.LogError("[DamageIndicatorManager] Indicator container is not assigned!");
            return;
        }

        for (int i = 0; i < INITIAL_POOL_SIZE; i++)
        {
            CreateIndicator();
        }
    }

    private DamageIndicator CreateIndicator()
    {
        DamageIndicator indicator = Instantiate(_indicatorPrefab, _indicatorContainer);
        indicator.gameObject.SetActive(false);
        _pool.Enqueue(indicator);
        return indicator;
    }

    private DamageIndicator GetFromPool()
    {
        if (_pool.Count == 0)
        {
            return CreateIndicator();
        }

        DamageIndicator indicator = _pool.Dequeue();
        
        // 비활성화된 것만 사용
        while (indicator.gameObject.activeSelf && _pool.Count > 0)
        {
            _pool.Enqueue(indicator);
            indicator = _pool.Dequeue();
        }

        return indicator;
    }

    private void ReturnToPool(DamageIndicator indicator)
    {
        indicator.Reset();
        _pool.Enqueue(indicator);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 데미지 인디케이터를 생성합니다.
    /// 짧은 시간(50ms) 내 같은 위치에서 발생한 데미지는 합산됩니다.
    /// </summary>
    /// <param name="worldPosition">데미지가 발생한 월드 좌표</param>
    /// <param name="damage">데미지 값</param>
    /// <param name="isHeal">회복 여부 (false = 데미지)</param>
    /// <param name="isLocalHit">본인 피격 여부</param>
    public void SpawnIndicator(Vector3 worldPosition, float damage, bool isHeal = false, bool isLocalHit = false)
    {
        // 카메라 체크 (서버에서는 null)
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
        }

        // FOV 체크 - 생성 시점에만 확인 (이후 시야 밖으로 이동해도 표시 유지)
        if (FOVController.LocalInstance != null && 
            !FOVController.LocalInstance.IsInsideFOV(worldPosition))
        {
            return; // 시야 밖 → 생성 안 함
        }

        // 카메라 뒤에 있으면 표시 안 함
        Vector3 viewportPos = _mainCamera.WorldToViewportPoint(worldPosition);
        if (viewportPos.z < 0) return;

        // 기존 펜딩 데미지 중 같은 위치 근처 + 같은 타입 찾기
        PendingDamage existingPending = FindNearbyPending(worldPosition, isHeal, isLocalHit);
        
        if (existingPending != null)
        {
            // 기존 펜딩에 데미지 합산
            existingPending.TotalDamage += damage;
        }
        else
        {
            // 새 펜딩 생성
            _pendingDamages.Add(new PendingDamage(worldPosition, damage, isHeal, isLocalHit));
            
            // Flush 코루틴이 없으면 시작
            if (_flushCoroutine == null)
            {
                _flushCoroutine = StartCoroutine(FlushPendingDamagesCoroutine());
            }
        }
    }

    /// <summary>
    /// 데미지 인디케이터를 생성합니다 (Transform 기반).
    /// </summary>
    public void SpawnIndicator(Transform target, float damage, bool isHeal = false, bool isLocalHit = false)
    {
        if (target == null) return;
        SpawnIndicator(target.position + Vector3.up * INDICATOR_HEIGHT_OFFSET, damage, isHeal, isLocalHit);
    }

    #endregion

    #region Damage Stacking

    /// <summary>
    /// 근처에 합산 가능한 펜딩 데미지를 찾습니다.
    /// 펜딩 리스트에 있는 동안에는 거리와 타입만 체크합니다.
    /// </summary>
    private PendingDamage FindNearbyPending(Vector3 position, bool isHeal, bool isLocalHit)
    {
        foreach (var pending in _pendingDamages)
        {
            // 같은 타입인지 확인 (데미지/회복, 본인/타인)
            if (pending.IsHeal != isHeal || pending.IsLocalHit != isLocalHit)
                continue;
            
            // 거리 확인
            float distance = Vector3.Distance(pending.Position, position);
            if (distance <= STACK_DISTANCE_THRESHOLD)
            {
                return pending;
            }
        }
        
        return null;
    }

    /// <summary>
    /// 펜딩된 데미지들을 실제 인디케이터로 생성합니다.
    /// </summary>
    private IEnumerator FlushPendingDamagesCoroutine()
    {
        // 스태킹 윈도우만큼 대기
        yield return new WaitForSeconds(STACK_TIME_WINDOW);
        
        // 모든 펜딩 데미지를 인디케이터로 생성
        foreach (var pending in _pendingDamages)
        {
            DamageIndicator indicator = GetFromPool();
            indicator.Initialize(pending.Position, pending.TotalDamage, pending.IsHeal, pending.IsLocalHit, ReturnToPool);
        }
        
        _pendingDamages.Clear();
        _flushCoroutine = null;
    }

    #endregion
}
