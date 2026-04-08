using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using FishNet;
using FishNet.Object;
using ProjectVoid.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로딩 화면 및 세션 대기 화면 관리
/// Lobby 씬에 배치, 씬 전환 후에도 유지
/// </summary>
public class LoadingUIManager : MonoBehaviour
{
    #region Constants

    private const int COUNTDOWN_TICK_START_SECOND = 3;

    #endregion

    #region Singleton

    private static LoadingUIManager _instance;
    public static LoadingUIManager Instance => _instance;

    #endregion

    #region Serialized Fields

    [Header("로딩 화면")]
    [SerializeField] private GameObject _loadingPanel;
    [SerializeField] private TextMeshProUGUI _loadingText;
    [SerializeField] private Slider _loadingProgressBar;
    [SerializeField] private TextMeshProUGUI _loadingTipText;

    [Header("세션 대기")]
    [SerializeField] private GameObject _sessionWaitingPanel;
    [SerializeField] private TextMeshProUGUI _countdownText;

    [Header("사운드")]
    [SerializeField] private AudioCue _countdownTickAudioCue;
    [SerializeField] private AudioCue _countdownStartAudioCue;

    #endregion

    #region Private Fields

    private bool _isLoading;
    private Coroutine _loadingCoroutine;
    private Task _loadingTask;
    private CancellationTokenSource _loadingCancellationTokenSource;
    private int _loadingRequestId;
    private CanvasGroup _loadingCanvasGroup;
    private CanvasGroup _sessionWaitingCanvasGroup;
    private Coroutine _countdownCoroutine;

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
            DontDestroyOnLoad(gameObject);
            InitializePanels();
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void OnDestroy()
    {
        CancelLoadingTask();
    }

    #endregion

    #region Initialization

    private void InitializePanels()
    {
        if (_loadingPanel != null)
        {
            _loadingCanvasGroup = _loadingPanel.GetComponent<CanvasGroup>();
            if (_loadingCanvasGroup == null) _loadingCanvasGroup = _loadingPanel.AddComponent<CanvasGroup>();
            _loadingCanvasGroup.alpha = 0f;
            _loadingPanel.SetActive(false);
        }

        if (_sessionWaitingPanel != null)
        {
            _sessionWaitingCanvasGroup = _sessionWaitingPanel.GetComponent<CanvasGroup>();
            if (_sessionWaitingCanvasGroup == null) _sessionWaitingCanvasGroup = _sessionWaitingPanel.AddComponent<CanvasGroup>();
            _sessionWaitingCanvasGroup.alpha = 0f;
            _sessionWaitingPanel.SetActive(false);
        }

        if (_countdownText != null)
        {
            _countdownText.text = string.Empty;
        }
    }

    #endregion

    #region Public Methods

    private bool _skipWaitingPanel = false;

    /// <summary>
    /// 로딩 화면을 표시하고 맵 준비를 기다립니다.
    /// </summary>
    /// <param name="skipWaitingPanel">true일 경우 로딩 완료 후 대기 패널 없이 바로 화면을 닫습니다 (연습 모드 등)</param>
    public void ShowLoadingAndWaitForMap(bool skipWaitingPanel = false)
    {
        if (_loadingPanel == null) { Debug.LogWarning("[LoadingUIManager] 로딩 패널이 설정되지 않았습니다."); return; }

        _skipWaitingPanel = skipWaitingPanel;
        _loadingRequestId++;
        int requestId = _loadingRequestId;

        CancelLoadingTask();

        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
        }

        if (_loadingCoroutine != null)
        {
            StopCoroutine(_loadingCoroutine);
            _loadingCoroutine = null;
        }

        if (_sessionWaitingPanel != null)
        {
            _sessionWaitingCanvasGroup.alpha = 0f;
            _sessionWaitingPanel.SetActive(false);
        }

        _isLoading = true;
        _loadingPanel.SetActive(true);
        if (_loadingCanvasGroup != null) _loadingCanvasGroup.alpha = 1f;

        UpdateLoadingProgress(0f);
        UpdateLoadingText("서버 연결 중...");
        UpdateLoadingTip(0);

