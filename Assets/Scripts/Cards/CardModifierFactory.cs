using System;
using System.Collections.Generic;

/// <summary>
/// 카드 Modifier 팩토리 (OCP 준수)
/// </summary>
public static class CardModifierFactory
{
    private static readonly Dictionary<string, Func<ICardModifier>> _modifierCreators = new()
    {
        // 특수 능력
        { "DoubleShot", () => new DoubleShotModifier() },
        { "NanoBloodClot", () => new NanoBloodClotModifier() },
        
        // 스탯 부스트 (5% 기본값, 카드별로 다르게 설정 가능)
        { "AttackBoost_5", () => new AttackBoostModifier(5f) },
        { "AttackBoost_10", () => new AttackBoostModifier(10f) },
        { "DefenseBoost_5", () => new DefenseBoostModifier(5f) },
        { "DefenseBoost_10", () => new DefenseBoostModifier(10f) },
        { "HealthBoost_20", () => new HealthBoostModifier(20f) },  // 고정 +20 HP
        { "SpeedBoost_1", () => new SpeedBoostModifier(1f) },      // 고정 +1 속도
        { "AttackSpeedBoost_5", () => new AttackSpeedBoostModifier(5f) },
        { "ReloadSpeedBoost_10", () => new ReloadSpeedBoostModifier(10f) },
        { "DashCooldownBoost_10", () => new DashCooldownBoostModifier(10f) },
    };

    /// <summary>
    /// AbilityID로 Modifier 생성
    /// </summary>
    public static ICardModifier Create(string abilityId)
    {
        if (string.IsNullOrEmpty(abilityId))
        {
            return null;
        }

        if (_modifierCreators.TryGetValue(abilityId, out var creator))
        {
            return creator();
        }
        
        UnityEngine.Debug.LogWarning($"[CardModifierFactory] 등록되지 않은 AbilityID: {abilityId}");
        return null;
    }

    /// <summary>
    /// 런타임에 Modifier 등록 (확장용)
    /// </summary>
    public static void Register(string abilityId, Func<ICardModifier> creator)
    {
        _modifierCreators[abilityId] = creator;
    }
}

