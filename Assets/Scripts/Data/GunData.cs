using Fusion;
using UnityEngine;

/// <summary>
/// 원거리 무기(총) 기본 데이터입니다. 서브클래스로 확장하여 다양한 총기 유형을 정의할 수 있습니다.
/// </summary>
[CreateAssetMenu(fileName = "New Gun", menuName = "Project VOID/Weapons/Gun")]
public class GunData : WeaponData
{
    #region Serialized Fields

    [Header("탄약 설정")]
    [Tooltip("한 탄창에 들어가는 총알 수")]
    [SerializeField] private int _magazineSize = 30;

    [Tooltip("최대 소지 탄약 수")]
    [SerializeField] private int _maxAmmo = 120;

    [Tooltip("재장전 시간 (초)")]
    [SerializeField] private float _reloadTime = 2f;

    [Header("발사체 설정")]
    [SerializeField] private float _projectileSpeed = 20f;
    [SerializeField] private NetworkPrefabRef _projectilePrefab;

    [Tooltip("발사체가 히트할 수 있는 레이어")]
    [SerializeField] private LayerMask _hitLayers;

    [Header("FOV 설정")]
    [Tooltip("비조준 시 원형 시야 거리 (미터)")]
    [SerializeField] private float _circularFOVRange = 10f;

    [Tooltip("조준 시 시야 각도 (도)")]
    [Range(30f, 120f)]
    [SerializeField] private float _aimFOVAngle = 60f;

    [Tooltip("조준 시 시야 거리 (미터) - 0이면 무기 사거리 사용")]
    [SerializeField] private float _aimFOVRange = 0f;

    [Header("시각 효과")]
    [SerializeField] private GameObject _muzzleFlashPrefab;

    #endregion

    #region Properties

    public int MagazineSize => _magazineSize;
    public int MaxAmmo => _maxAmmo;
    public float ReloadTime => _reloadTime;
    public float ProjectileSpeed => _projectileSpeed;
    public NetworkPrefabRef ProjectilePrefab => _projectilePrefab;
    public LayerMask HitLayers => _hitLayers;
    public GameObject MuzzleFlashPrefab => _muzzleFlashPrefab;
    public float CircularFOVRange => _circularFOVRange;
    public float AimFOVAngle => _aimFOVAngle;
    public float AimFOVRange => _aimFOVRange > 0f ? _aimFOVRange : Range;

    /// <summary>
    /// 한 번에 발사되는 발사체 수. 서브클래스에서 오버라이드 가능.
    /// </summary>
    public virtual int PelletCount => 1;

    /// <summary>
    /// 산탄 퍼짐 각도. 서브클래스에서 오버라이드 가능.
    /// </summary>
    public virtual float SpreadAngle => 0f;

    #endregion
}
