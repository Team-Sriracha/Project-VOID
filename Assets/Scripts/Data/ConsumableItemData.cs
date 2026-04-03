using UnityEngine;

/// <summary>
/// 소모 아이템 타입 열거형
/// </summary>
public enum ConsumableType
{
    /// <summary>
    /// 체력 회복
    /// </summary>
    Health,

    /// <summary>
    /// 탄약 충전
    /// </summary>
    Ammo
}

/// <summary>
/// 소모 아이템 데이터 (플레이어가 접촉 시 즉시 효과 적용 후 소멸)
/// 
/// [주의] 소모 아이템은 인벤토리/UI에 표시되지 않으므로 다음 필드는 설정 불필요:
/// - ItemID: 비워둬도 됨
/// - Icon: 비워둬도 됨
/// - Description: 비워둬도 됨
/// 
/// [필수 설정]
/// - ItemName: 디버그 로그용
/// - ModelPrefab: 월드에 스폰될 프리팹
/// - ConsumableType: Health 또는 Ammo
/// - Amount: 회복/충전량
/// </summary>
[CreateAssetMenu(fileName = "NewConsumableItem", menuName = "Project VOID/Items/Consumable Item")]
public class ConsumableItemData : ItemData
{
    #region Serialized Fields

    [Header("소모 아이템 설정")]
    [Tooltip("소모 아이템 타입")]
    [SerializeField] private ConsumableType _consumableType;

    [Tooltip("효과 수치 (체력: HP 회복량, 탄약: 충전량)")]
    [SerializeField] private float _amount = 20f;

    #endregion

    #region Properties

    /// <summary>
    /// 소모 아이템 타입을 반환합니다.
    /// </summary>
    public ConsumableType ConsumableType => _consumableType;

    /// <summary>
    /// 효과 수치를 반환합니다.
    /// </summary>
    public float Amount => _amount;

    #endregion

#if UNITY_EDITOR
    /// <summary>
    /// 에디터에서 ItemType을 자동으로 Consumable로 설정
    /// </summary>
    private void OnValidate()
    {
        // Reflection으로 private 필드 접근하여 ItemType 강제 설정
        var field = typeof(ItemData).GetField("_itemType", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field != null && (ItemType)field.GetValue(this) != ItemType.Consumable)
        {
            field.SetValue(this, ItemType.Consumable);
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }
#endif
}
