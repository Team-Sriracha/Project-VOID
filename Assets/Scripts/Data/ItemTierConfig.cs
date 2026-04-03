using UnityEngine;

/// <summary>
/// 등급별 설정을 담는 ScriptableObject
/// </summary>
[CreateAssetMenu(fileName = "ItemTierConfig", menuName = "Game/Item Tier Config")]
public class ItemTierConfig : ScriptableObject
{
    #region Singleton

    private static ItemTierConfig _instance;

    /// <summary>
    /// ItemTierConfig 싱글톤 인스턴스
    /// </summary>
    public static ItemTierConfig Instance => _instance;

    /// <summary>
    /// 싱글톤 인스턴스를 설정합니다. (GameStateManager에서 호출)
    /// </summary>
    public static void SetInstance(ItemTierConfig config)
    {
        _instance = config;
        if (_instance != null)
        {
            Debug.Log("[ItemTierConfig] 인스턴스 설정 완료");
        }
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// 등급별 설정 구조체
    /// </summary>
    [System.Serializable]
    public struct TierSettings
    {
        [Tooltip("등급")]
        public ItemTier Tier;

        [Tooltip("UI 표시 색상")]
        public Color TierColor;

        [Tooltip("무기 데미지 배율 (예: 1.0, 1.1, 1.2...)")]
        public float DamageMultiplier;

        [Tooltip("방어구 방어력 배율 (예: 1.0, 1.1, 1.2...)")]
        public float DefenseMultiplier;

        [Tooltip("인벤토리 슬롯 오버레이 이미지")]
        public Sprite TierOverlaySprite;

        [Tooltip("드랍 시 표시될 이펙트 프리팹")]
        public GameObject DropEffectPrefab;
    }

    #endregion

    #region Serialized Fields

    [Header("등급별 설정")]
    [SerializeField] private TierSettings[] _tierSettings = new TierSettings[]
    {
        new TierSettings { Tier = ItemTier.Normal, TierColor = Color.white, DamageMultiplier = 1.0f, DefenseMultiplier = 1.0f },
        new TierSettings { Tier = ItemTier.Rare, TierColor = new Color(0.2f, 0.6f, 1.0f), DamageMultiplier = 1.1f, DefenseMultiplier = 1.1f },
        new TierSettings { Tier = ItemTier.Epic, TierColor = new Color(1.0f, 0.5f, 0.0f), DamageMultiplier = 1.2f, DefenseMultiplier = 1.2f },
        new TierSettings { Tier = ItemTier.Unique, TierColor = new Color(0.8f, 0.2f, 0.8f), DamageMultiplier = 1.3f, DefenseMultiplier = 1.3f },
        new TierSettings { Tier = ItemTier.Legendary, TierColor = new Color(1.0f, 0.84f, 0.0f), DamageMultiplier = 1.4f, DefenseMultiplier = 1.4f }
    };

    #endregion

    #region Public Methods

    /// <summary>
    /// 특정 등급의 설정을 반환합니다.
    /// </summary>
    /// <param name="tier">조회할 등급</param>
    /// <returns>해당 등급의 설정</returns>
    public TierSettings GetSettings(ItemTier tier)
    {
        foreach (var settings in _tierSettings)
        {
            if (settings.Tier == tier)
            {
                return settings;
            }
        }

        // 기본값 반환
        Debug.LogWarning($"[ItemTierConfig] {tier} 등급 설정을 찾을 수 없습니다. 기본값 반환.");
        return new TierSettings
        {
            Tier = tier,
            TierColor = Color.white,
            DamageMultiplier = 1.0f,
            DefenseMultiplier = 1.0f
        };
    }

    #endregion
}
