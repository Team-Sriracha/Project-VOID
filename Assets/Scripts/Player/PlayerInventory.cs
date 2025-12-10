using Fusion;
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

    #region Networked Properties

    /// <summary>
    /// 5칸 인벤토리 (NetworkedItem의 NetworkId 저장, 빈 칸은 default)
    /// Why: 각 NetworkedItem은 독립적인 개체 (탄약, 내구도 등 고유 상태 보유)
    /// </summary>
    [Networked, Capacity(5)]
    public NetworkArray<NetworkId> ItemSlots { get; }

    /// <summary>
    /// 현재 장착 무기 슬롯
    /// </summary>
    [Networked]
    public int CurrentWeaponSlot { get; set; }

    #endregion

    #region Private Fields

    private NetworkedWeapon _weaponSystem;
    private PlayerController _controller;
    [SerializeField] private PlayerStats _playerStats;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        _weaponSystem = GetComponent<NetworkedWeapon>();
        _controller = GetComponent<PlayerController>();

        // Why: 서버는 기본 무기 장착
        if (HasStateAuthority)
        {
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
        CurrentWeaponSlot = SLOT_WEAPON;

        // Why: 기본 무기는 NetworkedWeapon에만 설정, ItemSlots는 비워둠
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
    /// 아이템을 인벤토리에 추가합니다 (서버만).
    /// </summary>
    /// <param name="networkedItem">NetworkedItem 참조</param>
    /// <returns>성공 여부</returns>
    public bool TryAddItem(NetworkedItem networkedItem)
    {
        if (!HasStateAuthority || networkedItem == null)
            return false;

        ItemData itemData = networkedItem.GetItemData();
        if (itemData == null)
        {
            Debug.LogWarning($"[PlayerInventory] NetworkedItem의 ItemData를 찾을 수 없습니다!");
            return false;
        }

        // Why: 이미 인벤토리에 같은 NetworkedItem이 있으면 거부
        if (HasNetworkedItem(networkedItem.Object.Id))
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
        NetworkId currentItemId = ItemSlots[targetSlot];

        // Why: 무기/방어구는 기존 아이템 드랍 후 교체
        if (currentItemId != default)
        {
            // 기존 아이템 드랍 (Owner를 None으로 설정)
            DropItemToWorld(targetSlot);
            Debug.Log($"[PlayerInventory] 슬롯 {targetSlot}의 아이템을 교체하기 위해 드랍했습니다!");
        }

        // 아이템 추가
        ItemSlots.Set(targetSlot, networkedItem.Object.Id);

        // Why: NetworkedItem의 Owner를 현재 플레이어로 설정
        networkedItem.Owner = Object.InputAuthority;
        Debug.Log($"[PlayerInventory] NetworkedItem Owner 설정: {networkedItem.Owner}, InputAuthority: {Object.InputAuthority}");

        // Why: 무기 슬롯에 추가된 경우 즉시 장착
        if (targetSlot == SLOT_WEAPON && itemData is WeaponData weaponData)
        {
            Debug.Log($"[PlayerInventory] EquipWeapon 호출: {weaponData.ItemName}");
            EquipWeapon(weaponData, networkedItem);
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
        if (!HasStateAuthority || newItem == null)
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
        NetworkId currentItemId = ItemSlots[slotIndex];
        if (currentItemId != default)
        {
            DropItemToWorld(slotIndex);

            // Why: 무기 슬롯이면 무기 해제
            if (slotIndex == SLOT_WEAPON)
            {
                UnequipWeapon();
            }
        }

        // 새 아이템 추가
        ItemSlots.Set(slotIndex, newItem.Object.Id);
        newItem.Owner = Object.InputAuthority;
        Debug.Log($"[PlayerInventory] SwapItem - NetworkedItem Owner 설정: {newItem.Owner}, InputAuthority: {Object.InputAuthority}");

        // Why: 무기 슬롯이면 무기 장착
        if (slotIndex == SLOT_WEAPON && newItemData is WeaponData weaponData)
        {
            EquipWeapon(weaponData, newItem);
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
        if (!HasStateAuthority)
            return;

        if (slotIndex < 0 || slotIndex >= SLOT_COUNT)
        {
            Debug.LogWarning($"[PlayerInventory] 잘못된 슬롯 인덱스: {slotIndex}");
            return;
        }

        NetworkId itemId = ItemSlots[slotIndex];
        if (itemId == default)
        {
            Debug.LogWarning($"[PlayerInventory] 슬롯 {slotIndex}이 비어있습니다!");
            return;
        }

        // 월드에 아이템 드랍
        DropItemToWorld(slotIndex);

        // 슬롯 비우기
        ItemSlots.Set(slotIndex, default);

        // Why: 무기 슬롯이면 기본 무기로 돌아감
        if (slotIndex == SLOT_WEAPON)
        {
            EquipDefaultWeapon();
        }

        Debug.Log($"[PlayerInventory] 슬롯 {slotIndex}의 아이템을 드랍했습니다!");
    }

    /// <summary>
    /// 아이템을 월드에 드랍합니다 (Owner를 None으로 설정).
    /// </summary>
    private void DropItemToWorld(int slotIndex)
    {
        NetworkId itemId = ItemSlots[slotIndex];
        if (itemId == default) return;

        // NetworkId로 NetworkedItem 찾기
        NetworkedItem item = GetNetworkedItemById(itemId);
        if (item != null)
        {
            // Why: 무기 슬롯 드랍 시 현재 탄약 상태를 아이템에 저장 (GunData만)
            if (slotIndex == SLOT_WEAPON && _weaponSystem != null && item.GetItemData() is GunData gunData)
            {
                item.CurrentAmmo = Mathf.Clamp(_weaponSystem.CurrentAmmo, 0, gunData.MagazineSize);
                item.TotalAmmo = Mathf.Clamp(_weaponSystem.TotalAmmo, 0, gunData.MaxAmmo);
            }

            // Why: 부모 해제 (플레이어에서 분리)
            item.transform.SetParent(null);

            // Owner 해제하고 위치/회전 변경
            item.Owner = PlayerRef.None;
            // Why: 플레이어 발 근처에서 드롭 (아래로 1유닛) - 짧게 떨어져서 부자연스럽지 않음
            item.transform.position = transform.position + Vector3.up * 0.5f;
            item.transform.rotation = Quaternion.Euler(0, -90, 0);

            Debug.Log($"[PlayerInventory] NetworkedItem의 Owner 해제 및 월드에 배치");
        }
        else
        {
            Debug.LogWarning($"[PlayerInventory] NetworkId {itemId}에 해당하는 NetworkedItem을 찾을 수 없습니다!");
        }
    }

    #endregion

    #region RPC Methods

    /// <summary>
    /// 클라이언트 → 서버: 아이템 드랍 요청
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestDropItem(int slotIndex, RpcInfo info = default)
    {
        DropItem(slotIndex);
    }

    /// <summary>
    /// 클라이언트 → 서버: 현재 무기 드랍 요청 (G키)
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestDropCurrentWeapon(RpcInfo info = default)
    {
        DropItem(SLOT_WEAPON);
    }

    /// <summary>
    /// 클라이언트 → 서버: 특정 슬롯으로 아이템 교체 요청 (픽업 패널에서 드래그)
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestSwapItem(int slotIndex, NetworkId itemNetworkId, RpcInfo info = default)
    {
        NetworkedItem item = GetNetworkedItemById(itemNetworkId);
        if (item != null)
        {
            SwapItem(slotIndex, item);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 아이템 타입에 맞는 슬롯 인덱스를 반환합니다.
    /// </summary>
    private int GetSlotForItemType(EItemType itemType)
    {
        return itemType switch
        {
            EItemType.Weapon => SLOT_WEAPON,
            EItemType.Armor => SLOT_ARMOR,
            EItemType.Usable => GetFirstEmptyUsableSlot(),
            _ => -1
        };
    }

    /// <summary>
    /// 빈 사용아이템 슬롯을 찾아 반환합니다.
    /// Why: 빈 슬롯이 없으면 마지막 칸(슬롯4)을 반환
    /// </summary>
    private int GetFirstEmptyUsableSlot()
    {
        for (int i = SLOT_USABLE_START; i < SLOT_COUNT; i++)
        {
            if (ItemSlots[i] == default)
            {
                return i;
            }
        }
        // 모든 슬롯이 차있으면 마지막 칸(슬롯4) 반환
        return SLOT_COUNT - 1;
    }

    /// <summary>
    /// 인벤토리에 특정 NetworkedItem이 있는지 확인합니다.
    /// </summary>
    private bool HasNetworkedItem(NetworkId itemId)
    {
        for (int i = 0; i < SLOT_COUNT; i++)
        {
            if (ItemSlots[i] == itemId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// NetworkId로 NetworkedItem을 찾습니다.
    /// </summary>
    private NetworkedItem GetNetworkedItemById(NetworkId itemId)
    {
        if (itemId == default) return null;

        // Why: Runner에서 NetworkId로 NetworkObject 찾기
        if (Runner.TryFindObject(itemId, out NetworkObject netObj))
        {
            return netObj.GetComponent<NetworkedItem>();
        }

        return null;
    }

    /// <summary>
    /// 월드에 아이템을 스폰합니다.
    /// </summary>
    private void SpawnItemInWorld(string itemID, Vector3 position)
    {
        ItemData itemData = ItemDatabase.GetItem(itemID);
        if (itemData == null || itemData.ModelPrefab == null)
        {
            Debug.LogWarning($"[PlayerInventory] ItemID '{itemID}'의 ModelPrefab이 없습니다!");
            return;
        }

        // NetworkedItem 프리팹 스폰
        NetworkObject itemObj = Runner.Spawn(
            itemData.ModelPrefab,
            position,
            Quaternion.identity
        );

        NetworkedItem networkedItem = itemObj.GetComponent<NetworkedItem>();
        if (networkedItem != null)
        {
            networkedItem.ItemID = itemID;
        }
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
            currentAmmo = Mathf.Clamp(sourceItem.CurrentAmmo, 0, gunData.MagazineSize);
            totalAmmo = Mathf.Clamp(sourceItem.TotalAmmo, 0, gunData.MaxAmmo);
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
            _weaponSystem.SetWeapon(_playerStats.DefaultWeapon);
            Debug.Log($"[PlayerInventory] 기본 무기 {_playerStats.DefaultWeapon.ItemName}로 돌아감!");
        }
        else
        {
            _weaponSystem.SetWeapon(null);
            Debug.LogWarning("[PlayerInventory] 기본 무기가 없습니다!");
        }
    }

    #endregion
}