        _loadingCancellationTokenSource = new CancellationTokenSource();
        _loadingTask = RunLoadingFlowAsync(requestId, _loadingCancellationTokenSource.Token);
    }

    /// <summary>
    /// 로딩 화면을 즉시 숨깁니다.
    /// </summary>
    public void HideLoadingScreen()
    {
        _isLoading = false;
        CancelLoadingTask();

        if (_loadingCoroutine != null) { StopCoroutine(_loadingCoroutine); _loadingCoroutine = null; }
        if (_countdownCoroutine != null) { StopCoroutine(_countdownCoroutine); _countdownCoroutine = null; }
        
        if (_loadingPanel != null)
        {
            _loadingCanvasGroup.alpha = 0f;
            _loadingPanel.SetActive(false);
        }
        
        if (_sessionWaitingPanel != null)
        {
            _sessionWaitingCanvasGroup.alpha = 0f;
            _sessionWaitingPanel.SetActive(false);
        }
    }

    /// <summary>
    /// 카운트다운 표시
    /// </summary>
    public void ShowCountdown(float durationSeconds)
    {
        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
        }

        if (_sessionWaitingPanel == null || _countdownText == null)
        {
            Debug.LogWarning("[LoadingUIManager] SessionWaitingPanel or CountdownText is null!");
            return;
        }

        // SessionWaitingPanel이 비활성화되어 있으면 활성화
        if (!_sessionWaitingPanel.activeSelf)
        {
            _sessionWaitingPanel.SetActive(true);
            if (_sessionWaitingCanvasGroup != null) _sessionWaitingCanvasGroup.alpha = 1f;
        }

        // LoadingPanel이 아직 활성화되어 있으면 비활성화
        if (_loadingPanel != null && _loadingPanel.activeSelf)
        {
            _loadingCanvasGroup.alpha = 0f;
            _loadingPanel.SetActive(false);
            _isLoading = false;
        }

        _countdownCoroutine = StartCoroutine(CountdownRoutine(durationSeconds));
    }

    #endregion

    #region Loading Coroutines

    private async Task RunLoadingFlowAsync(int requestId, CancellationToken cancellationToken)
    {
        Debug.Log("[LoadingUIManager] 비동기 로딩 플로우 시작");
        float startTime = Time.unscaledTime;

        try
        {
            UpdateLoadingProgress(0.1f);
            UpdateLoadingText("맵 매니저 대기 중...");

            NetworkMapManager mapManager = await WaitForMapManagerAsync(cancellationToken);
            if (!IsLoadingRequestValid(requestId, cancellationToken))
            {
                return;
            }

            if (mapManager == null)
            {
                Debug.LogError("[LoadingUIManager] NetworkMapManager를 찾을 수 없습니다! 로딩 강제 완료");
                FinishLoading();
                return;
            }

            UpdateLoadingText("맵 생성 중...");
            await WaitForMapReadyAsync(mapManager, cancellationToken);
            if (!IsLoadingRequestValid(requestId, cancellationToken))
            {
                return;
            }

            UpdateLoadingText("플레이어 스폰 중...");
            UpdateLoadingProgress(0.9f);
            await WaitForLocalPlayerAsync(cancellationToken);
            if (!IsLoadingRequestValid(requestId, cancellationToken))
            {
                return;
            }

            UpdateLoadingText("게임 준비 완료!");
            UpdateLoadingProgress(1f);

            float minDisplayTime = 1.5f;
            float elapsedTime = Time.unscaledTime - startTime;
            if (elapsedTime < minDisplayTime)
            {
                await DelaySecondsAsync(minDisplayTime - elapsedTime, cancellationToken);
            }

            if (!IsLoadingRequestValid(requestId, cancellationToken))
            {
                return;
            }

            FinishLoading();
        }
        catch (OperationCanceledException)
        {
            // 정상 취소 흐름
        }
        catch (Exception ex)
        {
            if (IsLoadingRequestValid(requestId, cancellationToken))
            {
                Debug.LogError($"[LoadingUIManager] 비동기 로딩 중 예외 발생: {ex.Message}");
                FinishLoading();
            }
        }
        finally
        {
            if (_loadingRequestId == requestId)
            {
                _loadingTask = null;
            }
        }
    }

    private async Task<NetworkMapManager> WaitForMapManagerAsync(CancellationToken cancellationToken)
    {
        const float maxWaitTime = 60f;
        const float checkInterval = 0.2f;
        float waitTime = 0f;

        Debug.Log("[LoadingUIManager] NetworkMapManager 검색 시작...");

        while (waitTime < maxWaitTime)
        {
            cancellationToken.ThrowIfCancellationRequested();

            NetworkManager networkManager = FindFirstObjectByType<NetworkManager>();
            if (networkManager != null && networkManager.HasSpawnedMapManager && networkManager.NetworkMapManager != null)
            {
                NetworkMapManager managerFromNetwork = networkManager.NetworkMapManager;
                if (managerFromNetwork.NetworkObject != null && managerFromNetwork.NetworkObject.IsSpawned)
                {
                    Debug.Log("[LoadingUIManager] NetworkMapManager 발견! (NetworkManager를 통해)");
                    return managerFromNetwork;
                }
            }

            NetworkMapManager[] managers = FindObjectsByType<NetworkMapManager>(FindObjectsSortMode.None);
            for (int i = 0; i < managers.Length; i++)
            {
                NetworkMapManager manager = managers[i];
                if (manager.NetworkObject != null && manager.NetworkObject.IsSpawned)
                {
                    Debug.Log("[LoadingUIManager] NetworkMapManager 발견! (직접 검색)");
                    return manager;
                }
            }

            waitTime += checkInterval;
            await DelaySecondsAsync(checkInterval, cancellationToken);
        }

        return null;
    }

    private async Task WaitForMapReadyAsync(NetworkMapManager mapManager, CancellationToken cancellationToken)
    {
        const float checkInterval = 0.2f;
        const float maxMapReadyWaitTime = 30f;
        const float tipChangeInterval = 3f;

        float mapReadyWaitTime = 0f;
        float tipElapsed = 0f;
        float progress = 0f;
        int tipIndex = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (mapManager == null)
            {
                Debug.LogWarning("[LoadingUIManager] NetworkMapManager가 파괴되어 맵 준비 대기를 종료합니다.");
                break;
            }

            bool isSpawned = mapManager.NetworkObject != null && mapManager.NetworkObject.IsSpawned;
            if (isSpawned && mapManager.IsMapReady.Value && mapManager.IsReady())
            {
                Debug.Log($"[LoadingUIManager] 맵 준비 완료 - IsMapReady: {mapManager.IsMapReady.Value}, IsReady: {mapManager.IsReady()}");
                break;
            }

            if (IsGameplayAlreadyStarted())
            {
                Debug.LogWarning("[LoadingUIManager] GameState가 이미 진행 중이어서 로딩 대기를 조기 종료합니다.");
                break;
            }

            mapReadyWaitTime += checkInterval;
            tipElapsed += checkInterval;

            if (mapReadyWaitTime >= maxMapReadyWaitTime)
            {
                Debug.LogWarning("[LoadingUIManager] 맵 준비 대기 타임아웃. 로딩을 강제 종료합니다.");
                break;
            }

            progress = Mathf.Lerp(progress, 0.85f, 0.08f);
            UpdateLoadingProgress(0.2f + progress * 0.7f);

            if (tipElapsed >= tipChangeInterval)
            {
                tipIndex = (tipIndex + 1) % _loadingTips.Length;
                UpdateLoadingTip(tipIndex);
                tipElapsed = 0f;
            }

            await DelaySecondsAsync(checkInterval, cancellationToken);
        }
    }

    private async Task WaitForLocalPlayerAsync(CancellationToken cancellationToken)
    {
        const float checkInterval = 0.2f;
        const float maxWaitTime = 30f;
        float waitTime = 0f;

        while (waitTime < maxWaitTime)
        {
            cancellationToken.ThrowIfCancellationRequested();

            PlayerCombat localPlayer = PlayerUtils.GetLocalPlayerCombat();
            if (localPlayer != null)
            {
                Debug.Log("[LoadingUIManager] 로컬 플레이어 발견 완료");
                return;
            }

            waitTime += checkInterval;
            await DelaySecondsAsync(checkInterval, cancellationToken);
        }

        Debug.LogWarning("[LoadingUIManager] 로컬 플레이어를 찾을 수 없습니다!");
    }

    private void FinishLoading()
    {
        if (!_isLoading)
        {
            return;
        }

        Debug.Log($"[LoadingUIManager] FinishLoading - skipWaitingPanel: {_skipWaitingPanel}");
        OnMapLoadingComplete?.Invoke();

        if (_skipWaitingPanel)
        {
            // 대기 패널 없이 바로 종료
            HideLoadingScreen();
        }
        else
        {
            // 기존대로 대기 패널 표시
            _loadingCoroutine = StartCoroutine(FadeOutLoadingAndShowWaiting());
        }
    }

    private static bool IsGameplayAlreadyStarted()
    {
        if (GameStateManager.Instance == null)
        {
            return false;
        }

        GameState state = GameStateManager.Instance.CurrentGameState.Value;
        return state == GameState.Countdown || state == GameState.Playing || state == GameState.Ended;
    }

    private bool IsLoadingRequestValid(int requestId, CancellationToken cancellationToken)
    {
        return _isLoading && _loadingRequestId == requestId && !cancellationToken.IsCancellationRequested;
    }

    private static async Task DelaySecondsAsync(float seconds, CancellationToken cancellationToken)
    {
        if (seconds <= 0f)
        {
            return;
        }

        int milliseconds = Mathf.Max(1, Mathf.CeilToInt(seconds * 1000f));
        await Task.Delay(milliseconds, cancellationToken);
    }

    private void CancelLoadingTask()
    {
        if (_loadingCancellationTokenSource == null)
        {
            return;
        }

        _loadingCancellationTokenSource.Cancel();
        _loadingCancellationTokenSource.Dispose();
        _loadingCancellationTokenSource = null;
        _loadingTask = null;
    }

    private IEnumerator FadeOutLoadingAndShowWaiting()
    {
        // 이미 카운트다운이 진행 중이거나 SessionWaitingPanel이 보이면 중복 실행 방지
        if (_countdownCoroutine != null || 
            (_sessionWaitingPanel != null && _sessionWaitingPanel.activeSelf && _sessionWaitingCanvasGroup.alpha > 0.9f))
        {
            Debug.Log("[LoadingUIManager] SessionWaitingPanel already visible or countdown running. Skipping fade transition.");
            if (_loadingPanel != null)
            {
                _loadingCanvasGroup.alpha = 0f;
                _loadingPanel.SetActive(false);
            }
            _isLoading = false;
            _loadingCoroutine = null;
            yield break;
        }
        
        // Fade out loading panel
        float fadeOutDuration = 0.5f;
        if (_loadingCanvasGroup != null)
        {
            float elapsed = 0f, startAlpha = _loadingCanvasGroup.alpha;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                _loadingCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / fadeOutDuration);
                yield return null;
            }
            _loadingCanvasGroup.alpha = 0f;
        }

        if (_loadingPanel != null) _loadingPanel.SetActive(false);
        _isLoading = false;
        _loadingCoroutine = null;

        // Show and fade in session waiting panel
        if (_sessionWaitingPanel != null)
        {
            _sessionWaitingPanel.SetActive(true);
            if (_sessionWaitingCanvasGroup != null)
            {
                float fadeInDuration = 0.3f;
                float elapsed = 0f;
                while (elapsed < fadeInDuration)
                {
                    elapsed += Time.deltaTime;
                    _sessionWaitingCanvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeInDuration);
                    yield return null;
                }
                _sessionWaitingCanvasGroup.alpha = 1f;
            }
        }

        if (_countdownText != null)
        {
            // 이미 Countdown 상태라면 곧 카운트다운 RPC가 올 것이므로 빈 텍스트 표시
            // 아직 Waiting 상태라면 "플레이어 대기 중..." 표시
            bool isAlreadyCountdown = GameStateManager.Instance != null && 
                                       GameStateManager.Instance.CurrentGameState.Value == GameState.Countdown;
            _countdownText.text = isAlreadyCountdown ? string.Empty : "플레이어 대기 중...";
        }

        Debug.Log("[LoadingUIManager] Waiting panel shown. Countdown will start via RPC from server.");
    }

    private IEnumerator CountdownRoutine(float durationSeconds)
    {
        float remaining = Mathf.Max(0f, durationSeconds);
        int lastAnnouncedSecond = -1;

        while (remaining > 0f)
        {
            int countdownSecond = Mathf.CeilToInt(remaining);
            _countdownText.text = countdownSecond.ToString();

            if (countdownSecond != lastAnnouncedSecond)
            {
                TryPlayCountdownTick(countdownSecond);
                lastAnnouncedSecond = countdownSecond;
            }

            yield return null;
            remaining -= Time.deltaTime;
        }

        _countdownText.text = "게임 시작!";
        AudioManager.Instance?.PlayUi(_countdownStartAudioCue);
        yield return new WaitForSeconds(0.5f);

        // Fade out session waiting panel
        if (_sessionWaitingCanvasGroup != null)
        {
            float fadeOutDuration = 0.3f;
            float elapsed = 0f;
            float startAlpha = _sessionWaitingCanvasGroup.alpha;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                _sessionWaitingCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / fadeOutDuration);
                yield return null;
            }
            _sessionWaitingCanvasGroup.alpha = 0f;
        }

        if (_sessionWaitingPanel != null) _sessionWaitingPanel.SetActive(false);
        _countdownCoroutine = null;
    }

    private void TryPlayCountdownTick(int countdownSecond)
    {
        if (countdownSecond > COUNTDOWN_TICK_START_SECOND)
        {
            return;
        }

        AudioManager.Instance?.PlayUi(_countdownTickAudioCue);
    }

    #endregion

    #region Helper Methods

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
