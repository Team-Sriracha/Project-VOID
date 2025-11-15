using Fusion;
using UnityEngine;

/// <summary>
/// 네트워크 동기화된 무기 시스템입니다.
/// </summary>
public class NetworkedWeapon : NetworkBehaviour
{
    #region Serialized Fields

    [Header("무기 설정")]
    [SerializeField] private WeaponData _weaponData;

    [Header("무기 장착 위치")]
    [SerializeField] private Transform _weaponAttachPoint;

    [Header("스탯 참조")]
    [SerializeField] private PlayerStats _playerStats;

    #endregion

    #region Private Fields

    // Why: FirePoint는 현재 장착된 무기의 FirePoint를 동적으로 찾음
    private Transform _firePoint;
    private NetworkedItem _currentEquippedItem; // 현재 장착된 NetworkedItem
    private AimVisualizer _aimVisualizer;

    #endregion

    #region Networked Properties

    [Networked] private TickTimer CooldownTimer { get; set; }

    // Why: 탄약 시스템 (GunData에만 적용)
    [Networked] public int CurrentAmmo { get; set; }
    [Networked] public int TotalAmmo { get; set; }
    [Networked] public NetworkBool IsReloading { get; set; }
    [Networked] public TickTimer ReloadTimer { get; set; }

    #endregion

    #region Properties

    public WeaponData CurrentWeaponData => _weaponData;
    public TickTimer AttackCooldownTimer => CooldownTimer;
    public Transform WeaponAttachPoint => _weaponAttachPoint;
    private bool CooldownExpired => CooldownTimer.ExpiredOrNotRunning(Runner);

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        // Why: AimVisualizer 참조 가져오기
        _aimVisualizer = GetComponent<AimVisualizer>();

        // Why: WeaponData가 없으면 PlayerStats의 기본 무기 사용
        if (_weaponData == null && _playerStats != null)
        {
            _weaponData = _playerStats.DefaultWeapon;
        }

        // Why: FirePoint는 UpdateFirePoint()에서 동적으로 설정됨
        _firePoint = _weaponAttachPoint != null ? _weaponAttachPoint : transform;

        // Why: 탄약 초기화 (GunData에만 적용)
        if (HasStateAuthority && _weaponData is GunData gunData)
        {
            CurrentAmmo = gunData.MagazineSize;
            TotalAmmo = gunData.MaxAmmo;
            IsReloading = false;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Why: 장착된 무기 모델이 변경되었는지 확인하고 FirePoint 업데이트 (모든 클라이언트에서 실행)
        UpdateFirePoint();

        // Why: State Authority(서버)만 재장전 타이머 처리
        if (!HasStateAuthority) return;

        if (IsReloading && ReloadTimer.ExpiredOrNotRunning(Runner))
        {
            CompleteReload();
        }
    }

    public override void Render()
    {
        // Why: 프레임 단위 정확도로 재장전 완료 타이밍 개선 (FixedUpdateNetwork는 틱 단위)
        if (HasStateAuthority && IsReloading && ReloadTimer.ExpiredOrNotRunning(Runner))
        {
            CompleteReload();
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // Why: 모델 관리 안 함 (NetworkedItem이 담당)
    }

    #endregion

    #region Weapon Management

    /// <summary>
    /// 무기 데이터를 변경합니다 (서버만).
    /// Why: 모델 관리는 NetworkedItem이 담당, 여기서는 데이터만 변경
    /// </summary>
    /// <param name="newWeaponData">새 무기 데이터 (null이면 무기 제거)</param>
    public void SetWeapon(WeaponData newWeaponData)
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("[NetworkedWeapon] SetWeapon은 서버만 호출할 수 있습니다!");
            return;
        }

