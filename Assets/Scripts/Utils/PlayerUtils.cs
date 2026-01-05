using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어 관련 공통 유틸리티 메서드
/// </summary>
public static class PlayerUtils
{
    private static PlayerCombat _cachedLocalCombat;
    private static PlayerInventory _cachedLocalInventory;

    /// <summary>
    /// 로컬 플레이어의 PlayerCombat을 반환합니다 (캐싱됨).
    /// </summary>
    public static PlayerCombat GetLocalPlayerCombat()
    {
        if (_cachedLocalCombat != null && _cachedLocalCombat.Object != null && _cachedLocalCombat.Object.IsValid)
        {
            return _cachedLocalCombat;
        }

        PlayerCombat[] allCombats = Object.FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None);
        foreach (var combat in allCombats)
        {
            NetworkObject netObj = combat.GetComponent<NetworkObject>();
            if (netObj != null && netObj.HasInputAuthority)
            {
                _cachedLocalCombat = combat;
                return combat;
            }
        }

        return null;
    }

    /// <summary>
    /// 로컬 플레이어의 PlayerInventory를 반환합니다 (캐싱됨).
    /// </summary>
    public static PlayerInventory GetLocalPlayerInventory()
    {
        if (_cachedLocalInventory != null && _cachedLocalInventory.Object != null && _cachedLocalInventory.Object.IsValid)
        {
            return _cachedLocalInventory;
        }

        PlayerInventory[] allInventories = Object.FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None);
        foreach (var inventory in allInventories)
        {
            NetworkObject netObj = inventory.GetComponent<NetworkObject>();
            if (netObj != null && netObj.HasInputAuthority)
            {
                _cachedLocalInventory = inventory;
                return inventory;
            }
        }

        return null;
    }

    /// <summary>
    /// 캐시를 초기화합니다 (씬 전환 시 호출).
    /// </summary>
    public static void ClearCache()
    {
        _cachedLocalCombat = null;
        _cachedLocalInventory = null;
    }
}
