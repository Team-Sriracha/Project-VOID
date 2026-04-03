using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>
/// 로그인 씬 UI 컨트롤러입니다.
/// 정책: 소셜 로그인(Google/Apple)과 Guest를 지원합니다.
/// </summary>
public class LoginUI : MonoBehaviour
{
    #region Constants

    private const string LOBBY_SCENE_NAME = "Lobby";
    private const string OFFLINE_GUEST_ONLY_MESSAGE = "로그인은 인터넷 연결이 필요합니다.";
    private const string NICKNAME_REQUIRED_MESSAGE = "소셜 로그인은 닉네임 설정이 필요합니다.";
    private const string DUPLICATE_DISPLAY_NAME_MESSAGE = "이미 사용 중인 닉네임입니다. 다른 이름을 입력해 주세요.";
    private const string NICKNAME_UI_BINDING_REQUIRED_MESSAGE = "닉네임 설정 UI 바인딩이 필요합니다.";
    private const string NICKNAME_GUIDE_MESSAGE = "닉네임을 설정해 주세요. 동일한 닉네임은 사용할 수 없습니다.";
    private const string GOOGLE_BROWSER_LOGIN_MESSAGE = "브라우저에서 Google 로그인 후 게임으로 돌아와 주세요.";
    private const int DISPLAY_NAME_MIN_LENGTH = 2;
    private const int DISPLAY_NAME_MAX_LENGTH = 16;
    private const float NETWORK_STATE_POLL_INTERVAL_SECONDS = 1f;

    #endregion

    #region Serialized Fields

    [Header("로그인 버튼")]
    [SerializeField] private Button _googleSignInButton;
    [SerializeField] private Button _appleSignInButton;
    [FormerlySerializedAs("_entryGuestLoginButton")]
    [SerializeField] private Button _guestSignInButton;

    [Header("로그인 패널 UI (선택)")]
    [SerializeField] private GameObject _entryPanel;

    [Header("닉네임 설정 UI (선택)")]
    [SerializeField] private GameObject _nicknameSetupPanel;
    [SerializeField] private TMP_InputField _nicknameInputField;
    [SerializeField] private Button _nicknameConfirmButton;
    [SerializeField] private TMP_Text _nicknameGuideText;

    [Header("상태 UI (선택)")]
    [SerializeField] private TMP_Text _statusText;
    [SerializeField] private GameObject _loadingIndicator;

    [Header("사운드")]
    [SerializeField] private AudioCue _uiClickAudioCue;

    #endregion

    #region Private Fields

    private IAuthService _authService;
    private IPlayerDataService _playerDataService;
    private bool _isNicknameSubmitInProgress;
    private bool _isOnlineAvailable;
    private bool _isNicknameFlowActive;
    private float _nextNetworkStatePollTime;
    private int _latestSignInAttemptId;
    private int _pendingSignInAttemptCount;
    private string _pendingNicknameUid = string.Empty;
    private PlayerProfile _pendingNicknameProfile;

    #endregion

    #region Unity Lifecycle

    private async void Start()
    {
        if (!ValidateRequiredBindings())
        {
            enabled = false;
            return;
        }

        BindButtonEvents();
        ConfigurePlatformButtons();
        HideNicknameSetup(resetPendingState: true);
        SetStatus("인증 시스템 초기화 중...");
        UpdateNetworkAwareButtonState(true);

        if (!await EnsureServicesReadyAsync("인증 시스템 초기화가 완료되지 않았습니다."))
        {
            SetNicknameSubmitInProgress(false);
            return;
        }

        SetStatus(string.Empty);

        if (_authService != null && _authService.IsSignedIn)
        {
            if (_authService.IsGoogleSignedIn)
            {
                await HandleSignedInAsync();
                return;
            }

            await ResetNonGoogleSessionAsync();
        }

        SetNicknameSubmitInProgress(false);
    }

    private void Update()
    {
        UpdateNetworkAwareButtonState();
    }

    private void OnDestroy()
    {
        UnbindButtonEvents();
    }

    #endregion

    #region UI Events

