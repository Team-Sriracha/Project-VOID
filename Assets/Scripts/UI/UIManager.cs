using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using FishNet;
using FishNet.Object;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 게임 UI를 관리하는 싱글톤 (전장 정보, 킬로그, 인벤토리, 오버헤드 UI 등)
/// 로딩 화면 관련 기능은 LoadingUIManager로 이동됨
/// </summary>
public class UIManager : MonoBehaviour
{
    #region Singleton

    private static UIManager _instance;
    public static UIManager Instance => _instance;

    public Transform OverheadUIContainer => _overheadUIContainer;
    public Canvas MainCanvas => _canvas;

    #endregion

    #region Serialized Fields

    [Header("전장 정보")]
    [SerializeField] private TextMeshProUGUI _alivePlayersText;
    [SerializeField] private TextMeshProUGUI _currentPhaseText;
    [SerializeField] private TextMeshProUGUI _gameTimeText;
    [SerializeField] private TextMeshProUGUI _killCountText;
    [SerializeField] private TextMeshProUGUI _nextPhaseTimeText;
    [SerializeField] private Slider _currentPhaseTimeBar;

    [Header("킬로그")]
    [SerializeField] private Transform _killLogContainer;
    [SerializeField] private KillLogEntry _killLogPrefab;

    [Header("플레이어 스탯")]
    [SerializeField] private TextMeshProUGUI _attackText;
    [SerializeField] private TextMeshProUGUI _defenseText;
    [SerializeField] private TextMeshProUGUI _speedText;
    [SerializeField] private TextMeshProUGUI _xpText;

    [Header("인벤토리 슬롯")]
    [SerializeField] private ItemSlotUI[] _itemSlots = new ItemSlotUI[5];

    [Header("게임 결과")]
    [SerializeField] private GameResultUI _gameResultUI;

    [Header("플레이어 오버헤드 UI")]
    [SerializeField] private PlayerOverheadUI _overheadUIPrefab;
    [SerializeField] private Transform _overheadUIContainer;

    [Header("카드 뽑기")]
    [SerializeField] private Button _cardDrawButton;
    [SerializeField] private TextMeshProUGUI _cardStackText;

    [Header("카드 선택 UI")]
    [SerializeField] private GameObject _cardSelectionPanel;
    [SerializeField] private Transform _cardContainer;
    [SerializeField] private CanvasGroup _cardSelectionCanvasGroup;
    [SerializeField] private float _cardScaleDuration = 0.5f;
    [SerializeField] private float _cardMoveDuration = 0.6f;
    [SerializeField] private float _cardDelay = 0.2f;

    [Header("보유 능력 UI")]
    [SerializeField] private Transform _abilityContainer;
    [SerializeField] private GameObject _abilityButtonPrefab;
    [SerializeField, Tooltip("능력 카드 프리뷰 미리보기 크기 (0.5 = 50%)")]
    private float _abilityPreviewScale = 0.5f;
    [SerializeField, Tooltip("능력 카드 프리뷰 투명도 (0.8 = 80%)")]
    private float _abilityPreviewAlpha = 0.9f;

    // ... (중략) ...

    #endregion

    #region Private Fields

    private Queue<KillLogEntry> _killLogPool = new Queue<KillLogEntry>();
    private PlayerInventory _cachedInventory;
    private NetworkedWeapon _cachedWeapon;
    private NetworkedArmor _cachedArmor;
    private PlayerController _cachedController;
    private PlayerCardSystem _cachedCardSystem;
    private Canvas _canvas;
    private Dictionary<Transform, PlayerOverheadUI> _overheadUIs = new Dictionary<Transform, PlayerOverheadUI>();
    private List<GameObject> _currentDisplayedCards = new List<GameObject>();
    private List<GameObject> _currentAbilityButtons = new List<GameObject>();
    private GameObject _abilityPreviewCard;

