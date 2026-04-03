using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    protected static UIManager _instance;
    public static UIManager Instance => _instance;

    public Transform OverheadUIContainer => GetOrResolveOverheadUIContainer();
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
    [SerializeField] private GameObject _gameResultPanel;
    [SerializeField] private TextMeshProUGUI _gameResultText;
    [SerializeField] private TextMeshProUGUI _gameResultStatsText;
    [SerializeField] private Button _gameResultReturnToLobbyButton;

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

    [Header("아이템 픽업 UI")]
    [SerializeField] private GameObject _pickupPanel;
    [SerializeField] private Transform _entryContainer;
    [SerializeField] private ItemPickupEntry _entryPrefab;

    [Header("일시정지 시스템")]
    [SerializeField] private GameObject _pausePanel;
    [SerializeField] private Button _pauseButton; // 우측 상단 열기 버튼
    [SerializeField] private Button _pauseCloseButton; // 패널 내 닫기 버튼
    [SerializeField] private Button _optionsButton;
    [SerializeField] private Button _exitGameButton;
    [Header("일시정지 - 종료 팝업")]
    [SerializeField] private GameObject _exitPopup;
    [SerializeField] private Button _confirmExitButton;
    [SerializeField] private Button _cancelExitButton;
    [SerializeField] private string _lobbySceneName = "Lobby";

    [Header("스킬 쿨타임 UI")]
    [SerializeField] private Image _skillIcon;
    [SerializeField] private Image _skillCooldownFill;

    [Header("사운드")]
    [SerializeField] private AudioCue _uiClickAudioCue;
    [SerializeField] private AudioCue _pauseOpenAudioCue;
    [SerializeField] private AudioCue _pauseCloseAudioCue;
    [SerializeField] private AudioCue _victoryAudioCue;
    [SerializeField] private AudioCue _defeatAudioCue;
    [SerializeField] private AudioCue _tieAudioCue;
    [SerializeField] private AudioCue _cardDrawAudioCue;
    [SerializeField] private AudioCue _inputRejectedAudioCue;

    #endregion

    #region Private Fields

    private const string RUNTIME_OVERHEAD_CANVAS_NAME = "MobOverheadCanvas";
    private const string RUNTIME_OVERHEAD_CONTAINER_NAME = "OverheadUIContainer";
    private const int RUNTIME_OVERHEAD_SORTING_OFFSET = 1;

    private Queue<KillLogEntry> _killLogPool = new Queue<KillLogEntry>();
    private PlayerInventory _cachedInventory;
    private NetworkedWeapon _cachedWeapon;
    private NetworkedArmor _cachedArmor;
    private PlayerController _cachedController;
    private PlayerCardSystem _cachedCardSystem;
    private Canvas _canvas;
    private Dictionary<Transform, PlayerOverheadUI> _overheadUIs = new Dictionary<Transform, PlayerOverheadUI>();
    private List<GameObject> _currentDisplayedCards = new List<GameObject>();
    private List<CardData> _currentDisplayedCardData = new List<CardData>();
    private List<GameObject> _currentAbilityButtons = new List<GameObject>();
    private GameObject _abilityPreviewCard;
    private bool _isGameResultInitialized;
    private bool _isLoadingRankedMmr;
    private bool _hasLoadedRankedMmr;
    private bool _isResultVisible;
    private int _cachedRankedMmr;
    private int _currentResultKillCount;
    private int _currentResultRank = 1;
    private Transform _resolvedOverheadUIContainer;

    /// <summary>
    /// 카드 선택 UI가 열려있는지 여부 (읽기 전용)
    /// </summary>
    public bool IsCardSelectionOpen { get; private set; }

    private List<ItemPickupEntry> _activePickupEntries = new List<ItemPickupEntry>();
    private ItemPickupDetector _pickupDetector;

    private const int DEFAULT_RANKED_MMR = 0;
    private const int MIN_RANKED_MMR = 0;
    private const int MAX_RANKED_MMR = 5000;
    private const int MIN_RANK_SCORE_DELTA = -40;
    private const int MAX_RANK_SCORE_DELTA = 40;
    private const float RANK_PLACEMENT_WEIGHT = 20f;
    private const int RANK_KILL_WEIGHT = 2;
    #endregion

    #region Unity Lifecycle

    protected virtual void Awake()
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
        InitializePauseSystem();
        InitializeGameResultUI();
    }
    
    private void Start()
    {
        // Late Joiner를 위해 기존 플레이어들의 OverheadUI 생성
        RegisterExistingPlayersUI();
        _ = CacheLocalRankedMmrAsync();
    }

    protected virtual void Update()
    {
        UpdateBattlefieldInfo();
        UpdatePlayerStats();
        UpdateInventoryUI();
        UpdateCardDrawButton();
        UpdateOwnedAbilityUI();
        UpdatePickupUI();
        UpdatePauseInput();
        UpdateSkillCooldownUI();
    }
    
    protected virtual void OnDestroy()
    {
        if (_pickupDetector != null)
        {
            _pickupDetector.OnItemsChanged -= UpdatePickupPanel;
        }

        if (_gameResultReturnToLobbyButton != null)
        {
            _gameResultReturnToLobbyButton.onClick.RemoveListener(OnReturnToLobbyFromResult);
        }
    }

    #endregion

    #region Initialization

    private void InitializeItemSlots()
    {
        EnsureMainCanvasReference();
        if (_canvas == null) { Debug.LogError("[UIManager] Canvas를 찾을 수 없습니다!"); return; }

        for (int i = 0; i < _itemSlots.Length; i++)
        {
            if (_itemSlots[i] != null) _itemSlots[i].Initialize(i, _canvas);
        }
    }

    private void InitializeGameResultUI()
    {
        if (_isGameResultInitialized)
        {
            return;
        }

        if (_gameResultReturnToLobbyButton != null)
        {
            _gameResultReturnToLobbyButton.onClick.RemoveListener(OnReturnToLobbyFromResult);
            _gameResultReturnToLobbyButton.onClick.AddListener(OnReturnToLobbyFromResult);
        }

        _isGameResultInitialized = true;
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
            _attackText.text = $"공격력: {_cachedWeapon.CurrentTotalDamage:F0}{bonusText}";
        }

        // DEF 표시 (카드 보너스 포함)
        if (_defenseText != null)
        {
            float def = _cachedArmor != null ? _cachedArmor.CurrentDefense : 0f;
            float defBonus = cardSystem != null ? cardSystem.GetStatBonus(StatType.Defense) * 100f : 0f;
            string bonusText = defBonus > 0 ? $" (+{defBonus:F0}%)" : "";
            _defenseText.text = $"방어력: {def:F0}{bonusText}";
        }

        // SPD 표시
        if (_speedText != null && _cachedController != null)
        {
            _speedText.text = $"이동속도: {_cachedController.GetCurrentMoveSpeed():F0}";
        }

        // XP 표시
        if (_xpText != null)
        {
            _xpText.text = $"XP: {localCombat.CurrentXP.Value:F0} / {localCombat.XPToNextLevel:F0}";
        }
    }

    /// <summary>
    /// 스킬 쿨타임 UI 업데이트 (근접무기 스킬 전용)
    /// </summary>
    private void UpdateSkillCooldownUI()
    {
        if (ShouldSkipDefaultSkillUI())
        {
            return;
        }

        if (_skillIcon == null) return;

        // 무기 캐싱
        if (_cachedWeapon == null)
        {
            var combat = PlayerUtils.GetLocalPlayerCombat();
            _cachedWeapon = combat?.GetComponent<NetworkedWeapon>();
        }
        if (_cachedWeapon == null) return;

        // 근접무기 + 스킬이 있는지 확인
        if (_cachedWeapon.CurrentWeaponData is MeleeWeaponData meleeData && meleeData.HasSkill)
        {
            var skillData = meleeData.SkillData;
            
            // UI 표시
            if (!_skillIcon.gameObject.activeSelf)
                _skillIcon.gameObject.SetActive(true);

            // 아이콘 설정
            if (skillData.SkillIcon != null)
                _skillIcon.sprite = skillData.SkillIcon;

            // 쿨타임 오버레이 (줄어드는 방식: 쿨다운 중에는 덮이고, 준비되면 사라짐)
            float remaining = _cachedWeapon.SkillCooldownRemainingTime;
            // 쿨다운 중: fillAmount = 남은시간/전체시간 (1→0으로 줄어듦)
            // 준비 완료: fillAmount = 0 (오버레이 안 보임)
            float fillAmount = (remaining <= 0f) ? 0f : remaining / skillData.Cooldown;

            if (_skillCooldownFill != null)
                _skillCooldownFill.fillAmount = fillAmount;
        }
        else
        {
            // 스킬이 없으면 UI 숨김
            if (_skillIcon.gameObject.activeSelf)
                _skillIcon.gameObject.SetActive(false);
        }
    }

    private bool ShouldSkipDefaultSkillUI()
    {
        return this is MobileUIManager || MobileUIManager.IsForceMobile || Application.isMobilePlatform;
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

        if (_cachedCardSystem.CardDrawStacks.Value <= 0 || IsCardSelectionOpen)
        {
            AudioManager.Instance?.PlayUi(_inputRejectedAudioCue);
            return;
        }
        
        AudioManager.Instance?.PlayUi(_cardDrawAudioCue ?? _uiClickAudioCue);
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
        _currentDisplayedCardData.Clear();

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

        while (_currentDisplayedCardData.Count <= index)
        {
            _currentDisplayedCardData.Add(null);
        }

        _currentDisplayedCardData[index] = cardData;

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

        AudioManager.Instance?.PlayUi(GetCardSelectAudioCue(index));
        _cachedCardSystem?.RPC_SelectCard(index);
        HideCardSelection();
    }

    private AudioCue GetCardSelectAudioCue(int index)
    {
        if (index >= 0 && index < _currentDisplayedCardData.Count)
        {
            CardData cardData = _currentDisplayedCardData[index];
            if (cardData != null)
            {
                CardConfig config = CardConfig.Instance;
                if (config != null)
                {
                    AudioCue rarityCue = config.GetCardSelectAudioCue(cardData.Rarity);
                    if (rarityCue != null)
                    {
                        return rarityCue;
                    }
                }
            }
        }

        return _uiClickAudioCue;
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
        pointerDown.callback.AddListener((data) => {
            var pData = data as PointerEventData;
            if (pData != null) ShowAbilityDetail(card, pData.position);
        });
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

    private void ShowAbilityDetail(CardData card, Vector2 screenPos)
    {
        CloseAbilityDetail();

        GameObject prefab = GetCardPrefab(card.Rarity);
        if (prefab == null) return;

        _abilityPreviewCard = Instantiate(prefab, _canvas.transform);
        
        RectTransform rect = _abilityPreviewCard.GetComponent<RectTransform>();
        
        // [Smart Pivot] 터치 위치에 따라 피벗을 조정하여 화면 밖으로 나가지 않게 함
        // 화면 오른쪽이면 피벗을 오른쪽(1)으로 설정하여 왼쪽으로 커지게 함
        // 화면 위쪽이면 피벗을 위쪽(1)으로 설정하여 아래로 커지게 함
        float pivotX = (screenPos.x > Screen.width * 0.5f) ? 1.1f : -0.1f; // 손가락에 가리지 않게 약간의 여유(0.1)
        float pivotY = (screenPos.y > Screen.height * 0.5f) ? 1.1f : -0.1f;
        
        // Clamp to 0 and 1 for strict bounds if preferred, but slight offset helps visibility
        pivotX = (screenPos.x > Screen.width * 0.5f) ? 1f : 0f;
        pivotY = (screenPos.y > Screen.height * 0.5f) ? 1f : 0f;

        rect.pivot = new Vector2(pivotX, pivotY);
        
        // 클릭 위치에 배치
        rect.position = screenPos;
        
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

    /// <summary>
    /// 승리 결과를 표시합니다.
    /// </summary>
    public void ShowVictory(int killCount)
    {
        AudioManager.Instance?.PlayUi(_victoryAudioCue);
        ShowGameResult(killCount, 1);
    }

    /// <summary>
    /// 패배 결과를 표시합니다.
    /// </summary>
    public void ShowDefeat(int killCount, int rank)
    {
        AudioManager.Instance?.PlayUi(_defeatAudioCue);
        ShowGameResult(killCount, rank);
    }

    /// <summary>
    /// 무승부 결과를 표시합니다.
    /// </summary>
    public void ShowTie(int killCount, int rank = 1)
    {
        AudioManager.Instance?.PlayUi(_tieAudioCue);
        ShowGameResult(killCount, Mathf.Max(1, rank));
    }

    private void ShowGameResult(int killCount, int rank)
    {
        InitializeGameResultUI();

        if (_gameResultPanel == null)
        {
            Debug.LogWarning("[UIManager] 결과 패널 참조가 없어 결과 UI를 표시할 수 없습니다.");
            return;
        }

        _currentResultKillCount = Mathf.Max(0, killCount);
        _currentResultRank = Mathf.Max(1, rank);
        _isResultVisible = true;

        ApplyGameResultText();
        _gameResultPanel.SetActive(true);

        if (!_hasLoadedRankedMmr)
        {
            _ = CacheLocalRankedMmrAsync();
        }
    }

    private void ApplyGameResultText()
    {
        int delta = CalculateRankScoreDelta(_currentResultRank, _currentResultKillCount);
        int currentRankScore = Mathf.Clamp(_cachedRankedMmr + delta, MIN_RANKED_MMR, MAX_RANKED_MMR);

        if (_gameResultText != null)
        {
            _gameResultText.text = $"등수 {_currentResultRank}위";
        }

        if (_gameResultStatsText != null)
        {
            _gameResultStatsText.text = $"킬 수: {_currentResultKillCount}\n현재 랭크 점수: {currentRankScore} ({FormatSignedValue(delta)})";
        }
    }

    private int CalculateRankScoreDelta(int rank, int killCount)
    {
        if (GameStateManager.Instance == null || GameStateManager.Instance.CurrentGameMode != GameMode.Ranked)
        {
            return 0;
        }

        int participantCount = ResolveRankedParticipantCount();
        int safeParticipants = Mathf.Max(2, participantCount);
        int safeRank = Mathf.Clamp(rank, 1, safeParticipants);
        float expectedRank = (safeParticipants + 1) * 0.5f;
        float performanceScore = expectedRank - safeRank;
        int placementDelta = Mathf.RoundToInt(performanceScore * RANK_PLACEMENT_WEIGHT);
        int combatDelta = Mathf.Max(0, killCount) * RANK_KILL_WEIGHT;
        return Mathf.Clamp(placementDelta + combatDelta, MIN_RANK_SCORE_DELTA, MAX_RANK_SCORE_DELTA);
    }

    private int ResolveRankedParticipantCount()
    {
        if (GameStateManager.Instance == null)
        {
            return 2;
        }

        int targetPlayerCount = GameStateManager.Instance.TargetPlayerCount.Value;
        int connectedPlayers = GameStateManager.Instance.ConnectedPlayers.Value;
        return Mathf.Max(2, Mathf.Max(targetPlayerCount, connectedPlayers));
    }

    private async Task CacheLocalRankedMmrAsync()
    {
        if (_isLoadingRankedMmr)
        {
            return;
        }

        _isLoadingRankedMmr = true;

        try
        {
            if (!ServiceLocator.TryGet<IAuthService>(out IAuthService authService) ||
                authService == null ||
                !authService.IsSignedIn ||
                string.IsNullOrWhiteSpace(authService.UserId))
            {
                _cachedRankedMmr = DEFAULT_RANKED_MMR;
                _hasLoadedRankedMmr = true;
                RefreshVisibleGameResult();
                return;
            }

            if (!ServiceLocator.TryGet<IPlayerDataService>(out IPlayerDataService playerDataService) ||
                playerDataService == null)
            {
                _cachedRankedMmr = DEFAULT_RANKED_MMR;
                _hasLoadedRankedMmr = true;
                RefreshVisibleGameResult();
                return;
            }

            PlayerProfile profile = await playerDataService.GetPlayerProfileAsync(authService.UserId.Trim());
            _cachedRankedMmr = Mathf.Clamp(profile?.RankedMmr ?? DEFAULT_RANKED_MMR, MIN_RANKED_MMR, MAX_RANKED_MMR);
            _hasLoadedRankedMmr = true;
            RefreshVisibleGameResult();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[UIManager] 랭크 점수 조회 실패: {ex.Message}");
        }
        finally
        {
            _isLoadingRankedMmr = false;
        }
    }

    private void RefreshVisibleGameResult()
    {
        if (!_isResultVisible)
        {
            return;
        }

        ApplyGameResultText();
    }

    private static string FormatSignedValue(int value)
    {
        return value >= 0 ? $"+{value}" : value.ToString();
    }

    private void OnReturnToLobbyFromResult()
    {
        OnConfirmExit();
    }

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

        Transform container = OverheadUIContainer != null ? OverheadUIContainer : _canvas.transform;
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

    #region Overhead UI Root

    private Transform GetOrResolveOverheadUIContainer()
    {
        if (_resolvedOverheadUIContainer != null)
        {
            return _resolvedOverheadUIContainer;
        }

        EnsureMainCanvasReference();

        if (IsDedicatedOverheadContainer(_overheadUIContainer))
        {
            _resolvedOverheadUIContainer = _overheadUIContainer;
            return _resolvedOverheadUIContainer;
        }

        _resolvedOverheadUIContainer = GetOrCreateRuntimeOverheadUIContainer();
        return _resolvedOverheadUIContainer;
    }

    private bool IsDedicatedOverheadContainer(Transform container)
    {
        if (container == null)
        {
            return false;
        }

        Canvas parentCanvas = container.GetComponentInParent<Canvas>();
        if (parentCanvas == null)
        {
            return false;
        }

        if (_canvas == null)
        {
            return true;
        }

        return parentCanvas != _canvas;
    }

    private Transform GetOrCreateRuntimeOverheadUIContainer()
    {
        if (_canvas == null)
        {
            return _overheadUIContainer;
        }

        GameObject canvasObject = GameObject.Find(RUNTIME_OVERHEAD_CANVAS_NAME);
        Canvas overheadCanvas = canvasObject != null ? canvasObject.GetComponent<Canvas>() : null;

        if (overheadCanvas == null)
        {
            canvasObject = new GameObject(RUNTIME_OVERHEAD_CANVAS_NAME);
            overheadCanvas = canvasObject.AddComponent<Canvas>();
        }

        ConfigureRuntimeOverheadCanvas(overheadCanvas);

        Transform container = overheadCanvas.transform.Find(RUNTIME_OVERHEAD_CONTAINER_NAME);
        if (container == null)
        {
            GameObject containerObject = new GameObject(RUNTIME_OVERHEAD_CONTAINER_NAME, typeof(RectTransform));
            RectTransform containerRect = containerObject.GetComponent<RectTransform>();
            containerRect.SetParent(overheadCanvas.transform, false);
            containerRect.anchorMin = Vector2.zero;
            containerRect.anchorMax = Vector2.one;
            containerRect.offsetMin = Vector2.zero;
            containerRect.offsetMax = Vector2.zero;
            container = containerRect;
        }

        return container;
    }

    private void ConfigureRuntimeOverheadCanvas(Canvas overheadCanvas)
    {
        if (_canvas == null || overheadCanvas == null)
        {
            return;
        }

        overheadCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overheadCanvas.overrideSorting = true;
        overheadCanvas.sortingLayerID = _canvas.sortingLayerID;
        overheadCanvas.sortingOrder = _canvas.sortingOrder - RUNTIME_OVERHEAD_SORTING_OFFSET;

        CanvasScaler overheadScaler = overheadCanvas.GetComponent<CanvasScaler>();
        if (overheadScaler == null)
        {
            overheadScaler = overheadCanvas.gameObject.AddComponent<CanvasScaler>();
        }

        CanvasScaler mainScaler = _canvas.GetComponent<CanvasScaler>();
        if (mainScaler != null)
        {
            overheadScaler.uiScaleMode = mainScaler.uiScaleMode;
            overheadScaler.referenceResolution = mainScaler.referenceResolution;
            overheadScaler.matchWidthOrHeight = mainScaler.matchWidthOrHeight;
            overheadScaler.referencePixelsPerUnit = mainScaler.referencePixelsPerUnit;
            overheadScaler.scaleFactor = mainScaler.scaleFactor;
        }
        else
        {
            overheadScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            overheadScaler.referenceResolution = new Vector2(1920f, 1080f);
            overheadScaler.matchWidthOrHeight = 0.5f;
        }

        GraphicRaycaster overheadRaycaster = overheadCanvas.GetComponent<GraphicRaycaster>();
        if (overheadRaycaster != null)
        {
            overheadRaycaster.enabled = false;
        }
    }

    private void EnsureMainCanvasReference()
    {
        if (_canvas != null)
        {
            return;
        }

        _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null)
        {
            _canvas = FindFirstObjectByType<Canvas>();
        }
    }

    #endregion
    #region 아이템 픽업 UI

    private void UpdatePickupUI()
    {
        // 카드 선택 UI가 열려있으면 피킹 패널 숨김
        if (IsCardSelectionOpen)
        {
            if (_pickupPanel != null && _pickupPanel.activeSelf)
                _pickupPanel.SetActive(false);
            return;
        }

        // 아직 초기화 안 됐으면 로컬 플레이어 찾기 시도
        if (_pickupDetector == null)
        {
            var inventory = PlayerUtils.GetLocalPlayerInventory();
            if (inventory != null)
            {
                _pickupDetector = inventory.GetComponent<ItemPickupDetector>();

                if (_pickupDetector != null)
                {
                    _pickupDetector.OnItemsChanged += UpdatePickupPanel;
                    // 이미 감지된 아이템이 있다면 즉시 업데이트
                    UpdatePickupPanel(_pickupDetector.GetNearbyItems());
                }
            }
        }
    }

    private void UpdatePickupPanel(List<NetworkedItem> nearbyItems)
    {
        // 기존 엔트리 제거
        foreach (var entry in _activePickupEntries)
        {
            if (entry != null) Destroy(entry.gameObject);
        }
        _activePickupEntries.Clear();

        if (nearbyItems == null || nearbyItems.Count == 0)
        {
            if (_pickupPanel != null)
                _pickupPanel.SetActive(false);
            return;
        }

        // 새 엔트리 생성
        if (_pickupPanel != null)
            _pickupPanel.SetActive(true);

        foreach (var item in nearbyItems)
        {
            if (item == null) continue;
            
            ItemData itemData = item.GetItemData();
            if (itemData == null) continue;

            NetworkedItem currentEquipped = GetCurrentEquippedItem(itemData.ItemType);

            if (_entryPrefab != null && _entryContainer != null)
            {
                ItemPickupEntry entry = Instantiate(_entryPrefab, _entryContainer);
                entry.Initialize(item, currentEquipped);
                _activePickupEntries.Add(entry);
            }
        }
    }

    private NetworkedItem GetCurrentEquippedItem(ItemType itemType)
    {
        if (_cachedInventory == null) return null;
        return _cachedInventory.GetEquippedItem(itemType);
    }

    #endregion

    #region 일시정지 시스템

    // Start나 Awake에서 이 메서드를 호출하거나, 리스너를 직접 Awake에 추가해도 됩니다.
    // 하지만 UIManager는 상속 구조이므로 InitializePauseSystem()을 Awake에서 호출하도록 수정합니다.
    private void InitializePauseSystem()
    {
        if (_pauseButton != null) _pauseButton.onClick.AddListener(OpenPauseMenu);
        if (_pauseCloseButton != null) _pauseCloseButton.onClick.AddListener(ClosePauseMenu);
        
        if (_optionsButton != null) _optionsButton.onClick.AddListener(OnOptionsClicked);
        if (_exitGameButton != null) _exitGameButton.onClick.AddListener(OnExitGameClicked);

        if (_confirmExitButton != null) _confirmExitButton.onClick.AddListener(OnConfirmExit);
        if (_cancelExitButton != null) _cancelExitButton.onClick.AddListener(OnCancelExit);

        if (_pausePanel != null) _pausePanel.SetActive(false);
        if (_exitPopup != null) _exitPopup.SetActive(false);
    }

    private void UpdatePauseInput()
    {
        // PC: ESC 키로 일시정지 토글
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            TogglePauseMenu();
        }
    }

    public void TogglePauseMenu()
    {
        if (_pausePanel != null)
        {
            if (_pausePanel.activeSelf) ClosePauseMenu();
            else OpenPauseMenu();
        }
    }

    public void OpenPauseMenu()
    {
        if (_pausePanel != null) _pausePanel.SetActive(true);
        AudioManager.Instance?.PlayUi(_pauseOpenAudioCue ?? _uiClickAudioCue);
        // 멀티플레이어 게임이므로 Time.timeScale 건드리지 않음
    }

    public void ClosePauseMenu()
    {
        if (_pausePanel != null) _pausePanel.SetActive(false);
        if (_exitPopup != null) _exitPopup.SetActive(false);
        AudioManager.Instance?.PlayUi(_pauseCloseAudioCue ?? _uiClickAudioCue);
    }

    private void OnOptionsClicked()
    {
        Debug.Log("Options clicked - To be implemented");
    }

    private void OnExitGameClicked()
    {
        AudioManager.Instance?.PlayUi(_uiClickAudioCue);
        if (_exitPopup != null)
        {
            if (_pausePanel != null) _pausePanel.SetActive(false); // [Modify] 패널 숨김
            _exitPopup.SetActive(true);
        }
        else
        {
            OnConfirmExit();
        }
    }

    private void OnConfirmExit()
    {
        AudioManager.Instance?.PlayUi(_uiClickAudioCue);
        // 최우선: MatchmakingManager를 통해 정상적인 퇴장 절차 수행 (Lobby 세션 나가기, 네트워크 종료 등)
        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.CancelAndReturnToLobby();
            return;
        }

        // Fallback: 매니저가 없을 경우 (예: 에디터 테스트) 수동 처리
        if (InstanceFinder.IsServerStarted)
        {
             InstanceFinder.ServerManager.StopConnection(true);
        }
        else if (InstanceFinder.ClientManager != null)
        {
            InstanceFinder.ClientManager.StopConnection();
        }
        UnityEngine.SceneManagement.SceneManager.LoadScene(_lobbySceneName);
    }

    private void OnCancelExit()
    {
        AudioManager.Instance?.PlayUi(_uiClickAudioCue);
        if (_exitPopup != null) _exitPopup.SetActive(false);
        if (_pausePanel != null) _pausePanel.SetActive(true); // [Modify] 패널 다시 표시
    }

    #endregion
}