    /// <summary>
    /// Google 로그인 버튼 클릭 처리입니다.
    /// </summary>
    public async void OnClickGoogleSignIn()
    {
        AudioManager.Instance?.PlayUi(_uiClickAudioCue);
        SetStatus(GOOGLE_BROWSER_LOGIN_MESSAGE);
        await TrySignInAsync(
            () => _authService.SignInWithGoogleAsync(),
            requiresOnline: true,
            preserveStatusMessage: true);
    }

    /// <summary>
    /// Apple 로그인 버튼 클릭 처리입니다.
    /// </summary>
    public async void OnClickAppleSignIn()
    {
        AudioManager.Instance?.PlayUi(_uiClickAudioCue);
        await TrySignInAsync(
            () => _authService.SignInWithAppleAsync(),
            requiresOnline: true);
    }

    /// <summary>
    /// 게스트 로그인 버튼 클릭 처리입니다.
    /// </summary>
    public async void OnClickGuestLogin()
    {
        AudioManager.Instance?.PlayUi(_uiClickAudioCue);
        await TrySignInAsync(
            () => _authService.SignInAnonymouslyAsync(),
            requiresOnline: true);
    }

    /// <summary>
    /// 닉네임 확인 버튼 클릭 처리입니다.
    /// </summary>
    public async void OnClickNicknameConfirm()
    {
        AudioManager.Instance?.PlayUi(_uiClickAudioCue);
        if (_isNicknameSubmitInProgress || !_isNicknameFlowActive)
        {
            return;
        }

        if (!await EnsureServicesReadyAsync("인증 시스템 초기화가 완료되지 않았습니다."))
        {
            return;
        }

        string uid = string.IsNullOrWhiteSpace(_pendingNicknameUid)
            ? _authService.UserId
            : _pendingNicknameUid;

        if (string.IsNullOrWhiteSpace(uid) || _authService.IsAnonymous)
        {
            SetStatus("닉네임을 설정할 사용자 정보를 찾을 수 없습니다.");
            return;
        }

        string rawName = _nicknameInputField != null ? _nicknameInputField.text : string.Empty;
        if (!TryValidateDisplayName(rawName, out string normalizedName, out string validationMessage))
        {
            SetStatus(validationMessage);
            return;
        }

        SetNicknameSubmitInProgress(true);

        bool committed = false;
        try
        {
            committed = await TryCommitDisplayNameAsync(uid, normalizedName);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LoginUI] 닉네임 저장 예외: {ex}");
            SetStatus("닉네임 저장 중 오류가 발생했습니다. 다시 시도해 주세요.");
        }
        finally
        {
            SetNicknameSubmitInProgress(false);
        }

        if (!committed)
        {
            return;
        }

