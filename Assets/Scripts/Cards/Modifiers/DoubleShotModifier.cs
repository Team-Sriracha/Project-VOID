using UnityEngine;

/// <summary>
/// 더블샷 (특수능력) - 총알 2배
/// </summary>
public class DoubleShotModifier : ICardModifier
{
    public int Priority => 5; // 특수능력은 높은 우선순위

    public void Initialize(PlayerCombat owner)
    {
        // 초기화 불필요
    }

    public void OnAcquired(PlayerCombat owner) { }

    public void OnTick(float tickDelta)
    {
        // 틱 처리 불필요
    }

    public void ModifyFire(ref FirePipelineData data)
    {
        // 연사 무기 (Automatic) -> 병렬 발사 (Parallel)
        if (data.FireMode == FireMode.FullAuto)
        {
            data.FinalProjectileCount *= 2;
            data.UseParallelFire = true;
            Debug.Log("[DoubleShotModifier] 연사 무기: 병렬 발사 (Parallel) 적용");
        }
        // 단발/반자동 무기 (SemiAuto) -> 점사 (Burst)
        else
        {
            data.BurstCount = 2; // 2점사
            data.BurstDelay = 0.15f; // 0.15초 간격
            Debug.Log("[DoubleShotModifier] 단발 무기: 점사 (Burst) 적용");
        }
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
        return 0f;
    }
}
