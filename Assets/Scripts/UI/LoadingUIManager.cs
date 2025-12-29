using System;
using System.Collections;
using ProjectVoid.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로딩 화면과 세션 대기 패널을 관리하는 싱글톤 (DontDestroyOnLoad)
/// Lobby 씬에 배치되며, 씬 전환 후에도 유지됩니다.
/// </summary>
public class LoadingUIManager : MonoBehaviour
{
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

    #endregion

    #region Private Fields

    private bool _isLoading;
    private Coroutine _loadingCoroutine;
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

    /// <summary>
    /// 로딩 화면을 표시하고 맵 준비를 기다립니다.
    /// </summary>
    public void ShowLoadingAndWaitForMap()
    {
        if (_isLoading) { Debug.LogWarning("[LoadingUIManager] 이미 로딩 중입니다."); return; }
        if (_loadingPanel == null) { Debug.LogWarning("[LoadingUIManager] 로딩 패널이 설정되지 않았습니다."); return; }

        if (_countdownCoroutine != null)
        {
            StopCoroutine(_countdownCoroutine);
            _countdownCoroutine = null;
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

        if (_loadingCoroutine != null) StopCoroutine(_loadingCoroutine);
        _loadingCoroutine = StartCoroutine(WaitForMapReadyCoroutine());
    }

    /// <summary>
    /// 로딩 화면을 즉시 숨깁니다.
    /// </summary>
    public void HideLoadingScreen()
    {
        _isLoading = false;
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
    /// 카운트다운을 표시합니다.
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

        // Why: SessionWaitingPanel이 비활성화되어 있으면 활성화
        if (!_sessionWaitingPanel.activeSelf)
        {
            _sessionWaitingPanel.SetActive(true);
            if (_sessionWaitingCanvasGroup != null) _sessionWaitingCanvasGroup.alpha = 1f;
        }

        // Why: LoadingPanel이 아직 활성화되어 있으면 비활성화
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

    private IEnumerator WaitForMapReadyCoroutine()
    {
        Debug.Log("[LoadingUIManager] WaitForMapReadyCoroutine 시작!");
        NetworkMapManager mapManager = null;
        float progress = 0f;
        float tipChangeInterval = 3f, lastTipChangeTime = Time.time;
        int tipIndex = 0;
        float checkInterval = 0.2f, minDisplayTime = 1.5f, startTime = Time.time;

        UpdateLoadingProgress(0.1f);
        UpdateLoadingText("맵 매니저 대기 중...");

        float maxWaitTime = 60f, waitTime = 0f;
        Debug.Log("[LoadingUIManager] NetworkMapManager 검색 시작...");
        
        while (mapManager == null && waitTime < maxWaitTime)
        {
            var networkManager = FindFirstObjectByType<NetworkManager>();
            if (networkManager != null && networkManager.HasSpawnedMapManager && networkManager.NetworkMapManager != null)
            {
                var mgr = networkManager.NetworkMapManager;
                if (mgr.Object != null && mgr.Object.IsValid)
                {
                    mapManager = mgr;
                    Debug.Log("[LoadingUIManager] NetworkMapManager 발견! (NetworkManager를 통해)");
                    break;
                }
            }

            if (mapManager == null)
            {
                var managers = FindObjectsByType<NetworkMapManager>(FindObjectsSortMode.None);
                foreach (var mgr in managers)
                {
                    if (mgr.Object != null && mgr.Object.IsValid)
                    {
                        mapManager = mgr;
                        Debug.Log("[LoadingUIManager] NetworkMapManager 발견! (직접 검색)");
                        break;
                    }
                }
            }

            if (mapManager == null)
            {
                waitTime += checkInterval;
                yield return new WaitForSeconds(checkInterval);
            }
        }

        if (mapManager == null)
        {
            Debug.LogError("[LoadingUIManager] NetworkMapManager를 찾을 수 없습니다! 로딩 강제 완료");
            FinishLoading();
            yield break;
        }

        // Why: Networked 속성은 Spawned() 이후에만 접근 가능하므로 IsValid 확인
        bool isValid = mapManager.Object != null && mapManager.Object.IsValid;
        Debug.Log($"[LoadingUIManager] NetworkMapManager 발견 - IsValid: {isValid}, IsMapReady: {(isValid ? mapManager.IsMapReady.ToString() : "N/A")}");
        UpdateLoadingText("맵 생성 중...");

        // Why: Networked 속성은 Spawned() 이후에만 접근 가능
        while (mapManager.Object == null || !mapManager.Object.IsValid || !mapManager.IsMapReady || !mapManager.IsReady())
        {
            // Object가 아직 유효하지 않으면 대기
            if (mapManager.Object == null || !mapManager.Object.IsValid)
            {
                yield return new WaitForSeconds(checkInterval);
                continue;
            }
            
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

        Debug.Log($"[LoadingUIManager] 맵 준비 완료 - IsMapReady: {mapManager.IsMapReady}, IsReady: {mapManager.IsReady()}");

        UpdateLoadingText("플레이어 스폰 중...");
        UpdateLoadingProgress(0.9f);

        PlayerCombat localPlayer = null;
        waitTime = 0f;
        while (localPlayer == null && waitTime < maxWaitTime)
        {
            localPlayer = PlayerUtils.GetLocalPlayerCombat();
            if (localPlayer == null)
            {
                waitTime += checkInterval;
                yield return new WaitForSeconds(checkInterval);
            }
        }

        if (localPlayer == null)
        {
            Debug.LogWarning("[LoadingUIManager] 로컬 플레이어를 찾을 수 없습니다!");
        }
        else
        {
            Debug.Log("[LoadingUIManager] 로컬 플레이어 발견 완료");
        }

        UpdateLoadingText("게임 준비 완료!");
        UpdateLoadingProgress(1f);

        float elapsedTime = Time.time - startTime;
        if (elapsedTime < minDisplayTime) yield return new WaitForSeconds(minDisplayTime - elapsedTime);

        FinishLoading();
    }

    private void FinishLoading()
    {
        Debug.Log("[LoadingUIManager] FinishLoading - transitioning to SessionWaitingPanel");
        OnMapLoadingComplete?.Invoke();
        if (_loadingCoroutine != null) StopCoroutine(_loadingCoroutine);
        _loadingCoroutine = StartCoroutine(FadeOutLoadingAndShowWaiting());
    }

    private IEnumerator FadeOutLoadingAndShowWaiting()
    {
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
            _countdownText.text = "플레이어 대기 중...";
        }

        Debug.Log("[LoadingUIManager] Waiting panel shown. Countdown will start via RPC from server.");
    }

    private IEnumerator CountdownRoutine(float durationSeconds)
    {
        float remaining = Mathf.Max(0f, durationSeconds);

        while (remaining > 0f)
        {
            _countdownText.text = Mathf.CeilToInt(remaining).ToString();
            yield return null;
            remaining -= Time.deltaTime;
        }

        _countdownText.text = "게임 시작!";
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
