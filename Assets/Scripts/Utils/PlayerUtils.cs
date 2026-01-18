using FishNet;
using FishNet.Object;
using UnityEngine;

/// <summary>
/// 플레이어 관련 공통 유틸리티 메서드
/// </summary>
public static class PlayerUtils
{
    #region Private Fields

    private static PlayerCombat _cachedLocalCombat;
    private static PlayerInventory _cachedLocalInventory;

    #endregion

    #region Public Methods

    /// <summary>
    /// 로컬 플레이어 PlayerCombat 반환 (캐싱됨)
    /// </summary>
    public static PlayerCombat GetLocalPlayerCombat()
    {
        if (_cachedLocalCombat != null && 
            _cachedLocalCombat.NetworkObject != null && 
            _cachedLocalCombat.NetworkObject.IsSpawned)
        {
            return _cachedLocalCombat;
        }

        // FishNet 최적화: 씬 전체 검색 대신 로컬 클라이언트 소유 객체만 순회
        if (InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Connection != null)
        {
            foreach (var netObj in InstanceFinder.ClientManager.Connection.Objects)
            {
                if (netObj.TryGetComponent(out PlayerCombat combat))
                {
                    _cachedLocalCombat = combat;
                    return combat;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 로컬 플레이어 PlayerInventory 반환 (캐싱됨)
    /// </summary>
    public static PlayerInventory GetLocalPlayerInventory()
    {
        if (_cachedLocalInventory != null && 
            _cachedLocalInventory.NetworkObject != null && 
            _cachedLocalInventory.NetworkObject.IsSpawned)
        {
            return _cachedLocalInventory;
        }

        // FishNet 최적화: 씬 전체 검색 대신 로컬 클라이언트 소유 객체만 순회
        if (InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Connection != null)
        {
            foreach (var netObj in InstanceFinder.ClientManager.Connection.Objects)
            {
                if (netObj.TryGetComponent(out PlayerInventory inventory))
                {
                    _cachedLocalInventory = inventory;
                    return inventory;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 캐시 초기화 (씬 전환 시 호출)
    /// </summary>
    public static void ClearCache()
    {
        _cachedLocalCombat = null;
        _cachedLocalInventory = null;
    }

    #endregion
}
