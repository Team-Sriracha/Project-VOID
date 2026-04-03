using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 인증/데이터 서비스를 초기화하고 ServiceLocator에 등록합니다.
/// </summary>
public class AuthServiceInitializer : MonoBehaviour
{
    #region Constants

    private const string FIREBASE_APP_TYPE_NAME = "Firebase.FirebaseApp, Firebase.App";
    private const string CLIENT_FORCE_OFFLINE_MESSAGE = "클라이언트 인증이 강제로 오프라인 모드로 설정되어 로그인할 수 없습니다.";
    private const string FIREBASE_UNAVAILABLE_MESSAGE = "Firebase 초기화가 완료되지 않아 로그인할 수 없습니다.";
    private const string FIREBASE_DEFAULT_APP_UNAVAILABLE_MESSAGE =
        "Firebase 기본 앱을 생성하지 못했습니다. Android 빌드에 google-services.xml이 포함되었는지 확인해 주세요.";

    #endregion

    #region Serialized Fields

    [Header("초기화 옵션")]
    [SerializeField] private bool _forceOfflineMode;
    [SerializeField] private bool _dontDestroyOnLoad = true;

    #endregion

    #region Private Fields

    private static AuthServiceInitializer _instance;
    private Task _initializationTask;
    private bool _isInitialized;

    #endregion

    #region Properties

    public static AuthServiceInitializer Instance => _instance;
    public bool IsInitialized => _isInitialized;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;

        if (_dontDestroyOnLoad)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private async void Start()
    {
        try
        {
            await InitializeServicesAsync();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AuthServiceInitializer] 서비스 초기화 예외: {ex}");
            _isInitialized = false;
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 서비스를 초기화합니다.
    /// </summary>
    public async Task InitializeServicesAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        if (_initializationTask != null)
        {
            await _initializationTask;
            return;
        }

        _initializationTask = InitializeServicesInternalAsync();
        await _initializationTask;
    }

    private async Task InitializeServicesInternalAsync()
    {
        _isInitialized = false;

        RegisterCoreServerServices();

        try
        {
            bool isServerRuntime = IsServerRuntime();
            (bool isFirebaseReady, string firebaseFailureReason) = await TryPrepareFirebaseAsync();
            bool useOfflineServerServices = isServerRuntime && (_forceOfflineMode || !isFirebaseReady);
            bool useUnavailableClientServices = !isServerRuntime && (_forceOfflineMode || !isFirebaseReady);
            string clientUnavailableReason = ResolveClientUnavailableReason(useUnavailableClientServices, firebaseFailureReason);

            RegisterFirebaseRuntimeContext(useOfflineServerServices || useUnavailableClientServices);

            IPlayerDataService playerDataService = CreatePlayerDataService(useOfflineServerServices, useUnavailableClientServices);
            ServiceLocator.Register(playerDataService);

            IAuthService authService = CreateAuthService(
                playerDataService,
                useOfflineServerServices,
                useUnavailableClientServices,
                clientUnavailableReason);
            ServiceLocator.Register(authService);

            RegisterPlatformSocialTokenProviders();
            RegisterDefaultSocialTokenProviders();

            string mode = useOfflineServerServices
                ? "ServerOffline"
                : useUnavailableClientServices
                    ? "ClientUnavailable"
                    : "Firebase";
            Debug.Log($"[AuthServiceInitializer] Auth services registered. Mode={mode}");

            if (useUnavailableClientServices && !string.IsNullOrWhiteSpace(clientUnavailableReason))
            {
                Debug.LogWarning($"[AuthServiceInitializer] ClientUnavailable 사유: {clientUnavailableReason}");
            }

            _isInitialized = true;
        }
        catch
        {
            _isInitialized = false;
            _initializationTask = null;
            throw;
        }
    }

    private IPlayerDataService CreatePlayerDataService(bool useOfflineServerServices, bool useUnavailableClientServices)
    {
        if (useOfflineServerServices)
        {
            return new OfflinePlayerDataService();
        }

        if (useUnavailableClientServices)
        {
            return new UnavailablePlayerDataService();
        }

        return new BackendPlayerDataService();
    }

    private IAuthService CreateAuthService(
        IPlayerDataService playerDataService,
        bool useOfflineServerServices,
        bool useUnavailableClientServices,
        string clientUnavailableReason)
    {
        if (useOfflineServerServices)
        {
            return new OfflineAuthService();
        }

        if (useUnavailableClientServices)
        {
            return new UnavailableAuthService(clientUnavailableReason);
        }

        return new FirebaseAuthService(playerDataService);
    }

