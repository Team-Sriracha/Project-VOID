using System.Collections.Generic;
using System.Linq;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// 플레이어 카드 시스템 (서버 권한)
/// </summary>
public class PlayerCardSystem : NetworkBehaviour
{
    #region Serialized Fields

    [SerializeField] private CardConfig _cardConfig;

    #endregion

    #region SyncVars

    /// <summary>
    /// 사용 가능한 카드 뽑기 스택
    /// </summary>
    public readonly SyncVar<int> CardDrawStacks = new();

    /// <summary>
    /// 보유 중인 카드 ID 목록 (최대 20개)
    /// </summary>
    public readonly SyncList<string> OwnedCardIds = new();

    /// <summary>
    /// 속도 배율 (CSP 동기화용 - 즉시 반영)
    /// </summary>
    public readonly SyncVar<float> SyncSpeedMultiplier = new(1f);

    #endregion

    #region Private Fields

    // 스탯 누적 추적 (서버)
    private Dictionary<StatType, float> _statBonuses = new();

    // 보유 특수능력 (서버)
    private string _commonAbilityId;
    private string _weaponAbilityId;

    // 활성화된 카드 효과들 (ID로 관리)
    private Dictionary<string, ICardModifier> _modifierById = new();
    private List<ICardModifier> _sortedModifiers = new();
    private bool _modifiersDirty = false;

    // 타이머
    private float _cardGrantTimer;

    // 현재 뽑기 중인 카드 목록 (서버 전용)
    private CardData[] _currentDrawnCards;

    // 참조
    private PlayerCombat _playerCombat;
    private NetworkedWeapon _weaponSystem;

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (_cardConfig == null)
        {
            Debug.LogError("[PlayerCardSystem] CardConfig가 할당되지 않았습니다!");
            return;
        }

        CardConfig.Instance = _cardConfig;

        _playerCombat = GetComponent<PlayerCombat>();
        _weaponSystem = GetComponent<NetworkedWeapon>();

