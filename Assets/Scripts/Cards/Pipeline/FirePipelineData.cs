using UnityEngine;
using FishNet.Connection;

/// <summary>
/// 발사 파이프라인 데이터
/// </summary>
public struct FirePipelineData
{
    // 데미지
    public float BaseDamage;
    public float FinalDamage;
    
    // 총알
    public int BaseProjectileCount;
    public int FinalProjectileCount;
    
    // 사거리
    public float BaseRange;
    public float FinalRange;
    
    // 속성
    public bool HasFireDamage;
    public bool HasPierce;
    public bool HasExplosive;
    
    // 발사 패턴
    public FireMode FireMode; // 무기의 발사 모드
    public bool UseParallelFire;
    public float SpreadAngle; // 무기 기본 확산각 (입력용)
    public int BurstCount;    // 점사 횟수 (1이면 단발)
    public float BurstDelay;  // 점사 간격
    
    // 추가 데미지
    public float FireDamageBonus;
    
    // 발사자
    public NetworkConnection Owner;
}
