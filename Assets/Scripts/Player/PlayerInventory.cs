using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// 플레이어 인벤토리 (5칸: 무기, 방어구, 사용아이템 3개)
/// </summary>
public class PlayerInventory : NetworkBehaviour
{
    #region Constants

    private const int SLOT_WEAPON = 0;
    private const int SLOT_ARMOR = 1;
    private const int SLOT_USABLE_START = 2;
    private const int SLOT_COUNT = 5;

    #endregion

    #region SyncVars

    /// <summary>
    /// 5칸 인벤토리 (NetworkedItem의 ObjectId 저장)
    /// 각 NetworkedItem은 독립적인 개체(탄약, 내구도 등 고유 상태 보유)이므로 ObjectId로 관리
    /// </summary>
    public readonly SyncList<int> ItemSlots = new SyncList<int>();

    /// <summary>
    /// 현재 장착 무기 슬롯
    /// </summary>
    public readonly SyncVar<int> CurrentWeaponSlot = new();

    #endregion

    #region Private Fields

    private NetworkedWeapon _weaponSystem;
    private NetworkedArmor _armorSystem;
    [SerializeField] private PlayerStats _playerStats;

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        
        _weaponSystem = GetComponent<NetworkedWeapon>();
        _armorSystem = GetComponent<NetworkedArmor>();

