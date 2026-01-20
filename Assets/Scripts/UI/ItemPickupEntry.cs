using FishNet.Object;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 픽업 패널의 단일 아이템 (비교 표시 포함)
/// </summary>
public class ItemPickupEntry : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    #region Serialized Fields

    [SerializeField] private Image _itemIcon;
    [SerializeField] private TextMeshProUGUI _itemName;
    [SerializeField] private TextMeshProUGUI _description;
    [SerializeField] private TextMeshProUGUI _statText;

    #endregion

    #region Private Fields

    private NetworkedItem _networkedItem;
    private Canvas _canvas;
    private CanvasGroup _canvasGroup;
    private RectTransform _rectTransform;
    private Vector3 _originalPosition;
    private Transform _originalParent;
    private bool _isDragging = false;

    private ScrollRect _parentScrollRect;
    private bool _draggingScrollRect = false;
    private float _pointerDownTime;
    private const float HOLD_THRESHOLD = 0.2f;

    #endregion

    #region Static Fields

    public static bool IsAnyItemDragging { get; set; }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
        {
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        _canvas = GetComponentInParent<Canvas>();
        _parentScrollRect = GetComponentInParent<ScrollRect>();
    }

    private void OnDisable()
    {
        if (_isDragging)
        {
            _isDragging = false;
            IsAnyItemDragging = false;
        }
        _draggingScrollRect = false;
    }

    #endregion

    #region Initialization

    public void Initialize(NetworkedItem item, NetworkedItem currentEquipped)
    {
        _networkedItem = item;
        ItemData itemData = item.GetItemData();

        if (itemData == null) return;

        if (_itemIcon != null) _itemIcon.sprite = itemData.Icon;
        if (_itemName != null) _itemName.text = itemData.ItemName;

        if (_description != null) _description.text = itemData.Description;

        // 등급에 따라 아이템 이름 색상 변경
        ApplyTierColor(item.Tier.Value);

        ItemData currentData = currentEquipped != null ? currentEquipped.GetItemData() : null;
        ItemTier currentTier = currentEquipped != null ? currentEquipped.Tier.Value : ItemTier.Normal;

        if (itemData is WeaponData weaponData)
        {
            ShowWeaponComparison(weaponData, currentData as WeaponData, item.Tier.Value, currentTier);
        }
        else if (itemData is ArmorItemData armorItem)
        {
            ShowArmorComparison(armorItem, currentData as ArmorItemData, item.Tier.Value, currentTier);
        }
        else
        {
            if (_statText != null) _statText.text = "";
        }

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0.8f;
        }
    }

    /// <summary>
    /// 등급에 따라 아이템 이름 색상을 적용합니다.
    /// </summary>
    private void ApplyTierColor(ItemTier tier)
    {
        if (_itemName == null) return;
        
        var config = ItemTierConfig.Instance;
        if (config != null)
        {
            var settings = config.GetSettings(tier);
            _itemName.color = settings.TierColor;
        }
        else
        {
            _itemName.color = Color.white;
        }
    }

    #endregion

    #region Stats Comparison

    private void ShowWeaponComparison(WeaponData newWeapon, WeaponData current, ItemTier newTier, ItemTier currentTier)
    {
        var config = ItemTierConfig.Instance;
        float newMultiplier = config != null ? config.GetSettings(newTier).DamageMultiplier : 1f;
        
        float newAtk = newWeapon.TotalDamage * newMultiplier;
        float newSpeed = 1f / newWeapon.AttackDelay;

        string atkText;
        string spdText;

        if (current != null)
        {
            float currentMultiplier = config != null ? config.GetSettings(currentTier).DamageMultiplier : 1f;
            float currentAtk = current.TotalDamage * currentMultiplier;
            float currentSpeed = 1f / current.AttackDelay;

            float atkDiff = newAtk - currentAtk;
            if (atkDiff > 0)
                atkText = $"데미지: <color=green>+{atkDiff:F0}</color>";
            else if (atkDiff < 0)
                atkText = $"데미지: <color=#FF6B6B>{atkDiff:F0}</color>";
            else
                atkText = $"데미지: {newAtk:F0}";

            float spdDiff = newSpeed - currentSpeed;
            if (spdDiff > 0)
                spdText = $"공격속도: <color=green>+{spdDiff:F1}</color>";
            else if (spdDiff < 0)
                spdText = $"공격속도: <color=#FF6B6B>{spdDiff:F1}</color>";
            else
                spdText = $"공격속도: {newSpeed:F1}";
        }
        else
        {
            atkText = $"데미지: {newAtk:F0}";
            spdText = $"공격속도: {newSpeed:F1}";
        }

        if (_statText != null)
            _statText.text = $"{atkText}\n{spdText}";
    }

    private void ShowArmorComparison(ArmorItemData newArmor, ArmorItemData current, ItemTier newTier, ItemTier currentTier)
    {
        var config = ItemTierConfig.Instance;
        float newMultiplier = config != null ? config.GetSettings(newTier).DefenseMultiplier : 1f;
        
        float newDef = newArmor.Defense * newMultiplier;

        if (current != null)
        {
            float currentMultiplier = config != null ? config.GetSettings(currentTier).DefenseMultiplier : 1f;
            float currentDef = current.Defense * currentMultiplier; // 현재 장비의 등급 보정치 적용

            float defDiff = newDef - currentDef;
            string defText;

            if (defDiff > 0)
                defText = $"방어력: <color=green>+{defDiff:F0}</color>";
            else if (defDiff < 0)
                defText = $"방어력: <color=#FF6B6B>{defDiff:F0}</color>";
            else
                defText = $"방어력: {newDef:F0}";

            if (_statText != null)
                _statText.text = defText;
        }
        else
        {
            if (_statText != null)
                _statText.text = $"방어력: {newDef:F0}";
        }
    }

    #endregion

    #region Drag Handlers

    public void OnPointerDown(PointerEventData eventData)
    {
        _pointerDownTime = Time.time;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        float holdDuration = Time.time - _pointerDownTime;

        if (holdDuration >= HOLD_THRESHOLD)
        {
            _isDragging = true;
            IsAnyItemDragging = true;
            _originalPosition = _rectTransform.position;
            _originalParent = transform.parent;

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0.6f;
                _canvasGroup.blocksRaycasts = false;
            }

            transform.SetParent(_canvas.transform);
        }
        else
        {
            _draggingScrollRect = true;
            if (_parentScrollRect != null)
            {
                ExecuteEvents.Execute(_parentScrollRect.gameObject, eventData, ExecuteEvents.beginDragHandler);
            }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_draggingScrollRect)
        {
            if (_parentScrollRect != null)
            {
                ExecuteEvents.Execute(_parentScrollRect.gameObject, eventData, ExecuteEvents.dragHandler);
            }
            return;
        }

        if (_rectTransform != null)
        {
            _rectTransform.position = eventData.position;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_draggingScrollRect)
        {
            _draggingScrollRect = false;
            if (_parentScrollRect != null)
            {
                ExecuteEvents.Execute(_parentScrollRect.gameObject, eventData, ExecuteEvents.endDragHandler);
            }
            return;
        }

        _isDragging = false;
        IsAnyItemDragging = false;

        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0.8f;
            _canvasGroup.blocksRaycasts = true;
        }

        transform.SetParent(_originalParent);
        _rectTransform.position = _originalPosition;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_isDragging && !_draggingScrollRect)
        {
            OnPickupClicked();
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_canvasGroup != null && !_isDragging)
        {
            _canvasGroup.alpha = 1.0f;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_canvasGroup != null && !_isDragging)
        {
            _canvasGroup.alpha = 0.8f;
        }
    }

    #endregion

    #region Pickup Logic

    private void OnPickupClicked()
    {
        if (_networkedItem == null) return;

        ItemData itemData = _networkedItem.GetItemData();
        if (itemData == null) return;

        int targetSlot = GetSlotForItemType(itemData.ItemType);
        if (targetSlot == -1) return;

        RequestSwapItem(targetSlot);
    }

    private void RequestSwapItem(int slotIndex)
    {
        PlayerInventory localInventory = PlayerUtils.GetLocalPlayerInventory();
        if (localInventory != null)
        {
            localInventory.RPC_RequestSwapItem(slotIndex, _networkedItem.NetworkObject.ObjectId);
        }
    }

    private int GetSlotForItemType(ItemType itemType)
    {
        return itemType switch
        {
            ItemType.Weapon => 0,
            ItemType.Armor => 1,
            ItemType.Usable => GetFirstEmptyUsableSlot(),
            _ => -1
        };
    }

    private int GetFirstEmptyUsableSlot()
    {
        PlayerInventory localInventory = PlayerUtils.GetLocalPlayerInventory();
        if (localInventory == null) return 4;
        
        if (localInventory.NetworkObject == null || !localInventory.NetworkObject.IsSpawned) return 4;

        for (int i = 2; i < 5; i++)
        {
            if (localInventory.ItemSlots[i] == default)
            {
                return i;
            }
        }

        return 4;
    }

    #endregion

    #region Public Accessors

    public NetworkedItem GetNetworkedItem() => _networkedItem;

    public string GetItemID() => _networkedItem?.ItemID.ToString();

    #endregion
}
