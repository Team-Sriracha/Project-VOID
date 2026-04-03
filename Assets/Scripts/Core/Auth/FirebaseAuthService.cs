using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Firebase 인증 서비스 구현체입니다.
/// </summary>
public class FirebaseAuthService : IAuthService
{
    #region Constants

    private const string FIREBASE_AUTH_TYPE_NAME = "Firebase.Auth.FirebaseAuth, Firebase.Auth";
    private const string FIREBASE_USER_TYPE_NAME = "Firebase.Auth.FirebaseUser, Firebase.Auth";
    private const string FIREBASE_AUTH_ERROR_TYPE_NAME = "Firebase.Auth.AuthError, Firebase.Auth";
    private const string FIREBASE_GOOGLE_PROVIDER_TYPE_NAME = "Firebase.Auth.GoogleAuthProvider, Firebase.Auth";
    private const string FIREBASE_OAUTH_PROVIDER_TYPE_NAME = "Firebase.Auth.OAuthProvider, Firebase.Auth";
    private const string AUTH_UNAVAILABLE_MESSAGE = "인터넷 연결 또는 Firebase 초기화가 준비되지 않아 로그인할 수 없습니다.";

    #endregion

    #region Private Fields

    private readonly OfflineAuthService _fallbackService;
    private readonly IPlayerDataService _playerDataService;

    private readonly object _firebaseAuth;
    private readonly bool _isFirebaseReady;
    private readonly EventInfo _stateChangedEvent;

    private string _guestId = string.Empty;
    private bool _suppressFallbackEvents;

    #endregion

    #region Properties

    /// <inheritdoc />
    public string UserId
    {
        get
        {
            if (ShouldReadIdentityFromFallback())
            {
                return _fallbackService.UserId;
            }

            if (!TryGetCurrentUser(out object user))
            {
                return string.Empty;
            }

            return GetStringProperty(user, "UserId");
        }
    }

    /// <inheritdoc />
    public string GuestId => ShouldUseFallbackSession()
        ? _fallbackService.GuestId
        : _guestId;

    /// <inheritdoc />
    public string DisplayName
    {
        get
        {
            if (ShouldReadIdentityFromFallback())
            {
                return _fallbackService.DisplayName;
            }

            if (!TryGetCurrentUser(out object user))
            {
                return string.Empty;
            }

            return GetStringProperty(user, "DisplayName");
        }
    }

    /// <inheritdoc />
    public string Email
    {
        get
        {
            if (ShouldReadIdentityFromFallback())
            {
                return _fallbackService.Email;
            }

            if (!TryGetCurrentUser(out object user))
            {
                return string.Empty;
            }

            return GetStringProperty(user, "Email");
        }
    }

    /// <inheritdoc />
    public bool IsSignedIn
    {
        get
        {
            if (ShouldUseFallbackSession())
            {
                return true;
            }

            return _isFirebaseReady ? TryGetCurrentUser(out _) : _fallbackService.IsSignedIn;
        }
    }

    /// <inheritdoc />
    public bool IsAnonymous
    {
        get
        {
            if (ShouldReadIdentityFromFallback())
            {
                return _fallbackService.IsAnonymous;
            }

            if (!TryGetCurrentUser(out object user))
            {
                return false;
            }

            return GetBoolProperty(user, "IsAnonymous");
        }
    }

    /// <inheritdoc />
    public bool IsGoogleSignedIn
    {
        get
        {
            if (ShouldReadIdentityFromFallback() || IsAnonymous)
            {
                return false;
            }

            return HasCurrentUserProvider("google.com");
        }
    }

    #endregion

    #region Events

    /// <inheritdoc />
    public event Action<AuthResult> OnAuthStateChanged;

    #endregion

    #region Constructor

    /// <summary>
    /// 서비스 인스턴스를 생성합니다.
    /// </summary>
    public FirebaseAuthService(IPlayerDataService playerDataService)
    {
        _playerDataService = playerDataService;
        _fallbackService = new OfflineAuthService();
        _fallbackService.OnAuthStateChanged += HandleFallbackAuthStateChanged;

        try
        {
            object firebaseAuth = TryGetFirebaseAuthInstance(out EventInfo stateChangedEvent);
            _firebaseAuth = firebaseAuth;
            _stateChangedEvent = stateChangedEvent;
            _isFirebaseReady = firebaseAuth != null;

            if (!_isFirebaseReady)
            {
                Debug.LogWarning("[FirebaseAuthService] Firebase Auth를 찾지 못해 로그인 불가 상태로 동작합니다.");
                return;
            }

            SubscribeAuthStateChanged();
            _ = SyncGuestIdForCurrentUserAsync();
        }
        catch (Exception ex)
        {
            Exception unwrapped = UnwrapException(ex);
            _firebaseAuth = null;
            _stateChangedEvent = null;
            _isFirebaseReady = false;
            Debug.LogError($"[FirebaseAuthService] Firebase Auth 초기화 예외: {FormatExceptionMessage(unwrapped)}\n{unwrapped}");
        }
    }

