using UnityEngine;

/// <summary>
/// 방어구 아이템 데이터 (미래 확장용)
/// </summary>
[CreateAssetMenu(fileName = "NewArmorItem", menuName = "Project VOID/Items/Armor Item")]
public class ArmorItemData : ItemData
{
    #region Serialized Fields

    [Header("방어구 설정")]
    [Tooltip("방어력")]
    [SerializeField] private float _defense = 0f;

    [Tooltip("이동 속도 감소")]
    [SerializeField] private float _moveSpeedPenalty = 0f;

    #endregion

    #region Properties

    /// <summary>
    /// 방어력을 반환합니다.
    /// </summary>
    public float Defense => _defense;

    /// <summary>
    /// 이동 속도 감소를 반환합니다.
    /// </summary>
    public float MoveSpeedPenalty => _moveSpeedPenalty;

    #endregion
}
