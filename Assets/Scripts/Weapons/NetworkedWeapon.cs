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

    #region Events

    /// <summary>
    /// 무기 변경 시 발생 (서버)
    /// </summary>
    public event System.Action<WeaponType> OnWeaponChanged;

    #endregion

    #region Private Fields

    private Transform _firePoint;
    private NetworkedItem _currentEquippedItem;
    private AimVisualizer _aimVisualizer;
    private PlayerAnimationController _animationController;
    private PlayerCardSystem _playerCardSystem;
    
    // 타이머 (서버에서만 사용)
    private float _cooldownEndTime;
    private float _reloadEndTime;

    #endregion

    #region SyncVars

    // 탄약 시스템 (GunData에만 적용)
    public readonly SyncVar<int> CurrentAmmo = new();
    public readonly SyncVar<int> TotalAmmo = new();
    public readonly SyncVar<bool> IsReloading = new();

    // 재장전 동기화 (클라이언트 UI용)
    public readonly SyncVar<uint> SyncReloadStartTick = new();
    public readonly SyncVar<float> SyncReloadDuration = new();

    // 발사 애니메이션 동기화 (Counter 패턴)
    public readonly SyncVar<int> FireCounter = new();

    #endregion

    #region Properties

    public WeaponData CurrentWeaponData => _weaponData;
    public Transform WeaponAttachPoint => _weaponAttachPoint;
    private bool CooldownExpired => Time.time >= _cooldownEndTime;
    
    /// <summary>
    /// 재장전 남은 시간 (UI용, 클라이언트 동기화)
    /// </summary>
    public float ReloadRemainingTime
    {
        get
        {
            if (!IsReloading.Value) return 0f;
            if (!IsReloading.Value) return 0f;
            uint passed = TimeManager.Tick - SyncReloadStartTick.Value;
            float elapsed = passed * (float)TimeManager.TickDelta;
            return Mathf.Max(0f, SyncReloadDuration.Value - elapsed);
        }
    }
    
    /// <summary>
    /// 공격 쿨다운 남은 시간 (UI용)
    /// </summary>
    public float AttackCooldownRemainingTime => CooldownExpired ? 0f : Mathf.Max(0f, _cooldownEndTime - Time.time);

    /// <summary>
    /// 등급 배율이 적용된 최종 데미지 (반올림, UI 및 실제 데미지 계산용)
    /// </summary>
    public float CurrentTotalDamage
    {
        get
        {
            if (_weaponData == null) return 0f;
            
            float baseDamage = _weaponData.TotalDamage;
            
            // 장착된 아이템이 있으면 등급 배율 적용
            if (_currentEquippedItem != null)
            {
                var config = ItemTierConfig.Instance;
                if (config != null)
                {
                    float multiplier = config.GetSettings(_currentEquippedItem.Tier.Value).DamageMultiplier;
                    return Mathf.Round(baseDamage * multiplier);
                }
            }
            
            return baseDamage;
        }
    }

    /// <summary>
    /// 최종 공격 속도 (초당 공격 횟수)
    /// </summary>
    public float GetAttackSpeed()
    {
        if (_weaponData == null || _weaponData.AttackDelay <= 0f) return 0f;
        
        float speedMultiplier = _playerCardSystem?.GetAttackSpeedMultiplier() ?? 1f;
        // 원래 딜레이: _weaponData.AttackDelay
        // 실제 딜레이: _weaponData.AttackDelay / speedMultiplier
        // 초당 공격 수: 1 / 실제 딜레이 = speedMultiplier / _weaponData.AttackDelay
        
        return speedMultiplier / _weaponData.AttackDelay;
    }

    /// <summary>
    /// 최종 데미지 (등급 + 카드 보너스 적용)
    /// UI 표시용
    /// </summary>
    public float GetFinalDamage()
    {
        float damage = CurrentTotalDamage;
        if (_playerCardSystem != null)
        {
            float atkBonus = _playerCardSystem.GetStatBonus(StatType.Attack);
            damage *= (1f + atkBonus);
        }
        return damage;
    }

    /// <summary>
    /// 실제 적용되는 재장전 시간 (기본 시간 / 카드 속도 보너스)
    /// UI 표시용
    /// </summary>
    public float EffectiveReloadTime
    {
        get
        {
            if (_weaponData is not GunData gunData) return 0f;
            
            float speedMultiplier = _playerCardSystem?.GetReloadSpeedMultiplier() ?? 1f;
            // 0으로 나누기 방지
            if (speedMultiplier <= 0f) return gunData.ReloadTime;
            
            return gunData.ReloadTime / speedMultiplier;
        }
    }

    /// <summary>
    /// 실제 적용되는 공격 딜레이 (기본 딜레이 / 카드 속도 보너스)
    /// UI 표시용
    /// </summary>
    public float EffectiveAttackDelay
    {
        get
        {
            if (_weaponData == null) return 0f;
            
            float speedMultiplier = _playerCardSystem?.GetAttackSpeedMultiplier() ?? 1f;
            if (speedMultiplier <= 0f) return _weaponData.AttackDelay;
            
            return _weaponData.AttackDelay / speedMultiplier;
        }
    }

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        
        _aimVisualizer = GetComponent<AimVisualizer>();
        _animationController = GetComponent<PlayerAnimationController>();
        _playerCardSystem = GetComponent<PlayerCardSystem>();

        if (_weaponData == null && _playerStats != null)
        {
            _weaponData = _playerStats.DefaultWeapon;
        }

        _firePoint = _weaponAttachPoint != null ? _weaponAttachPoint : transform;

        // OnChange 이벤트 구독
        FireCounter.OnChange += OnFireCounterChanged;

        // PlayerCardSystem 연동 (서버)
        if (IsServerInitialized)
        {
            PlayerCardSystem cardSystem = GetComponent<PlayerCardSystem>();
            if (cardSystem != null)
            {
                OnWeaponChanged += cardSystem.HandleWeaponChange;
            }
        }

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

        // 무기 교체 이벤트 발생 (서버)
        WeaponType newWeaponType = GetWeaponTypeFromData(newWeaponData);
        OnWeaponChanged?.Invoke(newWeaponType);

        // [Anim] 무기별 애니메이션 오버라이드 적용
        if (_animationController != null)
        {
            if (newWeaponData != null)
            {
                _animationController.SetWeaponAnimator(newWeaponData.OverrideController);
            }
            else
            {
                _animationController.SetWeaponAnimator(null);
            }
        }

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
            
            // [Anim] 무기 해제 시 기본값 복구
            if (_animationController != null)
            {
                _animationController.SetWeaponAnimator(null);
            }
            return;
        }

        ItemData itemData = ItemDatabase.GetItem(weaponID);
        if (itemData is WeaponData weaponData)
        {
            _weaponData = weaponData;
            
            // [Anim] 클라이언트에서도 오버라이드 적용
            if (_animationController != null)
            {
                _animationController.SetWeaponAnimator(weaponData.OverrideController);
            }
        }
        else
        {
            Debug.LogError($"[NetworkedWeapon] ItemID '{weaponID}'는 WeaponData가 아닙니다!");
        }
    }

    /// <summary>
    /// WeaponData로부터 무기 타입 결정
    /// </summary>
    private WeaponType GetWeaponTypeFromData(WeaponData data)
    {
        if (data == null) return WeaponType.Ranged; // 기본값
        return data is MeleeWeaponData ? WeaponType.Melee : WeaponType.Ranged;
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
            // 애니메이션 이벤트 사용 시, 여기서는 애니메이션만 재생 (ExecuteMeleeAttack 생략)
            if (meleeData.UseAnimationEvent)
            {
                if (_animationController != null)
                {
                    _animationController.PlayMeleeAttack();
                }
                // 실제 공격은 ServerRpc_MeleeHit에서 수행
            }
            else
            {
                // 즉시 공격 (기존 방식)
                ExecuteMeleeAttack(meleeData, direction);

                if (_animationController != null)
                {
                    _animationController.PlayMeleeAttack();
                }
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

        // 장전속도 보너스 적용 (높을수록 장전 시간 감소)
        float speedMultiplier = _playerCardSystem?.GetReloadSpeedMultiplier() ?? 1f;
        float finalReloadTime = gunData.ReloadTime / speedMultiplier;

        IsReloading.Value = true;
        IsReloading.Value = true;
        SyncReloadStartTick.Value = TimeManager.Tick;
        SyncReloadDuration.Value = finalReloadTime;
        _reloadEndTime = Time.time + finalReloadTime;

        // Reload 애니메이션 재생 (Counter 패턴, ReloadTime 전달하여 속도 자동 조절)
        if (_animationController != null)
        {
            _animationController.PlayReload(finalReloadTime);
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

        // 파이프라인 데이터 생성
        var pipelineData = new FirePipelineData
        {
            BaseDamage = CurrentTotalDamage,
            BaseProjectileCount = gunData.PelletCount,
            BaseRange = gunData.Range,
            FinalDamage = CurrentTotalDamage,
            FinalProjectileCount = gunData.PelletCount,
            FinalRange = gunData.Range,
            Owner = Owner,
            SpreadAngle = gunData.SpreadAngle,
            FireMode = gunData.FireMode, // 발사 모드 전달
            BurstCount = 1, 
            BurstDelay = 0f
        };

        // 카드 시스템 파이프라인 처리
        var cardSystem = GetComponent<PlayerCardSystem>();
        if (cardSystem != null)
        {
            pipelineData = cardSystem.ProcessFire(pipelineData);
        }

        // 데미지 계산: 총 데미지를 '기본 발사체 수'로 나눕니다.
        int baseCount = Mathf.Max(1, pipelineData.BaseProjectileCount);
        float damagePerProjectile = pipelineData.FinalDamage / baseCount; 

        // 첫 번째 사격 (즉시 실행)
        bool result = SpawnProjectiles(gunData, direction, pipelineData, damagePerProjectile);

        // 점사 (Burst) 처리
        if (pipelineData.BurstCount > 1)
        {
            StartCoroutine(FireBurstRoutine(gunData, direction, pipelineData, damagePerProjectile));
        }

        if (result)
        {
            RPC_SpawnMuzzleFlash();
        }
        
        return result;
    }

    private System.Collections.IEnumerator FireBurstRoutine(GunData gunData, Vector3 direction, FirePipelineData pipelineData, float damagePerProjectile)
    {
        int remainingBursts = pipelineData.BurstCount - 1;
        
        while (remainingBursts > 0)
        {
            // 딜레이 대기
            yield return new WaitForSeconds(pipelineData.BurstDelay);
            
            // 추가 사격
            if (SpawnProjectiles(gunData, direction, pipelineData, damagePerProjectile))
            {
                RPC_SpawnMuzzleFlash();
            }
            
            remainingBursts--;
        }
    }

    private bool SpawnProjectiles(GunData gunData, Vector3 direction, FirePipelineData pipelineData, float damagePerProjectile)
    {
        Vector3 spawnPos = transform.position + Vector3.up * _projectileSpawnHeight + direction * _projectileSpawnDistance;
        int pelletCount = Mathf.Max(1, pipelineData.FinalProjectileCount);
        float spreadAngle = pipelineData.SpreadAngle;
        bool anySpawned = false;

        // 병렬 발사 (Parallel Fire) 여부 확인
        // 카드가 병렬 발사를 요청했고(UseParallelFire), 정밀 사격 무기(확산각 거의 없음)인 경우에만 적용
        bool useParallelFire = pipelineData.UseParallelFire; // FireMode determines this, ignore spread angle check
        Vector3 rightParams = Vector3.Cross(direction, Vector3.up).normalized;

        for (int i = 0; i < pelletCount; i++)
        {
            Vector3 finalSpawnPos = spawnPos;
            Vector3 pelletDirection = direction;

            if (useParallelFire)
            {
                // 중심을 기준으로 좌우로 배치
                // 간격은 0.4f 정도로 설정
                float spacing = 0.4f;
                float offset = (i - (pelletCount - 1) / 2f) * spacing;
                finalSpawnPos += rightParams * offset;
            }
            else
            {
                // 기존 확산 로직
                pelletDirection = GetSpreadDirection(direction, spreadAngle);
            }

            Quaternion spawnRot = Quaternion.LookRotation(pelletDirection);
            NetworkObject spawnedObject = null;

            if (gunData.ProjectilePrefab != null)
            {
                GameObject projectileObj = Instantiate(gunData.ProjectilePrefab, finalSpawnPos, spawnRot);
                spawnedObject = projectileObj.GetComponent<NetworkObject>();
                
                if (spawnedObject != null)
                {
                    InstanceFinder.ServerManager.Spawn(spawnedObject);
                    // 1발당 데미지 전달
                    InitializeProjectileWithPipeline(spawnedObject, gunData, pelletDirection, pipelineData, damagePerProjectile);
                }
            }

            if (spawnedObject != null)
            {
                anySpawned = true;
            }
        }
        
        return anySpawned;
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
        // 파이프라인 데이터 생성
        var pipelineData = new FirePipelineData
        {
            BaseDamage = CurrentTotalDamage,
            BaseProjectileCount = 1,
            BaseRange = meleeData.Range,
            FinalDamage = CurrentTotalDamage,
            FinalProjectileCount = 1,
            FinalRange = meleeData.Range,
            Owner = Owner
        };

        // 카드 시스템 파이프라인 처리
        var cardSystem = GetComponent<PlayerCardSystem>();
        if (cardSystem != null)
        {
            pipelineData = cardSystem.ProcessFire(pipelineData);
        }

        // 근접 공격은 AimPoint에서 시작 (실제 공격 시작 위치)
        Vector3 attackPos = _aimVisualizer != null && _aimVisualizer.AimPoint != null
            ? _aimVisualizer.AimPoint.position
            : transform.position + Vector3.up * 1.5f;
        
        // 파이프라인 적용된 사거리 사용
        Collider[] hits = Physics.OverlapSphere(attackPos, pipelineData.FinalRange, meleeData.HitLayers);

        float halfAngle = meleeData.AttackAngle / 2f;

        foreach (Collider hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            Vector3 hitPoint = hit.ClosestPoint(attackPos);
            Vector3 toTarget = (hitPoint - attackPos).normalized;
            float angle = Vector3.Angle(direction, toTarget);

            if (angle <= halfAngle)
            {
                IDamageable damageable = hit.GetComponent<IDamageable>();

                if (damageable != null && damageable.IsAlive)
                {
                    // 파이프라인 적용된 데미지 사용
                    damageable.TakeDamage(pipelineData.FinalDamage, Owner, hitPoint);
                    RPC_SpawnMeleeHitEffect(hitPoint, -toTarget);
                }
            }
        }

        RPC_SpawnMeleeSwing(direction);
    }

    private void InitializeProjectile(NetworkObject obj, GunData gunData, Vector3 direction)
    {
        var projectile = obj != null ? obj.GetComponent<Projectile>() : null;
        if (projectile != null)
        {
            projectile.Initialize(direction, gunData.ProjectileSpeed, CurrentTotalDamage, Owner, gunData.Range, gunData.HitLayers.value);
        }
    }

    private void InitializeProjectileWithPipeline(NetworkObject obj, GunData gunData, Vector3 direction, FirePipelineData pipelineData, float damageOverride)
    {
        var projectile = obj != null ? obj.GetComponent<Projectile>() : null;
        if (projectile != null)
        {
            // 파이프라인에서 처리된 데이터 사용
            projectile.Initialize(
                direction, 
                gunData.ProjectileSpeed, 
                damageOverride,  // 파이프라인 최종 데미지 (1발당)
                Owner, 
                pipelineData.FinalRange,   // 파이프라인 최종 사거리
                gunData.HitLayers.value
            );
        }
    }

    #endregion

    #region Cooldown

    private void SetCooldown()
    {
        if (_weaponData != null)
        {
            // 공격속도 보너스 적용 (높을수록 쿨다운 감소)
            float speedMultiplier = _playerCardSystem?.GetAttackSpeedMultiplier() ?? 1f;
            _cooldownEndTime = Time.time + (_weaponData.AttackDelay / speedMultiplier);
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

    #region Animation Event Handling

    /// <summary>
    /// PlayerAnimationController의 OnMeleeHit 이벤트에서 호출 (Client Owner)
    /// </summary>
    public void OnAnimationEvent_MeleeHit()
    {
        if (!IsOwner) return;

        // 조준 방향으로 공격 요청
        Vector3 direction = transform.forward;
        if (_aimVisualizer != null)
        {
            direction = _aimVisualizer.GetAimDirection();
        }

        ServerRpc_MeleeHit(direction);
    }

    [ServerRpc]
    private void ServerRpc_MeleeHit(Vector3 direction)
    {
        // 검증: 현재 근접 무기인지, 애니메이션 이벤트 사용 설정인지
        if (_weaponData is not MeleeWeaponData meleeData || !meleeData.UseAnimationEvent)
        {
            return;
        }

        // 실제 데미지 처리
        ExecuteMeleeAttack(meleeData, direction);
    }

    #endregion
}