    #endregion

    #region Sign In

    /// <inheritdoc />
    public async Task<AuthResult> SignInAnonymouslyAsync()
    {
        if (!_isFirebaseReady)
        {
            return AuthResult.Failure(AuthErrorCode.NetworkError, AUTH_UNAVAILABLE_MESSAGE);
        }

        try
        {
            await ClearFallbackSessionAsync();

            object taskObject = TryInvokeInstanceMethod(_firebaseAuth, "SignInAnonymouslyAsync");
            if (taskObject == null)
            {
                return AuthResult.Failure(AuthErrorCode.Unknown, "Firebase 익명 로그인 메서드를 찾을 수 없습니다.");
            }

            await AwaitTaskAsync(taskObject);

            string uid = UserId;
            _guestId = ResolveGuestIdFromUid(uid);

            return await BuildSuccessResultAsync();
        }
        catch (Exception ex)
        {
            AuthResult firebaseFailure = MapExceptionToAuthResult(ex);
            Debug.LogWarning($"[FirebaseAuthService] Firebase 익명 로그인 실패. ErrorCode={firebaseFailure.ErrorCode}, Message={firebaseFailure.ErrorMessage}");
            return firebaseFailure.ErrorCode == AuthErrorCode.None
                ? AuthResult.Failure(AuthErrorCode.NetworkError, AUTH_UNAVAILABLE_MESSAGE)
                : firebaseFailure;
        }
    }

    /// <inheritdoc />
    public async Task<AuthResult> SignInWithGoogleAsync()
    {
        if (!_isFirebaseReady)
        {
            return AuthResult.Failure(AuthErrorCode.NetworkError, AUTH_UNAVAILABLE_MESSAGE);
        }

        Func<Task<SocialAuthToken>> requestTokenAsync = null;
        if (ServiceLocator.TryGet<IGoogleAuthTokenProvider>(out IGoogleAuthTokenProvider tokenProvider) &&
            tokenProvider != null &&
            tokenProvider is not UnsupportedGoogleAuthTokenProvider)
        {
            requestTokenAsync = tokenProvider.RequestGoogleTokenAsync;
        }
        else
        {
            string providerName = tokenProvider == null ? "(null)" : tokenProvider.GetType().Name;
            Debug.LogWarning($"[FirebaseAuthService] Google 토큰 제공자 사용 불가: {providerName}");
        }

        return await SignInWithSocialAsync(
            providerId: "google.com",
            providerDisplayName: "Google",
            requestTokenAsync: requestTokenAsync,
            createCredential: CreateGoogleCredential,
            credentialCreateFailureMessage: "Google Firebase Credential 생성에 실패했습니다.",
            requireNativeProvider: IsNativeGoogleSignInRequiredOnCurrentPlatform(),
            nativeProviderErrorPrefix: "Android 네이티브 Google 로그인 초기화 실패",
            nativeProviderRequiredMessage: "Android 네이티브 Google 로그인 제공자가 필요합니다. Credential Manager 브리지 구성을 확인해 주세요.",
            desktopProviderRequiredMessage: "데스크톱 환경에서는 Google 토큰 제공자 구현이 필요합니다.");
    }

    /// <inheritdoc />
    public async Task<AuthResult> SignInWithAppleAsync()
    {
        if (!_isFirebaseReady)
        {
            return AuthResult.Failure(AuthErrorCode.NetworkError, AUTH_UNAVAILABLE_MESSAGE);
        }

        Func<Task<SocialAuthToken>> requestTokenAsync = null;
        if (ServiceLocator.TryGet<IAppleAuthTokenProvider>(out IAppleAuthTokenProvider tokenProvider) &&
            tokenProvider != null &&
            tokenProvider is not UnsupportedAppleAuthTokenProvider)
        {
            requestTokenAsync = tokenProvider.RequestAppleTokenAsync;
        }

        return await SignInWithSocialAsync(
            providerId: "apple.com",
            providerDisplayName: "Apple",
            requestTokenAsync: requestTokenAsync,
            createCredential: CreateAppleCredential,
            credentialCreateFailureMessage: "Apple Firebase Credential 생성에 실패했습니다.",
            requireNativeProvider: false,
            nativeProviderErrorPrefix: string.Empty,
            nativeProviderRequiredMessage: string.Empty,
            desktopProviderRequiredMessage: "데스크톱 환경에서는 Apple 토큰 제공자 구현이 필요합니다.");
    }

