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

    [Header("발사체 생성 위치")]
    [Tooltip("플레이어 기준 발사체 생성 높이 (미터)")]
    [SerializeField] private float _projectileSpawnHeight = 1.23f;
    
    [Tooltip("플레이어 앞 발사체 생성 거리 (미터)")]
    [SerializeField] private float _projectileSpawnDistance = 1.5f;

    #endregion

    #region Private Fields

    private Transform _firePoint;
    private NetworkedItem _currentEquippedItem;
    private AimVisualizer _aimVisualizer;
    private PlayerAnimationController _animationController;
    private ProjectilePool _projectilePool;

    #endregion

    #region Networked Properties

    [Networked] private TickTimer CooldownTimer { get; set; }

    // Why: 탄약 시스템 (GunData에만 적용)
    [Networked] public int CurrentAmmo { get; set; }
    [Networked] public int TotalAmmo { get; set; }
    [Networked] public NetworkBool IsReloading { get; set; }
    [Networked] public TickTimer ReloadTimer { get; set; }

    // Why: 발사 애니메이션 동기화 (Counter 패턴)
    [Networked, OnChangedRender(nameof(OnFireCounterChanged))]
    public int FireCounter { get; set; }

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
        _aimVisualizer = GetComponent<AimVisualizer>();
        _animationController = GetComponent<PlayerAnimationController>();
        ServiceLocator.TryGet(Runner, out _projectilePool);

        if (_weaponData == null && _playerStats != null)
        {
            _weaponData = _playerStats.DefaultWeapon;
        }

        _firePoint = _weaponAttachPoint != null ? _weaponAttachPoint : transform;

        if (HasStateAuthority && _weaponData is GunData gunData)
        {
            CurrentAmmo = gunData.MagazineSize;
            TotalAmmo = gunData.MaxAmmo;
            IsReloading = false;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Why: Runner가 종료 중이거나 실행 중이 아니면 처리하지 않음
        if (Runner == null || !Runner.IsRunning) return;

        // Why: 장착된 무기 모델이 변경되었는지 확인하고 FirePoint 업데이트 (모든 클라이언트에서 실행)
        UpdateFirePoint();

        // Why: State Authority(서버)만 재장전 타이머 처리
        if (!HasStateAuthority) return;

        if (IsReloading && ReloadTimer.ExpiredOrNotRunning(Runner))
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
    public void SetWeapon(WeaponData newWeaponData, int currentAmmo = -1, int totalAmmo = -1)
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("[NetworkedWeapon] SetWeapon은 서버만 호출할 수 있습니다!");
            return;
        }

        // Why: 다른 무기로 전환하거나 드랍 시 진행 중인 재장전을 즉시 종료 (근접 공격이 막히는 이슈 방지)
        ResetReloadState();

        // Why: 모든 클라이언트에 무기 데이터 변경 알림
        string weaponID = newWeaponData != null ? newWeaponData.ItemID : "";
        RPC_SyncWeaponData(weaponID, currentAmmo, totalAmmo);
    }

    /// <summary>
    /// 무기 변경 시 재장전 상태와 타이머를 초기화합니다.
    /// </summary>
    private void ResetReloadState()
    {
        IsReloading = false;
        ReloadTimer = TickTimer.None;
    }

    /// <summary>
    /// 모든 클라이언트에 무기 데이터를 동기화합니다.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SyncWeaponData(string weaponID, int ammoCurrent, int ammoTotal, RpcInfo info = default)
    {
        // Why: ItemID로 WeaponData 로드
        if (string.IsNullOrEmpty(weaponID))
        {
            _weaponData = null;
            CurrentAmmo = 0;
            TotalAmmo = 0;
            IsReloading = false;
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
                int appliedCurrent = ammoCurrent >= 0 ? Mathf.Clamp(ammoCurrent, 0, gunData.MagazineSize) : gunData.MagazineSize;
                int appliedTotal = ammoTotal >= 0 ? Mathf.Clamp(ammoTotal, 0, gunData.MaxAmmo) : gunData.MaxAmmo;

                CurrentAmmo = appliedCurrent;
                TotalAmmo = appliedTotal;
                IsReloading = false;
                
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
        if (!HasStateAuthority || !CooldownExpired || IsReloading) return;

        if (_weaponData is GunData gunData)
        {
            if (CurrentAmmo <= 0) return;

            bool projectileSpawned = FireProjectile(gunData, direction);
            if (!projectileSpawned) return;

            CurrentAmmo--;
            FireCounter++; // Why: Counter 증가로 모든 클라이언트에서 애니메이션 재생

            if (CurrentAmmo == 0)
            {
                Reload();
            }
        }
        else if (_weaponData is MeleeWeaponData meleeData)
        {
            ExecuteMeleeAttack(meleeData, direction);

            if (_animationController != null)
            {
                _animationController.PlayPunch();
            }
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

        // Why: Reload 애니메이션 재생 (Counter 패턴, ReloadTime 전달하여 속도 자동 조절)
        if (_animationController != null)
        {
            _animationController.PlayReload(gunData.ReloadTime);
        }
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
        TotalAmmo = Mathf.Max(TotalAmmo, 0);
        IsReloading = false;

        // Why: Trigger 패턴이므로 별도의 종료 호출 불필요 (Has Exit Time으로 자동 복귀)
    }

    #endregion

    #region Weapon Actions

    private bool FireProjectile(GunData gunData, Vector3 direction)
    {
        if (Runner == null || !Runner.IsRunning) return false;

        Vector3 spawnPos = transform.position + Vector3.up * _projectileSpawnHeight + direction * _projectileSpawnDistance;
        int pelletCount = gunData.PelletCount;
        float spreadAngle = gunData.SpreadAngle;
        bool anySpawned = false;

        for (int i = 0; i < pelletCount; i++)
        {
            Vector3 pelletDirection = GetSpreadDirection(direction, spreadAngle);
            Quaternion spawnRot = Quaternion.LookRotation(pelletDirection);

            NetworkObject spawnedObject = null;

            if (_projectilePool != null)
            {
                spawnedObject = _projectilePool.Get(spawnPos, spawnRot);
                InitializeProjectile(spawnedObject, gunData, pelletDirection);
            }
            else
            {
                Vector3 capturedDir = pelletDirection;
                spawnedObject = Runner.Spawn(
                    gunData.ProjectilePrefab,
                    spawnPos,
                    spawnRot,
                    inputAuthority: PlayerRef.None,
                    (runner, obj) =>
                    {
                        InitializeProjectile(obj, gunData, capturedDir);
                    }
                );
            }

            if (spawnedObject != null)
            {
                anySpawned = true;
            }
        }

        if (!anySpawned) return false;

        RPC_SpawnMuzzleFlash();
        return true;
    }

    private Vector3 GetSpreadDirection(Vector3 baseDirection, float spreadAngle)
    {
        if (spreadAngle <= 0f) return baseDirection;

        float halfSpread = spreadAngle * 0.5f;
        float randomYaw = Random.Range(-halfSpread, halfSpread);
        float randomPitch = Random.Range(-halfSpread, halfSpread);

        Quaternion spreadRotation = Quaternion.Euler(randomPitch, randomYaw, 0f);
        Quaternion baseRotation = Quaternion.LookRotation(baseDirection);

        return (baseRotation * spreadRotation) * Vector3.forward;
    }

    private void ExecuteMeleeAttack(MeleeWeaponData meleeData, Vector3 direction)
    {
        // Why: 근접 공격은 AimPoint에서 시작 (실제 공격 시작 위치)
        Vector3 attackPos = _aimVisualizer != null && _aimVisualizer.AimPoint != null
            ? _aimVisualizer.AimPoint.position
            : transform.position + Vector3.up * 1.5f;
        
        // Why: Multi-Peer 환경에서 올바른 Physics 씬에서 OverlapSphere 수행
        Collider[] hits;
        if (Runner.SceneManager != null && Runner.SceneManager.TryGetPhysicsScene3D(out var physicsScene) && physicsScene.IsValid())
        {
            // Multi-Peer: 해당 Runner의 PhysicsScene에서 OverlapSphere
            hits = new Collider[32]; // 최대 32개 충돌체
            int hitCount = physicsScene.OverlapSphere(attackPos, meleeData.Range, hits, meleeData.HitLayers, QueryTriggerInteraction.Ignore);
            System.Array.Resize(ref hits, hitCount);
            Debug.Log($"[NetworkedWeapon] PhysicsScene OverlapSphere - Scene: {physicsScene}, HitCount: {hitCount}, Position: {attackPos}, Range: {meleeData.Range}");
        }
        else
        {
            // Fallback: 기본 Physics.OverlapSphere (Single-Peer)
            hits = Physics.OverlapSphere(attackPos, meleeData.Range, meleeData.HitLayers);
            Debug.LogWarning($"[NetworkedWeapon] Fallback Physics.OverlapSphere used! SceneManager: {Runner.SceneManager != null}");
        }

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

            Vector3 hitPoint = hit.ClosestPoint(attackPos);
            Vector3 toTarget = (hitPoint - attackPos).normalized;
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
                    // Why: 실제 피격 위치를 함께 전달하여 damage indicator가 정확한 위치에 표시되도록 함
                    damageable.TakeDamage(meleeData.Damage, Object.InputAuthority, hitPoint);

                    // Why: 실제 피격 위치(콜라이더 표면)에 이펙트 생성
                    RPC_SpawnMeleeHitEffect(hitPoint, -toTarget);
                }
            }
        }

        // Why: 이펙트는 FirePoint에서 생성 (무기 위치)
        RPC_SpawnMeleeSwing(direction);
    }

    private void InitializeProjectile(NetworkObject obj, GunData gunData, Vector3 direction)
    {
        var projectile = obj != null ? obj.GetComponent<Projectile>() : null;
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

    #region Weapon Animation

    /// <summary>
    /// FireCounter 변경 시 호출되어 무기 발사 애니메이션을 재생합니다 (모든 클라이언트).
    /// </summary>
    private void OnFireCounterChanged()
    {
        if (_currentEquippedItem == null)
        {
            Debug.LogWarning("[NetworkedWeapon] OnFireCounterChanged: _currentEquippedItem is null!");
            return;
        }

        Animator weaponAnimator = _currentEquippedItem.GetComponentInChildren<Animator>();
        if (weaponAnimator != null)
        {
            Debug.Log($"[NetworkedWeapon] Fire Animation Playing - Counter: {FireCounter}, Weapon: {_currentEquippedItem.name}");
            weaponAnimator.Play("Fire");
        }
        else
        {
            Debug.LogWarning($"[NetworkedWeapon] Animator not found on weapon: {_currentEquippedItem.name}");
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
