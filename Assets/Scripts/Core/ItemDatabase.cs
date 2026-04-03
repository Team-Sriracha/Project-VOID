using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 아이템 데이터를 관리하는 싱글톤
/// GameStateManager에서 프리팹으로 생성됨
/// </summary>
public class ItemDatabase : MonoBehaviour
{
    #region Singleton

    private static ItemDatabase _instance;


    /// <summary>
    /// ItemDatabase 싱글톤 인스턴스
    /// GameStateManager에서 프리팹으로 생성됨
    /// </summary>
    public static ItemDatabase Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<ItemDatabase>();
            }
            return _instance;
        }
    }

    #endregion

    
    #region Serialized Fields

    [Header("기본 무기")]
    [Tooltip("플레이어 시작 무기 (ItemID: Hand) - LootBox에서 제외됨")]
    [SerializeField] private WeaponData[] _defaultWeapons;

    [Header("무기 아이템")]
    [Tooltip("LootBox에서 드랍되는 무기")]
    [SerializeField] private WeaponData[] _weapons;

    [Header("방어구 아이템")]
    [Tooltip("LootBox에서 드랍되는 방어구")]
    [SerializeField] private ArmorItemData[] _armors;

    [Header("사용 아이템")]
    [Tooltip("LootBox에서 드랍되는 사용 아이템")]
    [SerializeField] private UsableItemData[] _usables;

    [Header("소모 아이템")]
    [Tooltip("LootBox에서 드랍되는 소모 아이템 (체력/탄약/실드)")]
    [SerializeField] private ConsumableItemData[] _consumables;

    #endregion

    #region Private Fields

    private Dictionary<string, ItemData> _itemDict = new Dictionary<string, ItemData>();
    private List<ItemData> _cachedLootableItems;

    #endregion
    
    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            LoadAllItems();
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }



    #region Initialization

    /// <summary>
    /// Inspector에서 할당된 ItemData 배열 로딩
    /// </summary>
    private void LoadAllItems()
    {
        // 기본 무기를 포함한 모든 아이템 데이터 로드 (LootBox 제외 로직은 별도 처리)

        LoadItemArray(_defaultWeapons, "기본 무기");
        LoadItemArray(_weapons, "무기");
        LoadItemArray(_armors, "방어구");
        LoadItemArray(_usables, "사용 아이템");
        LoadItemArray(_consumables, "소모 아이템");

        Debug.Log($"[ItemDatabase] 총 {_itemDict.Count}개 아이템 로드 완료");

        // 루팅 가능한 아이템 캐시 생성

        BuildLootableItemsCache();
    }

    /// <summary>
    /// 루팅 가능한 아이템 목록 미리 캐싱
    /// </summary>
    private void BuildLootableItemsCache()
    {
        _cachedLootableItems = new List<ItemData>();

        // 기본 무기를 제외한 아이템을 캐시에 추가

        if (_weapons != null)
        {
            foreach (var weapon in _weapons)
            {
                if (weapon != null) _cachedLootableItems.Add(weapon);
            }
        }

        if (_armors != null)
        {
            foreach (var armor in _armors)
            {
                if (armor != null) _cachedLootableItems.Add(armor);
            }
        }

        if (_usables != null)
        {
            foreach (var usable in _usables)
            {
                if (usable != null) _cachedLootableItems.Add(usable);
            }
        }

        if (_consumables != null)
        {
            foreach (var consumable in _consumables)
            {
                if (consumable != null) _cachedLootableItems.Add(consumable);
            }
        }

        Debug.Log($"[ItemDatabase] 루팅 가능 아이템 {_cachedLootableItems.Count}개 캐싱 완료");
    }

    /// <summary>
    /// ItemData 배열을 Dictionary에 추가
    /// </summary>
    private void LoadItemArray(ItemData[] items, string categoryName)
    {
        if (items == null || items.Length == 0)
        {
            Debug.LogWarning($"[ItemDatabase] {categoryName} 배열이 비어있습니다!");
            return;
        }

        foreach (var item in items)
        {
            if (item == null)
            {
                Debug.LogWarning($"[ItemDatabase] {categoryName}에 null 아이템이 포함되어 있습니다!");
                continue;
            }

            if (string.IsNullOrEmpty(item.ItemID))
            {
                Debug.LogWarning($"[ItemDatabase] ItemID가 비어있는 아이템 발견: {item.name} ({categoryName})");
                continue;
            }

            if (_itemDict.ContainsKey(item.ItemID))
            {
                Debug.LogWarning($"[ItemDatabase] 중복된 ItemID 발견: {item.ItemID} ({categoryName})");
                continue;
            }

            _itemDict[item.ItemID] = item;
        }

        Debug.Log($"[ItemDatabase] {categoryName}: {items.Length}개 로드");
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// ItemID로 ItemData 조회
    /// </summary>
    /// <param name="itemID">아이템 ID</param>
    /// <returns>ItemData 또는 null</returns>
    public static ItemData GetItem(string itemID)
    {
        if (Instance == null)
        {
            Debug.LogError("[ItemDatabase] Instance가 없습니다!");
            return null;
        }

        if (string.IsNullOrEmpty(itemID))
        {
            return null;
        }

        Instance._itemDict.TryGetValue(itemID, out ItemData item);
        return item;
    }

    /// <summary>
    /// 모든 아이템 목록 반환
    /// </summary>
    public static IReadOnlyDictionary<string, ItemData> GetAllItems()
    {
        if (Instance == null)
        {
            Debug.LogError("[ItemDatabase] Instance가 없습니다!");
            return null;
        }

        return Instance._itemDict;
    }

    /// <summary>
    /// LootBox에서 드랍 가능한 아이템만 반환 (기본 무기 제외)
    /// </summary>
    public static IReadOnlyList<ItemData> GetLootableItems()
    {
        if (Instance == null)
        {
            Debug.LogError("[ItemDatabase] Instance가 없습니다!");
            return new List<ItemData>();
        }

        if (Instance._cachedLootableItems == null)
        {
            Debug.LogWarning("[ItemDatabase] 캐시가 초기화되지 않았습니다!");
            return new List<ItemData>();
        }

        // 성능을 위해 매번 새로 생성하지 않고 캐시된 리스트 반환

        return Instance._cachedLootableItems;
    }

    #endregion
}
