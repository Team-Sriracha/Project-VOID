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

    private GameObject _currentWeaponModel;
    private Transform _firePoint;

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
    private bool CooldownExpired => CooldownTimer.ExpiredOrNotRunning(Runner);

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        // Why: WeaponData가 없으면 PlayerStats의 기본 무기 사용
        if (_weaponData == null && _playerStats != null)
        {
            _weaponData = _playerStats.DefaultWeapon;
        }

        if (_weaponData != null && _weaponData.ModelPrefab != null)
        {
            SpawnWeaponModel();
        }
        else
        {
            // Why: ModelPrefab이 없으면 WeaponAttachPoint를 FirePoint로 사용
            _firePoint = _weaponAttachPoint != null ? _weaponAttachPoint : transform;
        }

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
        if (_currentWeaponModel != null)
        {
            Destroy(_currentWeaponModel);
            _currentWeaponModel = null;
            _firePoint = null;
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
        if (_firePoint == null)
        {
            Debug.LogError("[NetworkedWeapon] FirePoint가 null입니다!");
            return;
        }

        Vector3 spawnPos = _firePoint.position;
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
                    projectile.IsInitialized = true;
                }
            }
        );

        RPC_SpawnMuzzleFlash();
    }

    private void ExecuteMeleeAttack(MeleeWeaponData meleeData, Vector3 direction)
    {
        Vector3 attackPos = _firePoint != null ? _firePoint.position : transform.position;
        Collider[] hits = Physics.OverlapSphere(attackPos, meleeData.Range, meleeData.HitLayers);
        float halfAngle = meleeData.AttackAngle / 2f;

        foreach (Collider hit in hits)
        {
            // Why: 자기 자신은 제외
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            Vector3 toTarget = (hit.transform.position - attackPos).normalized;
            float angle = Vector3.Angle(direction, toTarget);

            // Why: 공격 각도 내에 있는 대상만 히트
            if (angle <= halfAngle)
            {
                IDamageable damageable = hit.GetComponent<IDamageable>();
                if (damageable != null && damageable.IsAlive)
                {
                    damageable.TakeDamage(meleeData.Damage, Object.InputAuthority);

                    // Why: 히트 지점에 이펙트 생성
                    Vector3 hitPoint = hit.ClosestPoint(attackPos);
                    Vector3 hitNormal = (hitPoint - attackPos).normalized;
                    RPC_SpawnMeleeHitEffect(hitPoint, hitNormal);
                }
            }
        }

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

    #region Weapon Model

    private void SpawnWeaponModel()
    {
        // Why: 기존 모델이 있으면 제거
        if (_currentWeaponModel != null)
        {
            Destroy(_currentWeaponModel);
        }

        // Why: 무기 모델을 AttachPoint에 인스턴스화 (Prefab의 위치/회전 유지)
        Transform attachPoint = _weaponAttachPoint != null ? _weaponAttachPoint : transform;
        _currentWeaponModel = Instantiate(_weaponData.ModelPrefab, attachPoint);

        // Why: 무기 모델 내부에서 FirePoint 자동 검색 (무기마다 위치가 다름)
        _firePoint = _currentWeaponModel.transform.Find("FirePoint");

        if (_firePoint == null)
        {
            Debug.LogWarning($"[NetworkedWeapon] {_weaponData.WeaponName} 모델에 FirePoint가 없습니다. 무기 루트를 사용합니다.");
            _firePoint = _currentWeaponModel.transform;
        }
    }

    #endregion

    #region RPC Methods

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnMuzzleFlash()
    {
        if (_weaponData is not GunData gunData || gunData.MuzzleFlashPrefab == null) return;

        Vector3 pos = _firePoint != null ? _firePoint.position : transform.position;
        Quaternion rot = _firePoint != null ? _firePoint.rotation : transform.rotation;

        GameObject flash = Instantiate(gunData.MuzzleFlashPrefab, pos, rot);
        Destroy(flash, 0.5f);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnMeleeSwing(Vector3 direction)
    {
        if (_weaponData is not MeleeWeaponData meleeData || meleeData.SwingEffectPrefab == null) return;

        Vector3 pos = _firePoint != null ? _firePoint.position : transform.position;
        Quaternion rot = Quaternion.LookRotation(direction);

        GameObject swing = Instantiate(meleeData.SwingEffectPrefab, pos, rot);
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