    #endregion

    #region Session

    /// <inheritdoc />
    public async Task SignOutAsync()
    {
        if (!_isFirebaseReady)
        {
            await _fallbackService.SignOutAsync();
            return;
        }

        TryInvokeInstanceMethod(_firebaseAuth, "SignOut");
        await ClearFallbackSessionAsync();
        _guestId = string.Empty;
        OnAuthStateChanged?.Invoke(AuthResult.Failure(AuthErrorCode.None, string.Empty));
    }

    /// <inheritdoc />
    public async Task<string> GetIdTokenAsync(bool forceRefresh = false)
    {
        if (ShouldReadIdentityFromFallback())
        {
            return await _fallbackService.GetIdTokenAsync(forceRefresh);
        }

        if (!TryGetCurrentUser(out object user))
        {
            return string.Empty;
        }

        try
        {
            object tokenTask = TryInvokeInstanceMethod(user, "TokenAsync", forceRefresh)
                ?? TryInvokeInstanceMethod(user, "GetIdTokenAsync", forceRefresh)
                ?? TryInvokeInstanceMethod(user, "TokenAsync");

            if (tokenTask == null)
            {
                return string.Empty;
            }

            object tokenObject = await AwaitTaskResultAsync(tokenTask);
            return tokenObject?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    #endregion

    #region Firebase Event

    private void HandleFallbackAuthStateChanged(AuthResult result)
    {
        if (_suppressFallbackEvents)
        {
            return;
        }

        if (_isFirebaseReady && TryGetCurrentUser(out _))
        {
            return;
        }

        OnAuthStateChanged?.Invoke(result);
    }

    private void SubscribeAuthStateChanged()
    {
        if (_stateChangedEvent == null || _firebaseAuth == null)
        {
            return;
        }

        EventHandler handler = OnFirebaseStateChanged;
        _stateChangedEvent.AddEventHandler(_firebaseAuth, handler);
    }

    private async void OnFirebaseStateChanged(object sender, EventArgs e)
    {
        if (!IsSignedIn)
        {
            _guestId = string.Empty;
            OnAuthStateChanged?.Invoke(AuthResult.Failure(AuthErrorCode.None, string.Empty));
            return;
        }

        await SyncGuestIdForCurrentUserAsync();
        AuthResult result = await BuildSuccessResultAsync();
        OnAuthStateChanged?.Invoke(result);
    }

    #endregion

    #region Helper Methods

    private async Task<AuthResult> SignInWithCredentialAsync(object credential)
    {
        try
        {
            object taskObject = TryInvokeInstanceMethod(_firebaseAuth, "SignInWithCredentialAsync", credential);
            if (taskObject == null)
            {
                return AuthResult.Failure(AuthErrorCode.Unknown, "소셜 로그인 메서드를 찾을 수 없습니다.");
            }

            await AwaitTaskAsync(taskObject);
            await EnsureEmailProfileAsync(GetCurrentDefaultDisplayName(), Email);
            return await BuildSuccessResultAsync();
        }
        catch (Exception ex)
        {
            return MapExceptionToAuthResult(ex);
        }
    }

    private async Task<AuthResult?> TrySignInWithFirebaseProviderAsync(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        object providerData = TryCreateFederatedOAuthProviderData(providerId);
        if (providerData != null)
        {
            AuthResult? signInResult = await TrySignInWithFirebaseProviderObjectAsync(providerData);
            if (signInResult.HasValue)
            {
                return signInResult;
            }
        }

        object provider = TryCreateFederatedOAuthProvider(providerId, providerData);
        if (provider != null)
        {
            AuthResult? signInResult = await TrySignInWithFirebaseProviderObjectAsync(provider);
            if (signInResult.HasValue)
            {
                return signInResult;
            }
        }

        return null;
    }

    private async Task<AuthResult?> TrySignInWithFirebaseProviderObjectAsync(object providerObject)
    {
        if (providerObject == null)
        {
            return null;
        }

        object signInTask = TryInvokeInstanceMethod(_firebaseAuth, "SignInWithProviderAsync", providerObject);
        if (signInTask == null)
        {
            return null;
        }

        try
        {
            await AwaitTaskAsync(signInTask);
            await EnsureEmailProfileAsync(GetCurrentDefaultDisplayName(), Email);
            return await BuildSuccessResultAsync();
        }
        catch (Exception ex)
        {
            return MapExceptionToAuthResult(ex);
        }
    }

    private static object TryCreateFederatedOAuthProviderData(string providerId)
    {
        Type providerDataType = ResolveType("Firebase.Auth.FederatedOAuthProviderData, Firebase.Auth");
        if (providerDataType == null)
        {
            return null;
        }

        object providerData = TryCreateTypeInstance(providerDataType, providerId);
        if (providerData == null)
        {
            return null;
        }

        TrySetPropertyValue(providerData, "ProviderId", providerId);
        return providerData;
    }

    private static object TryCreateFederatedOAuthProvider(string providerId, object providerData)
    {
        Type providerType = ResolveType("Firebase.Auth.FederatedOAuthProvider, Firebase.Auth");
        if (providerType == null)
        {
            return null;
        }

        object provider = TryCreateTypeInstance(providerType, providerData)
            ?? TryCreateTypeInstance(providerType, providerId)
            ?? TryCreateTypeInstance(providerType, providerId, providerData);

        if (provider == null)
        {
            return null;
        }

        if (providerData != null)
        {
            if (!TrySetPropertyValue(provider, "ProviderData", providerData))
            {
                TryInvokeInstanceMethod(provider, "SetProviderData", providerData);
            }
        }

        TrySetPropertyValue(provider, "ProviderId", providerId);
        return provider;
    }

    private async Task<bool> EnsureEmailProfileAsync(string preferredDisplayName, string email)
    {
        string uid = UserId;
        if (string.IsNullOrWhiteSpace(uid))
        {
            return false;
        }

        PlayerProfile profile = await _playerDataService.GetPlayerProfileAsync(uid);
        if (profile == null)
        {
            string displayName = ResolveDisplayName(preferredDisplayName, uid);
            profile = await _playerDataService.CreatePlayerProfileAsync(uid, displayName, email, false);
            if (profile == null)
            {
                // Why: 기본 닉네임 충돌 시 로그인 자체는 유지하고, 닉네임에 랜덤 suffix를 붙여 재시도합니다.
                for (int attempt = 0; attempt < 5 && profile == null; attempt++)
                {
                    string fallbackDisplayName = $"{displayName}_{GetSafeSuffix(Guid.NewGuid().ToString("N"), 4)}";
                    profile = await _playerDataService.CreatePlayerProfileAsync(uid, fallbackDisplayName, email, false);
                }

                if (profile == null)
                {
                    return false;
                }
            }
        }

        profile.IsGuest = false;
        if (string.IsNullOrWhiteSpace(profile.Email))
        {
            profile.Email = email ?? string.Empty;
        }

        _guestId = string.Empty;
        return true;
    }

    private async Task<AuthResult> SignInWithSocialAsync(
        string providerId,
        string providerDisplayName,
        Func<Task<SocialAuthToken>> requestTokenAsync,
        Func<SocialAuthToken, object> createCredential,
        string credentialCreateFailureMessage,
        bool requireNativeProvider,
        string nativeProviderErrorPrefix,
        string nativeProviderRequiredMessage,
        string desktopProviderRequiredMessage)
    {
        await ClearFallbackSessionAsync();

        string tokenProviderError = string.Empty;
        if (requestTokenAsync != null && createCredential != null)
        {
            try
            {
                SocialAuthToken token = await requestTokenAsync();
                object credential = createCredential(token);
                if (credential == null)
                {
                    return AuthResult.Failure(AuthErrorCode.NotSupported, credentialCreateFailureMessage);
                }

                return await SignInWithCredentialAsync(credential);
            }
            catch (Exception ex)
            {
                tokenProviderError = ex.Message ?? string.Empty;
            }
        }

        if (requireNativeProvider)
        {
            if (!string.IsNullOrWhiteSpace(tokenProviderError))
            {
                return AuthResult.Failure(AuthErrorCode.NotSupported, $"{nativeProviderErrorPrefix}: {tokenProviderError}");
            }

            return AuthResult.Failure(AuthErrorCode.NotSupported, nativeProviderRequiredMessage);
        }

        if (IsFirebaseProviderSignInUnsupportedOnCurrentPlatform())
        {
            if (!string.IsNullOrWhiteSpace(tokenProviderError))
            {
                return AuthResult.Failure(AuthErrorCode.NotSupported, $"{providerDisplayName} 로그인 경로를 초기화하지 못했습니다: {tokenProviderError}");
            }

            return AuthResult.Failure(AuthErrorCode.NotSupported, desktopProviderRequiredMessage);
        }

        AuthResult? providerSignInResult = await TrySignInWithFirebaseProviderAsync(providerId);
        if (providerSignInResult.HasValue)
        {
            return providerSignInResult.Value;
        }

        if (!string.IsNullOrWhiteSpace(tokenProviderError))
        {
            return AuthResult.Failure(AuthErrorCode.NotSupported, $"{providerDisplayName} 로그인 경로를 초기화하지 못했습니다: {tokenProviderError}");
        }

        return AuthResult.Failure(
            AuthErrorCode.NotSupported,
            $"{providerDisplayName} 로그인 토큰 제공자 또는 Firebase Provider 로그인 경로를 찾을 수 없습니다.");
    }

    private Task SyncGuestIdForCurrentUserAsync()
    {
        if (ShouldUseFallbackSession())
        {
            _guestId = _fallbackService.GuestId;
            return Task.CompletedTask;
        }

        if (!IsSignedIn)
        {
            _guestId = string.Empty;
            return Task.CompletedTask;
        }

        string uid = UserId;
        if (string.IsNullOrWhiteSpace(uid))
        {
            _guestId = string.Empty;
            return Task.CompletedTask;
        }

        if (IsAnonymous)
        {
            _guestId = ResolveGuestIdFromUid(uid);
            return Task.CompletedTask;
        }

        _guestId = string.Empty;
        return Task.CompletedTask;
    }

    private async Task ClearFallbackSessionAsync()
    {
        if (!_fallbackService.IsSignedIn)
        {
            return;
        }

        _suppressFallbackEvents = true;
        try
        {
            await _fallbackService.SignOutAsync();
        }
        finally
        {
            _suppressFallbackEvents = false;
        }
    }

    private bool ShouldUseFallbackSession()
    {
        if (!_fallbackService.IsSignedIn)
        {
            return false;
        }

        if (!_isFirebaseReady)
        {
            return true;
        }

        return !TryGetCurrentUser(out _);
    }

    private bool ShouldReadIdentityFromFallback()
    {
        return ShouldUseFallbackSession() || !_isFirebaseReady;
    }

    private async Task<AuthResult> BuildSuccessResultAsync()
    {
        await SyncGuestIdForCurrentUserAsync();
        return AuthResult.Success(UserId, GuestId, ResolveCurrentDisplayName());
    }

    private object CreateGoogleCredential(SocialAuthToken token)
    {
        Type googleProviderType = ResolveType(FIREBASE_GOOGLE_PROVIDER_TYPE_NAME);
        if (googleProviderType != null)
        {
            object credential = TryInvokeStaticMethod(googleProviderType, "GetCredential", token.IdToken, token.AccessToken);
            if (credential != null)
            {
                return credential;
            }
        }

        // Why: GoogleAuthProvider가 없는 환경에서는 OAuthProvider로 대체 시도합니다.
        return CreateOAuthCredential("google.com", token);
    }

    private object CreateAppleCredential(SocialAuthToken token)
    {
        return CreateOAuthCredential("apple.com", token);
    }

    private object CreateOAuthCredential(string providerId, SocialAuthToken token)
    {
        Type oauthProviderType = ResolveType(FIREBASE_OAUTH_PROVIDER_TYPE_NAME);
        if (oauthProviderType == null)
        {
            return null;
        }

        object credential = TryInvokeStaticMethod(oauthProviderType, "GetCredential", providerId, token.IdToken, token.AccessToken, token.RawNonce)
            ?? TryInvokeStaticMethod(oauthProviderType, "GetCredential", providerId, token.IdToken, token.AccessToken)
            ?? TryInvokeStaticMethod(oauthProviderType, "GetCredential", providerId, token.IdToken)
            ?? TryInvokeStaticMethod(oauthProviderType, "GetCredential", providerId, token.AccessToken, token.RawNonce);

        return credential;
    }

    private string GetCurrentDefaultDisplayName()
    {
        string uid = UserId;
        return ResolveDisplayName(DisplayName, uid);
    }

    private string ResolveCurrentDisplayName()
    {
        if (IsAnonymous)
        {
            string guestId = string.IsNullOrWhiteSpace(GuestId) ? ResolveGuestIdFromUid(UserId) : GuestId;
            return $"Guest_{GetSafeSuffix(guestId, 4)}";
        }

        return string.IsNullOrWhiteSpace(DisplayName) ? GetCurrentDefaultDisplayName() : DisplayName;
    }

    private static string ResolveGuestIdFromUid(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return "G-00000000";
        }

        return $"G-{GetSafeSuffix(uid, 8)}";
    }

    private static string ResolveDisplayName(string displayName, string uid)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        return $"Player_{GetSafeSuffix(uid, 4)}";
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

    private static bool IsFirebaseProviderSignInUnsupportedOnCurrentPlatform()
    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
        return true;
#else
        return false;
#endif
    }

