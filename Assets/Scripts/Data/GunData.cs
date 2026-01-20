using UnityEngine;

/// <summary>
/// 원거리 무기(총) 기본 데이터입니다. 서브클래스로 확장하여 다양한 총기 유형을 정의할 수 있습니다.
/// </summary>
[CreateAssetMenu(fileName = "New Gun", menuName = "Project VOID/Weapons/Gun")]
public class GunData : WeaponData
{
    #region Serialized Fields

    [Header("탄약 설정")]
    [SerializeField] private int _magazineSize = 30;
    [SerializeField] private int _maxAmmo = 120;
    [SerializeField] private float _reloadTime = 2f;

    [Header("발사체 설정")]
    [SerializeField] private float _projectileSpeed = 20f;
    [SerializeField] private GameObject _projectilePrefab;
    [SerializeField] private LayerMask _hitLayers;

    [Header("FOV 설정")]
    [SerializeField] private float _circularFOVRange = 10f;
    [Range(30f, 120f)]
    [SerializeField] private float _aimFOVAngle = 60f;
    [SerializeField] private float _aimFOVRange = 0f;

    [Header("시각 효과")]
    [SerializeField] private GameObject _muzzleFlashPrefab;

    #endregion

    #region Properties

    /// <summary>
    /// 탄창 용량을 반환합니다.
    /// </summary>
    public int MagazineSize => _magazineSize;

    /// <summary>
    /// 최대 탄약 수를 반환합니다.
    /// </summary>
    public int MaxAmmo => _maxAmmo;

    /// <summary>
    /// 재장전 시간(초)을 반환합니다.
    /// </summary>
    public float ReloadTime => _reloadTime;

    /// <summary>
    /// 발사체 속도를 반환합니다.
    /// </summary>
    public float ProjectileSpeed => _projectileSpeed;

    /// <summary>
    /// 발사체 프리팹을 반환합니다.
    /// </summary>
    public GameObject ProjectilePrefab => _projectilePrefab;

    /// <summary>
    /// 피격 판정 레이어 마스크를 반환합니다.
    /// </summary>
    public LayerMask HitLayers => _hitLayers;

    /// <summary>
    /// 총구 화염 이펙트 프리팹을 반환합니다.
    /// </summary>
    public GameObject MuzzleFlashPrefab => _muzzleFlashPrefab;

    /// <summary>
    /// 원형 FOV 범위를 반환합니다.
    /// </summary>
    public float CircularFOVRange => _circularFOVRange;

    /// <summary>
    /// 조준 시 FOV 각도를 반환합니다.
    /// </summary>
    public float AimFOVAngle => _aimFOVAngle;

    /// <summary>
    /// 조준 시 FOV 범위를 반환합니다.
    /// </summary>
    public float AimFOVRange => _aimFOVRange > 0f ? _aimFOVRange : Range;

    /// <summary>
    /// 한 번에 발사되는 발사체 수. 서브클래스에서 오버라이드 가능.
    /// </summary>
    public virtual int PelletCount => 1;

    /// <summary>
    /// 총 데미지를 반환합니다 (Damage × PelletCount).
    /// </summary>
    public override float TotalDamage => Damage * PelletCount;

    /// <summary>
    /// 산탄 퍼짐 각도. 서브클래스에서 오버라이드 가능.
    /// </summary>
    public virtual float SpreadAngle => 0f;

    #endregion
}
