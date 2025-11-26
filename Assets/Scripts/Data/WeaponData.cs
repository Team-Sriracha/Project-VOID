using UnityEngine;

/// <summary>
/// 무기의 발사 모드를 정의합니다.
/// </summary>
public enum EFireMode
{
    SemiAuto,   // 단발 (클릭마다 1발)
    FullAuto    // 연사 (홀드 중 계속 발사)
}

/// <summary>
/// 무기 데이터의 기본 클래스입니다.
/// 모든 무기 타입의 공통 속성을 정의합니다.
/// ItemData를 상속하여 아이템 시스템과 통합됩니다.
/// </summary>
public abstract class WeaponData : ItemData
{
    #region Serialized Fields

    [Header("전투 속성")]
    [SerializeField] private float _damage = 10f;
    [SerializeField] private float _range = 10f;
    [SerializeField] private float _attackDelay = 0.5f;

    [Header("발사 모드")]
    [Tooltip("SemiAuto: 단발 (클릭마다 1발), FullAuto: 연사 (홀드 중 계속 발사)")]
    [SerializeField] private EFireMode _fireMode = EFireMode.SemiAuto;

    #endregion

    #region Properties

    /// <summary>
    /// 무기 이름을 반환합니다 (ItemData.ItemName 사용).
    /// </summary>
    public string WeaponName => ItemName;

    /// <summary>
    /// 무기의 데미지를 반환합니다.
    /// </summary>
    public float Damage => _damage;

    /// <summary>
    /// 무기의 사거리를 반환합니다.
    /// </summary>
    public float Range => _range;

    /// <summary>
    /// 공격 후 다음 공격까지의 대기 시간을 반환합니다.
    /// </summary>
    public float AttackDelay => _attackDelay;

    /// <summary>
    /// 무기의 발사 모드를 반환합니다.
    /// </summary>
    public EFireMode FireMode => _fireMode;

    // Why: ModelPrefab은 ItemData에서 상속받아 사용

    #endregion
}
