using Fusion;
using UnityEngine;

/// <summary>
/// 데미지를 받을 수 있는 오브젝트가 구현해야 하는 인터페이스입니다.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// 데미지를 받습니다.
    /// </summary>
    /// <param name="damage">받을 데미지량</param>
    /// <param name="attacker">공격자의 PlayerRef</param>
    /// <param name="hitPosition">피격 위치 (optional, 기본값 = Vector3.zero = 대상 중심 사용)</param>
    void TakeDamage(float damage, PlayerRef attacker, Vector3 hitPosition = default);

    /// <summary>
    /// 현재 생존 상태를 반환합니다.
    /// </summary>
    bool IsAlive { get; }
}
