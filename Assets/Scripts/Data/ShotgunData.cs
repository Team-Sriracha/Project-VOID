using UnityEngine;

/// <summary>
/// 샷건 무기 데이터입니다. 산탄 발사 기능을 제공합니다.
/// </summary>
[CreateAssetMenu(fileName = "New Shotgun", menuName = "Project VOID/Weapons/Shotgun")]
public class ShotgunData : GunData
{
    #region Serialized Fields

    [Header("산탄 설정")]
    [Tooltip("한 번에 발사되는 산탄 수")]
    [Range(2, 20)]
    [SerializeField] private int _pelletCount = 8;

    [Tooltip("산탄 퍼짐 각도 (도)")]
    [Range(5f, 45f)]
    [SerializeField] private float _spreadAngle = 20f;

    #endregion

    #region Properties

    public override int PelletCount => _pelletCount;
    public override float SpreadAngle => _spreadAngle;

    #endregion
}
