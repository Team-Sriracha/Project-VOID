using System;
using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Object;
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
    private NetworkedWeapon _cachedWeapon;
    private PlayerController _cachedController;
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
        // Late Joiner를 위해 기존 플레이어들의 OverheadUI 생성
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
        if (_cachedController == null) _cachedController = localCombat.GetComponent<PlayerController>();

        if (_attackText != null && _cachedWeapon != null && _cachedWeapon.CurrentWeaponData != null) _attackText.text = $"ATK: {_cachedWeapon.CurrentWeaponData.TotalDamage:F1}";
        if (_defenseText != null) _defenseText.text = "DEF: 0.0";
        if (_speedText != null && _cachedController != null) _speedText.text = $"SPD: {_cachedController.GetCurrentMoveSpeed():F1}";
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