    /// <summary>
    /// 카드 선택 UI가 열려있는지 여부 (읽기 전용)
    /// </summary>
    public bool IsCardSelectionOpen { get; private set; }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        InitializeItemSlots();
    }
    
    private void Start()
    {
        // Late Joiner를 위해 기존 플레이어들의 OverheadUI 생성
        RegisterExistingPlayersUI();
    }

    private void Update()
    {
        UpdateBattlefieldInfo();
        UpdatePlayerStats();
        UpdateInventoryUI();
        UpdateCardDrawButton();
        UpdateOwnedAbilityUI();
    }

    #endregion

    #region Initialization

    private void InitializeItemSlots()
    {
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        if (_canvas == null) { Debug.LogError("[UIManager] Canvas를 찾을 수 없습니다!"); return; }

        for (int i = 0; i < _itemSlots.Length; i++)
        {
            if (_itemSlots[i] != null) _itemSlots[i].Initialize(i, _canvas);
        }
    }
    
    #endregion
    
    #region 전장 정보

    private void UpdateBattlefieldInfo()
    {
        if (GameStateManager.Instance == null) return;

        if (_alivePlayersText != null) _alivePlayersText.text = $"{GameStateManager.Instance.AlivePlayers.Value}";
        if (_currentPhaseText != null) _currentPhaseText.text = $"{GameStateManager.Instance.CurrentPhase.Value}";

        if (_gameTimeText != null)
        {
            float time = GameStateManager.Instance.RemainingTime;
            _gameTimeText.text = $"{(int)time / 60:00}:{(int)time % 60:00}";
        }

        if (_nextPhaseTimeText != null)
        {
            float timeToNext = GameStateManager.Instance.TimeToNextPhase;
            if (GameStateManager.Instance.IsLastPhase || timeToNext <= 0f) _nextPhaseTimeText.text = "--:--";
            else _nextPhaseTimeText.text = $"{(int)timeToNext / 60:00}:{(int)timeToNext % 60:00}";
        }

        if (_currentPhaseTimeBar != null)
        {
            float timeToNext = GameStateManager.Instance.TimeToNextPhase;
            float phaseDuration = GameStateManager.Instance.CurrentPhaseDuration;
            _currentPhaseTimeBar.value = (GameStateManager.Instance.IsLastPhase || phaseDuration <= 0f) ? 0f : timeToNext / phaseDuration;
        }

        if (_killCountText != null)
        {
            PlayerCombat localCombat = PlayerUtils.GetLocalPlayerCombat();
            if (localCombat != null)
            {
                _killCountText.text = $"{localCombat.KillCount.Value}";
            }
        }
    }

    #endregion

    #region 킬로그

    public void AddKillLog(string killerName, string victimName, Sprite weaponIcon = null)
    {
        KillLogEntry entry = (_killLogPool.Count > 0) ? _killLogPool.Dequeue() : Instantiate(_killLogPrefab, _killLogContainer);
        entry.gameObject.SetActive(true);
        entry.Initialize(killerName, victimName, weaponIcon);
    }

    public void ReturnKillLogEntry(KillLogEntry entry)
    {
        entry.gameObject.SetActive(false);
        _killLogPool.Enqueue(entry);
    }

    #endregion

    #region 플레이어 스탯

    private void UpdatePlayerStats()
    {
        PlayerCombat localCombat = PlayerUtils.GetLocalPlayerCombat();
        if (localCombat == null) return;

        // 캐싱하여 매 프레임 GetComponent 호출 방지
        if (_cachedWeapon == null) _cachedWeapon = localCombat.GetComponent<NetworkedWeapon>();
        if (_cachedArmor == null) _cachedArmor = localCombat.GetComponent<NetworkedArmor>();
        if (_cachedController == null) _cachedController = localCombat.GetComponent<PlayerController>();

        PlayerCardSystem cardSystem = localCombat.GetComponent<PlayerCardSystem>();

        // ATK 표시 (카드 보너스 포함)
        if (_attackText != null && _cachedWeapon != null)
        {
            float atkBonus = cardSystem != null ? cardSystem.GetStatBonus(StatType.Attack) * 100f : 0f;
            string bonusText = atkBonus > 0 ? $" (+{atkBonus:F0}%)" : "";
            _attackText.text = $"ATK: {_cachedWeapon.CurrentTotalDamage:F0}{bonusText}";
        }

        // DEF 표시 (카드 보너스 포함)
        if (_defenseText != null)
        {
            float def = _cachedArmor != null ? _cachedArmor.CurrentDefense : 0f;
            float defBonus = cardSystem != null ? cardSystem.GetStatBonus(StatType.Defense) * 100f : 0f;
            string bonusText = defBonus > 0 ? $" (+{defBonus:F0}%)" : "";
            _defenseText.text = $"DEF: {def:F0}{bonusText}";
        }

        // SPD 표시
        if (_speedText != null && _cachedController != null)
        {
            _speedText.text = $"SPD: {_cachedController.GetCurrentMoveSpeed():F0}";
        }

        // XP 표시
        if (_xpText != null)
        {
            _xpText.text = $"XP: {localCombat.CurrentXP.Value:F0} / {localCombat.XPToNextLevel:F0}";
        }
    }

    #endregion

    #region 인벤토리 UI

    private void UpdateInventoryUI()
    {
        if (_cachedInventory == null) _cachedInventory = PlayerUtils.GetLocalPlayerInventory();
        if (_cachedInventory == null || _itemSlots == null) return;
        if (_cachedInventory.NetworkObject == null || !_cachedInventory.NetworkObject.IsSpawned) return;

        for (int i = 0; i < _itemSlots.Length && i < 5; i++)
        {
            if (_itemSlots[i] == null) continue;
            
            _itemSlots[i].SetItem(null);
            int itemNetObjId = _cachedInventory.GetItemSlotObjectId(i);

            if (itemNetObjId != 0)
            {
                // FishNet Spawned 딕셔너리 직접 조회 (FindObjectsByType보다 효율적)
                if (InstanceFinder.ClientManager != null && 
                    InstanceFinder.ClientManager.Objects.Spawned.TryGetValue(itemNetObjId, out var nob) &&
                    nob.TryGetComponent<NetworkedItem>(out var networkedItem))
                {
                    // Tier 정보와 함께 아이템 설정
                    _itemSlots[i].SetItem(networkedItem.GetItemData(), networkedItem.Tier.Value);
                }
            }
        }
    }

    #endregion

    #region 카드 뽑기

    private void UpdateCardDrawButton()
    {
        if (_cardDrawButton == null || _cardStackText == null) return;

        // 캐싱
        if (_cachedCardSystem == null)
        {
            var combat = PlayerUtils.GetLocalPlayerCombat();
            _cachedCardSystem = combat?.GetComponent<PlayerCardSystem>();
            
            if (_cachedCardSystem == null) return;
        }

        int stacks = _cachedCardSystem.CardDrawStacks.Value;
        int ownedCards = _cachedCardSystem.OwnedCardIds.Count;
        int maxCards = CardConfig.Instance?.MaxOwnedCards ?? 20;

        // MAX 표시
        if (ownedCards >= maxCards)
        {
            _cardStackText.text = "MAX";
            _cardDrawButton.interactable = false;
        }
        else
        {
            _cardStackText.text = stacks.ToString();
            // 카드 선택 창이 열려있으면 버튼 비활성화 (재뽑기 방지)
            _cardDrawButton.interactable = stacks > 0 && !IsCardSelectionOpen;
        }
    }

    /// <summary>
    /// 카드 뽑기 버튼 클릭 (Button OnClick에 연결)
    /// </summary>
    public void OnCardDrawButtonClicked()
    {
        if (_cachedCardSystem == null)
        {
            Debug.LogError("[UIManager] _cachedCardSystem이 null입니다! PlayerCardSystem을 찾을 수 없습니다.");
            return;
        }
        
        Debug.Log($"[UIManager] 카드 뽑기 요청. Stacks: {_cachedCardSystem.CardDrawStacks.Value}");
        _cachedCardSystem.RPC_RequestDrawCards();
    }

    #endregion

    #region 카드 선택 UI

    /// <summary>
    /// 카드 선택 UI 표시 (PlayerCardSystem에서 호출)
    /// </summary>
    public void ShowCardSelection(string[] cardIds)
    {
        if (_cardSelectionPanel == null || _cardContainer == null)
        {
            Debug.LogError("[UIManager] 카드 선택 UI가 설정되지 않았습니다!");
            return;
        }

        // 카드 선택 상태 설정
        IsCardSelectionOpen = true;

        // 기존 카드 제거
        foreach (var card in _currentDisplayedCards)
        {
            Destroy(card);
        }
        _currentDisplayedCards.Clear();

        _cardSelectionPanel.SetActive(true);
        
        if (_cardSelectionCanvasGroup != null)
        {
            _cardSelectionCanvasGroup.alpha = 0f;
            _cardSelectionCanvasGroup.DOFade(1f, 0.2f);
        }

        Vector3 startPos = _cardDrawButton != null 
            ? _cardDrawButton.transform.position 
            : new Vector3(Screen.width / 2f, 100f, 0f);

        for (int i = 0; i < cardIds.Length && i < 3; i++)
        {
            CreateCardUI(cardIds[i], i, startPos);
        }
    }

    private void CreateCardUI(string cardId, int index, Vector3 startPos)
    {
        CardData cardData = CardConfig.Instance?.GetCardById(cardId);
        if (cardData == null)
        {
            Debug.LogError($"[UIManager] CardID '{cardId}'를 찾을 수 없습니다!");
            return;
        }

        GameObject prefab = GetCardPrefab(cardData.Rarity);
        if (prefab == null)
        {
            Debug.LogError($"[UIManager] {cardData.Rarity} 등급 Prefab이 설정되지 않았습니다!");
            return;
        }

        GameObject cardObj = Instantiate(prefab, _cardContainer);
        _currentDisplayedCards.Add(cardObj);

        SetupCardUI(cardObj, cardData, index);

        // 애니메이션
        RectTransform rect = cardObj.GetComponent<RectTransform>();
        rect.position = startPos;
        rect.localScale = Vector3.zero;

        Vector3 targetPos = GetCardTargetPosition(index);

        Sequence seq = DOTween.Sequence();
        seq.Append(rect.DOScale(1f, _cardScaleDuration).SetEase(Ease.OutBack).SetDelay(index * _cardDelay));
        seq.Join(rect.DOMove(targetPos, _cardMoveDuration).SetEase(Ease.OutQuad).SetDelay(index * _cardDelay));
    }

    private GameObject GetCardPrefab(CardRarity rarity)
    {
        var config = CardConfig.Instance;
        if (config == null) return null;
        
        return rarity switch
        {
            CardRarity.OneStar => config.OneStarCardPrefab,
            CardRarity.TwoStar => config.TwoStarCardPrefab,
            CardRarity.ThreeStar => config.ThreeStarCardPrefab,
            _ => null
        };
    }

    private void SetupCardUI(GameObject cardObj, CardData data, int index)
    {
        // 아이콘
        Image iconImage = cardObj.transform.Find("Icon")?.GetComponent<Image>();
        if (iconImage != null && data.Icon != null)
        {
            iconImage.sprite = data.Icon;
            iconImage.enabled = true;
        }

        // 이름
        TextMeshProUGUI nameText = cardObj.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
        if (nameText != null)
        {
            nameText.text = data.CardName;
        }

        // 설명
        TextMeshProUGUI descText = cardObj.transform.Find("Description")?.GetComponent<TextMeshProUGUI>();
        if (descText != null)
        {
            descText.text = data.Description;
        }

        // 선택 버튼
        Button selectButton = cardObj.transform.Find("SelectButton")?.GetComponent<Button>();
        if (selectButton != null)
        {
            int capturedIndex = index;
            selectButton.onClick.AddListener(() => OnCardSelected(capturedIndex));
        }
    }

    private Vector3 GetCardTargetPosition(int index)
    {
        float scaleFactor = _canvas != null ? _canvas.scaleFactor : 1f;
        float offset = (index - 1) * 360f * scaleFactor;
        return _cardContainer.position + Vector3.right * offset;
    }

    private void OnCardSelected(int index)
    {
        if (_cachedCardSystem == null)
        {
            var combat = PlayerUtils.GetLocalPlayerCombat();
            _cachedCardSystem = combat?.GetComponent<PlayerCardSystem>();
        }

        _cachedCardSystem?.RPC_SelectCard(index);
        HideCardSelection();
    }

    private void HideCardSelection()
    {
        IsCardSelectionOpen = false;
        
        if (_cardSelectionCanvasGroup != null)
        {
            _cardSelectionCanvasGroup.DOFade(0f, 0.2f).OnComplete(() => _cardSelectionPanel.SetActive(false));
        }
        else
        {
            _cardSelectionPanel.SetActive(false);
        }
    }

    #endregion

    #region 보유 능력 UI

    private void UpdateOwnedAbilityUI()
    {
        if (_abilityContainer == null || _abilityButtonPrefab == null) return;
        if (_cachedCardSystem == null) return;

        var config = CardConfig.Instance;
        if (config == null) return;

        // 변경 감지를 위한 간단한 체크 (성능 최적화)
        int currentCount = 0;
        foreach (var cardId in _cachedCardSystem.OwnedCardIds)
        {
            CardData cardData = config.GetCardById(cardId);
            if (cardData != null && IsSpecialAbilityCard(cardData))
            {
                currentCount++;
            }
        }

        // 개수가 같으면 업데이트 불필요
        if (currentCount == _currentAbilityButtons.Count) return;

        // 기존 버튼 제거
        foreach (var btn in _currentAbilityButtons)
        {
            Destroy(btn);
        }
        _currentAbilityButtons.Clear();

        // 보유한 특수 능력 카드만 표시
        foreach (var cardId in _cachedCardSystem.OwnedCardIds)
        {
            CardData cardData = config.GetCardById(cardId);
            if (cardData != null && IsSpecialAbilityCard(cardData))
            {
                CreateAbilityButton(cardData);
            }
        }
    }

    private bool IsSpecialAbilityCard(CardData card)
    {
        if (string.IsNullOrEmpty(card.AbilityID)) return false;
        return !card.AbilityID.Contains("Boost");
    }

    private void CreateAbilityButton(CardData card)
    {
        GameObject btnObj = Instantiate(_abilityButtonPrefab, _abilityContainer);
        _currentAbilityButtons.Add(btnObj);

        // 프리팹 자체가 Icon Image
        Image iconImage = btnObj.GetComponent<Image>();
        if (iconImage != null && card.Icon != null)
        {
            iconImage.sprite = card.Icon;
        }

        // 입력 차단 컴포넌트 추가
        UIInputBlocker blocker = btnObj.GetComponent<UIInputBlocker>();
        if (blocker == null) blocker = btnObj.AddComponent<UIInputBlocker>();
        // 기본값 true이므로 별도 설정 불필요

        // PointerDown/PointerUp으로 누르고 있는 동안만 팝업 표시
        EventTrigger trigger = btnObj.GetComponent<EventTrigger>();
        if (trigger == null) trigger = btnObj.AddComponent<EventTrigger>();

        // PointerDown - 팝업 표시
        EventTrigger.Entry pointerDown = new EventTrigger.Entry();
        pointerDown.eventID = EventTriggerType.PointerDown;
        pointerDown.callback.AddListener((_) => ShowAbilityDetail(card));
        trigger.triggers.Add(pointerDown);

        // PointerUp - 팝업 숨김
        EventTrigger.Entry pointerUp = new EventTrigger.Entry();
        pointerUp.eventID = EventTriggerType.PointerUp;
        pointerUp.callback.AddListener((_) => CloseAbilityDetail());
        trigger.triggers.Add(pointerUp);

        // PointerExit - 버튼 밖으로 나가도 숨김
        EventTrigger.Entry pointerExit = new EventTrigger.Entry();
        pointerExit.eventID = EventTriggerType.PointerExit;
        pointerExit.callback.AddListener((_) => CloseAbilityDetail());
        trigger.triggers.Add(pointerExit);
    }

    private void ShowAbilityDetail(CardData card)
    {
        CloseAbilityDetail();

        GameObject prefab = GetCardPrefab(card.Rarity);
        if (prefab == null) return;

        _abilityPreviewCard = Instantiate(prefab, _canvas.transform);
        
        RectTransform rect = _abilityPreviewCard.GetComponent<RectTransform>();
        
        // 피벗을 좌하단으로 설정 (클릭 위치가 카드의 왼쪽 아래가 됨)
        rect.pivot = Vector2.zero;
        
        // 클릭 위치에 배치
        rect.position = Input.mousePosition;
        
        // 초기 스케일 0
        rect.localScale = Vector3.zero;
        
        // 투명도 설정
        CanvasGroup cg = _abilityPreviewCard.GetComponent<CanvasGroup>();
        if (cg == null) cg = _abilityPreviewCard.AddComponent<CanvasGroup>();
        cg.alpha = _abilityPreviewAlpha;
        cg.blocksRaycasts = false;
        
        // 카드 내용 설정
        SetupCardUI(_abilityPreviewCard, card, 0);

        // 미리보기에서는 선택 버튼 숨기기
        Transform selectBtn = _abilityPreviewCard.transform.Find("SelectButton");
        if (selectBtn != null)
        {
            selectBtn.gameObject.SetActive(false);
        }

        // 등장 애니메이션 (Draw와 유사한 느낌)
        rect.DOScale(_abilityPreviewScale, 0.4f).SetEase(Ease.OutBack);
    }

    private void CloseAbilityDetail()
    {
        if (_abilityPreviewCard != null)
        {
            Destroy(_abilityPreviewCard);
            _abilityPreviewCard = null;
        }
    }

    #endregion

    #region 게임 결과

    public void ShowVictory(int killCount, float survivalTime) => _gameResultUI?.ShowVictory(killCount, survivalTime);
    public void ShowDefeat(int killCount, float survivalTime, int rank) => _gameResultUI?.ShowDefeat(killCount, survivalTime, rank);

    #endregion

    #region 오버헤드 UI

    /// <summary>
    /// 기존 플레이어 OverheadUI 등록 (Late Joiner용)
    /// </summary>
    private void RegisterExistingPlayersUI()
    {
        // 지연 실행 (NetworkObject 완전 스폰 대기)
        StartCoroutine(RegisterExistingPlayersUICoroutine());
    }

    private IEnumerator RegisterExistingPlayersUICoroutine()
    {
        // 1프레임 대기 (모든 NetworkObject 스폰 완료 대기)
        yield return null;

        PlayerController[] allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        int registeredCount = 0;

        foreach (PlayerController player in allPlayers)
        {
            if (player != null && player.NetworkObject != null && player.NetworkObject.IsSpawned)
            {
                // 이미 등록된 플레이어 생략
                if (!_overheadUIs.ContainsKey(player.transform))
                {
                    RegisterPlayerOverheadUI(player.transform);
                    registeredCount++;
                }
            }
        }

        if (registeredCount > 0)
        {
            Debug.Log($"[UIManager] Late Joiner - {registeredCount}개 플레이어 OverheadUI 등록 완료");
        }
    }

    public void RegisterPlayerOverheadUI(Transform playerTransform)
    {
        if (_overheadUIPrefab == null) { Debug.LogError("[UIManager] OverheadUI 프리팹이 설정되지 않았습니다!"); return; }
        if (_overheadUIs.ContainsKey(playerTransform)) { Debug.LogWarning($"[UIManager] {playerTransform.name}의 오버헤드 UI가 이미 등록되어 있습니다!"); return; }

        Transform container = _overheadUIContainer != null ? _overheadUIContainer : _canvas.transform;
        PlayerOverheadUI ui = Instantiate(_overheadUIPrefab, container);
        ui.SetTargetPlayer(playerTransform);
        _overheadUIs.Add(playerTransform, ui);
    }

    public void UnregisterPlayerOverheadUI(Transform playerTransform)
    {
        if (playerTransform == null) return;
        if (_overheadUIs.TryGetValue(playerTransform, out PlayerOverheadUI ui))
        {
            if (ui != null) Destroy(ui.gameObject);
            _overheadUIs.Remove(playerTransform);
        }
    }

    #endregion
}
