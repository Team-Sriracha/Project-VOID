using Fusion;
using UnityEngine;

/// <summary>
/// 몹의 체력, 피해 처리, 사망, 리스폰 로직을 담당합니다.
/// IDamageable 인터페이스를 구현하여 플레이어와 동일한 방식으로 데미지를 받습니다.
/// </summary>
public class MobCombat : NetworkBehaviour, IDamageable
{
    #region Constants

    private const float MINIMUM_DAMAGE = 1f;

    #endregion

    #region Serialized Fields

    [Header("Mob 데이터")]
    [Tooltip("Mob의 스탯 데이터")]
    [SerializeField] private MobData _data;

    #endregion

    #region Networked Properties

    [Networked]
    public float HP { get; private set; }

    [Networked]
    public NetworkBool IsDead { get; private set; }

    /// <summary>
    /// Die 애니메이션 후 비활성화까지의 타이머 (5초)
    /// </summary>
    [Networked]
    private TickTimer DieAnimationTimer { get; set; }

    /// <summary>
    /// 비활성화 후 리스폰까지의 타이머 (MobData.RespawnTime)
    /// </summary>
    [Networked]
    private TickTimer RespawnTimer { get; set; }

    /// <summary>
    /// 현재 비활성화(숨김) 상태인지
    /// </summary>
    [Networked]
    private NetworkBool IsHidden { get; set; }

    #endregion

    #region Properties

    /// <summary>
    /// 몹이 살아있는지 여부를 나타냅니다.
    /// </summary>
    public bool IsAlive => HP > 0 && !IsDead;

    /// <summary>
    /// Mob 데이터를 반환합니다.
    /// </summary>
    public MobData GetMobData() => _data;

    #endregion

    #region Private Fields

    private MobAI _mobAI;
    private MobAnimationController _animationController;
    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        _mobAI = GetComponent<MobAI>();
        _animationController = GetComponent<MobAnimationController>();

        // Why: 스폰 위치 저장 (리스폰 시 사용)
        _spawnPosition = transform.position;
        _spawnRotation = transform.rotation;

