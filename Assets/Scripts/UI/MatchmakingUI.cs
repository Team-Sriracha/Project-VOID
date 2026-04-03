using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishNet;

/// <summary>
/// 매칭 UI 관리
/// 플레이어 수, 대기 상태, 방 코드 등 표시
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

    [Header("사운드")]
    [SerializeField] private AudioCue _uiClickAudioCue;

    #endregion

    #region Private Fields

    private MatchmakingManager _matchmakingManager;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        Debug.Log("[MatchmakingUI] Start - Matchmaking 씬에서 대기 UI 초기화");

        // 서버 모드인 경우 UI 비활성화
        if (InstanceFinder.IsServerStarted && !InstanceFinder.IsClientStarted)
        {
            if (_statusText) _statusText.gameObject.SetActive(false);
            if (_playerCountText) _playerCountText.gameObject.SetActive(false);
            if (_roomCodeText) _roomCodeText.gameObject.SetActive(false);
            if (_cancelButton) _cancelButton.gameObject.SetActive(false);
            this.enabled = false;
            return;
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

        // Matchmaking 씬 로드 완료 및 서버 접속 시작
        Debug.Log("[MatchmakingUI] Executing prepared matchmaking...");
        _matchmakingManager.ExecuteMatchmaking();
    }

    private void Update()
    {
        // Loading Indicator 회전 애니메이션 (시각적 효과만 Update 유지)
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

    private void UpdatePlayerCountFromNetwork()
    {
        if (_playerCountText == null || _matchmakingManager == null) return;

        int currentPlayers = _matchmakingManager.CurrentPlayers;
        int targetPlayers = _matchmakingManager.MaxPlayers;

        if (targetPlayers <= 0)
        {
            var gameState = GameStateManager.Instance;
            if (gameState != null && gameState.TargetPlayerCount.Value > 0)
            {
                targetPlayers = gameState.TargetPlayerCount.Value;
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

        if (_statusText != null)
        {
            _statusText.text = _matchmakingManager.StatusMessage ?? "플레이어 대기 중...";
        }

        if (_playerCountText != null)
        {
            _playerCountText.text = $"{_matchmakingManager.CurrentPlayers}/{_matchmakingManager.MaxPlayers}";
        }

        UpdateRoomCodeDisplay();
    }

    private void UpdateRoomCodeDisplay()
    {
        if (_roomCodeText == null || _matchmakingManager == null) return;

        if (_matchmakingManager.IsCustomGame && !string.IsNullOrEmpty(_matchmakingManager.RoomCode))
        {
            _roomCodeText.gameObject.SetActive(true);
            string formattedCode = _matchmakingManager.RoomCode?.ToUpper().Trim() ?? "";
            _roomCodeText.text = $"방 코드: {formattedCode}";
        }
        else
        {
            _roomCodeText.gameObject.SetActive(false);
        }
    }

    private void UpdateCancelButtonState()
    {
        if (_cancelButton == null || _matchmakingManager == null) return;

        var state = _matchmakingManager.CurrentState;
        bool canCancel = state != MatchmakingState.ConnectingToServer;
        
        _cancelButton.interactable = canCancel;
    }

    #endregion

    #region Event Handlers

    private void OnPlayerCountChanged(int current, int max)
    {
        UpdatePlayerCountFromNetwork();
    }

    private void OnRoomCodeReceived(string roomCode)
    {
        UpdateRoomCodeDisplay();
    }

    private void OnStatusUpdate(string status)
    {
        if (_statusText != null)
        {
            _statusText.text = status;
        }
        UpdateCancelButtonState(); // 상태 변경 시 버튼 상태도 갱신
    }

    private void OnCancelClicked()
    {
        AudioManager.Instance?.PlayUi(_uiClickAudioCue);
        _matchmakingManager?.CancelAndReturnToLobby();
    }

    private void OnMatchmakingCancelled()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
    }

    #endregion
}
