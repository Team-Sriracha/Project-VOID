using UnityEngine;

/// <summary>
/// 방어구 아이템 데이터 (미래 확장용)
/// </summary>
[CreateAssetMenu(fileName = "NewArmorItem", menuName = "Project VOID/Items/Armor Item")]
public class ArmorItemData : ItemData
{
    #region Serialized Fields

    [Header("방어구 설정")]
    [Tooltip("방어력 (역치 공식: 감소율 = 방어력/(방어력+100))")]
    [SerializeField] private float _defense = 0f;

    #endregion

    #region Properties

    /// <summary>
    /// 방어력을 반환합니다.
    /// </summary>
    public float Defense => _defense;

    #endregion
}