        if (HasStateAuthority)
        {
            InitializeStats();
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Why: Runner가 종료 중이거나 실행 중이 아니면 처리하지 않음
        if (Runner == null || !Runner.IsRunning) return;

        if (!HasStateAuthority) return;

        // Why: 사망 후 처리 (2단계 리스폰)
        if (IsDead)
        {
            // Phase 1: Die 애니메이션 5초 후 비활성화
            if (!IsHidden && DieAnimationTimer.Expired(Runner))
            {
                HideMob();
            }
            // Phase 2: RespawnTime 후 스폰 위치에서 활성화
            else if (IsHidden && RespawnTimer.Expired(Runner))
            {
                RespawnMob();
            }
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 몹의 초기 스탯을 설정합니다.
    /// </summary>
    private void InitializeStats()
    {
        if (_data == null)
        {
            Debug.LogError($"[MobCombat] {gameObject.name}: MobData가 설정되지 않았습니다!");
            return;
        }

        HP = _data.MaxHP;
        IsDead = false;
    }

    /// <summary>
    /// MobData를 설정하고 초기화합니다.
    /// MobSpawnManager에서 스폰 시 호출됩니다.
    /// </summary>
    /// <param name="data">적용할 MobData</param>
    public void InitializeWithData(MobData data)
    {
        _data = data;

        if (HasStateAuthority)
        {
            InitializeStats();
        }
    }

    #endregion

    #region Damage & Death

    /// <summary>
    /// 몹에게 데미지를 입힙니다 (IDamageable 인터페이스 구현).
    /// </summary>
    /// <param name="damage">받을 데미지량</param>
    /// <param name="attacker">공격자의 PlayerRef</param>
    /// <param name="hitPosition">피격 위치 (Vector3.zero면 transform.position 사용)</param>
    public void TakeDamage(float damage, PlayerRef attacker, Vector3 hitPosition = default)
    {
        if (!IsAlive) return;
        
        // Why: hitPosition이 zero면 대상 중심 사용
        Vector3 indicatorPos = hitPosition == Vector3.zero 
            ? transform.position + Vector3.up * 1f 
            : hitPosition;
        RPC_ApplyDamage(damage, attacker, indicatorPos);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ApplyDamage(float damage, PlayerRef attacker, Vector3 indicatorPosition)
    {
        if (!HasStateAuthority || !IsAlive) return;

        float finalDamage = CalculateFinalDamage(damage);
        HP = Mathf.Max(HP - finalDamage, 0);

        // 데미지 인디케이터 표시 (피격 위치에서)
        RPC_ShowDamageIndicator(indicatorPosition, finalDamage);

        // Why: MobAI에 피격 알림 → Alert 상태로 전환
        if (_mobAI != null)
        {
            _mobAI.OnDamaged(attacker);
        }

        // Why: 피격 애니메이션 재생
        if (_animationController != null)
        {
            _animationController.PlayHit();
        }

        Debug.Log($"[MobCombat] {gameObject.name}이(가) {finalDamage} 데미지를 받음. 남은 HP: {HP}");

        if (HP <= 0)
        {
            Die(attacker);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ShowDamageIndicator(Vector3 position, float damage)
    {
        // 각 클라이언트에서 FOV 체크 후 인디케이터 표시 (피격 위치에서)
        DamageIndicatorManager.Instance?.SpawnIndicator(position, damage);
    }

    /// <summary>
    /// 방어력을 고려하여 최종 데미지를 계산합니다.
    /// </summary>
    /// <param name="damage">원본 데미지</param>
    /// <returns>최종 데미지</returns>
    private float CalculateFinalDamage(float damage)
    {
        if (_data == null) return damage;

        // Why: 방어력 공식 (추후 확장 가능)
        float reduction = _data.Defense;
        return Mathf.Max(MINIMUM_DAMAGE, damage - reduction);
    }

    /// <summary>
    /// 몹 사망 처리를 수행합니다.
    /// </summary>
    /// <param name="killer">처치자의 PlayerRef</param>
    private void Die(PlayerRef killer)
    {
        if (!HasStateAuthority) return;

        IsDead = true;
        IsHidden = false;

        // Why: 사망 애니메이션 재생
        if (_animationController != null)
        {
            _animationController.PlayDie();
        }

        // Why: MobAI 상태를 Dead로 변경
        if (_mobAI != null)
        {
            _mobAI.SetState(MonsterState.Dead);
        }

        // Why: 처치자에게 XP 보상 지급
        ProcessKillerReward(killer);

        // Why: Collider 비활성화
        DisableColliders();

        // Why: 5초 후 비활성화 (Die 애니메이션 시간)
        DieAnimationTimer = TickTimer.CreateFromSeconds(Runner, 5f);
        Debug.Log($"[MobCombat] {gameObject.name} 사망. 5초 후 비활성화, 이후 {_data?.RespawnTime ?? 30f}초 후 리스폰.");
    }

    /// <summary>
    /// 처치자에게 XP 보상을 지급합니다.
    /// </summary>
    /// <param name="killer">처치자의 PlayerRef</param>
    private void ProcessKillerReward(PlayerRef killer)
    {
        if (killer == PlayerRef.None) return;
        if (_data == null) return;

        // Why: 처치자의 PlayerCombat을 찾아 XP 지급
        if (Runner.TryGetPlayerObject(killer, out var killerObject) &&
            killerObject.TryGetComponent<PlayerCombat>(out var killerCombat))
        {
            killerCombat.AddXP(_data.XPReward);
            Debug.Log($"[MobCombat] {killer}에게 {_data.XPReward} XP 지급.");
        }
    }

    /// <summary>
    /// 몹의 모든 Collider를 비활성화합니다.
    /// </summary>
    private void DisableColliders()
    {
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            if (col != null) col.enabled = false;
        }
    }

    /// <summary>
    /// 몹의 모든 Collider를 활성화합니다.
    /// </summary>
    private void EnableColliders()
    {
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            if (col != null) col.enabled = true;
        }
    }

    #endregion

    #region Respawn

    /// <summary>
    /// Die 애니메이션 후 몹을 비활성화합니다.
    /// </summary>
    private void HideMob()
    {
        if (!HasStateAuthority) return;

        IsHidden = true;

        // Why: 비활성화 (숨김)
        RPC_SetVisible(false);

        // Why: 스폰 위치로 미리 이동 (비활성화 상태에서)
        transform.position = _spawnPosition;
        transform.rotation = _spawnRotation;

        // Why: RespawnTime 후 리스폰 타이머 시작
        float respawnTime = _data?.RespawnTime ?? 30f;
        RespawnTimer = TickTimer.CreateFromSeconds(Runner, respawnTime);

        Debug.Log($"[MobCombat] {gameObject.name} 비활성화. {respawnTime}초 후 리스폰.");
    }

    /// <summary>
    /// 몹을 리스폰합니다.
    /// 스폰 위치에서 Idle 상태로 활성화됩니다.
    /// </summary>
    private void RespawnMob()
    {
        if (!HasStateAuthority) return;

        // Why: HP 회복
        if (_data != null)
        {
            HP = _data.MaxHP;
        }

        IsDead = false;
        IsHidden = false;

        // Why: Collider 재활성화
        EnableColliders();

        // Why: MobAI 상태를 Idle로 변경
        if (_mobAI != null)
        {
            _mobAI.SetState(MonsterState.Idle);
            _mobAI.ResetAI();
        }

        // Why: 애니메이션 초기화 (Idle 상태로)
        if (_animationController != null)
        {
            _animationController.ResetAnimator();
        }

        // Why: 활성화
        RPC_SetVisible(true);

        Debug.Log($"[MobCombat] {gameObject.name} 리스폰 완료.");
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SetVisible(bool visible)
    {
        // Why: 모든 Renderer 활성화/비활성화
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            if (renderer != null) renderer.enabled = visible;
        }
    }

    #endregion
}
