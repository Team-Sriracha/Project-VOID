using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using UnityEngine;

/// <summary>
/// 몹 체력, 피해, 사망, 리스폰 로직 관리
/// IDamageable 구현, 플레이어와 동일한 데미지 처리 방식
/// </summary>
public class MobCombat : NetworkBehaviour, IDamageable
{
    #region Constants

    private const float MINIMUM_DAMAGE = 1f;

    #endregion

    #region Serialized Fields

    [Header("Mob 데이터")]
    [Tooltip("Mob 스탯 데이터")]
    [SerializeField] private MobData _data;

    #endregion

    #region SyncVars

    public readonly SyncVar<float> HP = new();
    public readonly SyncVar<bool> IsDead = new();
    private readonly SyncVar<bool> IsHidden = new();

    // Timer replacements
    private float _dieAnimationEndTime;
    private float _respawnEndTime;

    #endregion

    #region Properties

    public bool IsAlive => HP.Value > 0 && !IsDead.Value;

    public MobData GetMobData() => _data;

    #endregion

    #region Private Fields

    private MobAI _mobAI;
    private MobAnimationController _animationController;
    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartServer()
    {
        base.OnStartServer();
        
        _mobAI = GetComponent<MobAI>();
        _animationController = GetComponent<MobAnimationController>();

        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;

        InitializeStats();
        


        TimeManager.OnTick += OnTick;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        _mobAI = GetComponent<MobAI>();
        _animationController = GetComponent<MobAnimationController>();
        

        if (MobSpawnManager.Instance != null)
        {
            MobSpawnManager.Instance.RegisterMob(NetworkObject);
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        
        if (MobSpawnManager.Instance != null)
        {
            MobSpawnManager.Instance.UnregisterMob(NetworkObject);
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        
        if (TimeManager != null)
        {
            TimeManager.OnTick -= OnTick;
        }
    }

    private void OnTick()
    {
        if (!IsServerInitialized) return;

        if (IsDead.Value)
        {
            // Phase 1: Die 애니메이션 5초 후 비활성화
            if (!IsHidden.Value && _dieAnimationEndTime > 0 && Time.time >= _dieAnimationEndTime)
            {
                HideMob();
            }
            // Phase 2: RespawnTime 후 스폰 위치에서 활성화
            else if (IsHidden.Value && _respawnEndTime > 0 && Time.time >= _respawnEndTime)
            {
                RespawnMob();
            }
        }
    }

    #endregion

    #region Initialization

    private void InitializeStats()
    {
        if (_data == null)
        {
            Debug.LogError($"[MobCombat] {gameObject.name}: MobData가 설정되지 않았습니다!");
            return;
        }

        HP.Value = _data.MaxHP;
        IsDead.Value = false;
    }

    public void InitializeWithData(MobData data)
    {
        _data = data;

        if (IsServerInitialized)
        {
            InitializeStats();
        }
    }

    #endregion

    #region Damage & Death

    public void TakeDamage(float damage, NetworkConnection attacker, Vector3 hitPosition = default)
    {
        if (!IsAlive) return;
        
        Vector3 indicatorPos = hitPosition == Vector3.zero 
            ? transform.position + Vector3.up * 1f 
            : hitPosition;
        
        // 서버에서 직접 데미지 처리
        if (IsServerInitialized)
        {
            ApplyDamageInternal(damage, attacker, indicatorPos);
        }
    }

    private void ApplyDamageInternal(float damage, NetworkConnection attacker, Vector3 indicatorPosition)
    {
        if (!IsServerInitialized || !IsAlive) return;

        float finalDamage = CalculateFinalDamage(damage);
        HP.Value = Mathf.Max(HP.Value - finalDamage, 0);

        RPC_ShowDamageIndicator(indicatorPosition, finalDamage);

        if (_mobAI != null)
        {
            _mobAI.OnDamaged(attacker);
        }

        if (_animationController != null)
        {
            _animationController.PlayHit();
        }

        Debug.Log($"[MobCombat] {gameObject.name}이(가) {finalDamage} 데미지를 받음. 남은 HP: {HP.Value}");

        if (HP.Value <= 0)
        {
            Die(attacker);
        }
    }

    [ObserversRpc]
    private void RPC_ShowDamageIndicator(Vector3 position, float damage)
    {
        // 실제 피격 위치(position)에 표시, 타겟 ID는 같은 타겟 데미지 합산용
        int targetId = gameObject.GetInstanceID();
        DamageIndicatorManager.Instance?.SpawnIndicator(position, damage, isHeal: false, isLocalHit: false, targetId: targetId);
    }

    private float CalculateFinalDamage(float damage)
    {
        if (_data == null) return damage;

        float reduction = _data.Defense;
        return Mathf.Max(MINIMUM_DAMAGE, damage - reduction);
    }

    private void Die(NetworkConnection killer)
    {
        if (!IsServerInitialized) return;

        IsDead.Value = true;
        IsHidden.Value = false;

        if (_animationController != null)
        {
            _animationController.PlayDie();
        }

        if (_mobAI != null)
        {
            _mobAI.SetState(MonsterState.Dead);
        }

        ProcessKillerReward(killer);

        RPC_DisableColliders();

        // 5초 후 비활성화 (Die 애니메이션 시간)
        _dieAnimationEndTime = Time.time + 5f;
        Debug.Log($"[MobCombat] {gameObject.name} 사망. 5초 후 비활성화, 이후 {_data?.RespawnTime ?? 30f}초 후 리스폰.");
    }

    private void ProcessKillerReward(NetworkConnection killer)
    {
        if (killer == null || killer.FirstObject == null) return;
        if (_data == null) return;

        PlayerCombat killerCombat = killer.FirstObject.GetComponent<PlayerCombat>();
        if (killerCombat != null)
        {
            killerCombat.AddXP(_data.XPReward);
            Debug.Log($"[MobCombat] Player에게 {_data.XPReward} XP 지급.");
        }
    }

    [ObserversRpc(RunLocally = true)]
    private void RPC_DisableColliders()
    {
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            if (col != null) col.enabled = false;
        }
    }

    [ObserversRpc(RunLocally = true)]
    private void RPC_EnableColliders()
    {
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            if (col != null) col.enabled = true;
        }
    }

    #endregion

    #region Respawn

    private void HideMob()
    {
        if (!IsServerInitialized) return;

        IsHidden.Value = true;
        _dieAnimationEndTime = 0;

        RPC_SetVisible(false);

        transform.position = _spawnPosition;
        transform.rotation = _spawnRotation;

        float respawnTime = _data?.RespawnTime ?? 30f;
        _respawnEndTime = Time.time + respawnTime;

        Debug.Log($"[MobCombat] {gameObject.name} 비활성화. {respawnTime}초 후 리스폰.");
    }

    private void RespawnMob()
    {
        if (!IsServerInitialized) return;

        _respawnEndTime = 0;

        if (_data != null)
        {
            HP.Value = _data.MaxHP;
        }

        IsDead.Value = false;
        IsHidden.Value = false;

        RPC_EnableColliders();

        if (_mobAI != null)
        {
            _mobAI.SetState(MonsterState.Idle);
            _mobAI.ResetAI();
        }

        if (_animationController != null)
        {
            _animationController.ResetAnimator();
        }

        RPC_SetVisible(true);

        Debug.Log($"[MobCombat] {gameObject.name} 리스폰 완료.");
    }

    [ObserversRpc]
    private void RPC_SetVisible(bool visible)
    {

        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            if (renderer != null) renderer.enabled = visible;
        }
    }

    #endregion
}