        TimeManager.OnTick += OnTick;
    }

    public override void OnStopServer()
    {
        base.OnStopServer();

        if (TimeManager != null)
        {
            TimeManager.OnTick -= OnTick;
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (_cardConfig != null && CardConfig.Instance == null)
        {
            CardConfig.Instance = _cardConfig;
        }

        // 클라이언트에서 카드 변경 감지하여 스탯/Modifier 동기화
        OwnedCardIds.OnChange += OnOwnedCardsChanged;
        
        // 초기 스탯 계산 (이미 보유한 카드가 있을 경우)
        if (OwnedCardIds.Count > 0)
        {
            RebuildStats();
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        OwnedCardIds.OnChange -= OnOwnedCardsChanged;
    }

    #endregion

    #region Client Sync Logic

    /// <summary>
    /// SyncList 변경 시 호출 (클라이언트)
    /// </summary>
    private void OnOwnedCardsChanged(SyncListOperation op, int index, string oldItem, string newItem, bool asServer)
    {
        // 서버에서 실행된 콜백은 무시 (이미 적용됨)
        if (asServer) return;

        // 클라이언트 사이드 스탯 재계산
        RebuildStats();
    }

    // 스탯 재계산 중 이벤트 발생 방지 플래그
    private bool _suppressStatEvents = false;

    /// <summary>
    /// 보유한 카드 ID 목록을 기반으로 스탯과 Modifier를 재구성 (클라이언트/Host용)
    /// </summary>
    private void RebuildStats()
    {
        // 재계산 전 MaxHP 저장
        float initialMaxHP = (_playerCombat != null) ? _playerCombat.MaxHP : 0f;

        // 이벤트 억제 시작
        _suppressStatEvents = true;

        // 초기화
        _statBonuses.Clear();
        _modifierById.Clear();
        _sortedModifiers.Clear();
        _modifiersDirty = false;
        _commonAbilityId = null;
        _weaponAbilityId = null;

        foreach (var cardId in OwnedCardIds)
        {
            var cardData = _cardConfig.GetCard(cardId);
            if (cardData != null)
            {
                ApplyCardEffectInternal(cardData);
            }
        }

        // 이벤트 억제 해제
        _suppressStatEvents = false;

        // MaxHP 변경분 체크 및 적용 (Host/Client 모두)
        if (_playerCombat != null)
        {
            float finalMaxHP = _playerCombat.MaxHP;
            if (!Mathf.Approximately(initialMaxHP, finalMaxHP))
            {
                _playerCombat.OnMaxHealthModified(initialMaxHP);
            }
        }
    }

    /// <summary>
    /// 내부적으로 카드 효과 적용 (서버/클라이언트 공용)
    /// 통합 구조: 모든 카드는 AbilityID로 Modifier 생성
    /// </summary>
    private ICardModifier ApplyCardEffectInternal(CardData card)
    {
        if (string.IsNullOrEmpty(card.AbilityID))
        {
            Debug.LogWarning($"[PlayerCardSystem] 카드 {card.CardID}에 AbilityID가 없습니다.");
            return null;
        }

        // Factory로 Modifier 생성
        ICardModifier modifier = CardModifierFactory.Create(card.AbilityID);
        bool isStatBoost = modifier is StatBoostModifierBase;

        // 고유 능력인 경우에만 ID 등록 (스탯 부스트는 예외)
        if (!isStatBoost)
        {
            if (card.Category == AbilityCategory.Common)
            {
                if (!string.IsNullOrEmpty(_commonAbilityId) && _commonAbilityId != card.AbilityID) return null;
                _commonAbilityId = card.AbilityID;
            }
            else if (card.Category != AbilityCategory.Common)
            {
                if (!string.IsNullOrEmpty(_weaponAbilityId) && _weaponAbilityId != card.AbilityID) return null;
                _weaponAbilityId = card.AbilityID;
            }
        }

        if (modifier != null)
        {
            // 스탯 부스트 Modifier인 경우 스탯 보너스 업데이트
            if (isStatBoost)
            {
                var statModifier = modifier as StatBoostModifierBase;
                UpdateStatBonus(statModifier.TargetStat, statModifier.GetStatBonus(statModifier.TargetStat));
            }

            modifier.Initialize(_playerCombat);
            AddModifier(card.AbilityID, modifier);
        }
        
        return modifier;
    }

    /// <summary>
    /// 스탯 보너스 업데이트
    /// </summary>
    private void UpdateStatBonus(StatType stat, float bonusToAdd)
    {
        float prevMaxHP = (_playerCombat != null) ? _playerCombat.MaxHP : 0f;

        float currentBonus = _statBonuses.GetValueOrDefault(stat, 0f);
        float newBonus = currentBonus + bonusToAdd;
        float maxBonus = _cardConfig.GetMaxBonus(stat);

        newBonus = Mathf.Min(newBonus, maxBonus);
        _statBonuses[stat] = newBonus;

        // Speed 타입인 경우 SyncVar 즉시 업데이트 (재계산 중에도 동기화 값 리셋 방지를 위해 필요하면 갱신)
        if (stat == StatType.Speed && IsServerInitialized)
        {
            SyncSpeedMultiplier.Value = 1f + newBonus;
        }

        // Health 타입인 경우 PlayerCombat에 알림 (이벤트 억제 플래그 확인)
        if (stat == StatType.Health && _playerCombat != null && !_suppressStatEvents)
        {
            _playerCombat.OnMaxHealthModified(prevMaxHP);
        }
    }

    #endregion

    #region Card Grant Timer

    private void OnTick()
    {
        if (!IsServerInitialized) return;
        if (_playerCombat != null && !_playerCombat.IsAlive) return;

        float tickDelta = (float)TimeManager.TickDelta;

        // Modifier들의 OnTick 호출 (패시브 효과용)
        foreach (var modifier in GetSortedModifiers())
        {
            modifier.OnTick(tickDelta);
        }

        // 카드 지급 로직
        if (OwnedCardIds.Count + CardDrawStacks.Value >= _cardConfig.MaxOwnedCards) return;

        _cardGrantTimer += tickDelta;
        if (_cardGrantTimer >= _cardConfig.CardGrantInterval)
        {
            _cardGrantTimer = 0f;
            CardDrawStacks.Value++;
        }
    }

    public void AddCardStack()
    {
        if (!IsServerInitialized) return;
        if (OwnedCardIds.Count + CardDrawStacks.Value >= _cardConfig.MaxOwnedCards) return;

        CardDrawStacks.Value++;
    }

    #endregion

    #region Card Draw

    [ServerRpc(RequireOwnership = true)]
    public void RPC_RequestDrawCards()
    {
        if (CardDrawStacks.Value <= 0)
        {
            Debug.LogWarning("[PlayerCardSystem] 카드 스택이 0입니다.");
            return;
        }
        if (OwnedCardIds.Count >= _cardConfig.MaxOwnedCards)
        {
            Debug.LogWarning("[PlayerCardSystem] 최대 카드 보유량 도달.");
            return;
        }

        var availableCards = GetAvailableCards();
        
        // 카드가 0장이면 뽑기 불가
        if (availableCards.Count == 0)
        {
            Debug.LogWarning("[PlayerCardSystem] 뽑을 카드가 없습니다.");
            return;
        }

        int countToDraw = Mathf.Min(3, availableCards.Count);
        _currentDrawnCards = DrawCards(availableCards, countToDraw);
        string[] cardIds = _currentDrawnCards.Select(c => c.CardID).ToArray();
        
        RPC_ShowCardSelection(Owner, cardIds);
    }

    private List<CardData> GetAvailableCards()
    {
        var result = new List<CardData>();
        WeaponType currentWeaponType = GetCurrentWeaponType();

        foreach (var card in _cardConfig.AllCards)
        {
            if (card == null || string.IsNullOrEmpty(card.AbilityID)) continue;

            // 1. 고유 카드 중복 체크 (IsUnique가 true이고 이미 가지고 있으면 제외)
            if (card.IsUnique && OwnedCardIds.Contains(card.CardID)) continue;

            // 2. 상호 배제 카드 체크 (MutuallyExclusiveCards에 있는 카드를 보유 중이면 제외)
            if (card.MutuallyExclusiveCards != null && card.MutuallyExclusiveCards.Count > 0)
            {
                bool isExcluded = false;
                foreach (var exCard in card.MutuallyExclusiveCards)
                {
                    if (exCard != null && OwnedCardIds.Contains(exCard.CardID))
                    {
                        isExcluded = true;
                        break;
                    }
                }
                if (isExcluded) continue;
            }

            // Modifier 미리 생성하여 타입 확인
            ICardModifier tempModifier = CardModifierFactory.Create(card.AbilityID);
            bool isStatBoost = (tempModifier is StatBoostModifierBase);

            if (isStatBoost)
            {
                var statModifier = tempModifier as StatBoostModifierBase;
                float currentBonus = _statBonuses.GetValueOrDefault(statModifier.TargetStat, 0f);
                float maxBonus = _cardConfig.GetMaxBonus(statModifier.TargetStat);
                
                // 현재 보너스가 이미 최대치 이상이면 제외
                if (currentBonus >= maxBonus) continue;
            }
            else
            {
                // 고유 능력 카테고리 슬롯 체크 (Common/Weapon)
                // 이미 해당 슬롯을 차지한 카드가 있고, 그것이 '다른' 카드라면 등장 불가
                // (IsUnique가 false라면 같은 카드는 등장 허용 -> 스택킹)
                if (card.Category == AbilityCategory.Common)
                {
                    if (!string.IsNullOrEmpty(_commonAbilityId) && _commonAbilityId != card.AbilityID)
                        continue;
                }
                else if (card.Category != AbilityCategory.Common)
                {
                    if (!string.IsNullOrEmpty(_weaponAbilityId) && _weaponAbilityId != card.AbilityID)
                        continue;
                }
            }

            // 무기 전용 카드 무기 타입 체크
            if (card.Category == AbilityCategory.Melee && currentWeaponType != WeaponType.Melee)
                continue;
            if (card.Category == AbilityCategory.Ranged && currentWeaponType != WeaponType.Ranged)
                continue;

            result.Add(card);
        }

        return result;
    }

    private CardData[] DrawCards(List<CardData> availableCards, int count)
    {
        var result = new CardData[count];
        var tempAvailable = new List<CardData>(availableCards);

        for (int i = 0; i < count; i++)
        {
            // 카드가 없으면 중단 (이론상 발생 안 함)
            if (tempAvailable.Count == 0) break;

            CardRarity rarity = RollRarity();
            var candidates = tempAvailable.Where(c => c.Rarity == rarity).ToList();

            if (candidates.Count == 0)
            {
                candidates = tempAvailable; // 해당 등급 없으면 전체 중에서
            }

            var picked = candidates[Random.Range(0, candidates.Count)];
            result[i] = picked;
            tempAvailable.Remove(picked); // 중복 뽑기 방지
        }

        return result;
    }

    private CardRarity RollRarity()
    {
        float roll = Random.value;

        if (roll < _cardConfig.ThreeStarChance)
            return CardRarity.ThreeStar;
        else if (roll < _cardConfig.ThreeStarChance + _cardConfig.TwoStarChance)
            return CardRarity.TwoStar;
        else
            return CardRarity.OneStar;
    }

    private WeaponType GetCurrentWeaponType()
    {
        if (_weaponSystem == null || _weaponSystem.CurrentWeaponData == null)
            return WeaponType.Ranged;

        return _weaponSystem.CurrentWeaponData is MeleeWeaponData 
            ? WeaponType.Melee 
            : WeaponType.Ranged;
    }

    #endregion

    #region Card Selection

    [TargetRpc]
    private void RPC_ShowCardSelection(NetworkConnection conn, string[] cardIds)
    {
        if (UIManager.Instance == null)
        {
            Debug.LogError("[PlayerCardSystem] UIManager.Instance가 null입니다!");
            return;
        }

        UIManager.Instance.ShowCardSelection(cardIds);
    }

    [ServerRpc(RequireOwnership = true)]
    public void RPC_SelectCard(int cardIndex)
    {
        if (_currentDrawnCards == null || cardIndex < 0 || cardIndex >= _currentDrawnCards.Length)
        {
            Debug.LogWarning("[PlayerCardSystem] 유효하지 않은 카드 인덱스");
            return;
        }

        CardData selected = _currentDrawnCards[cardIndex];
        ApplyCard(selected);

        CardDrawStacks.Value--;
        OwnedCardIds.Add(selected.CardID);
        _currentDrawnCards = null;
    }

    #endregion

    #region Card Application

    private void ApplyCard(CardData card)
    {
        ICardModifier modifier = ApplyCardEffectInternal(card);
        
        // 새로 획득한 카드인 경우 OnAcquired 호출
        if (modifier != null)
        {
            modifier.OnAcquired(_playerCombat);
        }
    }

    #endregion

    #region Modifier Management

    private void AddModifier(string id, ICardModifier modifier)
    {
        _modifierById[id] = modifier;
        _modifiersDirty = true;
    }

    private void RemoveModifier(string id)
    {
        if (_modifierById.Remove(id))
        {
            _modifiersDirty = true;
        }
    }

    private List<ICardModifier> GetSortedModifiers()
    {
        if (_modifiersDirty)
        {
            _sortedModifiers = _modifierById.Values.OrderBy(m => m.Priority).ToList();
            _modifiersDirty = false;
        }
        return _sortedModifiers;
    }

    #endregion

    #region Weapon Change Handler

    public void HandleWeaponChange(WeaponType newType)
    {
        if (string.IsNullOrEmpty(_weaponAbilityId)) return;

        var currentAbilityCard = _cardConfig.AllCards.FirstOrDefault(c => c.AbilityID == _weaponAbilityId);
        if (currentAbilityCard == null) return;

        bool shouldRemove = (currentAbilityCard.Category == AbilityCategory.Melee && newType != WeaponType.Melee) ||
                            (currentAbilityCard.Category == AbilityCategory.Ranged && newType != WeaponType.Ranged);

        if (shouldRemove)
        {
            OwnedCardIds.Remove(currentAbilityCard.CardID);
            
            // Modifier 제거
            RemoveModifier(_weaponAbilityId);
            
            _weaponAbilityId = null;
        }
    }

    #endregion

    #region Pipeline Processing

    public FirePipelineData ProcessFire(FirePipelineData data)
    {
        // 스탯 보너스 적용
        float atkBonus = GetStatBonus(StatType.Attack);
        data.FinalDamage = data.BaseDamage * (1f + atkBonus);
        data.FinalProjectileCount = data.BaseProjectileCount;
        data.FinalRange = data.BaseRange;

        // Modifier 적용 (정렬된 캐시 사용)
        foreach (var modifier in GetSortedModifiers())
        {
            modifier.ModifyFire(ref data);
        }
        
        return data;
    }

    public DamagePipelineData ProcessDamage(DamagePipelineData data)
    {
        data.FinalDamage = data.IncomingDamage * (1f - data.DamageReduction);

        // Modifier 적용
        foreach (var modifier in GetSortedModifiers())
        {
            modifier.ModifyDamage(ref data);
        }
        
        return data;
    }

    public void NotifyKill(PlayerCombat killer, PlayerCombat victim)
    {
        foreach (var modifier in GetSortedModifiers())
        {
            modifier.OnKill(killer, victim);
        }
    }

    #endregion

    #region Stat Getters

    public float GetStatBonus(StatType stat)
    {
        return _statBonuses.GetValueOrDefault(stat, 0f);
    }

    /// <summary>
    /// 속도 보너스 (PlayerController에서 사용)
    /// </summary>
    public float GetSpeedMultiplier()
    {
        // SyncVar 값 직접 사용 (CSP 동기화를 위해 즉시 반영)
        return SyncSpeedMultiplier.Value;
    }

    /// <summary>
    /// HP 보너스 (PlayerCombat에서 사용)
    /// </summary>
    public float GetHealthMultiplier()
    {
        return 1f + GetStatBonus(StatType.Health);
    }

    /// <summary>
    /// 공격속도 배수 (NetworkedWeapon에서 사용)
    /// 높을수록 빠름 (1.0 + bonus)
    /// </summary>
    public float GetAttackSpeedMultiplier()
    {
        return 1f + GetStatBonus(StatType.AttackSpeed);
    }

    /// <summary>
    /// 장전속도 배수 (NetworkedWeapon에서 사용)
    /// 높을수록 빠름 (1.0 + bonus)
    /// </summary>
    public float GetReloadSpeedMultiplier()
    {
        return 1f + GetStatBonus(StatType.ReloadSpeed);
    }

    /// <summary>
    /// 대시 쿨다운 배수 (PlayerController에서 사용)
    /// 감소이므로 (1.0 - bonus), 최소 0.1 보장
    /// </summary>
    public float GetDashCooldownMultiplier()
    {
        return Mathf.Max(0.1f, 1f - GetStatBonus(StatType.DashCooldown));
    }

    #endregion

    #region Session Reset

    /// <summary>
    /// 세션 종료 시 초기화
    /// </summary>
    public void ResetSession()
    {
        _statBonuses.Clear();
        _modifierById.Clear();
        _sortedModifiers.Clear();
        _modifiersDirty = false;
        _commonAbilityId = null;
        _weaponAbilityId = null;
        _cardGrantTimer = 0f;
        _currentDrawnCards = null;

        OwnedCardIds.Clear();
        CardDrawStacks.Value = 0;
    }

    #endregion
}
