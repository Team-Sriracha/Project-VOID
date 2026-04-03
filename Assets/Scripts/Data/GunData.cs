using UnityEngine;

/// <summary>
/// 원거리 무기(총) 공통 데이터입니다.
/// 샷건, 소총, 권총, 저격총처럼 발사체 기반 총기군은 모두 이 데이터 하나로 표현합니다.
/// 유탄발사기나 화염방사기처럼 특수한 총기도 발사체 프리팹과 발사 수/퍼짐 설정 조합으로 확장할 수 있습니다.
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

    [Header("발사 패턴")]
    [Tooltip("한 번의 발사 입력에 생성할 발사체 수")]
    [Range(1, 20)]
    [SerializeField] private int _projectilesPerShot = 1;
    [Tooltip("발사체 퍼짐 각도 (도)")]
    [Range(0f, 45f)]
    [SerializeField] private float _spreadAngle = 0f;

    [Header("FOV 설정")]
    [SerializeField] private float _circularFOVRange = 10f;
    [Range(30f, 120f)]
    [SerializeField] private float _aimFOVAngle = 60f;
    [SerializeField] private float _aimFOVRange = 0f;

    [Header("시각 효과")]
    [SerializeField] private GameObject _muzzleFlashPrefab;

    [Header("사운드")]
    [SerializeField] private AudioCue _fireAudioCue;
    [SerializeField] private AudioCue _reloadStartAudioCue;
    [SerializeField] private AudioCue _reloadCompleteAudioCue;
    [SerializeField] private AudioCue _emptyAmmoAudioCue;

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
    /// 발사 사운드를 반환합니다.
    /// </summary>
    public AudioCue FireAudioCue => _fireAudioCue;

    /// <summary>
    /// 재장전 시작 사운드를 반환합니다.
    /// </summary>
    public AudioCue ReloadStartAudioCue => _reloadStartAudioCue;

    /// <summary>
    /// 재장전 완료 사운드를 반환합니다.
    /// </summary>
    public AudioCue ReloadCompleteAudioCue => _reloadCompleteAudioCue;

    /// <summary>
    /// 빈 탄창 입력 사운드를 반환합니다.
    /// </summary>
    public AudioCue EmptyAmmoAudioCue => _emptyAmmoAudioCue;

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
    /// 한 번의 발사에 생성되는 발사체 수를 반환합니다.
    /// </summary>
    public int ProjectilesPerShot => Mathf.Max(1, _projectilesPerShot);

    /// <summary>
    /// 총 데미지를 반환합니다 (Damage × ProjectilesPerShot).
    /// </summary>
    public override float TotalDamage => Damage * ProjectilesPerShot;

    /// <summary>
    /// 발사체 퍼짐 각도를 반환합니다.
    /// </summary>
    public float SpreadAngle => _spreadAngle;

    #endregion
}
