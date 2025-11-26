using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어의 체력, 피해 처리, 사망, 레벨업 로직을 담당합니다.
/// </summary>
public class PlayerCombat : NetworkBehaviour, IDamageable
{
    #region Constants

    private const float DEFAULT_MAX_HP = 100f;
    private const float DEFAULT_XP_TO_NEXT_LEVEL = 100f;
    private const float MINIMUM_DAMAGE = 1f;
    private const int STARTING_LEVEL = 1;

    #endregion

    #region Serialized Fields

    [Header("Stats")]
    [SerializeField] private PlayerStats _stats;

    #endregion

    #region Private Fields

    private PlayerAnimationController _animationController;

    #endregion

    #region Networked Properties

    [Networked]
    public float HP { get; private set; }

    [Networked]
    public int KillCount { get; set; }

    [Networked]
    public int Level { get; private set; }

    [Networked]
    public float CurrentXP { get; private set; }

    #endregion

    #region Properties

    /// <summary>
    /// 플레이어가 살아있는지 여부를 나타냅니다.
    /// </summary>
    public bool IsAlive => HP > 0;

    /// <summary>
    /// 플레이어의 최대 체력(HP)을 반환합니다.
    /// </summary>
    public float MaxHP => _stats != null ? _stats.MaxHP : DEFAULT_MAX_HP;

    /// <summary>
    /// 다음 레벨까지 필요한 XP를 반환합니다.
    /// </summary>
    public float XPToNextLevel => _stats != null ? _stats.GetXPForLevel(Level + 1) : DEFAULT_XP_TO_NEXT_LEVEL;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        _animationController = GetComponent<PlayerAnimationController>();

        if (HasStateAuthority)
        {
            InitializeStats();
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 플레이어의 초기 스탯을 설정합니다.
    /// </summary>
    private void InitializeStats()
    {
        HP = _stats != null ? _stats.MaxHP : DEFAULT_MAX_HP;
        Level = STARTING_LEVEL;
        CurrentXP = 0f;
    }

    #endregion

    #region Damage & Healing

    /// <summary>
    /// 플레이어에게 데미지를 입힙니다 (IDamageable 인터페이스 구현).
    /// </summary>
    public void TakeDamage(float damage, PlayerRef attacker)
    {
        if (!IsAlive) return;
        RPC_ApplyDamage(damage, attacker);
    }

    /// <summary>
    /// 플레이어의 체력을 회복합니다.
    /// </summary>
    public void ApplyHeal(float amount)
    {
        if (!IsAlive) return;
        RPC_ApplyHeal(amount);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ApplyHeal(float amount)
    {
        if (!IsAlive) return;

        HP = Mathf.Min(HP + amount, MaxHP);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ApplyDamage(float damage, PlayerRef attacker)
    {
        if (!IsAlive) return;

        HP = Mathf.Max(HP - CalculateFinalDamage(damage), 0);

        if (HP <= 0)
        {
            Die(attacker);
        }
        else if (_animationController != null)
        {
            _animationController.PlayHit();
        }
    }

    /// <summary>
    /// 방어력을 고려하여 최종 데미지를 계산합니다.
    /// </summary>
    private float CalculateFinalDamage(float damage)
    {
        // TODO: 방어구 시스템 추가 시 방어력 계산 로직 구현
        return Mathf.Max(MINIMUM_DAMAGE, damage);
    }

    #endregion

    #region Death

    /// <summary>
    /// 플레이어 사망 처리를 수행합니다.
    /// </summary>
    private void Die(PlayerRef killer)
    {
        if (Runner == null || Object == null) return;

        if (_animationController != null)
        {
            _animationController.PlayDie();
        }

        ProcessKillerReward(killer);
        DisableColliders();

        RPC_ShowDefeatUI(Object.InputAuthority);

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPlayerDied(Object.InputAuthority, killer);
        }
    }

    /// <summary>
    /// 킬러에게 보상(킬 카운트, XP)을 지급합니다.
    /// </summary>
    private void ProcessKillerReward(PlayerRef killer)
    {
        if (killer == Object.StateAuthority) return;

        if (Runner.TryGetPlayerObject(killer, out var killerObject) &&
            killerObject.TryGetComponent<PlayerCombat>(out var killerCombat))
        {
            killerCombat.RPC_AddKill();
        }
    }

    /// <summary>
    /// 플레이어의 모든 Collider를 비활성화하고 Rigidbody를 kinematic으로 변경합니다 (사망 시).
    /// </summary>
    private void DisableColliders()
    {
        foreach (var col in GetComponentsInChildren<Collider>())
        {
            if (col != null) col.enabled = false;
        }

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
        }
    }

    #endregion

    #region Victory

    /// <summary>
    /// Victory 애니메이션을 재생합니다.
    /// </summary>
    public void PlayVictory()
    {
        if (!HasStateAuthority) return;

        if (_animationController != null)
        {
            _animationController.PlayVictory();
        }
    }

    #endregion

    #region XP & Leveling

    /// <summary>
    /// 플레이어에게 XP를 추가하고 레벨업 조건을 확인합니다.
    /// </summary>
    public void AddXP(float xpAmount)
    {
        if (!HasStateAuthority) return;

        CurrentXP += xpAmount;

        while (CurrentXP >= XPToNextLevel)
        {
            LevelUp();
        }
    }

    /// <summary>
    /// 플레이어를 레벨업시킵니다.
    /// </summary>
    private void LevelUp()
    {
        CurrentXP -= XPToNextLevel;
        Level++;
    }

    #endregion

    #region RPC Methods

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_AddKill()
    {
        KillCount++;

        if (_stats != null) AddXP(_stats.XPPerPlayerKill);
    }

    /// <summary>
    /// 특정 플레이어에게 패배 UI 표시 (서버 → 해당 플레이어)
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ShowDefeatUI(PlayerRef deadPlayer, RpcInfo info = default)
    {
        // Why: 이 클라이언트가 죽은 플레이어인지 확인 (InputAuthority를 가지고 있는지)
        if (!Object.HasInputAuthority) return;

        if (UIManager.Instance != null && GameStateManager.Instance != null)
        {
            int killCount = KillCount;
            float survivalTime = GameStateManager.Instance.GameElapsedTime;
            int rank = GameStateManager.Instance.AlivePlayers + 1; // 현재 생존자 수 + 1 = 순위

            UIManager.Instance.ShowDefeat(killCount, survivalTime, rank);
            Debug.Log($"[PlayerCombat] 패배 UI 표시 - Kills: {killCount}, Time: {survivalTime:F1}s, Rank: {rank}");
        }
    }

    #endregion
}
