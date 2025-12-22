using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 모든 아이템 데이터를 관리하는 싱글톤
/// Inspector에서 직접 ItemData 배열을 할당합니다.
/// </summary>
public class ItemDatabase : MonoBehaviour
{
    #region Singleton

    private static ItemDatabase _instance;

    /// <summary>
    /// ItemDatabase 싱글톤 인스턴스
    /// </summary>
    /// <summary>
    /// ItemDatabase 싱글톤 인스턴스 (Lazy Initialization 적용)
    /// </summary>
    public static ItemDatabase Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<ItemDatabase>();
                if (_instance == null)
                {
                    // Resources 폴더에서 프리팹 로드 시도
                    var prefab = Resources.Load<ItemDatabase>("ItemDatabase");
                    if (prefab != null)
                    {
                        var go = Instantiate(prefab);
                        go.name = "ItemDatabase";
                        _instance = go.GetComponent<ItemDatabase>();
                        Debug.Log("[ItemDatabase] Resources에서 프리팹 로드하여 생성됨");
                        if (_instance == null)
                        {
                            // 컴포넌트가 프리팹에 없는 경우
                            Debug.LogError("[ItemDatabase] 프리팹에 ItemDatabase 컴포넌트가 없습니다!");
                        }
                    }
                    else
                    {
                        // 프리팹도 없는 경우 빈 오브젝트라도 생성 (데이터는 비어있겠지만 null 참조는 방지)
                        Debug.LogWarning("[ItemDatabase] 씬에 ItemDatabase가 없고 Resources/ItemDatabase 프리팹도 없습니다. 빈 ItemDatabase를 생성합니다. (데이터 누락 주의)");
                        var go = new GameObject("ItemDatabase_AutoCreated");
                        _instance = go.AddComponent<ItemDatabase>();
                        _instance._isAutoCreated = true;
                    }
                }
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
    [SerializeField] private UsableItemData[] _consumables;

    #endregion

    #region Private Fields

    private Dictionary<string, ItemData> _itemDict = new Dictionary<string, ItemData>();
    private List<ItemData> _cachedLootableItems;
    private bool _isAutoCreated = false; // 자동 생성된 빈 껍데기인지 여부

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            LoadAllItems();
        }
        else
        {
            // Why: 기존 인스턴스가 자동 생성된 껍데기이고, 나는 씬에 배치된 진짜라면?
            if (_instance._isAutoCreated && !this._isAutoCreated)
            {
                Debug.LogWarning("[ItemDatabase] 자동 생성된 임시 인스턴스를 파괴하고, 실제 데이터가 있는 인스턴스로 교체합니다.");
                Destroy(_instance.gameObject);
                _instance = this;
                DontDestroyOnLoad(gameObject);
                LoadAllItems();
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Inspector에서 할당된 ItemData 배열을 로딩합니다.
    /// </summary>
    private void LoadAllItems()
    {
        // Why: 모든 아이템 로드 (기본 무기도 포함, LootBox 제외는 GetLootableItems에서 처리)
        LoadItemArray(_defaultWeapons, "기본 무기");
        LoadItemArray(_weapons, "무기");
        LoadItemArray(_armors, "방어구");
        LoadItemArray(_consumables, "사용 아이템");

        Debug.Log($"[ItemDatabase] 총 {_itemDict.Count}개 아이템 로드 완료");

        // Why: 루팅 가능한 아이템 목록 캐싱
        BuildLootableItemsCache();
    }

    /// <summary>
    /// 루팅 가능한 아이템 목록을 미리 캐싱합니다.
    /// </summary>
    private void BuildLootableItemsCache()
    {
        _cachedLootableItems = new List<ItemData>();

        // Why: 기본 무기(_defaultWeapons)는 제외하고, 나머지만 추가
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
    /// ItemData 배열을 Dictionary에 추가합니다.
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
    /// ItemID로 ItemData를 가져옵니다.
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
    /// 모든 아이템 목록을 반환합니다.
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
    /// LootBox에서 드랍 가능한 아이템만 반환합니다 (기본 무기 제외).
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

        // Why: 캐시된 리스트 반환 (매번 새로 생성하지 않음)
        return Instance._cachedLootableItems;
    }

    #endregion
}
