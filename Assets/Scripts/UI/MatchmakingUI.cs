using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Fusion;
using System.Linq;

/// <summary>
/// Matchmaking 씬에서 매칭 상태를 표시하는 UI
/// 플레이어 수, 대기 상태, 방 코드 등을 표시합니다.
/// </summary>
public class MatchmakingUI : MonoBehaviour
{
    #region Serialized Fields

    [Header("UI Elements")]
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private TMP_Text _playerCountText;
    [SerializeField] private TMP_Text _roomCodeText;
    [SerializeField] private Button _cancelButton;

    [Header("Optional")]
    [SerializeField] private GameObject _loadingIndicator;

    #endregion

    #region Private Fields

    private NetworkRunner _runner;
    private MatchmakingManager _matchmakingManager;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        Debug.Log("[MatchmakingUI] Start - Matchmaking 씬에서 대기 UI 초기화");

        // Why: 서버 모드(에디터/빌드)인 경우 UI 비활성화 및 로비 복귀 방지
        var runner = FindFirstObjectByType<NetworkRunner>();
        if (runner != null && runner.GameMode == Fusion.GameMode.Server)
        {
            Debug.Log("[MatchmakingUI] Server mode detected. Disabling UI logic.");
            // UI 요소 끄기
            if (_statusText) _statusText.gameObject.SetActive(false);
            if (_playerCountText) _playerCountText.gameObject.SetActive(false);
            if (_roomCodeText) _roomCodeText.gameObject.SetActive(false);
            if (_cancelButton) _cancelButton.gameObject.SetActive(false);
            this.enabled = false;
            return;
        }

        // Why: 새 씬 생성 시 EventSystem 누락 방지
        if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            Debug.Log("[MatchmakingUI] EventSystem missing - Creating one automatically.");
            var eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // MatchmakingManager 찾기 (DontDestroyOnLoad)
        _matchmakingManager = MatchmakingManager.Instance;
        if (_matchmakingManager == null)
        {
            _matchmakingManager = FindFirstObjectByType<MatchmakingManager>();
        }

        if (_matchmakingManager == null)
        {
            Debug.LogWarning("[MatchmakingUI] MatchmakingManager not found! Returning to Lobby.");
            UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
            return;
        }

        // 이벤트 등록
        _matchmakingManager.OnMatchmakingUIUpdate += OnStatusUpdate;
        _matchmakingManager.OnPlayerCountChanged += OnPlayerCountChanged;
        _matchmakingManager.OnCustomRoomCreated += OnRoomCodeReceived;
        _matchmakingManager.OnRoomJoined += OnRoomCodeReceived;
        _matchmakingManager.OnMatchmakingCancelled += OnMatchmakingCancelled;

        // 취소 버튼 이벤트
        if (_cancelButton != null)
        {
            _cancelButton.onClick.AddListener(OnCancelClicked);
        }

        // 초기값 설정
        UpdateUI();

