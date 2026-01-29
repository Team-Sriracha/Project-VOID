using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 데미지 인디케이터 생성 및 관리
/// 오브젝트 풀링 및 FOV 가시성 체크 담당
/// </summary>
/// Multi-Peer 호환성:
/// - 클라이언트 전용 (서버에서는 Camera.main이 null이므로 자동으로 무시됨)
/// - FOVController.LocalInstance가 없으면 FOV 체크 건너뜀
/// 
/// 데미지 합산:
/// - 50ms 내 같은 타겟에게 발생한 데미지를 합산하여 표시
/// - 샷건 펠렛은 같은 적에게는 합산, 다른 적에게는 각각 표시
/// </summary>
public class DamageIndicatorManager : MonoBehaviour
{
    #region Singleton

    public static DamageIndicatorManager Instance { get; private set; }

    #endregion

    #region Constants

    private const int INITIAL_POOL_SIZE = 20;
    private const float INDICATOR_HEIGHT_OFFSET = 1.5f;
    private const float STACK_TIME_WINDOW = 0.05f;  // 50ms - 같은 타겟에게 동시 피격 합산

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
        public int TargetId;  // 타겟 식별자 (GetInstanceID)
        public Vector3 Position;
        public float TotalDamage;
        public bool IsHeal;
        public bool IsLocalHit;
        public float SpawnTime;

        public PendingDamage(int targetId, Vector3 position, float damage, bool isHeal, bool isLocalHit)
        {
            TargetId = targetId;
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
    /// 데미지 인디케이터 생성
    /// 짧은 시간(50ms) 내 동일 타겟 데미지 합산
    /// </summary>
    /// <param name="worldPosition">데미지가 발생한 월드 좌표</param>
    /// <param name="damage">데미지 값</param>
    /// <param name="isHeal">회복 여부 (false = 데미지)</param>
    /// <param name="isLocalHit">본인 피격 여부</param>
    /// <param name="targetId">타겟 식별자 (GameObject.GetInstanceID). 0이면 위치 기반 합산</param>
    public void SpawnIndicator(Vector3 worldPosition, float damage, bool isHeal = false, bool isLocalHit = false, int targetId = 0)
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

        // 기존 펜딩 데미지 중 같은 타겟 + 같은 타입 찾기
        PendingDamage existingPending = FindMatchingPending(targetId, isHeal, isLocalHit);
        
        if (existingPending != null)
        {
            // 기존 펜딩에 데미지 합산
            existingPending.TotalDamage += damage;
        }
        else
        {
            // 새 펜딩 생성
            _pendingDamages.Add(new PendingDamage(targetId, worldPosition, damage, isHeal, isLocalHit));
            
            // Flush 코루틴이 없으면 시작
            if (_flushCoroutine == null)
            {
                _flushCoroutine = StartCoroutine(FlushPendingDamagesCoroutine());
            }
        }
    }

    /// <summary>
    /// 데미지 인디케이터 생성 (Transform 기반)
    /// Transform의 GetInstanceID를 타겟 식별자로 사용
    /// </summary>
    public void SpawnIndicator(Transform target, float damage, bool isHeal = false, bool isLocalHit = false)
    {
        if (target == null) return;
        int targetId = target.gameObject.GetInstanceID();
        SpawnIndicator(target.position + Vector3.up * INDICATOR_HEIGHT_OFFSET, damage, isHeal, isLocalHit, targetId);
    }

    #endregion

    #region Damage Stacking

    /// <summary>
    /// 같은 타겟에게 합산 가능한 펜딩 데미지를 찾습니다.
    /// 타겟 ID 일치 시 동일 타겟 판단
    /// </summary>
    private PendingDamage FindMatchingPending(int targetId, bool isHeal, bool isLocalHit)
    {
        // targetId가 없으면 합산하지 않음 (각각 표시)
        if (targetId == 0) return null;
        
        foreach (var pending in _pendingDamages)
        {
            // 같은 타입인지 확인 (데미지/회복, 본인/타인)
            if (pending.IsHeal != isHeal || pending.IsLocalHit != isLocalHit)
                continue;
            
            // 같은 타겟인지 확인
            if (pending.TargetId == targetId)
            {
                return pending;
            }
        }
        
        return null;
    }

    /// <summary>
    /// 펜딩된 데미지 실제 인디케이터로 생성
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
