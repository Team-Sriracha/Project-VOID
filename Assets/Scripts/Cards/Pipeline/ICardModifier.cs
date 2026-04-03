/// <summary>
/// 카드 효과 인터페이스 (파이프라인 단계)
/// </summary>
public interface ICardModifier
{
    /// <summary>
    /// 실행 우선순위 (낮을수록 먼저 실행)
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// 초기화 (Modifier 생성 시 호출, RebuildStats 등 재구성 시에도 호출됨)
    /// </summary>
    void Initialize(PlayerCombat owner);

    /// <summary>
    /// 카드 최초 획득 시 1회 호출 (즉시 효과용)
    /// </summary>
    void OnAcquired(PlayerCombat owner);
    
    /// <summary>
    /// 매 틱마다 호출 (패시브 효과용)
    /// </summary>
    void OnTick(float tickDelta);
    
    /// <summary>
    /// 발사 데이터 수정
    /// </summary>
    void ModifyFire(ref FirePipelineData data);
    
    /// <summary>
    /// 피해 데이터 수정
    /// </summary>
    void ModifyDamage(ref DamagePipelineData data);
    
    /// <summary>
    /// 킬 발생 시 호출
    /// </summary>
    void OnKill(PlayerCombat killer, PlayerCombat victim);

    /// <summary>
    /// 패시브 스탯 보너스 조회
    /// </summary>
    float GetStatBonus(StatType type);
}
