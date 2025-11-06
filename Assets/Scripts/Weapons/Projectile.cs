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
    [SerializeField] private float _minDistanceToCheckHit = 1f;
    [SerializeField] private string _projectileLayerName = "Projectile";
    [SerializeField] private string _ownProjectileLayerName = "PlayerOwnProjectile";

    [Header("시각 효과")]
    [SerializeField] private GameObject _hitEffectPrefab;

    #endregion

    #region Networked Properties

    [Networked] public Vector3 Direction { get; set; }
    [Networked] public float Speed { get; set; }
    [Networked] public float Damage { get; set; }
    [Networked] public PlayerRef Owner { get; set; }
    [Networked] public float MaxRange { get; set; }
    [Networked] public NetworkBool IsInitialized { get; set; }
    [Networked] private TickTimer LifeTimer { get; set; }
    [Networked] private Vector3 StartPosition { get; set; }

    // Why: RPC 전송 후 Despawn까지 딜레이 (RPC가 클라이언트에 도달할 시간 확보)
    [Networked] private TickTimer DespawnTimer { get; set; }

    #endregion

    #region Private Fields

    private bool _hasHit;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        _hasHit = false;

        // Why: 발사 시 본인 전용 Layer로 설정 (본인과 충돌 안함)
        int ownLayer = LayerMask.NameToLayer(_ownProjectileLayerName);
        if (ownLayer != -1)
        {
            gameObject.layer = ownLayer;
        }

        if (HasStateAuthority && IsInitialized)
        {
            LifeTimer = TickTimer.CreateFromSeconds(Runner, _maxLifetime);
            StartPosition = transform.position;
        }
    }

    public override void FixedUpdateNetwork()
    {
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

        // Why: 일정 거리 이동 후 일반 Projectile Layer로 변경 (다른 플레이어와 충돌 가능)
        if (traveledDistance >= _minDistanceToCheckHit && gameObject.layer != LayerMask.NameToLayer(_projectileLayerName))
        {
            int newLayer = LayerMask.NameToLayer(_projectileLayerName);
            gameObject.layer = newLayer;
        }

        // ✅ Transform 기반 이동 + Raycast 충돌 감지
        float moveDistance = Speed * Runner.DeltaTime;
        Vector3 movement = Direction.normalized * moveDistance;

        // Why: 이동 경로에 충돌 체크 (벽 통과 방지)
        if (Physics.Raycast(transform.position, Direction, out RaycastHit hit, moveDistance, ~LayerMask.GetMask(_ownProjectileLayerName)))
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

    #region Collision Handling

    private void HandleHit(RaycastHit hit)
    {
        if (_hasHit) return;

        // Why: 발사한 플레이어 본인과의 충돌 무시
        NetworkObject hitNetworkObject = hit.collider.GetComponentInParent<NetworkObject>();
        if (hitNetworkObject != null && hitNetworkObject.InputAuthority == Owner)
        {
            return;
        }

        _hasHit = true;

        // Why: 충돌 지점으로 이동 (정확한 히트)
        transform.position = hit.point;

        // Why: 데미지 처리
        IDamageable damageable = hit.collider.GetComponent<IDamageable>();
        if (damageable != null && damageable.IsAlive)
        {
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
