using UnityEngine;

/// <summary>
/// 플레이어의 모든 스탯 (HP, 이동/대시 파라미터 등)을 보관하는 ScriptableObject입니다.
/// Assets > Create > Project VOID > Player Stats 메뉴를 통해 생성할 수 있습니다.
/// </summary>
[CreateAssetMenu(fileName = "New Player Stats", menuName = "Project VOID/Player Stats")]
public class PlayerStats : ScriptableObject
{
    #region Serialized Fields

    [Header("체력")]
    [Tooltip("최대 체력 (HP)")]
    [SerializeField] private float _maxHP = 100f;

    [Header("이동")]
    [Tooltip("기본 이동 속도")]
    [SerializeField] private float _moveSpeed = 5f;

    [Header("대시")]
    [Tooltip("대시 중 속도")]
    [SerializeField] private float _dashSpeed = 15f;

    [Tooltip("대시 지속 시간 (초)")]
    [SerializeField] private float _dashDuration = 0.3f;

    [Tooltip("대시 쿨다운 (초)")]
    [SerializeField] private float _dashCooldown = 1f;

    [Header("레벨 & 경험치")]
    [Tooltip("레벨당 필요한 기본 XP 양")]
    [SerializeField] private float _baseXPPerLevel = 100f;

    [Tooltip("레벨이 오를수록 필요 XP 증가율 (1.0 = 선형, 1.5 = 지수형)")]
    [SerializeField] private float _xpScalingFactor = 1.2f;

    [Tooltip("플레이어 처치 시 획득하는 XP")]
    [SerializeField] private float _xpPerPlayerKill = 50f;



    [Header("기본 무기")]
    [Tooltip("플레이어가 시작할 때 가지는 기본 무기 (주먹 등)")]
    [SerializeField] private WeaponData _defaultWeapon;

    #endregion

    #region Properties

    /// <summary>
    /// 최대 체력 (HP)을 반환합니다.
    /// </summary>
    public float MaxHP => _maxHP;

    /// <summary>
    /// 기본 이동 속도를 반환합니다.
    /// </summary>
    public float MoveSpeed => _moveSpeed;

    /// <summary>
    /// 대시 중 속도를 반환합니다.
    /// </summary>
    public float DashSpeed => _dashSpeed;

    /// <summary>
    /// 대시 지속 시간 (초)을 반환합니다.
    /// </summary>
    public float DashDuration => _dashDuration;

    /// <summary>
    /// 대시 쿨다운 (초)을 반환합니다.
    /// </summary>
    public float DashCooldown => _dashCooldown;

    /// <summary>
    /// 레벨당 필요한 기본 XP 양을 반환합니다.
    /// </summary>
    public float BaseXPPerLevel => _baseXPPerLevel;

    /// <summary>
    /// 레벨이 오를수록 필요 XP 증가율을 반환합니다.
    /// </summary>
    public float XPScalingFactor => _xpScalingFactor;

    /// <summary>
    /// 플레이어 처치 시 획득하는 XP를 반환합니다.
    /// </summary>
    public float XPPerPlayerKill => _xpPerPlayerKill;



    /// <summary>
    /// 플레이어가 시작할 때 가지는 기본 무기를 반환합니다.
    /// </summary>
    public WeaponData DefaultWeapon => _defaultWeapon;

    #endregion

    #region Public Methods

    /// <summary>
    /// 특정 레벨에 도달하기 위해 필요한 총 XP를 계산합니다.
    /// </summary>
    /// <param name="level">목표 레벨</param>
    /// <returns>필요한 총 XP</returns>
    public float GetXPForLevel(int level)
    {
        if (level <= 1) return 0f;
        return _baseXPPerLevel * Mathf.Pow(level - 1, _xpScalingFactor);
    }

    #endregion
}
