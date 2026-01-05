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
    /// <param name="hitPosition">피격 위치 (Vector3.zero면 transform.position 사용)</param>
    public void TakeDamage(float damage, PlayerRef attacker, Vector3 hitPosition = default)
    {
        if (!HasStateAuthority) return;
        if (!IsAlive) return;
        
        // Why: hitPosition이 zero면 대상 중심 사용
        Vector3 indicatorPos = hitPosition == Vector3.zero 
            ? transform.position + Vector3.up * 1.5f 
            : hitPosition;
        RPC_ApplyDamage(damage, attacker, indicatorPos);
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
    private void RPC_ApplyDamage(float damage, PlayerRef attacker, Vector3 indicatorPosition)
    {
        if (!IsAlive) return;

        float finalDamage = CalculateFinalDamage(damage);
        HP = Mathf.Max(HP - finalDamage, 0);

        // 데미지 인디케이터 표시 (피격 위치에서)
        RPC_ShowDamageIndicator(indicatorPosition, finalDamage);

        if (HP <= 0)
        {
            Die(attacker);
        }
        else if (_animationController != null)
        {
            _animationController.PlayHit();
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ShowDamageIndicator(Vector3 position, float damage)
    {
        // Why: 로컬 플레이어(InputAuthority)가 맞은 경우 isLocalHit = true → 피격 색상 적용
        bool isLocalHit = Object.HasInputAuthority;
        DamageIndicatorManager.Instance?.SpawnIndicator(position, damage, isHeal: false, isLocalHit: isLocalHit);
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

        // Why: 킬러의 무기 ID 가져오기
        string killerWeaponID = null;
        if (Runner.TryGetPlayerObject(killer, out var killerObject) &&
            killerObject.TryGetComponent<NetworkedWeapon>(out var killerWeapon) &&
            killerWeapon.CurrentWeaponData != null)
        {
            killerWeaponID = killerWeapon.CurrentWeaponData.ItemID;
        }

        // Multi-Peer 호환성: Singleton 대신 Runner를 통해 GameStateManager 접근
        var gameStateManager = NetworkManager.GetManager(Runner)?.GameStateManager;
        if (gameStateManager != null)
        {
            gameStateManager.OnPlayerDied(Object.InputAuthority, killer, killerWeaponID);
        }
    }

    /// <summary>
    /// 킬러에게 보상(킬 카운트, XP)을 지급합니다 (서버에서 호출).
    /// </summary>
    private void ProcessKillerReward(PlayerRef killer)
    {
        if (killer == Object.StateAuthority) return;

        if (Runner.TryGetPlayerObject(killer, out var killerObject) &&
            killerObject.TryGetComponent<PlayerCombat>(out var killerCombat))
        {
            // Why: 서버에서 직접 KillCount 증가 (RPC 권한 문제 해결)
            killerCombat.AddPlayerKill();
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

        float xpToNext = XPToNextLevel;
        while (CurrentXP >= xpToNext)
        {
            LevelUp(xpToNext);
            xpToNext = XPToNextLevel;
        }
    }

    /// <summary>
    /// 플레이어를 레벨업시킵니다.
    /// </summary>
    private void LevelUp(float xpToNextLevel)
    {
        CurrentXP -= xpToNextLevel;
        Level++;
    }

    #endregion

    #region RPC Methods

    /// <summary>
    /// 플레이어 처치 시 킬 카운트를 증가시킵니다 (서버에서 직접 호출).
    /// </summary>
    public void AddPlayerKill()
    {
        if (!HasStateAuthority) return;

        KillCount++;
        if (_stats != null) AddXP(_stats.XPPerPlayerKill);
        Debug.Log($"[PlayerCombat] Player Kill! KillCount: {KillCount}");
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_AddKill(RpcInfo info = default)
    {
        if (!HasStateAuthority) return;
        if (info.Source != Object.InputAuthority) return;

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

        // Why: GameStateManager가 Spawned되지 않았으면 기본값 사용
            if (UIManager.Instance != null)
        {
            int killCount = KillCount;
            float survivalTime = 0f;
            int rank = 1;
            
            // Multi-Peer 호환: Singleton 대신 Runner를 통해 접근
            var gameStateManager = NetworkManager.GetManager(Runner)?.GameStateManager;
            
            if (gameStateManager != null && 
                gameStateManager.Object != null && 
                gameStateManager.Object.IsValid)
            {
                survivalTime = gameStateManager.GameElapsedTime;
                rank = gameStateManager.AlivePlayers + 1; // 현재 생존자 수 + 1 = 순위
            }

            UIManager.Instance.ShowDefeat(killCount, survivalTime, rank);
            // Debug.Log($"[PlayerCombat] 패배 UI 표시 - Kills: {killCount}, Time: {survivalTime:F1}s, Rank: {rank}");
        }
    }

    #endregion
}
