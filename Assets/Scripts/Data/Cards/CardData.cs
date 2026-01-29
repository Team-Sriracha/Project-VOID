using UnityEngine;

/// <summary>
/// 통합 카드 데이터
/// 모든 효과는 AbilityID로 CardModifierFactory에서 Modifier 생성
/// </summary>
[CreateAssetMenu(fileName = "Card", menuName = "Game/Cards/Card")]
public class CardData : ScriptableObject
{
    [Header("기본 정보")]
    public string CardID;
    public string CardName;
    [TextArea(3, 5)]
    public string Description;
    public Sprite Icon;
    public CardRarity Rarity;

    [Header("효과")]
    [Tooltip("CardModifierFactory에서 Modifier 생성에 사용")]
    public string AbilityID;
    
    [Tooltip("무기 전용 카드인 경우 카테고리 지정")]
    public AbilityCategory Category = AbilityCategory.Common;

    [Header("중복 및 상호 배제")]
    [Tooltip("체크 시 중복 획득 불가")]
    public bool IsUnique;

    [Tooltip("이 리스트에 있는 카드를 보유 중이면 이 카드는 등장하지 않음")]
    public System.Collections.Generic.List<CardData> MutuallyExclusiveCards;
}

