using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// 아이템 픽업 UI 패널 관리
/// 주변의 획득 가능한 아이템 목록을 보여주는 패널 제어
/// </summary>
public class ItemPickupUI : MonoBehaviour
{
    #region Serialized Fields

    [SerializeField] private GameObject _pickupPanel;
    [SerializeField] private Transform _entryContainer;
    [SerializeField] private ItemPickupEntry _entryPrefab;

    #endregion

    #region Private Fields

    private List<ItemPickupEntry> _activeEntries = new List<ItemPickupEntry>();
    private ItemPickupDetector _detector;
    private PlayerInventory _inventory;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        // Why: 게임 시작 전에는 플레이어가 없으므로 나중에 찾기
        if (_pickupPanel != null)
            _pickupPanel.SetActive(false);
    }

    private void Update()
    {
        // 카드 선택 UI가 열려있으면 피킹 패널 숨김
        if (UIManager.Instance != null && UIManager.Instance.IsCardSelectionOpen)
        {
            if (_pickupPanel != null && _pickupPanel.activeSelf)
                _pickupPanel.SetActive(false);
            return;
        }

        // 아직 초기화 안 됐으면 로컬 플레이어 찾기 시도
        if (_detector == null)
        {
            var inventory = PlayerUtils.GetLocalPlayerInventory();
            if (inventory != null)
            {
                _inventory = inventory;
                _detector = inventory.GetComponent<ItemPickupDetector>();

                if (_detector != null)
                {
                    _detector.OnItemsChanged += UpdatePickupPanel;
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (_detector != null)
        {
            _detector.OnItemsChanged -= UpdatePickupPanel;
        }
    }

    #endregion

    #region Panel Update

    private void UpdatePickupPanel(List<NetworkedItem> nearbyItems)
    {
        // 기존 엔트리 제거
        foreach (var entry in _activeEntries)
        {
            Destroy(entry.gameObject);
        }
        _activeEntries.Clear();

        if (nearbyItems.Count == 0)
        {
            if (_pickupPanel != null)
                _pickupPanel.SetActive(false);
            return;
        }

        // 새 엔트리 생성
        if (_pickupPanel != null)
            _pickupPanel.SetActive(true);

        foreach (var item in nearbyItems)
        {
            ItemData itemData = item.GetItemData();
            if (itemData == null)
            {
                Debug.LogWarning($"[ItemPickupUI] ItemData가 null입니다! ItemID: {item.ItemID}");
                continue;
            }

            NetworkedItem currentEquipped = GetCurrentEquippedItem(itemData.ItemType);

            ItemPickupEntry entry = Instantiate(_entryPrefab, _entryContainer);
            entry.Initialize(item, currentEquipped);
            _activeEntries.Add(entry);
        }
    }

    #endregion

    #region Helper Methods

    private NetworkedItem GetCurrentEquippedItem(ItemType itemType)
    {
        if (_inventory == null) return null;
        
        // Why: PlayerInventory가 아직 Spawned되지 않았으면 무시
        if (_inventory.NetworkObject == null || !_inventory.NetworkObject.IsSpawned) return null;

        int slotIndex = itemType switch
        {
            ItemType.Weapon => 0,
            ItemType.Armor => 1,
            _ => -1
        };

        if (slotIndex == -1) return null;

        int itemNetObjId = _inventory.GetItemSlotObjectId(slotIndex);
        if (itemNetObjId == 0) return null;

        // Why: FishNet의 Spawned 딕셔너리로 직접 조회 (O(1) - FindObjectsByType보다 훨씬 효율적)
        if (InstanceFinder.ClientManager != null && 
            InstanceFinder.ClientManager.Objects.Spawned.TryGetValue(itemNetObjId, out var nob) &&
            nob.TryGetComponent<NetworkedItem>(out var networkedItem))
        {
            return networkedItem;
        }

        return null;
    }

    // FindLocalPlayer removed: Use PlayerUtils instead

    #endregion
}