        // 서버 초기화 시 슬롯을 비우고 기본 무기 장착
        if (IsServerInitialized)
        {
            // 슬롯 초기화 (5칸)
            ItemSlots.Clear();
            for (int i = 0; i < SLOT_COUNT; i++)
            {
                ItemSlots.Add(0); // 0 = 빈 슬롯
            }
            
            InitializeDefaultWeapon();
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 기본 무기를 장착합니다.
    /// </summary>
    private void InitializeDefaultWeapon()
    {
        CurrentWeaponSlot.Value = SLOT_WEAPON;

        // 기본 무기는 NetworkedItem이 아니므로 ItemSlots에는 추가하지 않고
        // NetworkedWeapon 컴포넌트에 직접 설정
        if (_playerStats != null && _playerStats.DefaultWeapon != null)
        {
            if (_weaponSystem != null)
            {
                _weaponSystem.SetWeapon(_playerStats.DefaultWeapon);
                Debug.Log($"[PlayerInventory] 기본 무기 {_playerStats.DefaultWeapon.ItemName} 장착 완료!");
            }
        }
        else
        {
            Debug.LogWarning("[PlayerInventory] PlayerStats 또는 DefaultWeapon이 없습니다!");
        }
    }

    #endregion

    #region Item Management

    /// <summary>
    /// 무기가 장착되어 있는지 확인합니다.
    /// </summary>
    public bool HasWeaponEquipped()
    {
        return ItemSlots.Count > SLOT_WEAPON && ItemSlots[SLOT_WEAPON] != 0;
    }

    /// <summary>
    /// 아이템을 인벤토리에 추가합니다 (서버만).
    /// </summary>
    /// <param name="networkedItem">NetworkedItem 참조</param>
    /// <returns>성공 여부</returns>
    public bool TryAddItem(NetworkedItem networkedItem)
    {
        if (!IsServerInitialized || networkedItem == null)
            return false;

        ItemData itemData = networkedItem.GetItemData();
        if (itemData == null)
        {
            Debug.LogWarning($"[PlayerInventory] NetworkedItem의 ItemData를 찾을 수 없습니다!");
            return false;
        }

        int itemObjectId = networkedItem.NetworkObject.ObjectId;

        // 이미 인벤토리에 같은 NetworkedItem이 있으면 중복 추가 방지
        if (HasNetworkedItem(itemObjectId))
        {
            Debug.Log($"[PlayerInventory] 이미 같은 아이템을 소지하고 있습니다!");
            return false;
        }

        // 아이템 타입별로 슬롯 결정
        int targetSlot = GetSlotForItemType(itemData.ItemType);

        if (targetSlot == -1)
        {
            Debug.LogWarning($"[PlayerInventory] 알 수 없는 아이템 타입: {itemData.ItemType}");
            return false;
        }

        // 슬롯에 아이템이 이미 있는지 확인
        int currentItemId = ItemSlots[targetSlot];

        // 이미 해당 슬롯에 아이템이 있다면, 기존 아이템을 드랍하고 교체
        if (currentItemId != 0)
        {
            // 기존 아이템 드랍 (Owner를 null로 설정)
            DropItemToWorld(targetSlot);
            Debug.Log($"[PlayerInventory] 슬롯 {targetSlot}의 아이템을 교체하기 위해 드랍했습니다!");
        }

        // 아이템 추가
        ItemSlots[targetSlot] = itemObjectId;

        // NetworkedItem의 Owner를 현재 플레이어로 설정하여 권한 이전
        networkedItem.OwnerConnection.Value = Owner;
        Debug.Log($"[PlayerInventory] NetworkedItem Owner 설정: ClientId {Owner?.ClientId}");

        // 무기 슬롯에 추가된 경우 즉시 장착 로직 수행
        if (targetSlot == SLOT_WEAPON && itemData is WeaponData weaponData)
        {
            Debug.Log($"[PlayerInventory] EquipWeapon 호출: {weaponData.ItemName}");
            EquipWeapon(weaponData, networkedItem);
        }
        // 방어구 슬롯에 추가된 경우 즉시 장착 로직 수행
        else if (targetSlot == SLOT_ARMOR && itemData is ArmorItemData armorData)
        {
            Debug.Log($"[PlayerInventory] EquipArmor 호출: {armorData.ItemName}");
            EquipArmor(armorData, networkedItem);
        }

        Debug.Log($"[PlayerInventory] {itemData.ItemName}을(를) 슬롯 {targetSlot}에 추가했습니다!");
        return true;
    }

    /// <summary>
    /// 특정 슬롯의 아이템을 교체합니다 (서버만).
    /// </summary>
    /// <param name="slotIndex">교체할 슬롯 인덱스</param>
    /// <param name="newItem">새 NetworkedItem</param>
    /// <returns>성공 여부</returns>
    public bool SwapItem(int slotIndex, NetworkedItem newItem)
    {
        if (!IsServerInitialized || newItem == null)
            return false;

        if (slotIndex < 0 || slotIndex >= SLOT_COUNT)
        {
            Debug.LogWarning($"[PlayerInventory] 잘못된 슬롯 인덱스: {slotIndex}");
            return false;
        }

        ItemData newItemData = newItem.GetItemData();
        if (newItemData == null)
        {
            Debug.LogWarning($"[PlayerInventory] NetworkedItem의 ItemData를 찾을 수 없습니다!");
            return false;
        }

        // 기존 아이템 드랍 (있을 경우)
        int currentItemId = ItemSlots[slotIndex];
        if (currentItemId != 0)
        {
            DropItemToWorld(slotIndex);

            DropItemToWorld(slotIndex);

            // 무기 슬롯이었다면 무기 해제 처리도 병행
            if (slotIndex == SLOT_WEAPON)
            {
                UnequipWeapon();
            }
            // 방어구 슬롯이었다면 방어구 해제 처리도 병행
            else if (slotIndex == SLOT_ARMOR)
            {
                UnequipArmor();
            }
        }

        // 새 아이템 추가
        ItemSlots[slotIndex] = newItem.NetworkObject.ObjectId;
        newItem.OwnerConnection.Value = Owner;
        Debug.Log($"[PlayerInventory] SwapItem - NetworkedItem Owner 설정: ClientId {Owner?.ClientId}");

        // 무기 슬롯에 새 아이템이 장착되었으므로 무기 설정
        if (slotIndex == SLOT_WEAPON && newItemData is WeaponData weaponData)
        {
            EquipWeapon(weaponData, newItem);
        }
        // 방어구 슬롯에 새 아이템이 장착되었으므로 방어구 설정
        else if (slotIndex == SLOT_ARMOR && newItemData is ArmorItemData armorData)
        {
            EquipArmor(armorData, newItem);
        }

        Debug.Log($"[PlayerInventory] 슬롯 {slotIndex}을(를) {newItemData.ItemName}(으)로 교체했습니다!");
        return true;
    }

    /// <summary>
    /// 아이템을 드랍합니다 (서버만).
    /// </summary>
    /// <param name="slotIndex">슬롯 인덱스</param>
    public void DropItem(int slotIndex)
    {
        if (!IsServerInitialized)
            return;

        if (slotIndex < 0 || slotIndex >= SLOT_COUNT)
        {
            Debug.LogWarning($"[PlayerInventory] 잘못된 슬롯 인덱스: {slotIndex}");
            return;
        }

        int itemId = ItemSlots[slotIndex];
        if (itemId == 0)
        {
            Debug.LogWarning($"[PlayerInventory] 슬롯 {slotIndex}이 비어있습니다!");
            return;
        }

        // 월드에 아이템 드랍
        DropItemToWorld(slotIndex);

        // 슬롯 비우기
        ItemSlots[slotIndex] = 0;

        // 무기 슬롯을 드랍한 경우 기본 무기로 복구
        if (slotIndex == SLOT_WEAPON)
        {
            EquipDefaultWeapon();
        }
        // 방어구 슬롯을 드랍한 경우 방어구 해제
        else if (slotIndex == SLOT_ARMOR)
        {
            UnequipArmor();
        }

        Debug.Log($"[PlayerInventory] 슬롯 {slotIndex}의 아이템을 드랍했습니다!");
    }

    /// <summary>
    /// 아이템을 월드에 드랍합니다 (Owner를 null로 설정).
    /// </summary>
    private void DropItemToWorld(int slotIndex)
    {
        int itemId = ItemSlots[slotIndex];
        if (itemId == 0) return;

        // ObjectId로 NetworkedItem 찾기
        NetworkedItem item = GetNetworkedItemById(itemId);
        if (item != null)
        {
            // 무기 드랍 시 현재 탄약 상태를 아이템 데이터에 백업하여 유지
            if (slotIndex == SLOT_WEAPON && _weaponSystem != null && item.GetItemData() is GunData gunData)
            {
                item.CurrentAmmo.Value = Mathf.Clamp(_weaponSystem.CurrentAmmo.Value, 0, gunData.MagazineSize);
                item.TotalAmmo.Value = Mathf.Clamp(_weaponSystem.TotalAmmo.Value, 0, gunData.MaxAmmo);
            }

            // 부모 관계를 해제하고 플레이어 발 밑에 드랍
            item.transform.SetParent(null);

            // 자연스러운 연출을 위해 플레이어 위치에서 약간 아래로 떨어뜨림
            Vector3 dropPosition = transform.position + Vector3.up * 0.5f;
            item.transform.position = dropPosition;
            item.transform.rotation = Quaternion.Euler(0, -90, 0);

            // Owner 해제 (이후 서버가 물리 처리)
            item.OwnerConnection.Value = null;
        }
        else
        {
            Debug.LogWarning($"[PlayerInventory] ObjectId {itemId}에 해당하는 NetworkedItem을 찾을 수 없습니다!");
        }
    }

    #endregion

    #region RPC Methods

    /// <summary>
    /// 클라이언트 → 서버: 아이템 드랍 요청
    /// </summary>
    [ServerRpc]
    public void RPC_RequestDropItem(int slotIndex)
    {
        DropItem(slotIndex);
    }

    /// <summary>
    /// 클라이언트 → 서버: 현재 무기 드랍 요청 (G키)
    /// </summary>
    [ServerRpc]
    public void RPC_RequestDropCurrentWeapon()
    {
        DropItem(SLOT_WEAPON);
    }

    /// <summary>
    /// 클라이언트 → 서버: 특정 슬롯으로 아이템 교체 요청 (픽업 패널에서 드래그)
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RPC_RequestSwapItem(int slotIndex, int itemObjectId, NetworkConnection sender = null)
    {
        if (!IsServerInitialized) return;

        NetworkedItem item = GetNetworkedItemById(itemObjectId);
        if (item == null)
        {
            Debug.LogWarning($"[PlayerInventory] RPC_RequestSwapItem - ObjectId {itemObjectId} 아이템을 찾을 수 없습니다.");
            return;
        }

        if (item.OwnerConnection.Value != null)
        {
            Debug.LogWarning($"[PlayerInventory] RPC_RequestSwapItem - 이미 소유된 아이템입니다: Owner={item.OwnerConnection.Value?.ClientId}");
            return;
        }

        SwapItem(slotIndex, item);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 아이템 타입에 맞는 슬롯 인덱스를 반환합니다.
    /// </summary>
    private int GetSlotForItemType(ItemType itemType)
    {
        return itemType switch
        {
            ItemType.Weapon => SLOT_WEAPON,
            ItemType.Armor => SLOT_ARMOR,
            ItemType.Usable => GetFirstEmptyUsableSlot(),
            _ => -1
        };
    }

    /// <summary>
    /// 빈 사용아이템 슬롯을 찾아 반환
    /// 빈 슬롯이 없으면 -1 반환
    /// </summary>
    private int GetFirstEmptyUsableSlot()
    {
        for (int i = SLOT_USABLE_START; i < SLOT_COUNT; i++)
        {
            if (ItemSlots[i] == 0)
            {
                return i;
            }
        }
        // 모든 슬롯이 차있으면 -1 반환
        return -1;
    }

    /// <summary>
    /// 인벤토리에 특정 NetworkedItem이 있는지 확인합니다.
    /// </summary>
    private bool HasNetworkedItem(int itemObjectId)
    {
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (ItemSlots[i] == itemObjectId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 특정 슬롯의 아이템 ObjectId를 반환합니다.
    /// </summary>
    public int GetItemSlotObjectId(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= ItemSlots.Count)
            return 0;
        return ItemSlots[slotIndex];
    }

    /// <summary>
    /// ObjectId로 NetworkedItem을 찾습니다.
    /// </summary>
    private NetworkedItem GetNetworkedItemById(int objectId)
    {
        if (objectId == 0) return null;

        // ServerManager를 통해 ObjectId에 해당하는 NetworkObject 검색
        if (InstanceFinder.ServerManager != null &&   
            InstanceFinder.ServerManager.Objects.Spawned.TryGetValue(objectId, out NetworkObject netObj))
        {
            return netObj.GetComponent<NetworkedItem>();
        }

        return null;
    }

    /// <summary>
    /// 무기를 장착합니다. NetworkedItem이 제공되면 저장된 탄약을 적용합니다.
    /// </summary>
    private void EquipWeapon(WeaponData weaponData, NetworkedItem sourceItem = null)
    {
        if (_weaponSystem == null)
            return;

        int currentAmmo = -1;
        int totalAmmo = -1;

        if (sourceItem != null && weaponData is GunData gunData)
        {
            currentAmmo = Mathf.Clamp(sourceItem.CurrentAmmo.Value, 0, gunData.MagazineSize);
            totalAmmo = Mathf.Clamp(sourceItem.TotalAmmo.Value, 0, gunData.MaxAmmo);
        }

        _weaponSystem.SetWeapon(weaponData, currentAmmo, totalAmmo);
        Debug.Log($"[PlayerInventory] {weaponData.ItemName} 장착! (탄약 적용: {(currentAmmo >= 0 ? $"{currentAmmo}/{totalAmmo}" : "기본값")})");
    }

    /// <summary>
    /// 무기를 해제합니다.
    /// </summary>
    private void UnequipWeapon()
    {
        if (_weaponSystem == null)
            return;

        _weaponSystem.SetWeapon(null);
        Debug.Log("[PlayerInventory] 무기 해제!");
    }

    /// <summary>
    /// 기본 무기(주먹)로 돌아갑니다.
    /// </summary>
    private void EquipDefaultWeapon()
    {
        if (_weaponSystem == null)
            return;

        if (_playerStats != null && _playerStats.DefaultWeapon != null)
        {
            // [Fix] 드랍된 아이템 참조(currentEquippedItem)를 제거하기 위해 명시적으로 Detach 호출
            _weaponSystem.OnWeaponDetached();
            
            _weaponSystem.SetWeapon(_playerStats.DefaultWeapon);
            Debug.Log($"[PlayerInventory] 기본 무기 {_playerStats.DefaultWeapon.ItemName}로 돌아감!");
        }
        else
        {
            _weaponSystem.SetWeapon(null);
            Debug.LogWarning("[PlayerInventory] 기본 무기가 없습니다!");
        }
    }

    /// <summary>
    /// 방어구를 장착합니다.
    /// </summary>
    private void EquipArmor(ArmorItemData armorData, NetworkedItem sourceItem = null)
    {
        if (_armorSystem == null)
            return;

        _armorSystem.SetArmor(armorData);
        Debug.Log($"[PlayerInventory] {armorData.ItemName} 장착! (방어력: {armorData.Defense})");
    }

    /// <summary>
    /// 방어구를 해제합니다.
    /// </summary>
    private void UnequipArmor()
    {
        if (_armorSystem == null)
            return;

        _armorSystem.SetArmor(null);
        Debug.Log("[PlayerInventory] 방어구 해제!");
    }

    #endregion
}