        // Why: 모든 클라이언트에 무기 데이터 변경 알림
        string weaponID = newWeaponData != null ? newWeaponData.ItemID : "";
        RPC_SyncWeaponData(weaponID);
    }

    /// <summary>
    /// 모든 클라이언트에 무기 데이터를 동기화합니다.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SyncWeaponData(string weaponID, RpcInfo info = default)
    {
        // Why: ItemID로 WeaponData 로드
        if (string.IsNullOrEmpty(weaponID))
        {
            _weaponData = null;
            Debug.Log("[NetworkedWeapon] 무기 데이터 제거됨");
            return;
        }

        ItemData itemData = ItemDatabase.GetItem(weaponID);
        if (itemData is WeaponData weaponData)
        {
            _weaponData = weaponData;

            // 탄약 초기화 (GunData인 경우, 서버만)
            if (HasStateAuthority && _weaponData is GunData gunData)
            {
                CurrentAmmo = gunData.MagazineSize;
                TotalAmmo = gunData.MaxAmmo;
                IsReloading = false;
                Debug.Log($"[NetworkedWeapon] GunData 설정: {gunData.WeaponName}, Ammo: {CurrentAmmo}/{TotalAmmo}");
            }
            else if (_weaponData is MeleeWeaponData meleeData)
            {
                Debug.Log($"[NetworkedWeapon] MeleeWeaponData 설정: {meleeData.WeaponName}");
            }

            Debug.Log($"[NetworkedWeapon] 무기 데이터 동기화: {_weaponData.WeaponName}, Type: {_weaponData.GetType().Name}, HasStateAuthority: {HasStateAuthority}");
        }
        else
        {
            Debug.LogError($"[NetworkedWeapon] ItemID '{weaponID}'는 WeaponData가 아닙니다!");
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 무기를 발사합니다. 무기 타입에 따라 원거리/근거리 공격을 실행합니다.
    /// </summary>
    public void Fire(Vector3 direction)
    {
        // Why: State Authority(서버)만 발사 처리
        if (!HasStateAuthority || !CooldownExpired || IsReloading) return;

        if (_weaponData is GunData gunData)
        {
            // Why: 탄약이 있을 때만 발사
            if (CurrentAmmo <= 0) return;

            FireProjectile(gunData, direction);
            CurrentAmmo--;

            // Why: 탄창이 비었으면 자동 재장전
            if (CurrentAmmo == 0)
            {
                Reload();
            }
        }
        else if (_weaponData is MeleeWeaponData meleeData)
        {
            ExecuteMeleeAttack(meleeData, direction);
        }

        SetCooldown();
    }

    /// <summary>
    /// 재장전을 시작합니다. (GunData에만 적용)
    /// </summary>
    public void Reload()
    {
        // Why: State Authority(서버)만 재장전 처리
        if (!HasStateAuthority) return;

        if (_weaponData is not GunData gunData) return;

        // Why: 이미 재장전 중이거나 탄창이 가득 찬 경우 무시
        if (IsReloading || CurrentAmmo >= gunData.MagazineSize) return;

        // Why: 여분 탄약이 없으면 재장전 불가
        if (TotalAmmo <= 0) return;

        IsReloading = true;
        ReloadTimer = TickTimer.CreateFromSeconds(Runner, gunData.ReloadTime);
    }

    #endregion

    #region Reload

    /// <summary>
    /// 재장전을 완료합니다.
    /// </summary>
    private void CompleteReload()
    {
        if (_weaponData is not GunData gunData) return;

        // Why: 탄창을 채울 만큼의 탄약 계산
        int ammoNeeded = gunData.MagazineSize - CurrentAmmo;
        int ammoToReload = Mathf.Min(ammoNeeded, TotalAmmo);

        CurrentAmmo += ammoToReload;
        TotalAmmo -= ammoToReload;
        IsReloading = false;
    }

    #endregion

    #region Weapon Actions

    private void FireProjectile(GunData gunData, Vector3 direction)
    {
        // Why: 발사체는 AimPoint에서 생성 (실제 공격 시작 위치)
        Vector3 spawnPos = _aimVisualizer != null && _aimVisualizer.AimPoint != null
            ? _aimVisualizer.AimPoint.position
            : transform.position;
        Quaternion spawnRot = Quaternion.LookRotation(direction);

        // Why: onBeforeSpawned 콜백에서 Networked 프로퍼티 초기화
        Runner.Spawn(
            gunData.ProjectilePrefab,
            spawnPos,
            spawnRot,
            inputAuthority: PlayerRef.None, // Why: 발사체는 InputAuthority 불필요
            (runner, obj) =>
            {
                var projectile = obj.GetComponent<Projectile>();
                if (projectile != null)
                {
                    projectile.Direction = direction;
                    projectile.Speed = gunData.ProjectileSpeed;
                    projectile.Damage = gunData.Damage;
                    projectile.Owner = Object.InputAuthority;
                    projectile.MaxRange = gunData.Range;
                    projectile.HitLayerMask = gunData.HitLayers.value;
                    projectile.IsInitialized = true;
                }
            }
        );

        // Why: 이펙트는 FirePoint에서 생성 (무기 총구 위치)
        RPC_SpawnMuzzleFlash();
    }

    private void ExecuteMeleeAttack(MeleeWeaponData meleeData, Vector3 direction)
    {
        // Why: 근접 공격은 AimPoint에서 시작 (실제 공격 시작 위치)
        Vector3 attackPos = _aimVisualizer != null && _aimVisualizer.AimPoint != null
            ? _aimVisualizer.AimPoint.position
            : transform.position;
        Collider[] hits = Physics.OverlapSphere(attackPos, meleeData.Range, meleeData.HitLayers);
        float halfAngle = meleeData.AttackAngle / 2f;

        Debug.Log($"[NetworkedWeapon] ExecuteMeleeAttack - 감지된 충돌체: {hits.Length}개, 위치: {attackPos}, 범위: {meleeData.Range}");

        foreach (Collider hit in hits)
        {
            Debug.Log($"[NetworkedWeapon] 충돌체 발견: {hit.gameObject.name}, 레이어: {LayerMask.LayerToName(hit.gameObject.layer)}");

            // Why: 자기 자신은 제외
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                Debug.Log($"[NetworkedWeapon] {hit.gameObject.name}은 자기 자신이라 스킵");
                continue;
            }

            Vector3 toTarget = (hit.transform.position - attackPos).normalized;
            float angle = Vector3.Angle(direction, toTarget);

            Debug.Log($"[NetworkedWeapon] {hit.gameObject.name} - 각도: {angle}, 제한각도: {halfAngle}");

            // Why: 공격 각도 내에 있는 대상만 히트
            if (angle <= halfAngle)
            {
                IDamageable damageable = hit.GetComponent<IDamageable>();
                Debug.Log($"[NetworkedWeapon] {hit.gameObject.name} - IDamageable: {damageable != null}, IsAlive: {damageable?.IsAlive}");

                if (damageable != null && damageable.IsAlive)
                {
                    Debug.Log($"[NetworkedWeapon] {hit.gameObject.name}에 데미지 {meleeData.Damage} 적용!");
                    damageable.TakeDamage(meleeData.Damage, Object.InputAuthority);

                    // Why: 실제 피격 위치(콜라이더 표면)에 이펙트 생성
                    Vector3 hitPoint = hit.ClosestPoint(attackPos);
                    RPC_SpawnMeleeHitEffect(hitPoint, -toTarget);
                }
            }
        }

        // Why: 이펙트는 FirePoint에서 생성 (무기 위치)
        RPC_SpawnMeleeSwing(direction);
    }

    #endregion

    #region Cooldown

    private void SetCooldown()
    {
        if (_weaponData != null)
        {
            CooldownTimer = TickTimer.CreateFromSeconds(Runner, _weaponData.AttackDelay);
        }
    }

    #endregion

    #region FirePoint Management

    /// <summary>
    /// 현재 장착된 무기의 FirePoint를 동적으로 찾아 업데이트합니다.
    /// Why: NetworkedItem이 모델 관리를 담당하므로, 부착된 무기에서 FirePoint를 찾아야 함
    /// </summary>
    private void UpdateFirePoint()
    {
        if (_weaponAttachPoint == null)
        {
            _firePoint = transform;
            Debug.LogWarning("[NetworkedWeapon] WeaponAttachPoint가 null입니다!");
            return;
        }

        // Why: AttachPoint에 부착된 NetworkedItem 찾기
        NetworkedItem attachedItem = _weaponAttachPoint.GetComponentInChildren<NetworkedItem>();

        // Why: 장착된 아이템이 변경된 경우에만 FirePoint 업데이트
        if (attachedItem != _currentEquippedItem)
        {
            _currentEquippedItem = attachedItem;

            if (attachedItem != null)
            {
                // Why: 아이템이 완전히 부착되었는지 확인 (부모가 WeaponAttachPoint이고 localPosition이 설정되었는지)
                if (attachedItem.transform.parent != _weaponAttachPoint)
                {
                    Debug.Log($"[NetworkedWeapon] 아이템이 아직 부착 중입니다. FirePoint 업데이트 대기...");
                    // Why: 아이템이 완전히 부착되지 않았으므로 _currentEquippedItem을 null로 되돌려 다음 프레임에 재시도
                    _currentEquippedItem = null;
                    return;
                }

                // Why: 무기 모델에 "FirePoint" 자식이 있으면 사용, 없으면 모델 자체 사용
                Transform firePointChild = attachedItem.transform.Find("FirePoint");
                _firePoint = firePointChild != null ? firePointChild : attachedItem.transform;

                Debug.Log($"[NetworkedWeapon] FirePoint 업데이트 - WeaponData: {_weaponData?.WeaponName}, FirePoint Child 발견: {firePointChild != null}, FirePoint: {_firePoint.name}");
            }
            else
            {
                // Why: 장착된 무기가 없으면 (기본 무기) AimVisualizer의 AimPoint 사용
                if (_aimVisualizer != null && _aimVisualizer.AimPoint != null)
                {
                    _firePoint = _aimVisualizer.AimPoint;
                    Debug.Log($"[NetworkedWeapon] 기본 무기 - FirePoint를 AimPoint로 설정: {_firePoint.name}");
                }
                else
                {
                    _firePoint = _weaponAttachPoint;
                    Debug.Log($"[NetworkedWeapon] 기본 무기 - FirePoint를 AttachPoint로 설정: {_firePoint.name}");
                }
            }
        }
    }

    #endregion

    #region RPC Methods

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnMuzzleFlash()
    {
        if (_weaponData is not GunData gunData || gunData.MuzzleFlashPrefab == null) return;

        // Why: 기본 무기인 경우 (_currentEquippedItem이 null) AttachPoint를 parent로 사용
        bool isDefaultWeapon = _currentEquippedItem == null;
        Transform parent = isDefaultWeapon ? _weaponAttachPoint : (_firePoint != null ? _firePoint : transform);
        Vector3 pos = parent.position;
        Quaternion rot = parent.rotation;

        GameObject flash = Instantiate(gunData.MuzzleFlashPrefab, pos, rot, parent);
        Destroy(flash, 0.5f);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnMeleeSwing(Vector3 direction)
    {
        if (_weaponData is not MeleeWeaponData meleeData || meleeData.SwingEffectPrefab == null) return;

        // Why: 기본 무기인 경우 (_currentEquippedItem이 null) AttachPoint를 parent로 사용
        bool isDefaultWeapon = _currentEquippedItem == null;
        Transform parent = isDefaultWeapon ? _weaponAttachPoint : (_firePoint != null ? _firePoint : transform);
        Vector3 pos = parent.position;
        Quaternion rot = Quaternion.LookRotation(direction);

        GameObject swing = Instantiate(meleeData.SwingEffectPrefab, pos, rot, parent);
        Destroy(swing, 1f);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnMeleeHitEffect(Vector3 position, Vector3 normal)
    {
        if (_weaponData is not MeleeWeaponData meleeData || meleeData.HitEffectPrefab == null) return;

        GameObject hitEffect = Instantiate(meleeData.HitEffectPrefab, position, Quaternion.LookRotation(normal));
        Destroy(hitEffect, 1f);
    }

    #endregion
}
