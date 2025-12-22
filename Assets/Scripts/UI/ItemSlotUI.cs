using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 인벤토리 슬롯 UI (드래그 앤 드롭 지원)
/// Why: 슬롯 Image 자체가 빈 슬롯, ItemIcon은 동적 생성
/// </summary>
public class ItemSlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    #region Serialized Fields

    [SerializeField] private Image _slotBackground;

    #endregion

    #region Private Fields

    private int _slotIndex;
    private ItemData _currentItem;
    private Canvas _canvas;
    private GameObject _itemIconObject;
    private Image _itemIconImage;
    private RectTransform _draggingIcon;
    private RectTransform _inventoryPanelRect;
    private CanvasGroup _canvasGroup;
    private bool _isDragging = false;

    #endregion

    #region Initialization

    public void Initialize(int slotIndex, Canvas canvas)
    {
        _slotIndex = slotIndex;
        _canvas = canvas;

        // Why: CanvasGroup 초기화 (마우스 오버 효과용)
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        _canvasGroup.alpha = 0.8f;

        ClearSlot();
    }

    #endregion

    #region Item Management

    public void SetItem(ItemData itemData)
    {
        _currentItem = itemData;

        if (itemData != null)
        {
            // Why: ItemIcon이 없으면 동적 생성
            if (_itemIconObject == null)
            {
                CreateItemIcon();
            }

            _itemIconImage.sprite = itemData.Icon;
            _itemIconObject.SetActive(true);
        }
        else
        {
            ClearSlot();
        }
    }

    public void ClearSlot()
    {
        _currentItem = null;

        if (_itemIconObject != null)
        {
            _itemIconObject.SetActive(false);
        }
    }

    /// <summary>
    /// ItemIcon GameObject를 동적으로 생성합니다.
    /// </summary>
    private void CreateItemIcon()
    {
        _itemIconObject = new GameObject("ItemIcon");
        _itemIconObject.transform.SetParent(transform, false);

        _itemIconImage = _itemIconObject.AddComponent<Image>();
        _itemIconImage.raycastTarget = false; // Why: 드래그 이벤트는 슬롯이 받음

        RectTransform rt = _itemIconObject.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
    }

    #endregion

    #region Drag & Drop

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_currentItem == null) return;

        // Why: Canvas가 초기화되지 않았으면 자동으로 찾기
        if (_canvas == null)
        {
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                Debug.LogError("[ItemSlotUI] Canvas를 찾을 수 없습니다! Initialize()를 호출하세요.");
                return;
            }
        }

        _isDragging = true;

        // Why: 드래그 시작 시 플레이어 회전 차단
        ItemPickupEntry.IsAnyItemDragging = true;

        // 드래그 아이콘 생성
        _draggingIcon = CreateDragIcon();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_draggingIcon != null)
        {
            _draggingIcon.position = eventData.position;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _isDragging = false;

        // Why: 드래그 종료 시 플레이어 회전 허용
        ItemPickupEntry.IsAnyItemDragging = false;

        if (_draggingIcon != null)
        {
            Destroy(_draggingIcon.gameObject);
            _draggingIcon = null;
        }

        if (_currentItem == null) return;

        // Why: 다른 슬롯에 드롭했으면 OnDrop에서 처리됨
        if (eventData.pointerEnter != null && eventData.pointerEnter.GetComponent<ItemSlotUI>() != null)
        {
            return; // 다른 슬롯 위에 놓음
        }

        // Why: InventoryPanel 영역을 찾아서 그 밖에 놓았는지 체크
        RectTransform inventoryRect = GetInventoryPanelRect();
        if (inventoryRect != null)
        {
            // Why: InventoryPanel 밖에 놓았으면 바닥에 드랍
            if (!RectTransformUtility.RectangleContainsScreenPoint(inventoryRect, eventData.position, _canvas.worldCamera))
            {
                RequestDropItem();
            }
        }
    }

    /// <summary>
    /// 다른 슬롯 또는 픽업 패널에서 드롭되었을 때 호출됩니다.
    /// </summary>
    public void OnDrop(PointerEventData eventData)
    {
        // Case 1: 픽업 패널에서 드래그한 경우
        ItemPickupEntry pickupEntry = eventData.pointerDrag?.GetComponent<ItemPickupEntry>();
        if (pickupEntry != null)
        {
            HandlePickupEntryDrop(pickupEntry);
            return;
        }

        // Case 2: 인벤토리 슬롯에서 드래그한 경우
        ItemSlotUI draggedSlot = eventData.pointerDrag?.GetComponent<ItemSlotUI>();
        if (draggedSlot == null || draggedSlot == this) return;

        // Why: 사용 아이템 슬롯끼리만 교환 가능 (슬롯 2, 3, 4)
        if (_slotIndex < 2 || draggedSlot._slotIndex < 2)
        {
            Debug.Log("[ItemSlotUI] 사용 아이템 슬롯끼리만 교환할 수 있습니다!");
            return;
        }

        // 로컬에서 UI만 교환
        ExchangeItems(draggedSlot);
    }

    /// <summary>
    /// 픽업 패널에서 드롭된 아이템을 처리합니다.
    /// </summary>
    private void HandlePickupEntryDrop(ItemPickupEntry pickupEntry)
    {
        NetworkedItem networkedItem = pickupEntry.GetNetworkedItem();
        if (networkedItem == null) return;

        ItemData itemData = networkedItem.GetItemData();
        if (itemData == null) return;

        // Why: 아이템 타입과 슬롯이 맞는지 확인
        bool isValidSlot = itemData.ItemType switch
        {
            ItemType.Weapon => _slotIndex == 0,    // 무기는 슬롯 0
            ItemType.Armor => _slotIndex == 1,     // 방어구는 슬롯 1
            ItemType.Usable => _slotIndex >= 2,    // 사용 아이템은 슬롯 2~4
            _ => false
        };

        if (!isValidSlot)
        {
            Debug.Log($"[ItemSlotUI] {itemData.ItemName}을(를) 슬롯 {_slotIndex}에 놓을 수 없습니다!");
            return;
        }

        // Why: 서버에 아이템 교체 요청
        PlayerInventory localInventory = PlayerUtils.GetLocalPlayerInventory();
        if (localInventory != null)
        {
            localInventory.RPC_RequestSwapItem(_slotIndex, networkedItem.Object.Id);
            Debug.Log($"[ItemSlotUI] {itemData.ItemName}을(를) 슬롯 {_slotIndex}에 교체 요청!");
        }
    }

    /// <summary>
    /// 다른 슬롯과 아이템을 교환합니다 (로컬 UI만).
    /// </summary>
    private void ExchangeItems(ItemSlotUI otherSlot)
    {
        ItemData tempItem = _currentItem;
        SetItem(otherSlot._currentItem);
        otherSlot.SetItem(tempItem);

        Debug.Log($"[ItemSlotUI] 슬롯 {_slotIndex}와 슬롯 {otherSlot._slotIndex}의 아이템을 교환했습니다!");
    }

    private RectTransform CreateDragIcon()
    {
        GameObject iconObj = new GameObject("DragIcon");
        iconObj.transform.SetParent(_canvas.transform, false);

        Image image = iconObj.AddComponent<Image>();
        image.sprite = _currentItem.Icon;
        image.raycastTarget = false;

        RectTransform rt = iconObj.GetComponent<RectTransform>();
        // Why: 원래 슬롯의 크기를 그대로 사용
        RectTransform slotRect = GetComponent<RectTransform>();
        rt.sizeDelta = slotRect.sizeDelta;

        return rt;
    }

    private void RequestDropItem()
    {
        PlayerInventory localInventory = PlayerUtils.GetLocalPlayerInventory();
        if (localInventory != null)
        {
            localInventory.RPC_RequestDropItem(_slotIndex);
        }
    }

    /// <summary>
    /// InventoryPanel의 RectTransform을 찾아 반환합니다.
    /// </summary>
    private RectTransform GetInventoryPanelRect()
    {
        if (_inventoryPanelRect != null) return _inventoryPanelRect;

        // Why: 슬롯의 부모 컨테이너를 찾기
        Transform current = transform;
        while (current != null)
        {
            if (current.name.Contains("Inventory") || current.name.Contains("Slot"))
            {
                _inventoryPanelRect = current.GetComponent<RectTransform>();
                if (_inventoryPanelRect != null) return _inventoryPanelRect;
            }
            current = current.parent;
        }

        return null;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Why: 마우스 오버 시 더 진하게 표시 (alpha 1.0)
        if (_canvasGroup != null && !_isDragging)
        {
            _canvasGroup.alpha = 1.0f;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Why: 마우스가 벗어나면 원래 alpha로 복원
        if (_canvasGroup != null && !_isDragging)
        {
            _canvasGroup.alpha = 0.8f;
        }
    }

    #endregion
}
