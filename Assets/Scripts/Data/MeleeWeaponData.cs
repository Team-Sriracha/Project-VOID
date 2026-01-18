using UnityEngine;

/// <summary>
/// 근거리 무기 데이터를 정의합니다.
/// </summary>
[CreateAssetMenu(fileName = "New Melee Weapon", menuName = "Project VOID/Weapons/Melee Weapon Data")]
public class MeleeWeaponData : WeaponData
{
    #region Serialized Fields

    [Header("근접 공격 설정")]
    [Range(30f, 180f)]
    [SerializeField] private float _attackAngle = 90f;

    [SerializeField] private LayerMask _hitLayers;

    [Header("FOV 설정")]
    [Tooltip("근접 무기 장착 시 원형 시야 범위 (미터)")]
    [SerializeField] private float _circularFOVRange = 8f;

    [Header("시각 효과")]
    [SerializeField] private GameObject _swingEffectPrefab;
    [SerializeField] private GameObject _hitEffectPrefab;

    #endregion

    #region Properties

    /// <summary>
    /// 근접 공격의 각도를 반환합니다.
    /// </summary>
    public float AttackAngle => _attackAngle;

    /// <summary>
    /// 근접 공격이 히트할 수 있는 레이어를 반환합니다.
    /// </summary>
    public LayerMask HitLayers => _hitLayers;

    /// <summary>
    /// 근접 공격 휘두르기 이펙트 프리팹을 반환합니다.
    /// </summary>
    public GameObject SwingEffectPrefab => _swingEffectPrefab;

    /// <summary>
    /// 근접 공격 히트 이펙트 프리팹을 반환합니다.
    /// </summary>
    public GameObject HitEffectPrefab => _hitEffectPrefab;

    /// <summary>
    /// 근접 무기 장착 시 원형 시야 범위를 반환합니다.
    /// </summary>
    public float CircularFOVRange => _circularFOVRange;

    #endregion
}
