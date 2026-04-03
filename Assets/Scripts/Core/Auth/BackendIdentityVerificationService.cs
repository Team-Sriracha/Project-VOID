using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 백엔드 ID Token 검증 서비스입니다.
/// </summary>
public class BackendIdentityVerificationService : IIdentityVerificationService
{
    #region Constants

    private const string VERIFY_ENDPOINT_ENV_KEY = "PROJECTVOID_VERIFY_TOKEN_URL";
    private const string VERIFY_ENDPOINT_ARG_PREFIX = "--verify-token-url=";
    private const string BACKEND_SHARED_SECRET_ENV_KEY = "PROJECTVOID_BACKEND_SHARED_SECRET";
    private const string BACKEND_SHARED_SECRET_ARG_PREFIX = "--backend-shared-secret=";
    private const string BACKEND_SHARED_SECRET_HEADER_NAME = "X-ProjectVoid-Server-Key";
    private const string SERVER_CONTEXT_HEADER_NAME = "X-ProjectVoid-Server-Context";
    private const string UNITY_SERVER_PORT_ENV_KEY = "UNITY_SERVER_PORT";
    private const string PORT_ARG_PREFIX_LONG = "--port=";
    private const string PORT_ARG_PREFIX_SHORT = "-port=";
    private const int REQUEST_TIMEOUT_SECONDS = 10;
    private const int HEALTH_CHECK_TIMEOUT_SECONDS = 3;
    private const string HEALTH_CHECK_PATH = "/health";

    #endregion

    #region Public Types

    /// <summary>
    /// 인증 백엔드 상태 정보입니다.
    /// </summary>
    public readonly struct ServiceHealthInfo
    {
        #region Properties

        /// <summary>
        /// 사용 가능 여부입니다.
        /// </summary>
        public bool IsReady { get; }

        /// <summary>
        /// 상태 메시지입니다.
        /// </summary>
        public string StatusMessage { get; }

        /// <summary>
        /// 확인한 엔드포인트입니다.
        /// </summary>
        public string Endpoint { get; }

        #endregion

        #region Initialization

        public ServiceHealthInfo(bool isReady, string statusMessage, string endpoint)
        {
            IsReady = isReady;
            StatusMessage = string.IsNullOrWhiteSpace(statusMessage) ? string.Empty : statusMessage.Trim();
            Endpoint = string.IsNullOrWhiteSpace(endpoint) ? string.Empty : endpoint.Trim();
        }

        #endregion
    }

    #endregion

    #region Private Types

    [Serializable]
    private sealed class VerifyTokenRequest
    {
        public string IdToken;
        public string ServerContext;
    }

    [Serializable]
    private sealed class VerifyTokenResponse
    {
        public bool IsValid;
        public string FirebaseUid;
        public string GuestId;
        public string DisplayName;
        public string Email;
        public bool IsAnonymous;
        public string ErrorMessage;
    }

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public async Task<VerifiedIdentity> VerifyIdTokenAsync(string idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return CreateInvalidIdentity("ID Token이 비어 있습니다.");
        }

