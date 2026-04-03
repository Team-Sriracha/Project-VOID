using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카드 시스템 설정 관리
/// PlayerCardSystem에서 할당받음
/// </summary>
[CreateAssetMenu(fileName = "CardConfig", menuName = "Game/Card Config")]
public class CardConfig : ScriptableObject
{
    private static CardConfig _instance;
    public static CardConfig Instance
    {
        get => _instance;
        set => _instance = value;
    }

    [Header("카드 획득")]
    [Tooltip("카드 지급 간격 (초)")]
    public float CardGrantInterval = 40f;
    [Tooltip("최대 보유 가능 카드 수")]
    public int MaxOwnedCards = 20;

    [Header("등급 확률")]
    [Range(0f, 1f)]
    public float OneStarChance = 0.70f;
    [Range(0f, 1f)]
    public float TwoStarChance = 0.25f;
    [Range(0f, 1f)]
    public float ThreeStarChance = 0.05f;

    [Header("등급별 카드 Prefab")]
    [Tooltip("1성 카드 UI Prefab")]
    public GameObject OneStarCardPrefab;
    [Tooltip("2성 카드 UI Prefab")]
    public GameObject TwoStarCardPrefab;
    [Tooltip("3성 카드 UI Prefab")]
    public GameObject ThreeStarCardPrefab;

    [Header("카드 선택 사운드")]
    [Tooltip("등급 전용 사운드가 없을 때 사용할 기본 카드 선택음")]
    public AudioCue DefaultCardSelectAudioCue;
    [Tooltip("1성 카드 선택음")]
    public AudioCue OneStarCardSelectAudioCue;
    [Tooltip("2성 카드 선택음")]
    public AudioCue TwoStarCardSelectAudioCue;
    [Tooltip("3성 카드 선택음")]
    public AudioCue ThreeStarCardSelectAudioCue;

    [Header("스탯 최대치 (%)")]
    [Tooltip("공격력 최대 보너스")]
    public float MaxAtkBonus = 0.5f;
    [Tooltip("방어력 최대 보너스")]
    public float MaxDefBonus = 0.5f;
    [Tooltip("체력 최대 보너스 (고정값)")]
    public float MaxHPBonus = 100f;
    [Tooltip("속도 최대 보너스")]
    public float MaxSpdBonus = 0.3f;
    [Tooltip("공격속도 최대 보너스")]
    public float MaxAttackSpeedBonus = 0.3f;
    [Tooltip("장전속도 최대 보너스")]
    public float MaxReloadSpeedBonus = 0.5f;
    [Tooltip("대시 쿨다운 최대 감소")]
    public float MaxDashCooldownBonus = 0.5f;

    [Header("카드 풀")]
    [Tooltip("모든 카드")]
    public List<CardData> AllCards = new List<CardData>();

    /// <summary>
    /// 스탯별 최대 보너스 반환
    /// </summary>
    public float GetMaxBonus(StatType stat)
    {
        return stat switch
        {
            StatType.Attack => MaxAtkBonus,
            StatType.Defense => MaxDefBonus,
            StatType.Health => MaxHPBonus,
            StatType.Speed => MaxSpdBonus,
            StatType.AttackSpeed => MaxAttackSpeedBonus,
            StatType.ReloadSpeed => MaxReloadSpeedBonus,
            StatType.DashCooldown => MaxDashCooldownBonus,
            _ => 0.5f
        };
    }

    /// <summary>
    /// ID로 카드 데이터 검색
    /// </summary>
    public CardData GetCardById(string cardId)
    {
        foreach (var card in AllCards)
        {
            if (card != null && card.CardID == cardId) return card;
        }
        return null;
    }

    /// <summary>
    /// ID로 카드 데이터 검색 (GetCardById 래퍼)
    /// </summary>
    public CardData GetCard(string cardId)
    {
        return GetCardById(cardId);
    }

    /// <summary>
    /// 카드 희귀도에 맞는 선택 사운드를 반환합니다.
    /// </summary>
    public AudioCue GetCardSelectAudioCue(CardRarity rarity)
    {
        AudioCue rarityCue = rarity switch
        {
            CardRarity.OneStar => OneStarCardSelectAudioCue,
            CardRarity.TwoStar => TwoStarCardSelectAudioCue,
            CardRarity.ThreeStar => ThreeStarCardSelectAudioCue,
            _ => null
        };

        return rarityCue ?? DefaultCardSelectAudioCue;
    }
}
