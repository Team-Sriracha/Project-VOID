using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Android 환경에서 Google Sign-In 네이티브 플러그인을 사용해 토큰을 발급합니다.
/// </summary>
public class AndroidGoogleAuthTokenProvider : IGoogleAuthTokenProvider
{
    #region Constants

    private const string GOOGLE_SIGN_IN_TYPE_NAME = "Google.GoogleSignIn";
    private const string GOOGLE_SIGN_IN_CONFIGURATION_TYPE_NAME = "Google.GoogleSignInConfiguration";
    private const string STREAMING_CONFIG_FILE_NAME = "google-signin-mobile.json";
    private const string WEB_CLIENT_ID_ENV_KEY = "PROJECTVOID_GOOGLE_WEB_CLIENT_ID";
    private const string WEB_CLIENT_ID_ARG_PREFIX = "--google-web-client-id=";
    private const int GOOGLE_WEB_CLIENT_TYPE = 3;
    private const int REQUEST_TIMEOUT_SECONDS = 15;

    #endregion

    #region Private Types

    [Serializable]
    private sealed class MobileGoogleSignInConfig
    {
        public string web_client_id;
    }

    [Serializable]
    private sealed class GoogleServicesConfig
    {
        public GoogleClientConfig[] client;
    }

    [Serializable]
    private sealed class GoogleClientConfig
    {
        public GoogleOAuthClientConfig[] oauth_client;
    }

    [Serializable]
    private sealed class GoogleOAuthClientConfig
    {
        public string client_id;
        public int client_type;
    }

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public async Task<SocialAuthToken> RequestGoogleTokenAsync()
    {
#if !UNITY_ANDROID || UNITY_EDITOR
        throw new NotSupportedException("Android 네이티브 Google 로그인은 Android 실제 빌드에서만 지원됩니다.");
#else
        try
        {
            string webClientId = await ResolveWebClientIdAsync();
            if (string.IsNullOrWhiteSpace(webClientId))
            {
                throw new NotSupportedException(
                    "Android 네이티브 Google 로그인 Web Client ID를 찾지 못했습니다. " +
                    "PROJECTVOID_GOOGLE_WEB_CLIENT_ID 또는 StreamingAssets/google-signin-mobile.json 설정을 확인해 주세요.");
            }

            object googleSignIn = CreateGoogleSignInInstance(webClientId);
            object signInTask = TryInvokeInstanceMethod(googleSignIn, "SignIn");
            if (signInTask == null)
            {
                throw new NotSupportedException("GoogleSignIn.SignIn 호출에 실패했습니다.");
            }

            object signInUser = await AwaitTaskResultAsync(signInTask);
            if (signInUser == null)
            {
                throw new OperationCanceledException("Google 로그인이 취소되었습니다.");
            }

            string idToken = GetStringProperty(signInUser, "IdToken");
            string accessToken = GetStringProperty(signInUser, "AccessToken");

            if (string.IsNullOrWhiteSpace(idToken))
            {
                throw new NotSupportedException("Google ID 토큰을 수신하지 못했습니다. Web Client ID 설정을 확인해 주세요.");
            }

            return new SocialAuthToken(idToken, accessToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new NotSupportedException($"Android 네이티브 Google 로그인 실패: {ex.Message}");
        }
#endif
    }

    #endregion

    #region Google Sign-In

    private static object CreateGoogleSignInInstance(string webClientId)
    {
        Type googleSignInType = ResolveType(GOOGLE_SIGN_IN_TYPE_NAME);
        Type configurationType = ResolveType(GOOGLE_SIGN_IN_CONFIGURATION_TYPE_NAME);
        if (googleSignInType == null || configurationType == null)
        {
            throw new NotSupportedException(
                "Google Sign-In Unity 플러그인을 찾지 못했습니다. " +
                "google-signin-unity 패키지를 프로젝트에 추가해 주세요.");
        }

        object configuration = Activator.CreateInstance(configurationType);
        if (configuration == null)
        {
            throw new NotSupportedException("GoogleSignInConfiguration 인스턴스를 생성하지 못했습니다.");
        }

        TrySetPropertyValue(configuration, "WebClientId", webClientId);
        TrySetPropertyValue(configuration, "RequestIdToken", true);
        TrySetPropertyValue(configuration, "RequestEmail", true);
        TrySetPropertyValue(configuration, "UseGameSignIn", false);
        TrySetPropertyValue(configuration, "RequestAuthCode", false);
        TrySetPropertyValue(configuration, "ForceTokenRefresh", false);

        if (!TrySetPropertyValue(null, googleSignInType, "Configuration", configuration))
        {
            throw new NotSupportedException("GoogleSignIn.Configuration 설정에 실패했습니다.");
        }

        object defaultInstance = GetPropertyValue(null, googleSignInType, "DefaultInstance");
        if (defaultInstance == null)
        {
            throw new NotSupportedException("GoogleSignIn.DefaultInstance를 가져오지 못했습니다.");
        }

        // Why: 계정 선택 UI를 매번 확실히 띄우기 위해 기존 세션을 정리합니다.
        TryInvokeInstanceMethod(defaultInstance, "SignOut");
        return defaultInstance;
    }

    #endregion

    #region Config

    private static async Task<string> ResolveWebClientIdAsync()
    {
        string envClientId = Environment.GetEnvironmentVariable(WEB_CLIENT_ID_ENV_KEY);
        if (!string.IsNullOrWhiteSpace(envClientId))
        {
            return envClientId.Trim();
        }

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith(WEB_CLIENT_ID_ARG_PREFIX, StringComparison.Ordinal))
            {
                continue;
            }

            string value = arg[WEB_CLIENT_ID_ARG_PREFIX.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        string json = await ReadStreamingAssetTextAsync(STREAMING_CONFIG_FILE_NAME);
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        MobileGoogleSignInConfig mobileConfig = JsonUtility.FromJson<MobileGoogleSignInConfig>(json);
        if (!string.IsNullOrWhiteSpace(mobileConfig?.web_client_id))
        {
            return mobileConfig.web_client_id.Trim();
        }

        GoogleServicesConfig config = JsonUtility.FromJson<GoogleServicesConfig>(json);
        if (config?.client == null)
        {
            return string.Empty;
        }

        for (int i = 0; i < config.client.Length; i++)
        {
            GoogleClientConfig client = config.client[i];
            if (client?.oauth_client == null)
            {
                continue;
            }

            for (int j = 0; j < client.oauth_client.Length; j++)
            {
                GoogleOAuthClientConfig oauthClient = client.oauth_client[j];
                if (oauthClient == null ||
                    oauthClient.client_type != GOOGLE_WEB_CLIENT_TYPE ||
                    string.IsNullOrWhiteSpace(oauthClient.client_id))
                {
                    continue;
                }

                return oauthClient.client_id.Trim();
            }
        }

        return string.Empty;
    }

    private static async Task<string> ReadStreamingAssetTextAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        if (path.IndexOf("://", StringComparison.Ordinal) >= 0 ||
            path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase))
        {
            using UnityWebRequest request = UnityWebRequest.Get(path);
            request.timeout = REQUEST_TIMEOUT_SECONDS;

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

#if UNITY_2020_2_OR_NEWER
            if (request.result != UnityWebRequest.Result.Success)
#else
            if (request.isHttpError || request.isNetworkError)
#endif
            {
                throw new NotSupportedException(
                    $"StreamingAssets 설정 파일을 읽지 못했습니다. Path={path}, Error={request.error}");
            }

            return request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        }

