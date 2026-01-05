using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
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

    [Header("인벤토리 슬롯")]
    [SerializeField] private ItemSlotUI[] _itemSlots = new ItemSlotUI[5];

    [Header("게임 결과")]
    [SerializeField] private GameResultUI _gameResultUI;

    [Header("플레이어 오버헤드 UI")]
    [SerializeField] private PlayerOverheadUI _overheadUIPrefab;
    [SerializeField] private Transform _overheadUIContainer;

    #endregion

    #region Private Fields

    private Queue<KillLogEntry> _killLogPool = new Queue<KillLogEntry>();
    private PlayerInventory _cachedInventory;
    private Canvas _canvas;
    private Dictionary<Transform, PlayerOverheadUI> _overheadUIs = new Dictionary<Transform, PlayerOverheadUI>();

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
        // Why: Late Joiner를 위해 기존 플레이어들의 OverheadUI 생성
        RegisterExistingPlayersUI();
    }

    private void Update()
    {
        UpdateBattlefieldInfo();
        UpdatePlayerStats();
        UpdateInventoryUI();
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

        if (_attackText != null && weapon != null && weapon.CurrentWeaponData != null) _attackText.text = $"ATK: {weapon.CurrentWeaponData.TotalDamage:F1}";
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

    /// <summary>
    /// 기존에 스폰된 모든 플레이어의 OverheadUI를 등록합니다 (Late Joiner용).
    /// </summary>
    private void RegisterExistingPlayersUI()
    {
        // Why: 약간의 지연 후 실행 (NetworkObject가 완전히 스폰될 때까지 대기)
        StartCoroutine(RegisterExistingPlayersUICoroutine());
    }

    private IEnumerator RegisterExistingPlayersUICoroutine()
    {
        // Why: 1프레임 대기 (모든 NetworkObject가 스폰 완료될 때까지)
        yield return null;

        PlayerController[] allPlayers = FindObjectsByType<PlayerController>(FindObjectsSortMode.None);
        int registeredCount = 0;

        foreach (PlayerController player in allPlayers)
        {
            if (player != null && player.Object != null && player.Object.IsValid)
            {
                // Why: 이미 등록된 플레이어는 건너뛰기
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
