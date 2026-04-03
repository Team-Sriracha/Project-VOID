using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// LootBox별 아이템 드랍 풀 ScriptableObject
/// </summary>
[CreateAssetMenu(fileName = "LootBoxItemPool", menuName = "Game/LootBox Item Pool")]
public class LootBoxItemPool : ScriptableObject
{
    #region Nested Types

    /// <summary>
    /// 아이템 드랍 엔트리
    /// </summary>
    [System.Serializable]
    public struct ItemDropEntry
    {
        [Tooltip("드랍할 아이템 데이터")]
        public ItemData Item;

        [Tooltip("드랍 가중치 (상대적 확률)")]
        [Range(0f, 100f)]
        public float DropWeight;
    }

    #endregion

    #region Serialized Fields

    [Header("아이템 풀")]
    [Tooltip("이 LootBox에서 드랍 가능한 아이템 목록")]
    [SerializeField] private ItemDropEntry[] _itemPool;

    #endregion

    #region Properties

    /// <summary>
    /// 아이템 풀 배열
    /// </summary>
    public ItemDropEntry[] ItemPool => _itemPool;

    /// <summary>
    /// 아이템 풀이 비어있는지 확인
    /// </summary>
    public bool IsEmpty => _itemPool == null || _itemPool.Length == 0;

    #endregion

    #region Public Methods

    /// <summary>
    /// 가중치 기반으로 랜덤 아이템을 선택합니다.
    /// </summary>
    /// <returns>선택된 아이템 (없으면 null)</returns>
    public ItemData GetRandomItem()
    {
        if (IsEmpty)
        {
            Debug.LogWarning($"[LootBoxItemPool] {name}의 아이템 풀이 비어있습니다!");
            return null;
        }

        // 총 가중치 계산
        float totalWeight = 0f;
        foreach (var entry in _itemPool)
        {
            if (entry.Item != null)
            {
                totalWeight += entry.DropWeight;
            }
        }

        if (totalWeight <= 0f)
        {
            Debug.LogWarning($"[LootBoxItemPool] {name}의 총 가중치가 0입니다!");
            return null;
        }

        // 가중치 기반 랜덤 선택
        float randomValue = Random.Range(0f, totalWeight);
        float cumulative = 0f;

        foreach (var entry in _itemPool)
        {
            if (entry.Item == null) continue;

            cumulative += entry.DropWeight;
            if (randomValue <= cumulative)
            {
                return entry.Item;
            }
        }

        // 폴백: 마지막 유효한 아이템 반환
        for (int i = _itemPool.Length - 1; i >= 0; i--)
        {
            if (_itemPool[i].Item != null)
            {
                return _itemPool[i].Item;
            }
        }

        return null;
    }

    #endregion
}
