using Fusion;
using UnityEngine;

/// <summary>
/// 네트워크 동기화되는 발사체입니다.
/// 직선으로 이동하며 충돌 시 데미지를 줍니다.
/// </summary>
public class Projectile : NetworkBehaviour
{
    #region Serialized Fields

    [Header("발사체 설정")]
    [SerializeField] private float _maxLifetime = 5f;

    [Header("시각 효과")]
    [SerializeField] private GameObject _hitEffectPrefab;

    #endregion

    #region Networked Properties

    [Networked] public Vector3 Direction { get; set; }
    [Networked] public float Speed { get; set; }
    [Networked] public float Damage { get; set; }
    [Networked] public PlayerRef Owner { get; set; }
    [Networked] public float MaxRange { get; set; }
    [Networked] public int HitLayerMask { get; set; }
    [Networked] public NetworkBool IsInitialized { get; set; }
    [Networked] private TickTimer LifeTimer { get; set; }
    [Networked] private Vector3 StartPosition { get; set; }

    // Why: RPC 전송 후 Despawn까지 딜레이 (RPC가 클라이언트에 도달할 시간 확보)
    [Networked] private TickTimer DespawnTimer { get; set; }

    #endregion

    #region Private Fields

    private bool _hasHit;
    private Collider _projectileCollider;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        _hasHit = false;
        _projectileCollider = GetComponent<Collider>();

        // Why: 발사자의 모든 Collider와 충돌 무시 설정
        if (Runner.TryGetPlayerObject(Owner, out NetworkObject ownerObject))
        {
            IgnoreCollisionWithOwner(ownerObject);
        }

        if (HasStateAuthority && IsInitialized)
        {
            LifeTimer = TickTimer.CreateFromSeconds(Runner, _maxLifetime);
            StartPosition = transform.position;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Why: Runner가 종료 중이거나 실행 중이 아니면 처리하지 않음
        if (Runner == null || !Runner.IsRunning) return;

        // Why: State Authority(서버)만 처리
        if (!HasStateAuthority) return;

        // Why: DespawnTimer가 시작되면 충돌로 인한 제거가 예약된 상태
        //      다른 Despawn 조건(Lifetime, Range)을 무시하여 중복 제거 방지
        //      RPC가 클라이언트에 도달할 시간(3틱) 확보 후 제거
        if (DespawnTimer.IsRunning)
        {
            if (DespawnTimer.Expired(Runner))
            {
                Runner.Despawn(Object);
            }
            return;
        }

        if (!IsInitialized || _hasHit) return;

        if (LifeTimer.Expired(Runner))
        {
            Runner.Despawn(Object);
            return;
        }

        // Why: 무기의 Range만큼만 날아감
        float traveledDistance = Vector3.Distance(StartPosition, transform.position);
        if (traveledDistance >= MaxRange)
        {
            Runner.Despawn(Object);
            return;
        }

        // ✅ Transform 기반 이동 + Raycast 충돌 감지
        float moveDistance = Speed * Runner.DeltaTime;
        Vector3 movement = Direction.normalized * moveDistance;

        // Why: 이동 경로에 충돌 체크 (HitLayerMask 적용)
        if (Physics.Raycast(transform.position, Direction, out RaycastHit hit, moveDistance, HitLayerMask))
        {
            // 충돌 처리
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
    /// 발사자의 모든 Collider와 충돌을 무시합니다.
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

        int ignoredCount = 0;
        foreach (Collider ownerCollider in ownerColliders)
        {
            // Why: Trigger는 제외 (데미지 판정용 Trigger 등)
            if (ownerCollider.isTrigger) continue;

            // Why: 영구적으로 충돌 무시
            Physics.IgnoreCollision(_projectileCollider, ownerCollider, true);
            ignoredCount++;
        }
    }

    #endregion

    #region Collision Handling

    private void HandleHit(RaycastHit hit)
    {
        if (_hasHit) return;

        _hasHit = true;

        Debug.Log($"[Projectile] HandleHit - 충돌: {hit.collider.gameObject.name}, 레이어: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");

        // Why: 충돌 지점으로 이동 (정확한 히트)
        transform.position = hit.point;

        // Why: 데미지 처리
        IDamageable damageable = hit.collider.GetComponent<IDamageable>();
        Debug.Log($"[Projectile] {hit.collider.gameObject.name} - IDamageable: {damageable != null}, IsAlive: {damageable?.IsAlive}");

        if (damageable != null && damageable.IsAlive)
        {
            Debug.Log($"[Projectile] {hit.collider.gameObject.name}에 데미지 {Damage} 적용!");
            damageable.TakeDamage(Damage, Owner);
        }

        // Why: 히트 이펙트 생성
        RPC_SpawnHitEffect(hit.point, hit.normal);

        // Why: RPC 전송 후 3틱 뒤에 Despawn (RPC가 Client에 도달할 시간 확보)
        DespawnTimer = TickTimer.CreateFromTicks(Runner, 3);
    }

    #endregion

    #region RPC Methods

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnHitEffect(Vector3 position, Vector3 normal)
    {
        if (_hitEffectPrefab == null) return;

        GameObject effect = Instantiate(_hitEffectPrefab, position, Quaternion.LookRotation(normal));
        Destroy(effect, 2f);
    }

    #endregion
}