        if (!File.Exists(path))
        {
            throw new NotSupportedException($"StreamingAssets 설정 파일을 찾지 못했습니다: {path}");
        }

        return await File.ReadAllTextAsync(path);
    }

    #endregion

    #region Reflection Helpers

    private static Type ResolveType(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
        {
            return null;
        }

        Type resolvedType = Type.GetType(fullTypeName);
        if (resolvedType != null)
        {
            return resolvedType;
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

    private static object TryInvokeInstanceMethod(object target, string methodName, params object[] args)
    {
        if (target == null || string.IsNullOrWhiteSpace(methodName))
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

            try
            {
                return method.Invoke(target, BuildInvokeArguments(parameters, args));
            }
            catch (ArgumentException)
            {
                // Why: 오버로드 불일치 시 다음 메서드를 시도합니다.
            }
        }

        return null;
    }

    private static bool TrySetPropertyValue(object target, string propertyName, object value)
    {
        return TrySetPropertyValue(target, target?.GetType(), propertyName, value);
    }

    private static bool TrySetPropertyValue(object target, Type targetType, string propertyName, object value)
    {
        if (targetType == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        BindingFlags flags = BindingFlags.Public;
        flags |= target == null ? BindingFlags.Static : BindingFlags.Instance;

        PropertyInfo property = targetType.GetProperty(propertyName, flags);
        if (property == null || !property.CanWrite)
        {
            return false;
        }

        object convertedValue = ConvertValue(value, property.PropertyType);
        property.SetValue(target, convertedValue);
        return true;
    }

    private static object GetPropertyValue(object target, Type targetType, string propertyName)
    {
        if (targetType == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        BindingFlags flags = BindingFlags.Public;
        flags |= target == null ? BindingFlags.Static : BindingFlags.Instance;

        PropertyInfo property = targetType.GetProperty(propertyName, flags);
        return property?.GetValue(target);
    }

    private static string GetStringProperty(object target, string propertyName)
    {
        if (target == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return string.Empty;
        }

        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        object value = property?.GetValue(target);
        return value?.ToString() ?? string.Empty;
    }

    private static object ConvertValue(object value, Type targetType)
    {
        if (targetType == null)
        {
            return value;
        }

        if (value == null)
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        Type valueType = value.GetType();
        if (targetType.IsAssignableFrom(valueType))
        {
            return value;
        }

        Type underlyingType = Nullable.GetUnderlyingType(targetType);
        Type conversionType = underlyingType ?? targetType;

        try
        {
            return Convert.ChangeType(value, conversionType);
        }
        catch
        {
            return value;
        }
    }

    private static bool CanInvokeWithArguments(ParameterInfo[] parameters, object[] args)
    {
        if (parameters == null)
        {
            return false;
        }

        if (args == null)
        {
            args = Array.Empty<object>();
        }

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
                continue;
            }

            if (parameters[i].IsOptional)
            {
                invokeArguments[i] = Type.Missing;
                continue;
            }

            Type parameterType = parameters[i].ParameterType;
            bool isNullableValueType = Nullable.GetUnderlyingType(parameterType) != null;
            invokeArguments[i] = (!parameterType.IsValueType || isNullableValueType)
                ? null
                : Activator.CreateInstance(parameterType);
        }

        return invokeArguments;
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

    #endregion
}
