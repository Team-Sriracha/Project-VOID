using UnityEngine;

/// <summary>
/// 아이템 타입 열거형
/// </summary>
public enum ItemType
{
    None,
    Weapon,
    Armor,
    Usable
}

/// <summary>
/// 아이템 기본 데이터 (추상 클래스)
/// </summary>
public abstract class ItemData : ScriptableObject
{
    #region Serialized Fields

    [Header("기본 정보")]
    [Tooltip("고유 식별자 (예: weapon_ak47)")]
    [SerializeField] private string _itemID;

    [Tooltip("아이템 표시 이름")]
    [SerializeField] private string _itemName;

    [Tooltip("아이템 설명")]
    [SerializeField] private string _description;

    [Tooltip("UI 아이콘")]
    [SerializeField] private Sprite _icon;

    [Tooltip("모델 프리팹 (월드 & 플레이어 손 모두 사용)")]
    [SerializeField] private GameObject _modelPrefab;

    [Tooltip("아이템 타입")]
    [SerializeField] private ItemType _itemType;

    #endregion

    #region Properties

    /// <summary>
    /// 고유 식별자를 반환합니다.
    /// </summary>
    public string ItemID => _itemID;

    /// <summary>
    /// 아이템 이름을 반환합니다.
    /// </summary>
    public string ItemName => _itemName;

    /// <summary>
    /// 아이템 설명을 반환합니다.
    /// </summary>
    public string Description => _description;

    /// <summary>
    /// UI 아이콘을 반환합니다.
    /// </summary>
    public Sprite Icon => _icon;

    /// <summary>
    /// 모델 프리팹을 반환합니다 (월드 & 플레이어 손 모두 사용).
    /// </summary>
    public GameObject ModelPrefab => _modelPrefab;

    /// <summary>
    /// 아이템 타입을 반환합니다.
    /// </summary>
    public ItemType ItemType => _itemType;

    #endregion
}
