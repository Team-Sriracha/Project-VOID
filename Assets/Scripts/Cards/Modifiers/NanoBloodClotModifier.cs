using UnityEngine;

/// <summary>
/// 나노 혈정 (N007) - HP +25, 일정 시간마다 HP 5 회복
/// </summary>
public class NanoBloodClotModifier : ICardModifier
{
    #region Constants

    private const float INSTANT_HP_BONUS = 20f;
    private const float REGEN_AMOUNT = 5f;
    private const float REGEN_INTERVAL = 10f;

    #endregion

    #region Private Fields

    private PlayerCombat _owner;
    private float _regenTimer;

    #endregion

    #region Properties

    public int Priority => 10;

    #endregion

    #region ICardModifier Implementation

    public void Initialize(PlayerCombat owner)
    {
        _owner = owner;
        _regenTimer = 0f;
    }

    public void OnAcquired(PlayerCombat owner)
    {
        _owner = owner;
        
        // 즉시 HP 20 회복 (User Request)
        if (_owner != null && _owner.IsServerInitialized)
        {
            _owner.ApplyHeal(INSTANT_HP_BONUS);
            Debug.Log($"[NanoBloodClotModifier] OnAcquired 호출됨. 고정 치유량: {INSTANT_HP_BONUS}. (MaxHP 증가 없음)");
        }
    }

    public void OnTick(float tickDelta)
    {
        if (_owner == null || !_owner.IsAlive) return;

        _regenTimer += tickDelta;

        if (_regenTimer >= REGEN_INTERVAL)
        {
            _regenTimer -= REGEN_INTERVAL;

            if (_owner.IsServerInitialized)
            {
                _owner.ApplyHeal(REGEN_AMOUNT);
                Debug.Log($"[NanoBloodClotModifier] 패시브 HP 회복 +{REGEN_AMOUNT}");
            }
        }
    }

    public void ModifyFire(ref FirePipelineData data)
    {
        // 발사 파이프라인에 영향 없음
    }

    public void ModifyDamage(ref DamagePipelineData data)
    {
        // 데미지 파이프라인에 영향 없음
    }

    public void OnKill(PlayerCombat killer, PlayerCombat victim)
    {
        // 킬 이벤트 없음
    }

    public float GetStatBonus(StatType type)
    {
        // 나노 혈청은 스탯 보너스(MaxHP 증가)를 주지 않음
        return 0f;
    }

    #endregion
}
