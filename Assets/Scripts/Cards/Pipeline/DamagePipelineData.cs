using UnityEngine;
using FishNet.Connection;

/// <summary>
/// 피해 파이프라인 데이터
/// </summary>
public struct DamagePipelineData
{
    public float IncomingDamage;  // 들어오는 데미지
    public float FinalDamage;     // 최종 데미지
    
    public float DamageReduction; // 피해 감소율
    public float ReflectPercent;  // 반사율
    
    public NetworkConnection Attacker;
    public IDamageable Target;
    public Vector3 HitPosition;
}
