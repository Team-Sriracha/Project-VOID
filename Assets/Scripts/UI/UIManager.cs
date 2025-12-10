using System;
using System.Collections;
using System.Collections.Generic;
using ProjectVoid.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 모든 게임 UI를 통합 관리하는 싱글톤
/// 로비에서 이동된 매치메이킹 UI 로직을 포함합니다.
/// </summary>
public class UIManager : MonoBehaviour
{
    #region Singleton

    private static UIManager _instance;
    public static UIManager Instance => _instance;

    #endregion

    #region Serialized Fields

    [Header("매치메이킹")]
    [SerializeField] private GameObject _matchmakingPanel;
    [SerializeField] private TMP_Text _matchmakingStatusText;
    [SerializeField] private TMP_Text _matchmakingPlayerCountText;
    [SerializeField] private TMP_Text _roomCodeDisplayText;
    [SerializeField] private Button _matchmakingCancelButton;
    private bool _isCustomMode;

    [Header("로딩 화면")]
    [SerializeField] private GameObject _loadingPanel;
    [SerializeField] private TextMeshProUGUI _loadingText;
    [SerializeField] private Slider _loadingProgressBar;
    [SerializeField] private TextMeshProUGUI _loadingTipText;

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

    [Header("인벤토리 슬롯")]
    [SerializeField] private ItemSlotUI[] _itemSlots = new ItemSlotUI[5];

    [Header("게임 결과")]
    [SerializeField] private GameResultUI _gameResultUI;

    [Header("플레이어 오버헤드 UI")]
    [SerializeField] private PlayerOverheadUI _overheadUIPrefab;
    [SerializeField] private Transform _overheadUIContainer;

    #endregion

    #region Private Fields

    private MatchmakingManager _matchmakingManager;

    private Queue<KillLogEntry> _killLogPool = new Queue<KillLogEntry>();
    private PlayerInventory _cachedInventory;
    private Canvas _canvas;
    private Dictionary<Transform, PlayerOverheadUI> _overheadUIs = new Dictionary<Transform, PlayerOverheadUI>();
    
    private bool _isLoading;
    private Coroutine _loadingCoroutine;
    private CanvasGroup _loadingCanvasGroup;

    private readonly string[] _loadingTips = new string[]
    {
        "서버에서 맵을 생성하고 있습니다...",
        "아이템을 배치하고 있습니다...",
        "몬스터를 스폰하고 있습니다...",
        "게임 준비 중..."
    };

    #endregion

    #region Events

    public event Action OnMapLoadingComplete;

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