    private static void RegisterPlatformSocialTokenProviders()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        RegisterIfMissing<IGoogleAuthTokenProvider>(() => new DesktopGoogleAuthTokenProvider());
#elif UNITY_ANDROID && !UNITY_EDITOR
        RegisterIfMissing<IGoogleAuthTokenProvider>(() => new AndroidCredentialManagerGoogleAuthTokenProvider());
#endif
    }

    private static void RegisterCoreServerServices()
    {
        // 서버에서는 인증 UI를 사용하지 않지만, 신원 검증/전적 서비스는 등록해 둡니다.
        ServiceLocator.Register<IIdentityVerificationService>(new BackendIdentityVerificationService());
        ServiceLocator.Register<IServerMatchResultService>(new BackendMatchResultService());
    }

    private static void RegisterFirebaseRuntimeContext(bool useOffline)
    {
        ServiceLocator.Unregister<FirebaseRuntimeAppContext>();

        if (useOffline)
        {
            return;
        }

        FirebaseRuntimeAppContext context = TryCreateFirebaseRuntimeAppContext();
        if (context == null || context.App == null)
        {
            return;
        }

        ServiceLocator.Register(context);
        Debug.Log($"[AuthServiceInitializer] Firebase App context registered. AppName={context.AppName}, ProcessScoped={context.IsProcessScoped}");
    }

    private static FirebaseRuntimeAppContext TryCreateFirebaseRuntimeAppContext()
    {
        Type firebaseAppType = ResolveType(FIREBASE_APP_TYPE_NAME);
        if (firebaseAppType == null)
        {
            Debug.LogWarning("[AuthServiceInitializer] Firebase App 타입을 찾지 못해 런타임 App 컨텍스트를 생성하지 않습니다.");
            return null;
        }

        if (!TryGetFirebaseDefaultApp(firebaseAppType, out object defaultApp, out string failureReason))
        {
            Debug.LogWarning($"[AuthServiceInitializer] Firebase 런타임 App 컨텍스트 생성 실패: {failureReason}");
            return null;
        }

        string defaultAppName = ReadStringProperty(defaultApp, "Name");

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (TryCreateProcessScopedFirebaseApp(firebaseAppType, defaultApp, out object scopedApp, out string scopedAppName))
        {
            return new FirebaseRuntimeAppContext(scopedApp, scopedAppName, true);
        }
#endif

        return new FirebaseRuntimeAppContext(defaultApp, defaultAppName, false);
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    private static bool TryCreateProcessScopedFirebaseApp(
        Type firebaseAppType,
        object defaultApp,
        out object scopedApp,
        out string scopedAppName)
    {
        scopedApp = null;
        scopedAppName = string.Empty;

        if (firebaseAppType == null || defaultApp == null)
        {
            return false;
        }

        object options = ReadObjectProperty(defaultApp, "Options");
        if (options == null)
        {
            return false;
        }

        int processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        scopedAppName = $"ProjectVoid_{processId}_{Guid.NewGuid():N}";

        MethodInfo[] createMethods = firebaseAppType.GetMethods(BindingFlags.Public | BindingFlags.Static);
        for (int i = 0; i < createMethods.Length; i++)
        {
            MethodInfo method = createMethods[i];
            if (!string.Equals(method.Name, "Create", StringComparison.Ordinal))
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2)
            {
                continue;
            }

            if (!parameters[0].ParameterType.IsInstanceOfType(options))
            {
                continue;
            }

            if (parameters[1].ParameterType != typeof(string))
            {
                continue;
            }

            try
            {
                object created = method.Invoke(null, new[] { options, scopedAppName });
                if (created == null)
                {
                    continue;
                }

                scopedApp = created;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AuthServiceInitializer] Process-scoped Firebase App 생성 실패(계속 시도): {ex.Message}");
            }
        }

        return false;
    }
