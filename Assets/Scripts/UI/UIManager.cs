using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 모든 게임 UI를 통합 관리하는 싱글톤
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

    #endregion

    #region Private Fields

    private Queue<KillLogEntry> _killLogPool = new Queue<KillLogEntry>();
    private PlayerInventory _cachedInventory;
    private Canvas _canvas;

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
        }

        // Why: ItemSlotUI 초기화
        InitializeItemSlots();
    }

    private void InitializeItemSlots()
    {
        _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null)
        {
            _canvas = FindFirstObjectByType<Canvas>();
        }

        if (_canvas == null)
        {
            Debug.LogError("[UIManager] Canvas를 찾을 수 없습니다!");
            return;
        }

        // Why: 각 슬롯에 Canvas와 인덱스 설정
        for (int i = 0; i < _itemSlots.Length; i++)
        {
            if (_itemSlots[i] != null)
            {
                _itemSlots[i].Initialize(i, _canvas);
            }
        }
    }

    private void Update()
    {
        UpdateBattlefieldInfo();
        UpdatePlayerStats();
        UpdateInventoryUI();
    }

    #endregion

    #region 전장 정보

    private void UpdateBattlefieldInfo()
    {
        if (GameStateManager.Instance == null) return;

        if (_alivePlayersText != null)
        {
            _alivePlayersText.text = $"Alive: {GameStateManager.Instance.AlivePlayers}";
        }

        if (_currentPhaseText != null)
        {
            _currentPhaseText.text = $"Phase {GameStateManager.Instance.CurrentPhase}";
        }

        if (_gameTimeText != null)
        {
            float time = GameStateManager.Instance.RemainingTime;
            int minutes = Mathf.FloorToInt(time / 60f);
            int seconds = Mathf.FloorToInt(time % 60f);
            _gameTimeText.text = $"{minutes:00}:{seconds:00}";
        }

        if (_killCountText != null)
        {
            PlayerCombat localCombat = PlayerUtils.GetLocalPlayerCombat();
            if (localCombat != null)
            {
                _killCountText.text = $"Kills: {localCombat.KillCount}";
            }
        }
    }

    #endregion

    #region 킬로그

    public void AddKillLog(string killerName, string victimName)
    {
        KillLogEntry entry = GetKillLogEntry();
        entry.Initialize(killerName, victimName);
    }

    private KillLogEntry GetKillLogEntry()
    {
        if (_killLogPool.Count > 0)
        {
            KillLogEntry entry = _killLogPool.Dequeue();
            entry.gameObject.SetActive(true);
            return entry;
        }

        return Instantiate(_killLogPrefab, _killLogContainer);
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

        if (_attackText != null && weapon != null && weapon.CurrentWeaponData != null)
        {
            _attackText.text = $"ATK: {weapon.CurrentWeaponData.Damage:F1}";
        }

        if (_defenseText != null)
        {
            _defenseText.text = "DEF: 0.0";
        }

        if (_speedText != null && controller != null)
        {
            _speedText.text = $"SPD: {controller.GetCurrentMoveSpeed():F1}";
        }
    }

    #endregion

    #region 인벤토리 UI

    private void UpdateInventoryUI()
    {
        if (_cachedInventory == null)
        {
            _cachedInventory = PlayerUtils.GetLocalPlayerInventory();
        }

        if (_cachedInventory == null || _itemSlots == null) return;

        for (int i = 0; i < _itemSlots.Length && i < 5; i++)
        {
            if (_itemSlots[i] == null) continue;

            Fusion.NetworkId itemId = _cachedInventory.ItemSlots[i];

            if (itemId == default)
            {
                _itemSlots[i].SetItem(null);
            }
            else
            {
                if (_cachedInventory.Runner != null && _cachedInventory.Runner.TryFindObject(itemId, out Fusion.NetworkObject netObj))
                {
                    NetworkedItem networkedItem = netObj.GetComponent<NetworkedItem>();
                    if (networkedItem != null)
                    {
                        ItemData itemData = networkedItem.GetItemData();
                        _itemSlots[i].SetItem(itemData);
                    }
                }
            }
        }
    }

    #endregion

    #region 게임 결과

    /// <summary>
    /// 승리 UI를 표시합니다.
    /// </summary>
    public void ShowVictory(int killCount, float survivalTime)
    {
        if (_gameResultUI != null)
        {
            _gameResultUI.ShowVictory(killCount, survivalTime);
        }
    }

    /// <summary>
    /// 패배 UI를 표시합니다.
    /// </summary>
    public void ShowDefeat(int killCount, float survivalTime, int rank)
    {
        if (_gameResultUI != null)
        {
            _gameResultUI.ShowDefeat(killCount, survivalTime, rank);
        }
    }

    #endregion
}
