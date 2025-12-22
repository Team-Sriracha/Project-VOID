using Fusion;
using UnityEngine;

/// <summary>
/// 원거리 무기(총) 데이터를 정의합니다.
/// </summary>
[CreateAssetMenu(fileName = "New Gun", menuName = "Project VOID/Weapons/Gun Data")]
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

    [Header("시각 효과")]
    [SerializeField] private GameObject _muzzleFlashPrefab;

    #endregion

    #region Properties

    /// <summary>
    /// 한 탄창에 들어가는 총알 수를 반환합니다.
    /// </summary>
    public int MagazineSize => _magazineSize;

    /// <summary>
    /// 최대 소지 탄약 수를 반환합니다.
    /// </summary>
    public int MaxAmmo => _maxAmmo;

    /// <summary>
    /// 재장전 시간 (초)을 반환합니다.
    /// </summary>
    public float ReloadTime => _reloadTime;

    /// <summary>
    /// 발사체의 이동 속도를 반환합니다.
    /// </summary>
    public float ProjectileSpeed => _projectileSpeed;

    /// <summary>
    /// 발사체 프리팹 참조를 반환합니다.
    /// </summary>
    public NetworkPrefabRef ProjectilePrefab => _projectilePrefab;

    /// <summary>
    /// 발사체가 히트할 수 있는 레이어를 반환합니다.
    /// </summary>
    public LayerMask HitLayers => _hitLayers;

    /// <summary>
    /// 머즐 플래시 이펙트 프리팹을 반환합니다.
    /// </summary>
    public GameObject MuzzleFlashPrefab => _muzzleFlashPrefab;

    #endregion
}
