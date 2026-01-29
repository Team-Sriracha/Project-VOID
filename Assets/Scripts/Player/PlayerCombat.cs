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
    private NetworkedArmor _armorSystem;

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
    /// 플레이어 최대 체력(HP) 반환 (카드 보너스 적용)
    /// </summary>
    public float MaxHP
    {
        get
        {
            float baseMaxHP = _stats != null ? _stats.MaxHP : DEFAULT_MAX_HP;
            
            // PlayerCardSystem이 있으면 보너스 배율 적용
            var cardSystem = GetComponent<PlayerCardSystem>();
            if (cardSystem != null)
            {
                baseMaxHP *= cardSystem.GetHealthMultiplier();
            }
            
            return baseMaxHP;
        }
    }

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
        _armorSystem = GetComponent<NetworkedArmor>();

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
            float prevHP = HP.Value;
            HP.Value = Mathf.Min(HP.Value + amount, MaxHP);
            
            float healedAmount = HP.Value - prevHP;
            if (healedAmount > 0)
            {
                // 머리 위 1.5f 위치에 표시
                RPC_ShowFloatingText(transform.position + Vector3.up * 1.5f, healedAmount, isHeal: true);
            }
        }
    }

    private void ApplyDamageInternal(float damage, NetworkConnection attacker, Vector3 indicatorPosition)
    {
        if (!IsServerInitialized || !IsAlive) return;

        float finalDamage = CalculateFinalDamage(damage);
        HP.Value = Mathf.Max(HP.Value - finalDamage, 0);

        // 데미지 인디케이터 표시 (피격 위치에서)
        RPC_ShowFloatingText(indicatorPosition, finalDamage, isHeal: false);

        if (HP.Value <= 0)
        {
            Die(attacker);
        }
        else if (_animationController != null)
        {
            _animationController.PlayHit();
        }
    }

    /// <summary>
    /// 최대 체력 스탯이 변경되었을 때 호출 (PlayerCardSystem 등에서 호출)
    /// </summary>
    public void OnMaxHealthModified(float previousMaxHP)
    {
        if (!IsServerInitialized || !IsAlive) return;

        float currentMaxHP = MaxHP;
        float difference = currentMaxHP - previousMaxHP;

        // 최대 체력이 늘어난 만큼 현재 체력도 회복시켜 줌
        if (difference > 0)
        {
            HP.Value += difference;
            // 최대 체력 증가로 인한 회복도 표시
            RPC_ShowFloatingText(transform.position + Vector3.up * 1.5f, difference, isHeal: true);
        }
        
        // 현재 체력이 최대 체력을 넘지 않도록 (혹은 줄어들었을 때 깎이도록) 클램핑
        HP.Value = Mathf.Clamp(HP.Value, 0, currentMaxHP);
    }

    [ObserversRpc]
    private void RPC_ShowFloatingText(Vector3 position, float amount, bool isHeal)
    {
        // 로컬 플레이어(Owner)가 대상인 경우
        // 데미지: 빨간색(isLocalHit=true), 힐: 초록색(isLocalHit 무관하게 힐 색상)
        bool isLocalTarget = IsOwner;
        
        // 실제 피격 위치(position)에 표시, 타겟 ID는 같은 타겟 합산용
        int targetId = gameObject.GetInstanceID();
        DamageIndicatorManager.Instance?.SpawnIndicator(position, amount, isHeal: isHeal, isLocalHit: isLocalTarget, targetId: targetId);
    }

    /// <summary>
    /// 실제 적용되는 방어력 (기본 방어력 + 카드 보너스)
    /// UI 표시 및 데미지 계산에 사용됨
    /// </summary>
    public float EffectiveDefense
    {
        get
        {
            // 1. 기본 방어력 (아이템 등)
            float baseDefense = _armorSystem?.CurrentDefense ?? 0f;
            
            // 2. 카드 시스템의 방어력 보너스 적용
            var cardSystem = GetComponent<PlayerCardSystem>();
            if (cardSystem != null)
            {
                float defBonus = cardSystem.GetStatBonus(StatType.Defense);
                return baseDefense * (1f + defBonus);
            }
            
            return baseDefense;
        }
    }

    /// <summary>
    /// 방어력을 고려한 최종 데미지 계산
    /// 역치 공식: 감소율 = 방어력 / (방어력 + 100)
    /// </summary>
    private float CalculateFinalDamage(float damage)
    {
        // 1 & 2. 유효 방어력 계산 (EffectiveDefense 속성 사용)
        float finalDefense = EffectiveDefense;

        // 3. 역치 공식을 통한 감소율 계산 using K = 100
        // (NetworkedArmor.ARMOR_CONSTANT가 private이므로 여기서 hardcode or make public. Assuming 100 based on NetworkedArmor)
        const float ARMOR_CONSTANT = 100f;
        float damageReduction = 0f;
        if (finalDefense > 0f)
        {
            damageReduction = finalDefense / (finalDefense + ARMOR_CONSTANT);
        }

        // 4. 파이프라인 처리 (추가 감소 효과 등)
        var pipelineData = new DamagePipelineData
        {
            IncomingDamage = damage,
            FinalDamage = damage * (1f - damageReduction),
            DamageReduction = damageReduction,
            Attacker = null, // Attacker info assumed to be passed if needed, but CalculateFinalDamage currently takes only float
            Target = this,
            HitPosition = Vector3.zero 
        };

        var cardSystem = GetComponent<PlayerCardSystem>();
        if (cardSystem != null)
        {
            pipelineData = cardSystem.ProcessDamage(pipelineData);
        }
        
        return Mathf.Max(MINIMUM_DAMAGE, pipelineData.FinalDamage);
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
        
        // 카드 스택 추가
        PlayerCardSystem cardSystem = GetComponent<PlayerCardSystem>();
        if (cardSystem != null)
        {
            cardSystem.AddCardStack();
        }
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
