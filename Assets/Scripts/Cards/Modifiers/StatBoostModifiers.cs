using UnityEngine;

/// <summary>
/// 스탯 부스트 Modifier 기본 클래스
/// 각 스탯별 Modifier가 상속
/// </summary>
public abstract class StatBoostModifierBase : ICardModifier
{
    protected PlayerCombat _owner;
    protected float _bonusPercent;

    public int Priority => 10;

    public StatBoostModifierBase(float bonusPercent)
    {
        _bonusPercent = bonusPercent / 100f; // 퍼센트 → 비율 변환
    }

    public abstract StatType TargetStat { get; }

    public virtual void Initialize(PlayerCombat owner)
    {
        _owner = owner;
    }

    public virtual void OnAcquired(PlayerCombat owner) { }

    public virtual void OnTick(float tickDelta) { }
    public virtual void ModifyFire(ref FirePipelineData data) { }
    public virtual void ModifyDamage(ref DamagePipelineData data) { }
    public virtual void OnKill(PlayerCombat killer, PlayerCombat victim) { }

    public float GetStatBonus(StatType type)
    {
        return type == TargetStat ? _bonusPercent : 0f;
    }
}

#region Concrete Stat Modifiers

/// <summary>
/// 공격력 증가
/// </summary>
public class AttackBoostModifier : StatBoostModifierBase
{
    public AttackBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.Attack;
}

/// <summary>
/// 방어력 증가
/// </summary>
public class DefenseBoostModifier : StatBoostModifierBase
{
    public DefenseBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.Defense;
}

/// <summary>
/// 최대 HP 증가
/// </summary>
public class HealthBoostModifier : StatBoostModifierBase
{
    public HealthBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.Health;
}

/// <summary>
/// 이동속도 증가
/// </summary>
public class SpeedBoostModifier : StatBoostModifierBase
{
    public SpeedBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.Speed;
}

/// <summary>
/// 공격속도 증가
/// </summary>
public class AttackSpeedBoostModifier : StatBoostModifierBase
{
    public AttackSpeedBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.AttackSpeed;
}

/// <summary>
/// 장전속도 증가
/// </summary>
public class ReloadSpeedBoostModifier : StatBoostModifierBase
{
    public ReloadSpeedBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.ReloadSpeed;
}

/// <summary>
/// 대시 쿨다운 감소
/// </summary>
public class DashCooldownBoostModifier : StatBoostModifierBase
{
    public DashCooldownBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.DashCooldown;
}

#endregion