    private static bool IsNativeGoogleSignInRequiredOnCurrentPlatform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }

    private static object TryGetFirebaseAuthInstance(out EventInfo stateChangedEvent)
    {
        stateChangedEvent = null;

        Type authType = ResolveType(FIREBASE_AUTH_TYPE_NAME);
        if (authType == null)
        {
            return null;
        }

        object auth = TryGetFirebaseAuthFromRuntimeContext(authType);
        if (auth == null)
        {
            PropertyInfo defaultInstanceProperty = authType.GetProperty("DefaultInstance", BindingFlags.Public | BindingFlags.Static);
            auth = defaultInstanceProperty?.GetValue(null);
        }

        if (auth != null)
        {
            stateChangedEvent = auth.GetType().GetEvent("StateChanged", BindingFlags.Public | BindingFlags.Instance)
                ?? authType.GetEvent("StateChanged", BindingFlags.Public | BindingFlags.Instance);
        }

        return auth;
    }

    private static object TryGetFirebaseAuthFromRuntimeContext(Type authType)
    {
        if (authType == null)
        {
            return null;
        }

        if (!ServiceLocator.TryGet<FirebaseRuntimeAppContext>(out FirebaseRuntimeAppContext context) ||
            context == null ||
            context.App == null)
        {
            return null;
        }

        object auth = TryInvokeStaticMethod(authType, "GetAuth", context.App);
        if (auth != null)
        {
            Debug.Log($"[FirebaseAuthService] FirebaseAuth.GetAuth(runtimeApp) 사용: AppName={context.AppName}, ProcessScoped={context.IsProcessScoped}");
        }

        return auth;
    }

    private bool TryGetCurrentUser(out object user)
    {
        user = null;
        if (_firebaseAuth == null)
        {
            return false;
        }

        Type authType = _firebaseAuth.GetType();
        PropertyInfo currentUserProperty = authType.GetProperty("CurrentUser", BindingFlags.Public | BindingFlags.Instance);
        user = currentUserProperty?.GetValue(_firebaseAuth);

        if (user == null)
        {
            return false;
        }

        Type firebaseUserType = ResolveType(FIREBASE_USER_TYPE_NAME);
        return firebaseUserType == null || firebaseUserType.IsInstanceOfType(user);
    }

    private bool HasCurrentUserProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId) || !TryGetCurrentUser(out object user))
        {
            return false;
        }

        string currentProviderId = GetStringProperty(user, "ProviderId");
        if (string.Equals(currentProviderId, providerId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        PropertyInfo providerDataProperty = user.GetType().GetProperty("ProviderData", BindingFlags.Public | BindingFlags.Instance);
        object providerData = providerDataProperty?.GetValue(user);
        if (providerData is not System.Collections.IEnumerable enumerable)
        {
            return false;
        }

        foreach (object entry in enumerable)
        {
            string entryProviderId = GetStringProperty(entry, "ProviderId");
            if (string.Equals(entryProviderId, providerId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetStringProperty(object target, string propertyName)
    {
        if (target == null)
        {
            return string.Empty;
        }

        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        object value = property?.GetValue(target);
        return value?.ToString() ?? string.Empty;
    }

    private static bool GetBoolProperty(object target, string propertyName)
    {
        if (target == null)
        {
            return false;
        }

        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        object value = property?.GetValue(target);

        if (value is bool boolValue)
        {
            return boolValue;
        }

        if (value == null)
        {
            return false;
        }

        return bool.TryParse(value.ToString(), out bool parsed) && parsed;
    }

    private static bool TrySetPropertyValue(object target, string propertyName, object value)
    {
        if (target == null)
        {
            return false;
        }

        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property == null || !property.CanWrite)
        {
            return false;
        }

        try
        {
            property.SetValue(target, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object TryCreateTypeInstance(Type targetType, params object[] preferredArgs)
    {
        if (targetType == null)
        {
            return null;
        }

        try
        {
            object defaultInstance = Activator.CreateInstance(targetType, true);
            if (defaultInstance != null)
            {
                return defaultInstance;
            }
        }
        catch
        {
            // Why: 기본 생성자가 없거나 접근 불가하면 생성자 시그니처 매칭으로 계속 시도합니다.
        }

        ConstructorInfo[] constructors = targetType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        for (int i = 0; i < constructors.Length; i++)
        {
            ConstructorInfo constructor = constructors[i];
            ParameterInfo[] parameters = constructor.GetParameters();
            if (!CanInvokeWithArguments(parameters, preferredArgs))
            {
                continue;
            }

            object[] invokeArguments = BuildInvokeArguments(parameters, preferredArgs);
            try
            {
                return constructor.Invoke(invokeArguments);
            }
            catch
            {
                // Why: 생성자 오버로드 시그니처가 달라 실패하면 다음 생성자를 시도합니다.
            }
        }

        return null;
    }

    private static object TryInvokeInstanceMethod(object target, string methodName, params object[] args)
    {
        if (target == null)
        {
            return null;
        }

        MethodInfo[] methods = target.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == methodName)
            .ToArray();

        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            ParameterInfo[] parameters = method.GetParameters();
            if (!CanInvokeWithArguments(parameters, args))
            {
                continue;
            }

            object[] invokeArguments = BuildInvokeArguments(parameters, args);
            try
            {
                return method.Invoke(target, invokeArguments);
            }
            catch (ArgumentException)
            {
                // Why: 오버로드 시그니처가 맞지 않으면 다음 메서드를 시도합니다.
            }
        }

        return null;
    }

    private static object TryInvokeStaticMethod(Type targetType, string methodName, params object[] args)
    {
        if (targetType == null)
        {
            return null;
        }

        MethodInfo[] methods = targetType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == methodName)
            .ToArray();

        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            ParameterInfo[] parameters = method.GetParameters();
            if (!CanInvokeWithArguments(parameters, args))
            {
                continue;
            }

            object[] invokeArguments = BuildInvokeArguments(parameters, args);
            try
            {
                return method.Invoke(null, invokeArguments);
            }
            catch (ArgumentException)
            {
                // Why: 오버로드 시그니처가 맞지 않으면 다음 메서드를 시도합니다.
            }
        }

        return null;
    }

    private static bool CanInvokeWithArguments(ParameterInfo[] parameters, object[] args)
    {
        if (args.Length > parameters.Length)
        {
            return false;
        }

        for (int i = args.Length; i < parameters.Length; i++)
        {
            if (parameters[i].IsOptional)
            {
                continue;
            }

            Type parameterType = parameters[i].ParameterType;
            bool isNullableValueType = Nullable.GetUnderlyingType(parameterType) != null;
            if (parameterType.IsValueType && !isNullableValueType)
            {
                return false;
            }
        }

        return true;
    }

    private static object[] BuildInvokeArguments(ParameterInfo[] parameters, object[] args)
    {
        object[] invokeArguments = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i < args.Length)
            {
                invokeArguments[i] = args[i];
            }
            else
            {
                if (parameters[i].IsOptional)
                {
                    invokeArguments[i] = Type.Missing;
                    continue;
                }

                Type parameterType = parameters[i].ParameterType;
                bool isNullableValueType = Nullable.GetUnderlyingType(parameterType) != null;
                if (!parameterType.IsValueType || isNullableValueType)
                {
                    invokeArguments[i] = null;
                }
                else
                {
                    invokeArguments[i] = Activator.CreateInstance(parameterType);
                }
            }
        }

        return invokeArguments;
    }

    private static async Task AwaitTaskAsync(object taskObject)
    {
        if (taskObject is Task task)
        {
            await task;
        }
    }

    private static async Task<object> AwaitTaskResultAsync(object taskObject)
    {
        if (taskObject is not Task task)
        {
            return null;
        }

        await task;

        PropertyInfo resultProperty = taskObject.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
        return resultProperty?.GetValue(taskObject);
    }

    private static Type ResolveType(string assemblyQualifiedTypeName)
    {
        if (string.IsNullOrWhiteSpace(assemblyQualifiedTypeName))
        {
            return null;
        }

        Type resolvedType = Type.GetType(assemblyQualifiedTypeName);
        if (resolvedType != null)
        {
            return resolvedType;
        }

        int commaIndex = assemblyQualifiedTypeName.IndexOf(',');
        string fullTypeName = commaIndex >= 0
            ? assemblyQualifiedTypeName[..commaIndex].Trim()
            : assemblyQualifiedTypeName.Trim();

        if (string.IsNullOrWhiteSpace(fullTypeName))
        {
            return null;
        }

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type candidate = assemblies[i].GetType(fullTypeName, false);
            if (candidate != null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static AuthResult MapExceptionToAuthResult(Exception exception)
    {
        Exception unwrapped = UnwrapException(exception);
        string authErrorName = ResolveAuthErrorName(unwrapped);

        AuthErrorCode errorCode = authErrorName switch
        {
            "InvalidEmail" => AuthErrorCode.InvalidEmail,
            "WrongPassword" => AuthErrorCode.WrongPassword,
            "UserNotFound" => AuthErrorCode.UserNotFound,
            "InvalidCredential" => AuthErrorCode.WrongPassword,
            "InvalidLoginCredentials" => AuthErrorCode.WrongPassword,
            "EmailNotFound" => AuthErrorCode.UserNotFound,
            "EmailAlreadyInUse" => AuthErrorCode.EmailAlreadyInUse,
            "WeakPassword" => AuthErrorCode.WeakPassword,
            "NetworkRequestFailed" => AuthErrorCode.NetworkError,
            "CredentialAlreadyInUse" => AuthErrorCode.EmailAlreadyInUse,
            "Unimplemented" => AuthErrorCode.NotSupported,
            _ => AuthErrorCode.Unknown
        };

        string message = string.IsNullOrWhiteSpace(unwrapped.Message)
            ? "인증 중 오류가 발생했습니다."
            : unwrapped.Message;

        Debug.LogWarning($"[FirebaseAuthService] Auth 예외 매핑: RawType={unwrapped.GetType().Name}, AuthErrorName={authErrorName}, ErrorCode={errorCode}, Message={message}");

        return AuthResult.Failure(errorCode, message);
    }

    private static string ResolveAuthErrorName(Exception exception)
    {
        PropertyInfo errorCodeProperty = exception.GetType().GetProperty("ErrorCode", BindingFlags.Public | BindingFlags.Instance);
        object errorCodeValue = errorCodeProperty?.GetValue(exception);

        if (errorCodeValue != null)
        {
            if (errorCodeValue.GetType().IsEnum)
            {
                return errorCodeValue.ToString();
            }

            if (long.TryParse(errorCodeValue.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long code))
            {
                Type authErrorType = ResolveType(FIREBASE_AUTH_ERROR_TYPE_NAME);
                if (authErrorType != null)
                {
                    string enumName = Enum.GetName(authErrorType, code);
                    if (!string.IsNullOrWhiteSpace(enumName))
                    {
                        return enumName;
                    }
                }
            }
        }

        string message = exception.Message ?? string.Empty;
        if (message.IndexOf("invalid email", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "InvalidEmail";
        }

        if (message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 &&
            message.IndexOf("wrong", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "WrongPassword";
        }

        if (message.IndexOf("INVALID_LOGIN_CREDENTIALS", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "InvalidLoginCredentials";
        }

        if (message.IndexOf("invalid credential", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "InvalidCredential";
        }

        if (message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "UserNotFound";
        }

        if (message.IndexOf("email not found", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "EmailNotFound";
        }

        if (message.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0 &&
            message.IndexOf("use", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "EmailAlreadyInUse";
        }

        if (message.IndexOf("weak", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "WeakPassword";
        }

        if (message.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "NetworkRequestFailed";
        }

        return string.Empty;
    }

    private static string FormatExceptionMessage(Exception exception)
    {
        if (exception == null)
        {
            return "알 수 없는 예외";
        }

        return string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : $"{exception.GetType().Name}: {exception.Message}";
    }

    private static Exception UnwrapException(Exception exception)
    {
        Exception current = exception;

        while (true)
        {
            if (current is TargetInvocationException targetInvocationException && targetInvocationException.InnerException != null)
            {
                current = targetInvocationException.InnerException;
                continue;
            }

            if (current is AggregateException aggregateException && aggregateException.InnerExceptions.Count == 1)
            {
                current = aggregateException.InnerExceptions[0];
                continue;
            }

            return current;
        }
    }

    #endregion
}
