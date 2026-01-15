using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// FishNet.Managing.NetworkManager와 커스텀 NetworkManager 구분을 위한 alias
using CustomNetworkManager = NetworkManager;

/// <summary>
/// 플레이어 체력, 피해, 사망, 레벨업 로직 관리
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

    #region SyncVars

    public readonly SyncVar<float> HP = new();
    public readonly SyncVar<int> KillCount = new();
    public readonly SyncVar<int> Level = new();
    public readonly SyncVar<float> CurrentXP = new();

    #endregion

    #region Properties

    /// <summary>
    /// 플레이어 생존 여부
    /// </summary>
    public bool IsAlive => HP.Value > 0;

    /// <summary>
    /// 플레이어 최대 체력(HP) 반환
    /// </summary>
    public float MaxHP => _stats != null ? _stats.MaxHP : DEFAULT_MAX_HP;

    /// <summary>
    /// 다음 레벨까지 필요한 XP 반환
    /// </summary>
    public float XPToNextLevel => _stats != null ? _stats.GetXPForLevel(Level.Value + 1) : DEFAULT_XP_TO_NEXT_LEVEL;

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        
        _animationController = GetComponent<PlayerAnimationController>();

        if (IsServerInitialized)
        {
            InitializeStats();
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 플레이어 초기 스탯 설정
    /// </summary>
    private void InitializeStats()
    {
        HP.Value = _stats != null ? _stats.MaxHP : DEFAULT_MAX_HP;
        Level.Value = STARTING_LEVEL;
        CurrentXP.Value = 0f;
    }

    #endregion

    #region Damage & Healing

    /// <summary>
    /// 플레이어에게 데미지 적용 (IDamageable 구현)
    /// </summary>
    /// <param name="hitPosition">피격 위치 (Vector3.zero면 transform.position 사용)</param>
    public void TakeDamage(float damage, NetworkConnection attacker, Vector3 hitPosition = default)
    {
        if (!IsAlive) return;
        
        // hitPosition이 zero면 대상 중심 사용
        Vector3 indicatorPos = hitPosition == Vector3.zero 
            ? transform.position + Vector3.up * 1.5f 
            : hitPosition;
        
        // 서버에서 직접 데미지 처리
        if (IsServerInitialized)
        {
            ApplyDamageInternal(damage, attacker, indicatorPos);
        }
    }

    /// <summary>
    /// 플레이어 체력 회복
    /// </summary>
    public void ApplyHeal(float amount)
    {
        if (!IsAlive) return;
        if (IsServerInitialized)
        {
            HP.Value = Mathf.Min(HP.Value + amount, MaxHP);
        }
    }

    private void ApplyDamageInternal(float damage, NetworkConnection attacker, Vector3 indicatorPosition)
    {
        if (!IsServerInitialized || !IsAlive) return;

        float finalDamage = CalculateFinalDamage(damage);
        HP.Value = Mathf.Max(HP.Value - finalDamage, 0);

        // 데미지 인디케이터 표시 (피격 위치에서)
        RPC_ShowDamageIndicator(indicatorPosition, finalDamage);

        if (HP.Value <= 0)
        {
            Die(attacker);
        }
        else if (_animationController != null)
        {
            _animationController.PlayHit();
        }
    }

    [ObserversRpc]
    private void RPC_ShowDamageIndicator(Vector3 position, float damage)
    {
        // 로컬 플레이어(Owner)가 맞은 경우 isLocalHit = true로 피격 색상 적용
        bool isLocalHit = IsOwner;
        // 실제 피격 위치(position)에 표시, 타겟 ID는 같은 타겟 데미지 합산용
        int targetId = gameObject.GetInstanceID();
        DamageIndicatorManager.Instance?.SpawnIndicator(position, damage, isHeal: false, isLocalHit: isLocalHit, targetId: targetId);
    }

    /// <summary>
    /// 방어력을 고려한 최종 데미지 계산
    /// </summary>
    private float CalculateFinalDamage(float damage)
    {
        // TODO: 방어구 시스템 추가 시 방어력 계산 로직 구현
        return Mathf.Max(MINIMUM_DAMAGE, damage);
    }

    #endregion

    #region Death

    /// <summary>
    /// 플레이어 사망 처리
    /// </summary>
    private void Die(NetworkConnection killer)
    {
        if (!IsServerInitialized) return;

        if (_animationController != null)
        {
            _animationController.PlayDie();
        }

        ProcessKillerReward(killer);
        DisableColliders();

        RPC_ShowDefeatUI(Owner);

        // 킬러의 무기 ID 가져오기
        string killerWeaponID = null;
        if (killer != null)
        {
            var killerObject = CustomNetworkManager.Instance?.GetPlayerNetworkObject(killer);
            if (killerObject != null && 
                killerObject.TryGetComponent<NetworkedWeapon>(out var killerWeapon) &&
                killerWeapon.CurrentWeaponData != null)
            {
                killerWeaponID = killerWeapon.CurrentWeaponData.ItemID;
            }
        }

        // GameStateManager에 사망 알림
        var gameStateManager = CustomNetworkManager.Instance?.GameStateManager;
        if (gameStateManager != null)
        {
            gameStateManager.OnPlayerDied(Owner, killer, killerWeaponID);
        }
    }

    /// <summary>
    /// 킬러에게 보상 지급 (킬 카운트, XP)
    /// </summary>
    private void ProcessKillerReward(NetworkConnection killer)
    {
        if (killer == null || killer == Owner) return;

        var killerObject = CustomNetworkManager.Instance?.GetPlayerNetworkObject(killer);
        if (killerObject != null && killerObject.TryGetComponent<PlayerCombat>(out var killerCombat))
        {
            // 서버에서 직접 KillCount 증가 (RPC 권한 문제 해결)
            killerCombat.AddPlayerKill();
        }
    }

    /// <summary>
    /// 사망 시 모든 Collider 비활성화 및 Rigidbody kinematic 변경
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
    /// Victory 애니메이션 재생
    /// </summary>
    public void PlayVictory()
    {
        if (!IsServerInitialized) return;

        if (_animationController != null)
        {
            _animationController.PlayVictory();
        }
    }

    #endregion

    #region XP & Leveling

    /// <summary>
    /// XP 추가 및 레벨업 조건 확인
    /// </summary>
    public void AddXP(float xpAmount)
    {
        if (!IsServerInitialized) return;

        CurrentXP.Value += xpAmount;

        float xpToNext = XPToNextLevel;
        while (CurrentXP.Value >= xpToNext)
        {
            LevelUp(xpToNext);
            xpToNext = XPToNextLevel;
        }
    }

    /// <summary>
    /// 플레이어 레벨업 처리
    /// </summary>
    private void LevelUp(float xpToNextLevel)
    {
        CurrentXP.Value -= xpToNextLevel;
        Level.Value++;
    }

    #endregion

    #region RPC Methods

    /// <summary>
    /// 플레이어 처치 시 킬 카운트 증가 (서버 전용)
    /// </summary>
    public void AddPlayerKill()
    {
        if (!IsServerInitialized) return;

        KillCount.Value++;
        if (_stats != null) AddXP(_stats.XPPerPlayerKill);
    }

    /// <summary>
    /// 특정 플레이어에게 패배 UI 표시 (서버 → 해당 플레이어)
    /// </summary>
    [TargetRpc]
    private void RPC_ShowDefeatUI(NetworkConnection target)
    {
        if (UIManager.Instance != null)
        {
            int killCount = KillCount.Value;
            float survivalTime = 0f;
            int rank = 1;
            
            var gameStateManager = GameStateManager.Instance;
            
            if (gameStateManager != null)
            {
                survivalTime = gameStateManager.GameElapsedTime.Value;
                rank = gameStateManager.AlivePlayers.Value + 1; // 현재 생존자 수 + 1 = 순위
            }

            UIManager.Instance.ShowDefeat(killCount, survivalTime, rank);
        }
    }

    #endregion
}