        string endpoint = ResolveVerifyEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return CreateInvalidIdentity("PROJECTVOID_VERIFY_TOKEN_URL이 설정되지 않아 온라인 ID Token 검증을 수행할 수 없습니다.");
        }

        try
        {
            string serverContext = ResolveServerContext();
            VerifyTokenRequest requestBody = new VerifyTokenRequest
            {
                IdToken = idToken,
                ServerContext = serverContext
            };
            string requestJson = JsonUtility.ToJson(requestBody);

            using UnityWebRequest request = new UnityWebRequest(endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(requestJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = REQUEST_TIMEOUT_SECONDS;
            request.SetRequestHeader("Content-Type", "application/json");

            string sharedSecret = ResolveBackendSharedSecret();
            if (!string.IsNullOrWhiteSpace(sharedSecret))
            {
                request.SetRequestHeader(BACKEND_SHARED_SECRET_HEADER_NAME, sharedSecret);
            }

            if (!string.IsNullOrWhiteSpace(serverContext))
            {
                request.SetRequestHeader(SERVER_CONTEXT_HEADER_NAME, serverContext);
            }

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                return CreateInvalidIdentity($"토큰 검증 요청 실패: {request.error}");
            }

            if (request.responseCode < 200 || request.responseCode >= 300)
            {
                return CreateInvalidIdentity($"토큰 검증 HTTP 실패: {request.responseCode}");
            }

            string responseJson = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return CreateInvalidIdentity("토큰 검증 응답이 비어 있습니다.");
            }

            VerifyTokenResponse response = JsonUtility.FromJson<VerifyTokenResponse>(responseJson);
            if (response == null)
            {
                return CreateInvalidIdentity("토큰 검증 응답 파싱에 실패했습니다.");
            }

            if (!response.IsValid || string.IsNullOrWhiteSpace(response.FirebaseUid))
            {
                return CreateInvalidIdentity(string.IsNullOrWhiteSpace(response.ErrorMessage)
                    ? "토큰 검증에 실패했습니다."
                    : response.ErrorMessage);
            }

            return new VerifiedIdentity
            {
                IsValid = true,
                FirebaseUid = response.FirebaseUid.Trim(),
                GuestId = string.IsNullOrWhiteSpace(response.GuestId) ? string.Empty : response.GuestId.Trim().ToUpperInvariant(),
                DisplayName = string.IsNullOrWhiteSpace(response.DisplayName)
                    ? $"Player_{GetSafeSuffix(response.FirebaseUid, 4)}"
                    : response.DisplayName.Trim(),
                Email = response.Email ?? string.Empty,
                IsAnonymous = response.IsAnonymous,
                ErrorMessage = string.Empty
            };
        }
        catch (Exception ex)
        {
            return CreateInvalidIdentity($"토큰 검증 예외: {ex.Message}");
        }
    }

    /// <summary>
    /// 인증 백엔드 헬스 상태를 확인합니다.
    /// </summary>
    public static async Task<ServiceHealthInfo> CheckServiceHealthAsync()
    {
        string verifyEndpoint = ResolveVerifyEndpoint();
        if (string.IsNullOrWhiteSpace(verifyEndpoint))
        {
            return new ServiceHealthInfo(false, "PROJECTVOID_VERIFY_TOKEN_URL이 설정되지 않았습니다.", string.Empty);
        }

        string sharedSecret = ResolveBackendSharedSecret();
        if (string.IsNullOrWhiteSpace(sharedSecret))
        {
            return new ServiceHealthInfo(false, "PROJECTVOID_BACKEND_SHARED_SECRET이 설정되지 않았습니다.", verifyEndpoint);
        }

        string healthEndpoint = ResolveHealthEndpoint(verifyEndpoint);
        if (string.IsNullOrWhiteSpace(healthEndpoint))
        {
            return new ServiceHealthInfo(false, "인증 백엔드 health 엔드포인트를 계산할 수 없습니다.", verifyEndpoint);
        }

        try
        {
            using UnityWebRequest request = UnityWebRequest.Get(healthEndpoint);
            request.timeout = HEALTH_CHECK_TIMEOUT_SECONDS;

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                return new ServiceHealthInfo(false, $"인증 백엔드 헬스 체크 실패: {request.error}", healthEndpoint);
            }

            if (request.responseCode < 200 || request.responseCode >= 300)
            {
                return new ServiceHealthInfo(false, $"인증 백엔드 헬스 체크 HTTP 실패: {request.responseCode}", healthEndpoint);
            }

            return new ServiceHealthInfo(true, "인증 백엔드 정상", healthEndpoint);
        }
        catch (Exception ex)
        {
            return new ServiceHealthInfo(false, $"인증 백엔드 헬스 체크 예외: {ex.Message}", healthEndpoint);
        }
    }

    /// <summary>
    /// 인프라성 인증 실패 메시지 여부를 반환합니다.
    /// </summary>
    public static bool IsInfrastructureFailureMessage(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return false;
        }

        return errorMessage.StartsWith("PROJECTVOID_VERIFY_TOKEN_URL", StringComparison.Ordinal) ||
               errorMessage.StartsWith("PROJECTVOID_BACKEND_SHARED_SECRET", StringComparison.Ordinal) ||
               errorMessage.StartsWith("토큰 검증 요청 실패", StringComparison.Ordinal) ||
               errorMessage.StartsWith("토큰 검증 HTTP 실패", StringComparison.Ordinal) ||
               errorMessage.StartsWith("토큰 검증 예외", StringComparison.Ordinal) ||
               errorMessage.StartsWith("인증 백엔드 헬스 체크", StringComparison.Ordinal);
    }

    #endregion

    #region Helper Methods

    private static VerifiedIdentity CreateInvalidIdentity(string errorMessage)
    {
        return new VerifiedIdentity
        {
            IsValid = false,
            ErrorMessage = errorMessage ?? "알 수 없는 오류"
        };
    }

    private static string ResolveVerifyEndpoint()
    {
        return ResolveArgumentOrEnvironment(VERIFY_ENDPOINT_ARG_PREFIX, VERIFY_ENDPOINT_ENV_KEY);
    }

    private static string ResolveBackendSharedSecret()
    {
        return ResolveArgumentOrEnvironment(BACKEND_SHARED_SECRET_ARG_PREFIX, BACKEND_SHARED_SECRET_ENV_KEY);
    }

    private static string ResolveHealthEndpoint(string verifyEndpoint)
    {
        if (string.IsNullOrWhiteSpace(verifyEndpoint) ||
            !Uri.TryCreate(verifyEndpoint, UriKind.Absolute, out Uri verifyUri))
        {
            return string.Empty;
        }

        UriBuilder builder = new UriBuilder(verifyUri)
        {
            Path = HEALTH_CHECK_PATH,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString();
    }

    private static string ResolveServerContext()
    {
        string port = Environment.GetEnvironmentVariable(UNITY_SERVER_PORT_ENV_KEY)?.Trim();
        if (string.IsNullOrWhiteSpace(port))
        {
            port = ResolvePortFromCommandLineArgs();
        }

        if (!string.IsNullOrWhiteSpace(port))
        {
            return $"port:{port}";
        }

        return "global";
    }

    private static string ResolveArgumentOrEnvironment(string argumentPrefix, string environmentKey)
    {
        if (string.IsNullOrWhiteSpace(argumentPrefix))
        {
            return Environment.GetEnvironmentVariable(environmentKey)?.Trim();
        }

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith(argumentPrefix, StringComparison.Ordinal))
            {
                return arg.Substring(argumentPrefix.Length).Trim();
            }
        }

        return Environment.GetEnvironmentVariable(environmentKey)?.Trim();
    }

    private static string ResolvePortFromCommandLineArgs()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (args == null || args.Length == 0)
        {
            return string.Empty;
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (arg.StartsWith(PORT_ARG_PREFIX_LONG, StringComparison.Ordinal))
            {
                return arg.Substring(PORT_ARG_PREFIX_LONG.Length).Trim();
            }

            if (arg.StartsWith(PORT_ARG_PREFIX_SHORT, StringComparison.Ordinal))
            {
                return arg.Substring(PORT_ARG_PREFIX_SHORT.Length).Trim();
            }

            if ((string.Equals(arg, "-port", StringComparison.Ordinal) ||
                 string.Equals(arg, "--port", StringComparison.Ordinal)) &&
                (i + 1) < args.Length)
            {
                return args[i + 1].Trim();
            }
        }

        return string.Empty;
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
