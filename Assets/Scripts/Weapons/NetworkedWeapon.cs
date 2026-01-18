using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// 네트워크 동기화된 무기 시스템
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
    
    // 타이머 (서버에서만 사용)
    private float _cooldownEndTime;
    private float _reloadEndTime;

    #endregion

    #region SyncVars

    // 탄약 시스템 (GunData에만 적용)
    public readonly SyncVar<int> CurrentAmmo = new();
    public readonly SyncVar<int> TotalAmmo = new();
    public readonly SyncVar<bool> IsReloading = new();

    // 발사 애니메이션 동기화 (Counter 패턴)
    public readonly SyncVar<int> FireCounter = new();

    #endregion

    #region Properties

    public WeaponData CurrentWeaponData => _weaponData;
    public Transform WeaponAttachPoint => _weaponAttachPoint;
    private bool CooldownExpired => Time.time >= _cooldownEndTime;
    
    /// <summary>
    /// 재장전 남은 시간 (UI용)
    /// </summary>
    public float ReloadRemainingTime => IsReloading.Value ? Mathf.Max(0f, _reloadEndTime - Time.time) : 0f;
    
    /// <summary>
    /// 공격 쿨다운 남은 시간 (UI용)
    /// </summary>
    public float AttackCooldownRemainingTime => CooldownExpired ? 0f : Mathf.Max(0f, _cooldownEndTime - Time.time);

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        
        _aimVisualizer = GetComponent<AimVisualizer>();
        _animationController = GetComponent<PlayerAnimationController>();

        if (_weaponData == null && _playerStats != null)
        {
            _weaponData = _playerStats.DefaultWeapon;
        }

        _firePoint = _weaponAttachPoint != null ? _weaponAttachPoint : transform;

        // OnChange 이벤트 구독
        FireCounter.OnChange += OnFireCounterChanged;

        if (IsServerInitialized && _weaponData is GunData gunData)
        {
            CurrentAmmo.Value = gunData.MagazineSize;
            TotalAmmo.Value = gunData.MaxAmmo;
            IsReloading.Value = false;
        }

        ResetFirePoint();
        TimeManager.OnTick += OnTick;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        FireCounter.OnChange -= OnFireCounterChanged;
        if (TimeManager != null)
        {
            TimeManager.OnTick -= OnTick;
        }
    }

    private void OnTick()
    {
        // 서버만 재장전 타이머 처리
        if (!IsServerInitialized) return;

        if (IsReloading.Value && Time.time >= _reloadEndTime)
        {
            CompleteReload();
        }
    }

    #endregion

    #region Weapon Management

    /// <summary>
    /// 무기 데이터 변경 (서버 전용)
    /// 모델 관리는 NetworkedItem이 담당, 여기서는 데이터만 변경
    /// </summary>
    /// <param name="newWeaponData">새 무기 데이터 (null이면 무기 제거)</param>
    public void SetWeapon(WeaponData newWeaponData, int currentAmmo = -1, int totalAmmo = -1)
    {
        if (!IsServerInitialized)
        {
            Debug.LogWarning("[NetworkedWeapon] SetWeapon은 서버만 호출 가능");
            return;
        }

        // 다른 무기로 전환하거나 드랍 시 진행 중인 재장전을 즉시 종료 (근접 공격이 막히는 이슈 방지)
        ResetReloadState();

        // 무기 데이터 설정
        _weaponData = newWeaponData;
        string weaponID = newWeaponData != null ? newWeaponData.ItemID : "";

        // 탄약 초기화 (서버에서 직접 SyncVar 설정)
        if (newWeaponData is GunData gunData)
        {
            int appliedCurrent = currentAmmo >= 0 ? Mathf.Clamp(currentAmmo, 0, gunData.MagazineSize) : gunData.MagazineSize;
            int appliedTotal = totalAmmo >= 0 ? Mathf.Clamp(totalAmmo, 0, gunData.MaxAmmo) : gunData.MaxAmmo;

            CurrentAmmo.Value = appliedCurrent;
            TotalAmmo.Value = appliedTotal;
        }
        else if (newWeaponData == null)
        {
            CurrentAmmo.Value = 0;
            TotalAmmo.Value = 0;
            OnWeaponDetached(); // 무기 제거 시 FirePoint 초기화
        }

        IsReloading.Value = false;

        // 클라이언트에 무기 데이터 동기화 (탄약은 SyncVar가 자동 동기화)
        RPC_SyncWeaponData(weaponID);
    }

    /// <summary>
    /// 무기 변경 시 재장전 상태와 타이머 초기화
    /// </summary>
    private void ResetReloadState()
    {
        IsReloading.Value = false;
        _reloadEndTime = 0f;
    }

    /// <summary>
    /// 모든 클라이언트에 무기 데이터 동기화
    /// </summary>
    [ObserversRpc]
    private void RPC_SyncWeaponData(string weaponID)
    {
        // ItemID로 WeaponData 로드 (클라이언트용)
        if (string.IsNullOrEmpty(weaponID))
        {
            _weaponData = null;
            return;
        }

        ItemData itemData = ItemDatabase.GetItem(weaponID);
        if (itemData is WeaponData weaponData)
        {
            _weaponData = weaponData;
        }
        else
        {
            Debug.LogError($"[NetworkedWeapon] ItemID '{weaponID}'는 WeaponData가 아닙니다!");
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 무기 발사. 무기 타입에 따라 원거리/근거리 공격 실행
    /// </summary>
    public void Fire(Vector3 direction)
    {
        if (!IsServerInitialized || !CooldownExpired || IsReloading.Value) return;

        if (_weaponData is GunData gunData)
        {
            if (CurrentAmmo.Value <= 0) return;

            bool projectileSpawned = FireProjectile(gunData, direction);
            if (!projectileSpawned) return;

            CurrentAmmo.Value--;
            FireCounter.Value++; // Counter 증가로 모든 클라이언트에서 애니메이션 재생

            if (CurrentAmmo.Value == 0)
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
    /// 재장전 시작 (GunData에만 적용)
    /// </summary>
    public void Reload()
    {
        // 서버만 재장전 처리
        if (!IsServerInitialized) return;

        if (_weaponData is not GunData gunData) return;

        // 이미 재장전 중이거나 탄창이 가득 찬 경우 무시
        if (IsReloading.Value || CurrentAmmo.Value >= gunData.MagazineSize) return;

        // 여분 탄약이 없으면 재장전 불가
        if (TotalAmmo.Value <= 0) return;

        IsReloading.Value = true;
        _reloadEndTime = Time.time + gunData.ReloadTime;

        // Reload 애니메이션 재생 (Counter 패턴, ReloadTime 전달하여 속도 자동 조절)
        if (_animationController != null)
        {
            _animationController.PlayReload(gunData.ReloadTime);
        }
    }

    #endregion

    #region Reload

    /// <summary>
    /// 재장전 완료
    /// </summary>
    private void CompleteReload()
    {
        if (_weaponData is not GunData gunData) return;

        // 탄창을 채울 만큼의 탄약 계산
        int ammoNeeded = gunData.MagazineSize - CurrentAmmo.Value;
        int ammoToReload = Mathf.Min(ammoNeeded, TotalAmmo.Value);

        CurrentAmmo.Value += ammoToReload;
        TotalAmmo.Value -= ammoToReload;
        TotalAmmo.Value = Mathf.Max(TotalAmmo.Value, 0);
        IsReloading.Value = false;
    }

    #endregion

    #region Weapon Actions

    private bool FireProjectile(GunData gunData, Vector3 direction)
    {
        if (!IsServerInitialized) return false;

        Vector3 spawnPos = transform.position + Vector3.up * _projectileSpawnHeight + direction * _projectileSpawnDistance;
        int pelletCount = gunData.PelletCount;
        float spreadAngle = gunData.SpreadAngle;
        bool anySpawned = false;

        for (int i = 0; i < pelletCount; i++)
        {
            Vector3 pelletDirection = GetSpreadDirection(direction, spreadAngle);
            Quaternion spawnRot = Quaternion.LookRotation(pelletDirection);

            NetworkObject spawnedObject = null;

            if (gunData.ProjectilePrefab != null)
            {
                GameObject projectileObj = Instantiate(gunData.ProjectilePrefab, spawnPos, spawnRot);
                spawnedObject = projectileObj.GetComponent<NetworkObject>();
                
                if (spawnedObject != null)
                {
                    InstanceFinder.ServerManager.Spawn(spawnedObject);
                    InitializeProjectile(spawnedObject, gunData, pelletDirection);
                }
                else
                {
                    Debug.LogError($"[NetworkedWeapon] 투사체에 NetworkObject 컴포넌트가 없습니다!");
                    Destroy(projectileObj);
                }
            }
            else
            {
                Debug.LogError($"[NetworkedWeapon] ProjectilePrefab이 null입니다!");
            }

            if (spawnedObject != null)
            {
                anySpawned = true;
            }
        }

        if (!anySpawned)
        {
            return false;
        }

        RPC_SpawnMuzzleFlash();
        return true;
    }

    private Vector3 GetSpreadDirection(Vector3 baseDirection, float spreadAngle)
    {
        if (spreadAngle <= 0f) return baseDirection;

        float halfSpread = spreadAngle * 0.5f;
        float randomYaw = Random.Range(-halfSpread, halfSpread);
        // Pitch를 0으로 고정하여 수평으로만 퍼지도록 (Y 높이 유지)
        float randomPitch = 0f;

        Quaternion spreadRotation = Quaternion.Euler(randomPitch, randomYaw, 0f);
        Quaternion baseRotation = Quaternion.LookRotation(baseDirection);

        return (baseRotation * spreadRotation) * Vector3.forward;
    }

    private void ExecuteMeleeAttack(MeleeWeaponData meleeData, Vector3 direction)
    {
        // 근접 공격은 AimPoint에서 시작 (실제 공격 시작 위치)
        Vector3 attackPos = _aimVisualizer != null && _aimVisualizer.AimPoint != null
            ? _aimVisualizer.AimPoint.position
            : transform.position + Vector3.up * 1.5f;
        
        // Fishnet: 세션당 별도 프로세스이므로 기본 Physics 사용
        Collider[] hits = Physics.OverlapSphere(attackPos, meleeData.Range, meleeData.HitLayers);

        float halfAngle = meleeData.AttackAngle / 2f;

        foreach (Collider hit in hits)
        {
            // 자기 자신은 제외
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            Vector3 hitPoint = hit.ClosestPoint(attackPos);
            Vector3 toTarget = (hitPoint - attackPos).normalized;
            float angle = Vector3.Angle(direction, toTarget);

            // 공격 각도 내에 있는 대상만 히트
            if (angle <= halfAngle)
            {
                IDamageable damageable = hit.GetComponent<IDamageable>();

                if (damageable != null && damageable.IsAlive)
                {
                    // 실제 피격 위치를 함께 전달하여 damage indicator가 정확한 위치에 표시되도록 함
                    damageable.TakeDamage(meleeData.Damage, Owner, hitPoint);

                    // 실제 피격 위치(콜라이더 표면)에 이펙트 생성
                    RPC_SpawnMeleeHitEffect(hitPoint, -toTarget);
                }
            }
        }

        // 이펙트는 FirePoint에서 생성 (무기 위치)
        RPC_SpawnMeleeSwing(direction);
    }

    private void InitializeProjectile(NetworkObject obj, GunData gunData, Vector3 direction)
    {
        var projectile = obj != null ? obj.GetComponent<Projectile>() : null;
        if (projectile != null)
        {
            // Spawn 이후 호출되므로 transform.position이 정확함
            projectile.Initialize(direction, gunData.ProjectileSpeed, gunData.Damage, Owner, gunData.Range, gunData.HitLayers.value);
        }
    }

    #endregion

    #region Cooldown

    private void SetCooldown()
    {
        if (_weaponData != null)
        {
            _cooldownEndTime = Time.time + _weaponData.AttackDelay;
        }
    }

    #endregion

    #region Weapon Animation

    /// <summary>
    /// FireCounter 변경 시 호출되어 무기 발사 애니메이션 재생 (모든 클라이언트)
    /// </summary>
    private void OnFireCounterChanged(int prev, int next, bool asServer)
    {
        if (asServer) return; // 클라이언트에서만 애니메이션 재생
        
        if (_currentEquippedItem == null)
        {
            return;
        }

        Animator weaponAnimator = _currentEquippedItem.GetComponentInChildren<Animator>();
        if (weaponAnimator != null)
        {
            weaponAnimator.Play("Fire");
        }
    }

    #endregion

    #region FirePoint Management

    /// <summary>
    /// NetworkedItem 부착 시 호출
    /// Event-Driven 방식으로 FirePoint 갱신
    /// </summary>
    public void OnWeaponAttached(NetworkedItem item)
    {
        _currentEquippedItem = item;
        
        if (item != null)
        {
            Transform firePointChild = item.transform.Find("FirePoint");
            _firePoint = firePointChild != null ? firePointChild : item.transform;
        }
        else
        {
            ResetFirePoint();
        }
    }

    /// <summary>
    /// 무기가 제거되었을 때 호출
    /// </summary>
    public void OnWeaponDetached()
    {
        _currentEquippedItem = null;
        ResetFirePoint();
    }

    private void ResetFirePoint()
    {
        if (_aimVisualizer != null && _aimVisualizer.AimPoint != null)
        {
            _firePoint = _aimVisualizer.AimPoint;
        }
        else
        {
            _firePoint = _weaponAttachPoint != null ? _weaponAttachPoint : transform;
        }
    }

    #endregion

    #region RPC Methods

    [ObserversRpc]
    private void RPC_SpawnMuzzleFlash()
    {
        if (_weaponData is not GunData gunData || gunData.MuzzleFlashPrefab == null) return;

        // 기본 무기인 경우 (_currentEquippedItem이 null) AttachPoint를 parent로 사용
        bool isDefaultWeapon = _currentEquippedItem == null;
        Transform parent = isDefaultWeapon ? _weaponAttachPoint : (_firePoint != null ? _firePoint : transform);
        Vector3 pos = parent.position;
        Quaternion rot = parent.rotation;

        GameObject flash = Instantiate(gunData.MuzzleFlashPrefab, pos, rot, parent);
        Destroy(flash, 0.5f);
    }

    [ObserversRpc]
    private void RPC_SpawnMeleeSwing(Vector3 direction)
    {
        if (_weaponData is not MeleeWeaponData meleeData || meleeData.SwingEffectPrefab == null) return;

        // 기본 무기인 경우 (_currentEquippedItem이 null) AttachPoint를 parent로 사용
        bool isDefaultWeapon = _currentEquippedItem == null;
        Transform parent = isDefaultWeapon ? _weaponAttachPoint : (_firePoint != null ? _firePoint : transform);
        Vector3 pos = parent.position;
        Quaternion rot = Quaternion.LookRotation(direction);

        GameObject swing = Instantiate(meleeData.SwingEffectPrefab, pos, rot, parent);
        Destroy(swing, 1f);
    }

    [ObserversRpc]
    private void RPC_SpawnMeleeHitEffect(Vector3 position, Vector3 normal)
    {
        if (_weaponData is not MeleeWeaponData meleeData || meleeData.HitEffectPrefab == null) return;

        GameObject hitEffect = Instantiate(meleeData.HitEffectPrefab, position, Quaternion.LookRotation(normal));
        Destroy(hitEffect, 1f);
    }

    #endregion
}