#endif

    private static object ReadObjectProperty(object target, string propertyName)
    {
        if (target == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(target);
    }

    private static string ReadStringProperty(object target, string propertyName)
    {
        object value = ReadObjectProperty(target, propertyName);
        return value?.ToString() ?? string.Empty;
    }

    private static void RegisterDefaultSocialTokenProviders()
    {
        // Why: 소셜 로그인 구현체가 없을 때도 오류 원인을 명확히 안내하기 위해 기본 제공자를 등록합니다.
        RegisterIfMissing<IGoogleAuthTokenProvider>(() => new UnsupportedGoogleAuthTokenProvider());
        RegisterIfMissing<IAppleAuthTokenProvider>(() => new UnsupportedAppleAuthTokenProvider());
    }

    private static void RegisterIfMissing<T>(Func<T> createService) where T : class
    {
        if (createService == null || ServiceLocator.TryGet<T>(out _))
        {
            return;
        }

        T service = createService();
        if (service != null)
        {
            ServiceLocator.Register<T>(service);
        }
    }

    private string ResolveClientUnavailableReason(bool useUnavailableClientServices, string firebaseFailureReason)
    {
        if (!useUnavailableClientServices)
        {
            return string.Empty;
        }

        if (_forceOfflineMode)
        {
            return CLIENT_FORCE_OFFLINE_MESSAGE;
        }

        return string.IsNullOrWhiteSpace(firebaseFailureReason)
            ? FIREBASE_UNAVAILABLE_MESSAGE
            : firebaseFailureReason;
    }

    private static async Task<(bool isReady, string failureReason)> TryPrepareFirebaseAsync()
    {
#if UNITY_SERVER
        return (false, "서버 런타임에서는 Firebase 클라이언트 인증을 사용하지 않습니다.");
#else
        try
        {
            Type firebaseAppType = ResolveType(FIREBASE_APP_TYPE_NAME);
            if (firebaseAppType == null)
            {
                const string message = "Firebase SDK를 찾지 못했습니다. 모바일 빌드에 Firebase App/Auth 패키지가 포함되어 있는지 확인해 주세요.";
                Debug.LogWarning($"[AuthServiceInitializer] {message}");
                return (false, message);
            }

            MethodInfo checkMethod = firebaseAppType.GetMethod(
                "CheckAndFixDependenciesAsync",
                BindingFlags.Public | BindingFlags.Static);
            if (checkMethod == null)
            {
                const string message = "Firebase 의존성 체크 메서드를 찾지 못했습니다. Firebase SDK 설치 상태를 확인해 주세요.";
                Debug.LogWarning($"[AuthServiceInitializer] {message}");
                return (false, message);
            }

            object taskObject = checkMethod.Invoke(null, null);
            if (taskObject is not Task task)
            {
                const string message = "Firebase 의존성 체크 작업을 시작하지 못했습니다.";
                Debug.LogWarning($"[AuthServiceInitializer] {message}");
                return (false, message);
            }

            await task;

            PropertyInfo resultProperty = taskObject.GetType().GetProperty("Result");
            object dependencyStatus = resultProperty?.GetValue(taskObject);
            bool isAvailable = string.Equals(
                dependencyStatus?.ToString(),
                "Available",
                StringComparison.OrdinalIgnoreCase);

            if (!isAvailable)
            {
                string message =
                    $"Firebase 의존성 상태가 '{dependencyStatus}' 입니다. google-services.json, Android Firebase 종속성, Play Services 환경을 확인해 주세요.";
                Debug.LogWarning($"[AuthServiceInitializer] {message}");
                return (false, message);
            }

            if (!TryGetFirebaseDefaultApp(firebaseAppType, out _, out string defaultAppFailureReason))
            {
                Debug.LogWarning($"[AuthServiceInitializer] {defaultAppFailureReason}");
                return (false, defaultAppFailureReason);
            }

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            Exception unwrapped = UnwrapException(ex);
            string detailMessage = FormatExceptionMessage(unwrapped);
            string message = $"Firebase 초기화 예외가 발생했습니다: {detailMessage}";
            Debug.LogWarning($"[AuthServiceInitializer] {message}\n{unwrapped}");
            return (false, message);
        }
#endif
    }

    private static bool TryGetFirebaseDefaultApp(
        Type firebaseAppType,
        out object defaultApp,
        out string failureReason)
    {
        defaultApp = null;
        failureReason = string.Empty;

        if (firebaseAppType == null)
        {
            failureReason = "Firebase App 타입을 찾지 못했습니다.";
            return false;
        }

        PropertyInfo defaultInstanceProperty = firebaseAppType.GetProperty("DefaultInstance", BindingFlags.Public | BindingFlags.Static);
        if (defaultInstanceProperty == null)
        {
            failureReason = "FirebaseApp.DefaultInstance 프로퍼티를 찾지 못했습니다.";
            return false;
        }

        try
        {
            defaultApp = defaultInstanceProperty.GetValue(null);
        }
        catch (Exception ex)
        {
            Exception unwrapped = UnwrapException(ex);
            failureReason = BuildFirebaseDefaultAppFailureMessage(
                $"FirebaseApp.DefaultInstance 접근 예외: {FormatExceptionMessage(unwrapped)}");
            return false;
        }

        if (defaultApp == null)
        {
            failureReason = BuildFirebaseDefaultAppFailureMessage("FirebaseApp.DefaultInstance가 null입니다.");
            return false;
        }

        return true;
    }

    private static string BuildFirebaseDefaultAppFailureMessage(string detailMessage)
    {
        return string.IsNullOrWhiteSpace(detailMessage)
            ? FIREBASE_DEFAULT_APP_UNAVAILABLE_MESSAGE
            : $"{FIREBASE_DEFAULT_APP_UNAVAILABLE_MESSAGE} Detail={detailMessage}";
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
            if (current is TargetInvocationException targetInvocationException &&
                targetInvocationException.InnerException != null)
            {
                current = targetInvocationException.InnerException;
                continue;
            }

            if (current is AggregateException aggregateException &&
                aggregateException.InnerExceptions.Count == 1)
            {
                current = aggregateException.InnerExceptions[0];
                continue;
            }

            return current;
        }
    }

    private static bool IsServerRuntime()
    {
#if UNITY_SERVER
        return true;
#else
        return false;
#endif
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

    #endregion
}
