using UnityEngine;

/// <summary>
/// 스탯 부스트 Modifier 기본 클래스 (퍼센트 기반)
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

/// <summary>
/// 고정값 스탯 부스트 Modifier 기본 클래스
/// HP, 속도 등 고정값을 사용하는 스탯용
/// </summary>
public abstract class FixedStatBoostModifierBase : ICardModifier
{
    protected PlayerCombat _owner;
    protected float _fixedBonus;

    public int Priority => 10;

    public FixedStatBoostModifierBase(float fixedValue)
    {
        _fixedBonus = fixedValue;
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
        return type == TargetStat ? _fixedBonus : 0f;
    }
}

#region Concrete Stat Modifiers

/// <summary>
/// 공격력 증가 (퍼센트)
/// </summary>
public class AttackBoostModifier : StatBoostModifierBase
{
    public AttackBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.Attack;
}

/// <summary>
/// 방어력 증가 (퍼센트)
/// </summary>
public class DefenseBoostModifier : StatBoostModifierBase
{
    public DefenseBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.Defense;
}

/// <summary>
/// 최대 HP 증가 (고정값)
/// </summary>
public class HealthBoostModifier : FixedStatBoostModifierBase
{
    public HealthBoostModifier(float fixedValue) : base(fixedValue) { }
    public override StatType TargetStat => StatType.Health;

    /// <summary>
    /// 카드 획득 시 고정값만큼 HP 회복
    /// </summary>
    public override void OnAcquired(PlayerCombat owner)
    {
        if (owner != null && owner.IsServerInitialized)
        {
            // 고정값만큼 HP 회복 (MaxHP 상한선 자동 적용)
            owner.ApplyHeal(_fixedBonus);
        }
    }
}

/// <summary>
/// 이동속도 증가 (고정값)
/// </summary>
public class SpeedBoostModifier : FixedStatBoostModifierBase
{
    public SpeedBoostModifier(float fixedValue) : base(fixedValue) { }
    public override StatType TargetStat => StatType.Speed;
}

/// <summary>
/// 공격속도 증가 (퍼센트)
/// </summary>
public class AttackSpeedBoostModifier : StatBoostModifierBase
{
    public AttackSpeedBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.AttackSpeed;
}

/// <summary>
/// 장전속도 증가 (퍼센트)
/// </summary>
public class ReloadSpeedBoostModifier : StatBoostModifierBase
{
    public ReloadSpeedBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.ReloadSpeed;
}

/// <summary>
/// 대시 쿨다운 감소 (퍼센트)
/// </summary>
public class DashCooldownBoostModifier : StatBoostModifierBase
{
    public DashCooldownBoostModifier(float percent) : base(percent) { }
    public override StatType TargetStat => StatType.DashCooldown;
}

#endregion
