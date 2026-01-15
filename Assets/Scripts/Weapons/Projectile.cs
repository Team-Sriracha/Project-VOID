using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using UnityEngine;

/// <summary>
/// 네트워크 동기화 발사체
/// 직선 이동 및 충돌 시 데미지 처리
/// </summary>
public class Projectile : NetworkBehaviour
{
    #region Serialized Fields

    [Header("발사체 설정")]
    [SerializeField] private float _maxLifetime = 5f;

    [Header("시각 효과")]
    [SerializeField] private GameObject _hitEffectPrefab;

    #endregion

    #region SyncVars

    public readonly SyncVar<Vector3> Direction = new();
    public readonly SyncVar<float> Speed = new();
    public readonly SyncVar<float> Damage = new();
    public readonly SyncVar<NetworkConnection> OwnerConnection = new();
    public readonly SyncVar<float> MaxRange = new();
    public readonly SyncVar<int> HitLayerMask = new();
    public readonly SyncVar<bool> IsInitialized = new();
    
    // Timer replacements
    private readonly SyncVar<float> _lifeEndTime = new();
    private readonly SyncVar<Vector3> _startPosition = new();
    private readonly SyncVar<float> _despawnTime = new();
    private readonly SyncVar<bool> _despawnScheduled = new();

    #endregion

    #region Private Fields

    private bool _hasHit;
    private Collider _projectileCollider;

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartServer()
    {
        base.OnStartServer();
        
        _hasHit = false;
        _projectileCollider = GetComponent<Collider>();

        if (IsInitialized.Value)
        {
            _lifeEndTime.Value = Time.time + _maxLifetime;
            _startPosition.Value = transform.position;
        }

        // TimeManager.OnTick 이벤트 구독
        TimeManager.OnTick += OnTick;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        _projectileCollider = GetComponent<Collider>();
        
        // 발사자의 모든 Collider와 충돌 무시 설정
        if (OwnerConnection.Value != null && OwnerConnection.Value.FirstObject != null)
        {
            IgnoreCollisionWithOwner(OwnerConnection.Value.FirstObject);
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        
        // TimeManager.OnTick 이벤트 구독 해제
        if (TimeManager != null)
        {
            TimeManager.OnTick -= OnTick;
        }
    }

    private void OnTick()
    {
        // 서버(State Authority)만 처리
        if (!IsServerInitialized) return;

        // DespawnTimer 시작 시 충돌로 인한 제거 예약됨
        if (_despawnScheduled.Value)
        {
            if (Time.time >= _despawnTime.Value)
            {
                ServerManager.Despawn(gameObject);
            }
            return;
        }

        if (!IsInitialized.Value || _hasHit) return;

        // Lifetime 체크
        if (Time.time >= _lifeEndTime.Value)
        {
            ServerManager.Despawn(gameObject);
            return;
        }

        // 무기 사거리만큼 비행
        float traveledDistance = Vector3.Distance(_startPosition.Value, transform.position);
        if (traveledDistance >= MaxRange.Value)
        {
            ServerManager.Despawn(gameObject);
            return;
        }

        // Transform 기반 이동 + Raycast 충돌 감지
        float moveDistance = Speed.Value * (float)TimeManager.TickDelta;
        Vector3 movement = Direction.Value.normalized * moveDistance;

        // Fishnet: 기본 Physics.Raycast 사용 (세션당 별도 프로세스이므로 물리 씬 분리 불필요)
        if (Physics.Raycast(transform.position, Direction.Value, out RaycastHit hit, moveDistance, HitLayerMask.Value))
        {
            HandleHit(hit);
        }
        else
        {
            // 충돌 없으면 이동
            transform.position += movement;
        }
    }

    #endregion

    #region Collision Ignore

    /// <summary>
    /// 발사자 Collider와 충돌 무시
    /// </summary>
    private void IgnoreCollisionWithOwner(NetworkObject ownerObject)
    {
        if (_projectileCollider == null)
        {
            Debug.LogWarning("[Projectile] Collider가 없습니다!");
            return;
        }

        // Why: 발사자의 모든 Collider 찾기 (본체 + 자식 포함)
        Collider[] ownerColliders = ownerObject.GetComponentsInChildren<Collider>();

        foreach (Collider ownerCollider in ownerColliders)
        {
            // Why: Trigger는 제외 (데미지 판정용 Trigger 등)
            if (ownerCollider.isTrigger) continue;

            // Why: 영구적으로 충돌 무시
            Physics.IgnoreCollision(_projectileCollider, ownerCollider, true);
        }
    }

    #endregion

    #region Collision Handling

    private void HandleHit(RaycastHit hit)
    {
        if (_hasHit) return;

        if (OwnerConnection.Value == null)
        {
            _hasHit = true;
            ServerManager.Despawn(gameObject);
            return;
        }

        _hasHit = true;

        // Why: 충돌 지점으로 이동 (정확한 히트)
        transform.position = hit.point;

        // Why: 데미지 처리
        IDamageable damageable = hit.collider.GetComponent<IDamageable>();
        if (damageable != null && damageable.IsAlive)
        {
            // Why: 실제 피격 위치를 함께 전달하여 damage indicator가 정확한 위치에 표시되도록 함
            damageable.TakeDamage(Damage.Value, OwnerConnection.Value, hit.point);
        }

        // Why: 히트 이펙트 생성
        RPC_SpawnHitEffect(hit.point, hit.normal);

        // Why: RPC 전송 후 0.1초 뒤에 Despawn (RPC가 Client에 도달할 시간 확보)
        _despawnScheduled.Value = true;
        _despawnTime.Value = Time.time + 0.1f;
    }

    #endregion

    #region RPC Methods

    [ObserversRpc]
    private void RPC_SpawnHitEffect(Vector3 position, Vector3 normal)
    {
        if (_hitEffectPrefab == null) return;

        GameObject effect = Instantiate(_hitEffectPrefab, position, Quaternion.LookRotation(normal));
        Destroy(effect, 2f);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 서버에서 발사체를 초기화합니다.
    /// </summary>
    public void Initialize(Vector3 direction, float speed, float damage, NetworkConnection owner, float maxRange, int hitLayerMask)
    {
        Direction.Value = direction.normalized;
        Speed.Value = speed;
        Damage.Value = damage;
        OwnerConnection.Value = owner;
        MaxRange.Value = maxRange;
        HitLayerMask.Value = hitLayerMask;
        IsInitialized.Value = true;
        
        _lifeEndTime.Value = Time.time + _maxLifetime;
        _startPosition.Value = transform.position;
    }

    #endregion
}