        InitializeLoadingPanel();
        InitializeItemSlots();
    }
    
    private void Start()
    {
        FindMatchmakingManager();
        RegisterMatchmakingEventHandlers();

        if (_matchmakingManager != null && _matchmakingManager.IsMatchmaking)
        {
            ShowMatchmakingPanel();
        }
    }

    private void OnDestroy()
    {
        UnregisterMatchmakingEventHandlers();
    }

    public bool IsMatchmakingPanelActive => _matchmakingPanel != null && _matchmakingPanel.activeSelf;

    private void Update()
    {
        if (_matchmakingPanel != null && _matchmakingPanel.activeSelf)
        {
            // 매치메이킹 중일 때 지속적으로 UI 갱신 (폴링)
            if (_matchmakingManager != null)
            {
                UpdateMatchmakingStatus(_matchmakingManager.StatusMessage);
                OnPlayerCountChanged(_matchmakingManager.CurrentPlayers, _matchmakingManager.MaxPlayers);
                if (_isCustomMode) UpdateRoomCodeDisplay(_matchmakingManager.RoomCode);
            }
            return;
        }

        UpdateBattlefieldInfo();
        UpdatePlayerStats();
        UpdateInventoryUI();
    }

    #endregion
    
    #region Matchmaking Handlers

    private void FindMatchmakingManager()
    {
        if (_matchmakingManager == null) _matchmakingManager = MatchmakingManager.Instance;
        if (_matchmakingManager == null) _matchmakingManager = FindFirstObjectByType<MatchmakingManager>();
        if (_matchmakingManager == null) Debug.LogError("[UIManager] MatchmakingManager not found!");
    }

    private void RegisterMatchmakingEventHandlers()
    {
        _matchmakingCancelButton?.onClick.AddListener(OnCancelMatchmakingClicked);

        if (_matchmakingManager != null)
        {
            _matchmakingManager.OnMatchmakingUIUpdate += OnMatchmakingUpdate;
            _matchmakingManager.OnCustomRoomCreated += OnCustomRoomCreated;
            _matchmakingManager.OnRoomJoined += OnRoomJoined;
            _matchmakingManager.OnMatchmakingFailed += OnMatchmakingFailed;
            _matchmakingManager.OnMatchmakingCancelled += OnMatchmakingCancelled;
            _matchmakingManager.OnPlayerCountChanged += OnPlayerCountChanged;
            _matchmakingManager.OnMatchmakingSuccess += OnMatchmakingSuccess;
        }
    }

    private void UnregisterMatchmakingEventHandlers()
    {
        if (_matchmakingManager != null)
        {
            _matchmakingManager.OnMatchmakingUIUpdate -= OnMatchmakingUpdate;
            _matchmakingManager.OnCustomRoomCreated -= OnCustomRoomCreated;
            _matchmakingManager.OnRoomJoined -= OnRoomJoined;
            _matchmakingManager.OnMatchmakingFailed -= OnMatchmakingFailed;
            _matchmakingManager.OnMatchmakingCancelled -= OnMatchmakingCancelled;
            _matchmakingManager.OnPlayerCountChanged -= OnPlayerCountChanged;
            _matchmakingManager.OnMatchmakingSuccess -= OnMatchmakingSuccess;
        }
    }

    public void ShowMatchmakingPanel()
    {
        if (_matchmakingPanel == null || _matchmakingManager == null) return;
        
        _isCustomMode = _matchmakingManager.IsCustomGame;
        SetupMatchmakingPanel(_isCustomMode);
        
        OnPlayerCountChanged(_matchmakingManager.CurrentPlayers, _matchmakingManager.MaxPlayers);
        OnMatchmakingUpdate(_matchmakingManager.StatusMessage);
        if(_isCustomMode) OnRoomJoined(_matchmakingManager.RoomCode);

        _matchmakingPanel.SetActive(true);
    }

    private void HideMatchmakingPanel()
    {
        if (_matchmakingPanel != null) _matchmakingPanel.SetActive(false);
    }
    
    private void OnMatchmakingSuccess()
    {
        HideMatchmakingPanel();
        ShowLoadingAndWaitForMap();
    }

    private void OnCancelMatchmakingClicked()
    {
        _matchmakingManager?.CancelMatchmaking();
    }

    private void UpdateMatchmakingStatus(string status)
    {
        if (_matchmakingStatusText != null) _matchmakingStatusText.text = status;
    }

    private void UpdateRoomCodeDisplay(string roomCode)
    {
        Debug.Log($"[UIManager] UpdateRoomCodeDisplay called - roomCode: '{roomCode}', _isCustomMode: {_isCustomMode}, _roomCodeDisplayText != null: {_roomCodeDisplayText != null}");

        if (_roomCodeDisplayText != null && _isCustomMode)
        {
            // Why: 방 코드를 ABC123 형식으로 표시 (특수문자 없음)
            string formattedCode = RoomCodeGenerator.Format(roomCode);
            _roomCodeDisplayText.text = $"방 코드 : {formattedCode}";
            Debug.Log($"[UIManager] Room code displayed - Original: '{roomCode}', Formatted: '{formattedCode}'");
        }
        else
        {
            if (_roomCodeDisplayText == null)
                Debug.LogWarning("[UIManager] _roomCodeDisplayText is null!");
            if (!_isCustomMode)
                Debug.LogWarning("[UIManager] _isCustomMode is false!");
        }
    }
    
    private void SetupMatchmakingPanel(bool showRoomCode)
    {
        if (_roomCodeDisplayText != null)
            _roomCodeDisplayText.gameObject.SetActive(showRoomCode);
    }

    private void OnMatchmakingUpdate(string status) => UpdateMatchmakingStatus(status);

    private void OnCustomRoomCreated(string roomCode)
    {
        Debug.Log($"[UIManager] OnCustomRoomCreated called with roomCode: '{roomCode}'");
        UpdateRoomCodeDisplay(roomCode);
        UpdateMatchmakingStatus("플레이어 대기 중...");
    }

    private void OnRoomJoined(string roomCode)
    {
        UpdateRoomCodeDisplay(roomCode);
        UpdateMatchmakingStatus("게임 시작 대기 중...");
    }

    private void OnMatchmakingFailed(string errorMessage)
    {
        Debug.LogError($"[UIManager] Matchmaking failed: {errorMessage}");
        UpdateMatchmakingStatus($"오류: {errorMessage}");
        SceneManager.LoadScene("Lobby");
    }

    private void OnMatchmakingCancelled()
    {
        SceneManager.LoadScene("Lobby");
    }

    private void OnPlayerCountChanged(int current, int max)
    {
        if (_matchmakingPlayerCountText != null)
        {
            _matchmakingPlayerCountText.text = $"{current}/{max}";
        }
    }

    #endregion

    #region Initialization

    private void InitializeLoadingPanel()
    {
        if (_loadingPanel != null)
        {
            _loadingCanvasGroup = _loadingPanel.GetComponent<CanvasGroup>();
            if (_loadingCanvasGroup == null) _loadingCanvasGroup = _loadingPanel.AddComponent<CanvasGroup>();
            _loadingPanel.SetActive(false);
        }
    }

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
        if (GameStateManager.Instance == null || GameStateManager.Instance.Object == null || !GameStateManager.Instance.Object.IsValid) return;

        if (_alivePlayersText != null) _alivePlayersText.text = $"{GameStateManager.Instance.AlivePlayers}";
        if (_currentPhaseText != null) _currentPhaseText.text = $"{GameStateManager.Instance.CurrentPhase}";

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
            if (localCombat != null && localCombat.Object != null && localCombat.Object.IsValid)
            {
                _killCountText.text = $"{localCombat.KillCount}";
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

        NetworkedWeapon weapon = localCombat.GetComponent<NetworkedWeapon>();
        PlayerController controller = localCombat.GetComponent<PlayerController>();

        if (_attackText != null && weapon != null && weapon.CurrentWeaponData != null) _attackText.text = $"ATK: {weapon.CurrentWeaponData.Damage:F1}";
        if (_defenseText != null) _defenseText.text = "DEF: 0.0";
        if (_speedText != null && controller != null) _speedText.text = $"SPD: {controller.GetCurrentMoveSpeed():F1}";
    }

    #endregion

    #region 인벤토리 UI

    private void UpdateInventoryUI()
    {
        if (_cachedInventory == null) _cachedInventory = PlayerUtils.GetLocalPlayerInventory();
        if (_cachedInventory == null || _itemSlots == null || _cachedInventory.Object == null || !_cachedInventory.Object.IsValid) return;

        for (int i = 0; i < _itemSlots.Length && i < 5; i++)
        {
            if (_itemSlots[i] == null) continue;
            
            _itemSlots[i].SetItem(null);
            Fusion.NetworkId itemId = _cachedInventory.ItemSlots[i];

            if (itemId != default && _cachedInventory.Runner != null && _cachedInventory.Runner.TryFindObject(itemId, out Fusion.NetworkObject netObj))
            {
                if (netObj.TryGetComponent<NetworkedItem>(out var networkedItem))
                {
                    _itemSlots[i].SetItem(networkedItem.GetItemData());
                }
            }
        }
    }

    #endregion

    #region 게임 결과

    public void ShowVictory(int killCount, float survivalTime) => _gameResultUI?.ShowVictory(killCount, survivalTime);
    public void ShowDefeat(int killCount, float survivalTime, int rank) => _gameResultUI?.ShowDefeat(killCount, survivalTime, rank);

    #endregion

    #region 오버헤드 UI

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

    #region 로딩 화면

    public void ShowLoadingAndWaitForMap()
    {
        if (_isLoading) { Debug.LogWarning("[UIManager] 이미 로딩 중입니다."); return; }
        if (_loadingPanel == null) { Debug.LogWarning("[UIManager] 로딩 패널이 설정되지 않았습니다."); return; }

        _isLoading = true;
        _loadingPanel.SetActive(true);
        if (_loadingCanvasGroup != null) _loadingCanvasGroup.alpha = 1f;

        UpdateLoadingProgress(0f);
        UpdateLoadingText("서버 연결 중...");
        UpdateLoadingTip(0);

        if (_loadingCoroutine != null) StopCoroutine(_loadingCoroutine);
        _loadingCoroutine = StartCoroutine(WaitForMapReadyCoroutine());
    }

    public void HideLoadingScreen()
    {
        _isLoading = false;
        if (_loadingCoroutine != null) { StopCoroutine(_loadingCoroutine); _loadingCoroutine = null; }
        if (_loadingPanel != null) _loadingPanel.SetActive(false);
    }

    private IEnumerator WaitForMapReadyCoroutine()
    {
        NetworkMapManager mapManager = null;
        float progress = 0f;
        float tipChangeInterval = 3f, lastTipChangeTime = Time.time;
        int tipIndex = 0;
        float checkInterval = 0.2f, minDisplayTime = 1.5f, startTime = Time.time;

        UpdateLoadingProgress(0.1f);
        UpdateLoadingText("맵 매니저 대기 중...");
        
        float maxWaitTime = 60f, waitTime = 0f;
        while (mapManager == null && waitTime < maxWaitTime)
        {
            var managers = FindObjectsByType<NetworkMapManager>(FindObjectsSortMode.None);
            foreach (var mgr in managers)
            {
                if (mgr.Runner != null && (mgr.Runner.IsClient || mgr.Runner.IsSharedModeMasterClient) && mgr.Object.IsValid)
                {
                    mapManager = mgr;
                    break;
                }
            }
            if (mapManager == null) { waitTime += checkInterval; yield return new WaitForSeconds(checkInterval); }
        }

        if (mapManager == null)
        {
            Debug.LogError("[UIManager] NetworkMapManager를 찾을 수 없습니다! 로딩 강제 완료");
            FinishLoading();
            yield break;
        }

        UpdateLoadingText("맵 생성 중...");

        while (!mapManager.IsMapReady || !mapManager.IsReady())
        {
            progress = Mathf.Lerp(progress, 0.85f, Time.deltaTime * 0.5f);
            UpdateLoadingProgress(0.2f + progress * 0.7f);

            if (Time.time - lastTipChangeTime > tipChangeInterval)
            {
                tipIndex = (tipIndex + 1) % _loadingTips.Length;
                UpdateLoadingTip(tipIndex);
                lastTipChangeTime = Time.time;
            }
            yield return new WaitForSeconds(checkInterval);
        }

        UpdateLoadingText("게임 시작!");
        UpdateLoadingProgress(1f);

        float elapsedTime = Time.time - startTime;
        if (elapsedTime < minDisplayTime) yield return new WaitForSeconds(minDisplayTime - elapsedTime);

        FinishLoading();
    }

    private void FinishLoading()
    {
        OnMapLoadingComplete?.Invoke();
        if (_loadingCoroutine != null) StopCoroutine(_loadingCoroutine);
        _loadingCoroutine = StartCoroutine(FadeOutLoadingScreen());
    }

    private IEnumerator FadeOutLoadingScreen()
    {
        float fadeOutDuration = 0.5f;
        if (_loadingCanvasGroup == null)
        {
            if (_loadingPanel != null) _loadingPanel.SetActive(false);
            _isLoading = false;
            yield break;
        }

        float elapsed = 0f, startAlpha = _loadingCanvasGroup.alpha;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            _loadingCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / fadeOutDuration);
            yield return null;
        }

        _loadingCanvasGroup.alpha = 0f;
        if (_loadingPanel != null) _loadingPanel.SetActive(false);
        _isLoading = false;
        _loadingCoroutine = null;
    }

    private void UpdateLoadingProgress(float progress)
    {
        if (_loadingProgressBar != null) _loadingProgressBar.value = Mathf.Clamp01(progress);
    }

    private void UpdateLoadingText(string text)
    {
        if (_loadingText != null) _loadingText.text = text;
    }

    private void UpdateLoadingTip(int index)
    {
        if (_loadingTipText != null && _loadingTips != null && _loadingTips.Length > 0)
        {
            _loadingTipText.text = _loadingTips[index % _loadingTips.Length];
        }
    }

    #endregion
}