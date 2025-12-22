using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 아이템 픽업 UI 패널 관리
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
        // Why: 아직 초기화 안 됐으면 로컬 플레이어 찾기 시도
        if (_detector == null)
        {
            var localPlayer = FindLocalPlayer();
            if (localPlayer != null)
            {
                _detector = localPlayer.GetComponent<ItemPickupDetector>();
                _inventory = localPlayer.GetComponent<PlayerInventory>();

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

            ItemData currentEquipped = GetCurrentEquippedItem(itemData.ItemType);

            ItemPickupEntry entry = Instantiate(_entryPrefab, _entryContainer);
            entry.Initialize(item, currentEquipped);
            _activeEntries.Add(entry);
        }
    }

    #endregion

    #region Helper Methods

    private ItemData GetCurrentEquippedItem(ItemType itemType)
    {
        if (_inventory == null) return null;
        
        // Why: PlayerInventory가 아직 Spawned되지 않았으면 무시
        if (_inventory.Object == null || !_inventory.Object.IsValid) return null;

        int slotIndex = itemType switch
        {
            ItemType.Weapon => 0,
            ItemType.Armor => 1,
            _ => -1
        };

        if (slotIndex == -1) return null;

        // Why: NetworkId로 NetworkedItem 찾아서 ItemData 가져오기
        Fusion.NetworkId itemNetId = _inventory.ItemSlots[slotIndex];
        if (itemNetId == default) return null;

        // Why: Runner에서 NetworkId로 NetworkObject 찾기
        if (_inventory.Runner != null && _inventory.Runner.TryFindObject(itemNetId, out Fusion.NetworkObject netObj))
        {
            NetworkedItem networkedItem = netObj.GetComponent<NetworkedItem>();
            if (networkedItem != null)
            {
                return networkedItem.GetItemData();
            }
        }

        return null;
    }

    private GameObject FindLocalPlayer()
    {
        var allPlayers = FindObjectsByType<Fusion.NetworkObject>(FindObjectsSortMode.None);
        foreach (var player in allPlayers)
        {
            if (player.HasInputAuthority)
                return player.gameObject;
        }
        return null;
    }

    #endregion
}