        // Why: Matchmaking 씬 로드 완료 → 실제 서버 접속 시작
        Debug.Log("[MatchmakingUI] Executing prepared matchmaking...");
        _matchmakingManager.ExecuteMatchmaking();
    }

    private void Update()
    {
        // Runner가 없으면 다시 찾기
        if (_runner == null || !_runner.IsRunning)
        {
            FindNetworkRunner();
        }

        // 실시간 플레이어 수 갱신
        UpdatePlayerCountFromRunner();

        // Why: 커스텀 모드일 때 방 코드 갱신 (Session Properties에서 읽어옴)
        UpdateRoomCodeDisplay();

        // Why: 서버 접속 중에는 취소 버튼 비활성화 (불완전한 취소로 인한 씬 중복 방지)
        UpdateCancelButtonState();

        // Loading Indicator 회전 애니메이션
        if (_loadingIndicator != null && _loadingIndicator.activeSelf)
        {
            _loadingIndicator.transform.Rotate(0, 0, -200f * Time.deltaTime);
        }
    }

    private void OnDestroy()
    {
        if (_matchmakingManager != null)
        {
            _matchmakingManager.OnMatchmakingUIUpdate -= OnStatusUpdate;
            _matchmakingManager.OnPlayerCountChanged -= OnPlayerCountChanged;
            _matchmakingManager.OnCustomRoomCreated -= OnRoomCodeReceived;
            _matchmakingManager.OnRoomJoined -= OnRoomCodeReceived;
            _matchmakingManager.OnMatchmakingCancelled -= OnMatchmakingCancelled;
        }

        if (_cancelButton != null)
        {
            _cancelButton.onClick.RemoveListener(OnCancelClicked);
        }
    }

    #endregion

    #region Private Methods

    private void FindNetworkRunner()
    {
        // DontDestroyOnLoad 객체들 중에서 LobbyGameRunner 찾기
        var runners = FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
        foreach (var runner in runners)
        {
            if (runner != null && runner.IsRunning && runner.gameObject.name.Contains("LobbyGameRunner"))
            {
                _runner = runner;
                Debug.Log($"[MatchmakingUI] NetworkRunner found: {runner.gameObject.name}");
                break;
            }
        }
    }

    private void UpdatePlayerCountFromRunner()
    {
        if (_runner == null || !_runner.IsRunning) return;
        if (_playerCountText == null) return;

        int currentPlayers = _runner.ActivePlayers.Count();
        int targetPlayers = _matchmakingManager?.MaxPlayers ?? 0;

        // MatchmakingManager에서 목표 인원 가져오기
        if (targetPlayers <= 0 && _matchmakingManager != null)
        {
            targetPlayers = _matchmakingManager.MaxPlayers;
        }

        // GameStateManager에서도 확인
        if (targetPlayers <= 0)
        {
            var gameState = GameStateManager.Instance;
            if (gameState != null && gameState.TargetPlayerCount > 0)
            {
                targetPlayers = gameState.TargetPlayerCount;
            }
        }

        if (targetPlayers > 0)
        {
            _playerCountText.text = $"{currentPlayers}/{targetPlayers}";
        }
        else
        {
            _playerCountText.text = $"{currentPlayers}/...";
        }
    }

    private void UpdateUI()
    {
        if (_matchmakingManager == null) return;

        // 상태 텍스트
        if (_statusText != null)
        {
            _statusText.text = _matchmakingManager.StatusMessage ?? "플레이어 대기 중...";
        }

        // 플레이어 수
        if (_playerCountText != null)
        {
            _playerCountText.text = $"{_matchmakingManager.CurrentPlayers}/{_matchmakingManager.MaxPlayers}";
        }

        // 방 코드 (커스텀 게임인 경우)
        UpdateRoomCodeDisplay();
    }

    /// <summary>
    /// 방 코드 표시를 갱신합니다 (Update에서 호출)
    /// </summary>
    private void UpdateRoomCodeDisplay()
    {
        if (_roomCodeText == null || _matchmakingManager == null) return;

        if (_matchmakingManager.IsCustomGame && !string.IsNullOrEmpty(_matchmakingManager.RoomCode))
        {
            _roomCodeText.gameObject.SetActive(true);
            string formattedCode = RoomCodeGenerator.Format(_matchmakingManager.RoomCode);
            _roomCodeText.text = $"방 코드: {formattedCode}";
        }
        else
        {
            _roomCodeText.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 취소 버튼 활성화 상태를 갱신합니다.
    /// 서버 접속 중에는 취소 버튼을 비활성화합니다.
    /// </summary>
    private void UpdateCancelButtonState()
    {
        if (_cancelButton == null || _matchmakingManager == null) return;

        var state = _matchmakingManager.CurrentState;
        
        // Why: 서버 접속 중에만 취소 불가 (네트워크 연결이 진행 중이므로)
        // 플레이어 대기 중에는 취소 가능
        bool canCancel = state != MatchmakingState.ConnectingToServer;
        
        _cancelButton.interactable = canCancel;
    }

    #endregion

    #region Event Handlers

    private void OnStatusUpdate(string status)
    {
        if (_statusText != null)
        {
            _statusText.text = status;
        }
    }

    private void OnPlayerCountChanged(int current, int max)
    {
        if (_playerCountText != null)
        {
            _playerCountText.text = $"{current}/{max}";
        }
    }

    private void OnRoomCodeReceived(string roomCode)
    {
        if (_roomCodeText != null && !string.IsNullOrEmpty(roomCode))
        {
            _roomCodeText.gameObject.SetActive(true);
            string formattedCode = RoomCodeGenerator.Format(roomCode);
            _roomCodeText.text = $"방 코드: {formattedCode}";
        }
    }

    private void OnCancelClicked()
    {
        Debug.Log("[MatchmakingUI] Cancel button clicked");
        _matchmakingManager?.CancelAndReturnToLobby();
    }

    private void OnMatchmakingCancelled()
    {
        Debug.Log("[MatchmakingUI] Matchmaking cancelled - returning to Lobby");
        UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
    }

    #endregion
}