        HideNicknameSetup(resetPendingState: true, showEntryPanel: false);
        await CompleteSignedInFlowAsync(uid);
    }

    #endregion

    #region Flow Methods

    private async Task TrySignInAsync(
        Func<Task<AuthResult>> signInHandler,
        bool requiresOnline,
        bool preserveStatusMessage = false)
    {
        if (signInHandler == null)
        {
            return;
        }

        if (!await EnsureServicesReadyAsync("인증 시스템 초기화가 완료되지 않았습니다.") ||
            !CanProceed(requiresOnline))
        {
            return;
        }

        if (!preserveStatusMessage)
        {
            SetStatus(string.Empty);
        }

        int attemptId = BeginSignInAttempt();
        try
        {
            AuthResult result = await ExecuteSignInAsync(signInHandler);
            await HandleAuthResultSafelyAsync(result, attemptId);
        }
        finally
        {
            EndSignInAttempt();
        }
    }

    private async Task HandleAuthResultAsync(AuthResult result, int attemptId)
    {
        if (!result.IsSuccess)
        {
            if (!IsLatestSignInAttempt(attemptId))
            {
                // Why: 사용자가 재시도한 뒤 늦게 도착한 이전 실패 결과가 최신 상태 메시지를 덮지 않도록 무시합니다.
                return;
            }

            if (_authService != null && _authService.IsSignedIn)
            {
                // Why: 중복 로그인 시도에서 늦게 도착한 실패 결과가 성공 상태를 덮어쓰지 않도록 무시합니다.
                return;
            }

            Debug.LogWarning($"[LoginUI] 인증 실패: ErrorCode={result.ErrorCode}, Message={result.ErrorMessage}");
            SetStatus(MapErrorMessage(result));
            return;
        }

        await HandleSignedInAsync();
    }

    private async Task HandleSignedInAsync()
    {
        if (!await EnsureServicesReadyAsync("인증 서비스가 초기화되지 않았습니다."))
        {
            return;
        }

        string uid = _authService.UserId;
        if (string.IsNullOrWhiteSpace(uid))
        {
            SetStatus("유효한 사용자 정보를 찾을 수 없습니다.");
            return;
        }

        if (_authService.IsAnonymous)
        {
            SetStatus(string.Empty);
            HideNicknameSetup(resetPendingState: true, showEntryPanel: false);
            LoadLobbyScene();
            return;
        }

        PlayerProfile profile = await _playerDataService.GetPlayerProfileAsync(uid);
        if (IsNicknameSetupRequired(profile))
        {
            string initialDisplayName = ResolveInitialDisplayName(uid, profile);

            if (profile == null)
            {
                profile = await _playerDataService.CreatePlayerProfileAsync(
                    uid,
                    initialDisplayName,
                    _authService.Email,
                    false);
            }

            if (!ShowNicknameSetup(uid, profile, initialDisplayName))
            {
                SetStatus(NICKNAME_UI_BINDING_REQUIRED_MESSAGE);
            }

            return;
        }

        SetStatus(string.Empty);
        HideNicknameSetup(resetPendingState: true, showEntryPanel: false);
        await CompleteSignedInFlowAsync(uid);
    }

    private async Task<bool> TryCommitDisplayNameAsync(string uid, string displayName)
    {
        PlayerProfile profile = _pendingNicknameProfile ?? await _playerDataService.GetPlayerProfileAsync(uid);

        bool isSameAsCurrent = profile != null &&
            string.Equals((profile.DisplayName ?? string.Empty).Trim(), displayName, StringComparison.OrdinalIgnoreCase);

        if (profile == null)
        {
            profile = await _playerDataService.CreatePlayerProfileAsync(
                uid,
                displayName,
                _authService?.Email,
                false);

            if (profile == null)
            {
                SetStatus(DUPLICATE_DISPLAY_NAME_MESSAGE);
                return false;
            }
        }

        bool updated = await _playerDataService.UpdateDisplayNameAsync(uid, displayName);
        if (!updated)
        {
            SetStatus(isSameAsCurrent
                ? "닉네임 저장에 실패했습니다. 다시 시도해 주세요."
                : DUPLICATE_DISPLAY_NAME_MESSAGE);
            return false;
        }

        PlayerProfile refreshedProfile = await _playerDataService.GetPlayerProfileAsync(uid);
        if (refreshedProfile == null || string.IsNullOrWhiteSpace(refreshedProfile.DisplayName))
        {
            SetStatus("닉네임 저장 후 프로필을 확인하지 못했습니다.");
            return false;
        }

        _pendingNicknameProfile = refreshedProfile;
        SetStatus($"닉네임이 '{refreshedProfile.DisplayName}'(으)로 설정되었습니다.");
        return true;
    }

    private async Task CompleteSignedInFlowAsync(string uid)
    {
        SetLoadingIndicatorVisible(true);
        try
        {
            await _playerDataService.UpdateLastLoginAtAsync(uid);
            LoadLobbyScene();
        }
        finally
        {
            SetLoadingIndicatorVisible(false);
        }
    }

    private void LoadLobbyScene()
    {
        if (Application.CanStreamedLevelBeLoaded(LOBBY_SCENE_NAME))
        {
            SceneManager.LoadScene(LOBBY_SCENE_NAME);
            return;
        }

        SetStatus($"씬 '{LOBBY_SCENE_NAME}'을(를) 찾을 수 없습니다.");
    }

    #endregion

    #region Helper Methods

    private void ResolveServices()
    {
        _authService = ServiceLocator.Get<IAuthService>();
        _playerDataService = ServiceLocator.Get<IPlayerDataService>();
    }

    private async Task<bool> EnsureServicesReadyAsync(string errorMessage)
    {
        if (_authService == null || _playerDataService == null)
        {
            ResolveServices();
        }

        if (_authService != null && _playerDataService != null)
        {
            return true;
        }

        AuthServiceInitializer initializer = AuthServiceInitializer.Instance;
        if (initializer == null)
        {
            initializer = FindFirstObjectByType<AuthServiceInitializer>(FindObjectsInactive.Include);
        }

        if (initializer != null)
        {
            try
            {
                await initializer.InitializeServicesAsync();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LoginUI] 인증 서비스 초기화 대기 중 예외: {ex.Message}");
            }

            ResolveServices();
            if (_authService != null && _playerDataService != null)
            {
                return true;
            }
        }

        SetStatus(errorMessage);
        return false;
    }

    private static async Task<AuthResult> ExecuteSignInAsync(Func<Task<AuthResult>> signInHandler)
    {
        try
        {
            return await signInHandler();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LoginUI] 로그인 예외: {ex}");
            return AuthResult.Failure(AuthErrorCode.Unknown, ex.Message);
        }
    }

    private async Task HandleAuthResultSafelyAsync(AuthResult result, int attemptId)
    {
        try
        {
            await HandleAuthResultAsync(result, attemptId);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LoginUI] 인증 결과 처리 예외: {ex}");

            if (IsLatestSignInAttempt(attemptId))
            {
                SetStatus("로그인 처리 중 오류가 발생했습니다. 다시 시도해 주세요.");
            }
        }
    }

    private bool ValidateRequiredBindings()
    {
        bool isValid = true;

        if (_guestSignInButton == null)
        {
            Debug.LogError("[LoginUI] _guestSignInButton 참조가 비어 있습니다.");
            isValid = false;
        }

        if (!isValid)
        {
            Debug.LogError("[LoginUI] 필수 UI 참조 누락으로 LoginUI를 비활성화합니다.");
        }
        else if (_statusText == null)
        {
            Debug.LogWarning("[LoginUI] _statusText가 비어 있어 상태 메시지를 표시하지 않습니다.");
        }

        return isValid;
    }

    private async Task ResetNonGoogleSessionAsync()
    {
        if (_authService == null || !_authService.IsSignedIn)
        {
            return;
        }

        try
        {
            await _authService.SignOutAsync();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LoginUI] 비 Google 자동 세션 정리 실패: {ex.Message}");
        }
    }

    private void BindButtonEvents()
    {
        if (_googleSignInButton != null)
        {
            BindButtonClick(_googleSignInButton, OnClickGoogleSignIn, nameof(OnClickGoogleSignIn));
        }

        if (_appleSignInButton != null)
        {
            BindButtonClick(_appleSignInButton, OnClickAppleSignIn, nameof(OnClickAppleSignIn));
        }

        if (_guestSignInButton != null)
        {
            BindButtonClick(_guestSignInButton, OnClickGuestLogin, nameof(OnClickGuestLogin));
        }

        if (_nicknameConfirmButton != null)
        {
            BindButtonClick(_nicknameConfirmButton, OnClickNicknameConfirm, nameof(OnClickNicknameConfirm));
        }
    }

    private void UnbindButtonEvents()
    {
        if (_googleSignInButton != null)
        {
            _googleSignInButton.onClick.RemoveListener(OnClickGoogleSignIn);
        }

        if (_appleSignInButton != null)
        {
            _appleSignInButton.onClick.RemoveListener(OnClickAppleSignIn);
        }

        if (_guestSignInButton != null)
        {
            _guestSignInButton.onClick.RemoveListener(OnClickGuestLogin);
        }

        if (_nicknameConfirmButton != null)
        {
            _nicknameConfirmButton.onClick.RemoveListener(OnClickNicknameConfirm);
        }
    }

    private void ConfigurePlatformButtons()
    {
        if (_googleSignInButton != null)
        {
#if UNITY_ANDROID || UNITY_IOS || UNITY_STANDALONE_WIN || UNITY_EDITOR
            _googleSignInButton.gameObject.SetActive(true);
#else
            _googleSignInButton.gameObject.SetActive(false);
#endif
        }

        if (_appleSignInButton != null)
        {
#if UNITY_IOS
            _appleSignInButton.gameObject.SetActive(true);
#else
            _appleSignInButton.gameObject.SetActive(false);
#endif
        }
    }

    private void UpdateNetworkAwareButtonState(bool force = false)
    {
        if (!force && Time.unscaledTime < _nextNetworkStatePollTime)
        {
            return;
        }

        _nextNetworkStatePollTime = Time.unscaledTime + NETWORK_STATE_POLL_INTERVAL_SECONDS;

        bool isOnline = IsOnlineAvailable();
        if (!force && _isOnlineAvailable == isOnline)
        {
            return;
        }

        _isOnlineAvailable = isOnline;

        bool canUseLoginButtons = !_isNicknameFlowActive;

        if (_googleSignInButton != null && _googleSignInButton.gameObject.activeSelf)
        {
            _googleSignInButton.interactable = isOnline && canUseLoginButtons;
        }

        if (_appleSignInButton != null && _appleSignInButton.gameObject.activeSelf)
        {
            _appleSignInButton.interactable = isOnline && canUseLoginButtons;
        }

        if (_guestSignInButton != null)
        {
            _guestSignInButton.interactable = isOnline && canUseLoginButtons;
        }

        if (_nicknameConfirmButton != null && _nicknameConfirmButton.gameObject.activeSelf)
        {
            _nicknameConfirmButton.interactable = _isNicknameFlowActive && !_isNicknameSubmitInProgress;
        }

        if (!_isNicknameFlowActive)
        {
            if (!isOnline && _pendingSignInAttemptCount == 0)
            {
                SetStatus(OFFLINE_GUEST_ONLY_MESSAGE);
            }
            else if (isOnline && _statusText != null && _statusText.text == OFFLINE_GUEST_ONLY_MESSAGE)
            {
                SetStatus(string.Empty);
            }
        }
    }

    private bool CanProceed(bool requiresOnline)
    {
        if (_isNicknameFlowActive)
        {
            SetStatus("닉네임 설정을 먼저 완료해 주세요.");
            return false;
        }

        if (requiresOnline && !IsOnlineAvailable())
        {
            SetStatus(OFFLINE_GUEST_ONLY_MESSAGE);
            return false;
        }

        return true;
    }

    private int BeginSignInAttempt()
    {
        _latestSignInAttemptId++;
        _pendingSignInAttemptCount++;
        UpdateNetworkAwareButtonState(true);
        return _latestSignInAttemptId;
    }

    private void EndSignInAttempt()
    {
        _pendingSignInAttemptCount = Math.Max(0, _pendingSignInAttemptCount - 1);
        UpdateNetworkAwareButtonState(true);
    }

    private bool IsLatestSignInAttempt(int attemptId)
    {
        return attemptId == _latestSignInAttemptId;
    }

    private void SetNicknameSubmitInProgress(bool isInProgress)
    {
        _isNicknameSubmitInProgress = isInProgress;
        UpdateNetworkAwareButtonState(true);
    }

    private void SetLoadingIndicatorVisible(bool isVisible)
    {
        if (_loadingIndicator == null)
        {
            return;
        }

        if (_loadingIndicator.activeSelf == isVisible)
        {
            return;
        }

        _loadingIndicator.SetActive(isVisible);
    }

    private void SetStatus(string message)
    {
        if (_statusText != null)
        {
            _statusText.text = message ?? string.Empty;
        }
    }

    private bool ShowNicknameSetup(string uid, PlayerProfile profile, string initialDisplayName)
    {
        if (!HasNicknameSetupBindings())
        {
            return false;
        }

        _pendingNicknameUid = uid ?? string.Empty;
        _pendingNicknameProfile = profile;
        _isNicknameFlowActive = true;

        SetEntryPanelVisible(false);
        _nicknameSetupPanel.SetActive(true);

        _nicknameInputField.text = string.IsNullOrWhiteSpace(initialDisplayName)
            ? string.Empty
            : initialDisplayName.Trim();

        _nicknameInputField.ActivateInputField();
        _nicknameInputField.Select();

        if (_nicknameGuideText != null)
        {
            _nicknameGuideText.text = NICKNAME_GUIDE_MESSAGE;
        }

        SetStatus(NICKNAME_REQUIRED_MESSAGE);
        UpdateNetworkAwareButtonState(true);
        return true;
    }

    private void HideNicknameSetup(bool resetPendingState, bool showEntryPanel = true)
    {
        if (_nicknameSetupPanel != null)
        {
            _nicknameSetupPanel.SetActive(false);
        }

        SetEntryPanelVisible(showEntryPanel);
        _isNicknameFlowActive = false;

        if (resetPendingState)
        {
            _pendingNicknameUid = string.Empty;
            _pendingNicknameProfile = null;
        }
    }

    private bool HasNicknameSetupBindings()
    {
        return _nicknameSetupPanel != null &&
               _nicknameInputField != null &&
               _nicknameConfirmButton != null;
    }

    private void SetEntryPanelVisible(bool isVisible)
    {
        if (_entryPanel == null || _entryPanel.activeSelf == isVisible)
        {
            return;
        }

        _entryPanel.SetActive(isVisible);
    }

    private static bool IsNicknameSetupRequired(PlayerProfile profile)
    {
        if (profile == null)
        {
            return true;
        }

        if (profile.IsGuest)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            return true;
        }

        return !profile.IsDisplayNameConfirmed;
    }

    private string ResolveInitialDisplayName(string uid, PlayerProfile profile)
    {
        if (profile != null && !string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            return profile.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_authService?.DisplayName))
        {
            return _authService.DisplayName.Trim();
        }

        return $"Player_{GetSafeSuffix(uid, 4)}";
    }

    private static bool TryValidateDisplayName(string rawName, out string normalizedName, out string validationMessage)
    {
        normalizedName = string.IsNullOrWhiteSpace(rawName) ? string.Empty : rawName.Trim();

        if (normalizedName.Length < DISPLAY_NAME_MIN_LENGTH || normalizedName.Length > DISPLAY_NAME_MAX_LENGTH)
        {
            validationMessage = $"닉네임은 {DISPLAY_NAME_MIN_LENGTH}~{DISPLAY_NAME_MAX_LENGTH}자로 입력해 주세요.";
            return false;
        }

        for (int i = 0; i < normalizedName.Length; i++)
        {
            char c = normalizedName[i];
            if (char.IsWhiteSpace(c))
            {
                validationMessage = "닉네임에는 공백을 사용할 수 없습니다.";
                return false;
            }

            if (char.IsLetterOrDigit(c) || c == '_')
            {
                continue;
            }

            validationMessage = "닉네임은 한글/영문/숫자/밑줄(_)만 사용할 수 있습니다.";
            return false;
        }

        validationMessage = string.Empty;
        return true;
    }

    private static string MapErrorMessage(AuthResult result)
    {
        return result.ErrorCode switch
        {
            AuthErrorCode.NetworkError => string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "네트워크 오류가 발생했습니다. 인터넷 연결을 확인해 주세요."
                : result.ErrorMessage,
            AuthErrorCode.NotSupported => string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "현재 빌드에서 지원되지 않는 로그인 방식입니다."
                : result.ErrorMessage,
            AuthErrorCode.DuplicateDisplayName => string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? DUPLICATE_DISPLAY_NAME_MESSAGE
                : result.ErrorMessage,
            _ => string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "로그인에 실패했습니다. 다시 시도해 주세요."
                : result.ErrorMessage
        };
    }

    private static bool IsOnlineAvailable()
    {
        return Application.internetReachability != NetworkReachability.NotReachable;
    }

    private static void BindButtonClick(Button button, UnityAction action, string methodName)
    {
        if (button == null || action == null)
        {
            return;
        }

        // Why: 인스펙터 Persistent 바인딩이 이미 있으면 런타임 AddListener를 추가하지 않아
        // 동일 클릭에서 핸들러가 2회 실행되는 문제를 방지합니다.
        if (HasPersistentMethod(button.onClick, methodName))
        {
            button.onClick.RemoveListener(action);
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static bool HasPersistentMethod(UnityEventBase unityEvent, string methodName)
    {
        if (unityEvent == null || string.IsNullOrWhiteSpace(methodName))
        {
            return false;
        }

        int count = unityEvent.GetPersistentEventCount();
        for (int i = 0; i < count; i++)
        {
            if (string.Equals(unityEvent.GetPersistentMethodName(i), methodName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetSafeSuffix(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0000";
        }

        string trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed.ToUpperInvariant()
            : trimmed[^maxLength..].ToUpperInvariant();
    }

    #endregion
}
