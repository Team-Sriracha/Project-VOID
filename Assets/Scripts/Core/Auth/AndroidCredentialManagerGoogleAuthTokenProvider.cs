using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Android Credential Manager 기반 네이티브 Google Sign-In 토큰 제공자입니다.
/// </summary>
public class AndroidCredentialManagerGoogleAuthTokenProvider : IGoogleAuthTokenProvider
{
    #region Constants

    private const string BRIDGE_CLASS_NAME = "com.projectvoid.auth.GoogleCredentialManagerBridge";
    private const string UNITY_RECEIVER_OBJECT_NAME = "GoogleCredentialManagerReceiver";
    private const string UNITY_SUCCESS_METHOD = "OnGoogleSignInSuccess";
    private const string UNITY_ERROR_METHOD = "OnGoogleSignInError";

    private const string STREAMING_CONFIG_FILE_NAME = "google-signin-mobile.json";
    private const string WEB_CLIENT_ID_ENV_KEY = "PROJECTVOID_GOOGLE_WEB_CLIENT_ID";
    private const string WEB_CLIENT_ID_ARG_PREFIX = "--google-web-client-id=";

    private const int REQUEST_TIMEOUT_SECONDS = 20;
    private const int AUTH_TIMEOUT_SECONDS = 90;

    #endregion

    #region Private Types

    [Serializable]
    private sealed class MobileGoogleSignInConfig
    {
        public string web_client_id;
    }

    [Serializable]
    private sealed class SuccessPayload
    {
        public string idToken;
        public string accessToken;
    }

    [Serializable]
    private sealed class ErrorPayload
    {
        public string code;
        public string message;
    }

    private sealed class CallbackReceiver : MonoBehaviour
    {
        #region Events

        public event Action<string> SuccessReceived;
        public event Action<string> ErrorReceived;

        #endregion

        #region Unity Message Handlers

        public void OnGoogleSignInSuccess(string payload)
        {
            SuccessReceived?.Invoke(payload ?? string.Empty);
        }

        public void OnGoogleSignInError(string payload)
        {
            ErrorReceived?.Invoke(payload ?? string.Empty);
        }

        #endregion
    }

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public async Task<SocialAuthToken> RequestGoogleTokenAsync()
    {
#if !UNITY_ANDROID || UNITY_EDITOR
        throw new NotSupportedException("Android 네이티브 Google 로그인은 Android 실제 빌드에서만 지원됩니다.");
#else
        string webClientId = await ResolveWebClientIdAsync();
        if (string.IsNullOrWhiteSpace(webClientId))
        {
            throw new NotSupportedException(
                "Android 네이티브 Google 로그인 Web Client ID를 찾지 못했습니다. " +
                "PROJECTVOID_GOOGLE_WEB_CLIENT_ID 또는 StreamingAssets/google-signin-mobile.json 설정을 확인해 주세요.");
        }

        CallbackReceiver receiver = EnsureCallbackReceiver();
        var tcs = new TaskCompletionSource<SocialAuthToken>();

        void OnSuccess(string payload)
        {
            if (TryParseSuccessPayload(payload, out SocialAuthToken token, out string error))
            {
                tcs.TrySetResult(token);
                return;
            }

            tcs.TrySetException(new NotSupportedException(error));
        }

        void OnError(string payload)
        {
            ParseErrorPayload(payload, out string code, out string message);
            if (string.Equals(code, "canceled", StringComparison.OrdinalIgnoreCase))
            {
                tcs.TrySetException(new OperationCanceledException(string.IsNullOrWhiteSpace(message)
                    ? "Google 로그인이 취소되었습니다."
                    : message));
                return;
            }

            string resolvedMessage = string.IsNullOrWhiteSpace(message)
                ? "Android 네이티브 Google 로그인 중 오류가 발생했습니다."
                : message;
            tcs.TrySetException(new NotSupportedException(resolvedMessage));
        }

        receiver.SuccessReceived += OnSuccess;
        receiver.ErrorReceived += OnError;

        try
        {
            StartNativeGoogleSignIn(webClientId);

            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(AUTH_TIMEOUT_SECONDS)));
            if (completed != tcs.Task)
            {
                throw new TimeoutException("Google 로그인 승인 대기 시간이 초과되었습니다.");
            }

            return await tcs.Task;
        }
        finally
        {
            receiver.SuccessReceived -= OnSuccess;
            receiver.ErrorReceived -= OnError;
        }
#endif
    }

    #endregion

    #region Native Bridge

    private static void StartNativeGoogleSignIn(string webClientId)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        if (activity == null)
        {
            throw new NotSupportedException("Android Activity를 찾지 못해 Google 로그인을 시작할 수 없습니다.");
        }

        using AndroidJavaClass bridge = new AndroidJavaClass(BRIDGE_CLASS_NAME);
        bridge.CallStatic(
            "beginSignIn",
            activity,
            UNITY_RECEIVER_OBJECT_NAME,
            UNITY_SUCCESS_METHOD,
            UNITY_ERROR_METHOD,
            webClientId);
#endif
    }

    private static CallbackReceiver EnsureCallbackReceiver()
    {
        GameObject target = GameObject.Find(UNITY_RECEIVER_OBJECT_NAME);
        if (target == null)
        {
            target = new GameObject(UNITY_RECEIVER_OBJECT_NAME);
            UnityEngine.Object.DontDestroyOnLoad(target);
        }

        CallbackReceiver receiver = target.GetComponent<CallbackReceiver>();
        if (receiver == null)
        {
            receiver = target.AddComponent<CallbackReceiver>();
        }

        return receiver;
    }

    #endregion

    #region Payload Parsing

    private static bool TryParseSuccessPayload(string payload, out SocialAuthToken token, out string error)
    {
        token = default;
        error = string.Empty;

        SuccessPayload data = ParseJson<SuccessPayload>(payload);
        if (data == null)
        {
            error = "Google 로그인 응답 형식이 올바르지 않습니다.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(data.idToken))
        {
            error = "Google ID 토큰을 수신하지 못했습니다.";
            return false;
        }

        token = new SocialAuthToken(data.idToken, data.accessToken ?? string.Empty);
        return true;
    }

    private static void ParseErrorPayload(string payload, out string code, out string message)
    {
        code = string.Empty;
        message = string.Empty;

        ErrorPayload data = ParseJson<ErrorPayload>(payload);
        if (data == null)
        {
            if (!string.IsNullOrWhiteSpace(payload))
            {
                message = payload;
            }

            return;
        }

        code = data.code ?? string.Empty;
        message = data.message ?? string.Empty;
    }

    private static T ParseJson<T>(string payload) where T : class
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<T>(payload);
        }
        catch
        {
            return null;
        }
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

        MobileGoogleSignInConfig config = JsonUtility.FromJson<MobileGoogleSignInConfig>(json);
        return string.IsNullOrWhiteSpace(config?.web_client_id)
            ? string.Empty
            : config.web_client_id.Trim();
    }

    private static async Task<string> ReadStreamingAssetTextAsync(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        string path = Path.Combine(Application.streamingAssetsPath, fileName);
        bool isWebUri = path.IndexOf("://", StringComparison.Ordinal) >= 0 ||
                        path.StartsWith("jar:", StringComparison.OrdinalIgnoreCase);

        if (isWebUri)
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
}
