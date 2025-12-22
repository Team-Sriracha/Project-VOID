using Fusion;
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
    private const float HOLD_THRESHOLD = 0.2f; // 0.2초 이상 누르면 아이템 드래그 모드

    #endregion

    #region Static Fields

    /// <summary>
    /// 현재 UI에서 드래그가 진행 중인지 여부
    /// </summary>
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

    public void Initialize(NetworkedItem item, ItemData currentEquipped)
    {
        _networkedItem = item;
        ItemData itemData = item.GetItemData();

        if (itemData == null) return;

        if (_itemIcon != null) _itemIcon.sprite = itemData.Icon;
        if (_itemName != null) _itemName.text = itemData.ItemName;
        if (_description != null) _description.text = itemData.Description;

        if (itemData is WeaponData weaponData)
        {
            ShowWeaponComparison(weaponData, currentEquipped as WeaponData);
        }
        else if (itemData is ArmorItemData armorItem)
        {
            ShowArmorComparison(armorItem, currentEquipped as ArmorItemData);
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

    #endregion

    #region Stats Comparison

    /// <summary>
    /// 무기 스탯 비교를 표시합니다.
    /// </summary>
    private void ShowWeaponComparison(WeaponData newWeapon, WeaponData current)
    {
        float newAtk = newWeapon.Damage;
        float newSpeed = 1f / newWeapon.AttackDelay;

        string atkText;
        string spdText;

        if (current != null)
        {
            float currentAtk = current.Damage;
            float currentSpeed = 1f / current.AttackDelay;

            float atkDiff = newAtk - currentAtk;
            if (atkDiff > 0)
                atkText = $"ATK: <color=green>+{atkDiff:F0}</color>";
            else if (atkDiff < 0)
                atkText = $"ATK: <color=red>{atkDiff:F0}</color>";
            else
                atkText = $"ATK: {newAtk:F0}";

            float spdDiff = newSpeed - currentSpeed;
            if (spdDiff > 0)
                spdText = $"ASPD: <color=green>+{spdDiff:F1}</color>";
            else if (spdDiff < 0)
                spdText = $"ASPD: <color=red>{spdDiff:F1}</color>";
            else
                spdText = $"ASPD: {newSpeed:F1}";
        }
        else
        {
            atkText = $"ATK: {newAtk:F0}";
            spdText = $"ASPD: {newSpeed:F1}";
        }

        if (_statText != null)
            _statText.text = $"{atkText}  {spdText}";
    }

    /// <summary>
    /// 방어구 스탯 비교를 표시합니다.
    /// </summary>
    private void ShowArmorComparison(ArmorItemData newArmor, ArmorItemData current)
    {
        float newDef = newArmor.Defense;

        if (current != null)
        {
            float defDiff = newDef - current.Defense;
            string defText;

            if (defDiff > 0)
                defText = $"DEF: <color=green>+{defDiff:F0}</color>";
            else if (defDiff < 0)
                defText = $"DEF: <color=red>{defDiff:F0}</color>";
            else
                defText = $"DEF: {newDef:F0}";

            if (_statText != null)
                _statText.text = defText;
        }
        else
        {
            if (_statText != null)
                _statText.text = $"DEF: {newDef:F0}";
        }
    }

    #endregion

    #region Drag Handlers

    public void OnPointerDown(PointerEventData eventData)
    {
        // Why: 드래그 시작 시간 기록 (Press and Hold 감지용)
        _pointerDownTime = Time.time;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        float holdDuration = Time.time - _pointerDownTime;

        // Why: 0.2초 이상 누르고 있었으면 아이템 드래그, 빠르게 드래그하면 스크롤
        if (holdDuration >= HOLD_THRESHOLD)
        {
            // 길게 누름 → 아이템 드래그
            _isDragging = true;
            IsAnyItemDragging = true;
            _originalPosition = _rectTransform.position;
            _originalParent = transform.parent;

            // Why: 드래그 중에는 반투명하게
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0.6f;
                _canvasGroup.blocksRaycasts = false;
            }

            // Why: Canvas 최상위로 이동 (다른 UI 위에 표시)
            transform.SetParent(_canvas.transform);
        }
        else
        {
            // 빠른 드래그 → ScrollRect로 전달
            _draggingScrollRect = true;
            if (_parentScrollRect != null)
            {
                ExecuteEvents.Execute(_parentScrollRect.gameObject, eventData, ExecuteEvents.beginDragHandler);
            }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        // Why: ScrollRect 드래그 중이면 ScrollRect로 전달
        if (_draggingScrollRect)
        {
            if (_parentScrollRect != null)
            {
                ExecuteEvents.Execute(_parentScrollRect.gameObject, eventData, ExecuteEvents.dragHandler);
            }
            return;
        }

        // Why: 아이템 드래그 - 마우스 따라다니기
        if (_rectTransform != null)
        {
            _rectTransform.position = eventData.position;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // Why: ScrollRect 드래그 종료
        if (_draggingScrollRect)
        {
            _draggingScrollRect = false;
            if (_parentScrollRect != null)
            {
                ExecuteEvents.Execute(_parentScrollRect.gameObject, eventData, ExecuteEvents.endDragHandler);
            }
            return;
        }

        // Why: 아이템 드래그 종료
        _isDragging = false;
        IsAnyItemDragging = false;

        // Why: 드래그 종료 시 원래 alpha로 복원
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 0.8f;
            _canvasGroup.blocksRaycasts = true;
        }

        // Why: 원래 위치로 복귀 (Drop이 성공하면 파괴됨)
        transform.SetParent(_originalParent);
        _rectTransform.position = _originalPosition;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Why: 드래그가 아닌 클릭일 때만 픽업
        if (!_isDragging && !_draggingScrollRect)
        {
            OnPickupClicked();
        }
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

    #region Pickup Logic

    /// <summary>
    /// 클릭 시 아이템 타입에 맞는 슬롯으로 자동 픽업
    /// </summary>
    private void OnPickupClicked()
    {
        if (_networkedItem == null) return;

        ItemData itemData = _networkedItem.GetItemData();
        if (itemData == null) return;

        // Why: 아이템 타입에 따라 클라이언트가 슬롯 결정
        int targetSlot = GetSlotForItemType(itemData.ItemType);
        if (targetSlot == -1) return;

        RequestSwapItem(targetSlot);
    }

    /// <summary>
    /// 서버에 아이템 교체 요청
    /// </summary>
    private void RequestSwapItem(int slotIndex)
    {
        PlayerInventory localInventory = PlayerUtils.GetLocalPlayerInventory();
        if (localInventory != null)
        {
            localInventory.RPC_RequestSwapItem(slotIndex, _networkedItem.Object.Id);
        }
    }

    /// <summary>
    /// 아이템 타입에 맞는 슬롯 인덱스 반환
    /// </summary>
    private int GetSlotForItemType(ItemType itemType)
    {
        return itemType switch
        {
            ItemType.Weapon => 0,   // 무기 슬롯
            ItemType.Armor => 1,    // 방어구 슬롯
            ItemType.Usable => GetFirstEmptyUsableSlot(), // 빈 사용 아이템 슬롯
            _ => -1
        };
    }

    /// <summary>
    /// 빈 사용 아이템 슬롯 찾기 (없으면 슬롯 4)
    /// </summary>
    private int GetFirstEmptyUsableSlot()
    {
        PlayerInventory localInventory = PlayerUtils.GetLocalPlayerInventory();
        if (localInventory == null) return 4;
        
        // Why: PlayerInventory가 아직 Spawned되지 않았으면 기본값 반환
        if (localInventory.Object == null || !localInventory.Object.IsValid) return 4;

        // 슬롯 2, 3, 4 중 빈 슬롯 찾기
        for (int i = 2; i < 5; i++)
        {
            if (localInventory.ItemSlots[i] == default)
            {
                return i;
            }
        }

        // 모두 차있으면 슬롯 2
        return 4;
    }

    #endregion

    #region Public Accessors

    /// <summary>
    /// NetworkedItem을 반환합니다 (드래그 앤 드롭용).
    /// </summary>
    public NetworkedItem GetNetworkedItem() => _networkedItem;

    /// <summary>
    /// 아이템 ID를 반환합니다.
    /// </summary>
    public string GetItemID() => _networkedItem?.ItemID.ToString();

    #endregion
}
