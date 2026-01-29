using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// 네트워크 동기화된 방어구 시스템
/// </summary>
public class NetworkedArmor : NetworkBehaviour
{
    #region Constants

    /// <summary>
    /// 역치 공식의 기준 상수 (K)
    /// 감소율 = Defense / (Defense + ARMOR_CONSTANT)
    /// </summary>
    private const float ARMOR_CONSTANT = 100f;

    #endregion

    #region Serialized Fields

    [Header("방어구 장착 위치")]
    [SerializeField] private Transform _armorAttachPoint;

    #endregion

    #region Private Fields

    private ArmorItemData _armorData;
    private NetworkedItem _currentEquippedItem;

    #endregion

    #region Properties

    /// <summary>
    /// 방어구 모델 부착 위치
    /// </summary>
    public Transform ArmorAttachPoint => _armorAttachPoint;

    /// <summary>
    /// 현재 장착된 방어구 데이터
    /// </summary>
    public ArmorItemData CurrentArmorData => _armorData;

    /// <summary>
    /// 현재 방어력 수치 (등급 배율 적용, 반올림)
    /// </summary>
    public float CurrentDefense
    {
        get
        {
            if (_armorData == null) return 0f;
            
            float baseDefense = _armorData.Defense;
            
            // 등급 배율 적용 후 반올림
            if (_currentEquippedItem != null)
            {
                var config = ItemTierConfig.Instance;
                if (config != null)
                {
                    float multiplier = config.GetSettings(_currentEquippedItem.Tier.Value).DefenseMultiplier;
                    return Mathf.Round(baseDefense * multiplier);
                }
            }
            
            return baseDefense;
        }
    }

    /// <summary>
    /// 현재 데미지 감소율 (0~1)
    /// 역치 공식: Defense / (Defense + K)
    /// </summary>
    public float CurrentDamageReduction
    {
        get
        {
            float defense = CurrentDefense;
            if (defense <= 0f) return 0f;
            return defense / (defense + ARMOR_CONSTANT);
        }
    }

    #endregion

    #region Armor Management

    /// <summary>
    /// 방어구 데이터 변경 (서버 전용)
    /// </summary>
    /// <param name="newArmorData">새 방어구 데이터 (null이면 해제)</param>
    public void SetArmor(ArmorItemData newArmorData)
    {
        if (!IsServerInitialized)
        {
            Debug.LogWarning("[NetworkedArmor] SetArmor는 서버만 호출 가능");
            return;
        }

        _armorData = newArmorData;
        string armorID = newArmorData != null ? newArmorData.ItemID : "";

        // 클라이언트에 방어구 데이터 동기화
        RPC_SyncArmorData(armorID);
    }

    /// <summary>
    /// 모든 클라이언트에 방어구 데이터 동기화
    /// </summary>
    [ObserversRpc]
    private void RPC_SyncArmorData(string armorID)
    {
        // ItemID로 ArmorItemData 로드 (클라이언트용)
        if (string.IsNullOrEmpty(armorID))
        {
            _armorData = null;
            return;
        }

        ItemData itemData = ItemDatabase.GetItem(armorID);
        if (itemData is ArmorItemData armorData)
        {
            _armorData = armorData;
        }
        else
        {
            Debug.LogError($"[NetworkedArmor] ItemID '{armorID}'는 ArmorItemData가 아닙니다!");
        }
    }

    #endregion

    #region Attachment Callbacks

    /// <summary>
    /// NetworkedItem 부착 시 호출
    /// </summary>
    public void OnArmorAttached(NetworkedItem item)
    {
        _currentEquippedItem = item;
    }

    /// <summary>
    /// 방어구 제거 시 호출
    /// </summary>
    public void OnArmorDetached()
    {
        _currentEquippedItem = null;
    }

    #endregion
}
