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
/// </summary>
public abstract class WeaponData : ScriptableObject
{
    #region Serialized Fields

    [Header("기본 정보")]
    [SerializeField] private string _weaponName = "Weapon";

    [Header("전투 속성")]
    [SerializeField] private float _damage = 10f;
    [SerializeField] private float _range = 10f;
    [SerializeField] private float _attackDelay = 0.5f;

    [Header("발사 모드")]
    [Tooltip("SemiAuto: 단발 (클릭마다 1발), FullAuto: 연사 (홀드 중 계속 발사)")]
    [SerializeField] private EFireMode _fireMode = EFireMode.SemiAuto;

    [Header("모델")]
    [Tooltip("무기 모델 프리팹 (플레이어 손에 표시됨)")]
    [SerializeField] private GameObject _modelPrefab;

    #endregion

    #region Properties

    /// <summary>
    /// 무기 이름을 반환합니다.
    /// </summary>
    public string WeaponName => _weaponName;

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

    /// <summary>
    /// 무기 모델 프리팹을 반환합니다.
    /// </summary>
    public GameObject ModelPrefab => _modelPrefab;

    #endregion
}
