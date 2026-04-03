using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Windows/Editor 환경에서 Google Desktop OAuth(PKCE) 토큰을 발급합니다.
/// </summary>
public class DesktopGoogleAuthTokenProvider : IGoogleAuthTokenProvider
{
    #region Constants

    private const string AUTHORIZATION_ENDPOINT = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TOKEN_ENDPOINT = "https://oauth2.googleapis.com/token";
    private const string GOOGLE_SCOPE = "openid email profile";
    private const string DESKTOP_GOOGLE_CONFIG_FILE_NAME = "google-services-desktop.json";

    private const string DESKTOP_CLIENT_ID_ENV_KEY = "PROJECTVOID_GOOGLE_DESKTOP_CLIENT_ID";
    private const string DESKTOP_CLIENT_SECRET_ENV_KEY = "PROJECTVOID_GOOGLE_DESKTOP_CLIENT_SECRET";
    private const string DESKTOP_REDIRECT_URI_ENV_KEY = "PROJECTVOID_GOOGLE_DESKTOP_REDIRECT_URI";

    private const string DESKTOP_CLIENT_ID_ARG_PREFIX = "--google-desktop-client-id=";
    private const string DESKTOP_CLIENT_SECRET_ARG_PREFIX = "--google-desktop-client-secret=";
    private const string DESKTOP_REDIRECT_URI_ARG_PREFIX = "--google-desktop-redirect-uri=";

    private const int REQUEST_TIMEOUT_SECONDS = 30;
    private const int AUTH_TIMEOUT_SECONDS = 300;
    private const int AUTO_LOOPBACK_BIND_MAX_ATTEMPTS = 8;

    #endregion

    #region Private Types

    [Serializable]
    private sealed class GoogleServicesConfig
    {
        public DesktopOAuthConfig desktop_google_oauth;
        public GoogleClientConfig[] client;
    }

    [Serializable]
    private sealed class DesktopOAuthConfig
    {
        public string client_id;
        public string client_secret;
        public string redirect_uri;

        // Why: 기존 파일 호환용 필드입니다.
        public string device_client_id;
        public string device_client_secret;
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

    [Serializable]
    private sealed class InstalledDesktopOAuthRootConfig
    {
        public InstalledDesktopOAuthConfig installed;
    }

    [Serializable]
    private sealed class InstalledDesktopOAuthConfig
    {
        public string client_id;
        public string client_secret;
        public string[] redirect_uris;
    }

    [Serializable]
    private sealed class TokenResponse
    {
        public string access_token;
        public string id_token;
        public string error;
        public string error_description;
    }

    private sealed class DesktopOAuthCredentials
    {
        public string ClientId;
        public string ClientSecret;
        public string RedirectUri;
        public string Source;
    }

    private sealed class AuthorizationCallback
    {
        public string Code;
        public string State;
        public string Error;
        public string ErrorDescription;
    }

    private readonly struct HttpFormResponse
    {
        public HttpFormResponse(bool isSuccess, long statusCode, string body, string error)
        {
            IsSuccess = isSuccess;
            StatusCode = statusCode;
            Body = body ?? string.Empty;
            Error = error ?? string.Empty;
        }

        public bool IsSuccess { get; }
        public long StatusCode { get; }
        public string Body { get; }
        public string Error { get; }
    }

    private readonly struct TokenExchangeAttempt
    {
        public TokenExchangeAttempt(HttpFormResponse response, TokenResponse payload)
        {
            Response = response;
            Payload = payload;
        }

        public HttpFormResponse Response { get; }
        public TokenResponse Payload { get; }
    }

    private sealed class LoopbackRedirectListener : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly int _port;
        private readonly string _redirectUri;
        private bool _disposed;

        public string RedirectUri => _redirectUri;

        public LoopbackRedirectListener(string redirectUri)
        {
            if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri parsedUri))
            {
                throw new NotSupportedException($"Desktop OAuth redirect_uri를 해석할 수 없습니다: {redirectUri}");
            }

            string normalizedPath = string.IsNullOrWhiteSpace(parsedUri.AbsolutePath) ? "/" : parsedUri.AbsolutePath;
            if (!normalizedPath.EndsWith("/", StringComparison.Ordinal))
            {
                normalizedPath += "/";
            }

            _listener = new TcpListener(IPAddress.Loopback, parsedUri.Port);

            try
            {
                _listener.Start(1);
                _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _redirectUri = $"{parsedUri.Scheme}://{parsedUri.Host}:{_port}{normalizedPath}";
            }
            catch (Exception ex)
            {
                throw new NotSupportedException(
                    $"로컬 OAuth 콜백 리스너를 시작하지 못했습니다. RedirectUri={redirectUri}, Error={ex.Message}");
            }
        }

        public async Task<AuthorizationCallback> WaitForCallbackAsync(TimeSpan timeout)
        {
            Task<TcpClient> acceptTask = _listener.AcceptTcpClientAsync();
            Task completedTask = await Task.WhenAny(acceptTask, Task.Delay(timeout));
            if (completedTask != acceptTask)
            {
                throw new TimeoutException("Google 로그인 승인 대기 시간이 초과되었습니다.");
            }

            using TcpClient client = await acceptTask;
            using NetworkStream stream = client.GetStream();

            string requestHead = await ReadHttpRequestHeadAsync(stream);
            string requestTarget = ExtractRequestTarget(requestHead);
            string queryString = ExtractQueryStringFromRequestTarget(requestTarget, _port);
            Dictionary<string, string> query = ParseQuery(queryString);

            AuthorizationCallback callback = new AuthorizationCallback
            {
                Code = GetQueryValue(query, "code"),
                State = GetQueryValue(query, "state"),
                Error = GetQueryValue(query, "error"),
                ErrorDescription = GetQueryValue(query, "error_description")
            };

            await WriteBrowserResponseAsync(stream, callback);
            return callback;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _listener.Stop();
        }
    }

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public async Task<SocialAuthToken> RequestGoogleTokenAsync()
    {
        DesktopOAuthCredentials credentials = LoadDesktopGoogleCredentials();
        Debug.Log(
            $"[DesktopGoogleAuthTokenProvider] OAuth 설정 로드: Source={credentials.Source}, " +
            $"ClientId={MaskClientId(credentials.ClientId)}, " +
            $"HasSecret={!string.IsNullOrWhiteSpace(credentials.ClientSecret)}, " +
            $"RedirectUri={(string.IsNullOrWhiteSpace(credentials.RedirectUri) ? "(auto loopback)" : credentials.RedirectUri)}");

        bool hasFixedRedirectUri = !string.IsNullOrWhiteSpace(credentials.RedirectUri);
        int maxAttempts = hasFixedRedirectUri ? 1 : AUTO_LOOPBACK_BIND_MAX_ATTEMPTS;
        Exception lastBindConflict = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            string requestedRedirectUri = ResolveRedirectUri(credentials.RedirectUri, 0);

            try
            {
                using LoopbackRedirectListener listener = new LoopbackRedirectListener(requestedRedirectUri);
                string redirectUri = listener.RedirectUri;

                string state = CreateRandomUrlSafeString(24);
                string codeVerifier = CreateCodeVerifier();
                string codeChallenge = CreateCodeChallenge(codeVerifier);
                string authorizeUrl = BuildAuthorizeUrl(credentials.ClientId, redirectUri, state, codeChallenge);

                Debug.Log($"[DesktopGoogleAuthTokenProvider] OAuth 브라우저 열기: RedirectUri={redirectUri}, Attempt={attempt}/{maxAttempts}");
                Application.OpenURL(authorizeUrl);

                AuthorizationCallback callback = await listener.WaitForCallbackAsync(TimeSpan.FromSeconds(AUTH_TIMEOUT_SECONDS));
                ValidateAuthorizationCallback(callback, state);

                TokenResponse token = await ExchangeAuthorizationCodeAsync(
                    credentials.ClientId,
                    credentials.ClientSecret,
                    redirectUri,
                    callback.Code,
                    codeVerifier);

                if (string.IsNullOrWhiteSpace(token.id_token) && string.IsNullOrWhiteSpace(token.access_token))
                {
                    throw new NotSupportedException("Google 토큰 응답이 비어 있습니다.");
                }

                return new SocialAuthToken(token.id_token, token.access_token);
            }
            catch (Exception ex) when (!hasFixedRedirectUri && IsLoopbackBindConflict(ex))
            {
                lastBindConflict = ex;
                Debug.LogWarning(
                    $"[DesktopGoogleAuthTokenProvider] 루프백 포트 충돌 감지. 요청 RedirectUri={requestedRedirectUri}, 재시도 {attempt}/{maxAttempts}");
                await Task.Yield();
            }
            catch (Exception ex) when (hasFixedRedirectUri && IsLoopbackBindConflict(ex))
            {
                throw new NotSupportedException(
                    $"Google 리디렉트 포트가 이미 사용 중입니다. " +
                    $"동시에 여러 클라이언트를 실행하려면 고정 redirect_uri를 제거하거나 포트를 다르게 지정해 주세요. " +
                    $"RedirectUri={requestedRedirectUri}",
                    ex);
            }
        }

        throw new NotSupportedException(
            "Google 루프백 콜백 포트를 할당하지 못했습니다. 잠시 후 다시 시도해 주세요.",
            lastBindConflict);
    }

    #endregion

    #region OAuth Flow

    private static string BuildAuthorizeUrl(string clientId, string redirectUri, string state, string codeChallenge)
    {
        var queryParams = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("response_type", "code"),
            new KeyValuePair<string, string>("scope", GOOGLE_SCOPE),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("state", state),
            new KeyValuePair<string, string>("code_challenge", codeChallenge),
            new KeyValuePair<string, string>("code_challenge_method", "S256"),
            new KeyValuePair<string, string>("access_type", "offline"),
            new KeyValuePair<string, string>("prompt", "consent select_account")
        };

        StringBuilder builder = new StringBuilder(AUTHORIZATION_ENDPOINT);
        builder.Append('?');
        builder.Append(BuildFormBody(queryParams));
        return builder.ToString();
    }

    private static void ValidateAuthorizationCallback(AuthorizationCallback callback, string expectedState)
    {
        if (callback == null)
        {
            throw new NotSupportedException("Google 인증 응답을 수신하지 못했습니다.");
        }

        if (!string.Equals(callback.State, expectedState, StringComparison.Ordinal))
        {
            throw new NotSupportedException("Google 인증 state 검증에 실패했습니다.");
        }

        if (!string.IsNullOrWhiteSpace(callback.Error))
        {
            if (string.Equals(callback.Error, "access_denied", StringComparison.OrdinalIgnoreCase))
            {
                throw new OperationCanceledException("Google 로그인이 취소되었습니다.");
            }

            throw new NotSupportedException(
                BuildOAuthErrorMessage(
                    "Google 인증 승인에 실패했습니다.",
                    callback.Error,
                    callback.ErrorDescription,
                    string.Empty,
                    0));
        }

        if (string.IsNullOrWhiteSpace(callback.Code))
        {
            throw new NotSupportedException("Google 인증 코드가 응답에 없습니다.");
        }
    }

    private static async Task<TokenResponse> ExchangeAuthorizationCodeAsync(
        string clientId,
        string clientSecret,
        string redirectUri,
        string code,
        string codeVerifier)
    {
        TokenExchangeAttempt firstAttempt = await RequestTokenExchangeAsync(
            clientId,
            clientSecret,
            redirectUri,
            code,
            codeVerifier);

        if (IsTokenExchangeSuccessful(firstAttempt))
        {
            return firstAttempt.Payload;
        }

        if (!string.IsNullOrWhiteSpace(clientSecret) &&
            IsInvalidClientUnauthorized(firstAttempt))
        {
            TokenExchangeAttempt noSecretAttempt = await RequestTokenExchangeAsync(
                clientId,
                string.Empty,
                redirectUri,
                code,
                codeVerifier);

            if (IsClientSecretMissing(noSecretAttempt))
            {
                throw new NotSupportedException(
                    $"Google OAuth 클라이언트 비밀키가 올바르지 않습니다. " +
                    $"Google Cloud 콘솔에서 동일한 데스크톱 클라이언트의 Client Secret을 다시 복사해 주세요. " +
                    $"ClientId={MaskClientId(clientId)}");
            }
        }

        throw new NotSupportedException(
            BuildOAuthErrorMessage(
                $"Google 토큰 교환에 실패했습니다. ClientId={MaskClientId(clientId)}, SecretProvided={!string.IsNullOrWhiteSpace(clientSecret)}",
                firstAttempt.Payload != null ? firstAttempt.Payload.error : string.Empty,
                firstAttempt.Payload != null ? firstAttempt.Payload.error_description : string.Empty,
                firstAttempt.Response.Error,
                firstAttempt.Response.StatusCode));
    }

    private static async Task<TokenExchangeAttempt> RequestTokenExchangeAsync(
        string clientId,
        string clientSecret,
        string redirectUri,
        string code,
        string codeVerifier)
    {
        var formFields = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("code_verifier", codeVerifier)
        };

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            formFields.Add(new KeyValuePair<string, string>("client_secret", clientSecret));
        }

        HttpFormResponse response = await PostFormAsync(TOKEN_ENDPOINT, formFields);
        TokenResponse payload = ParseJson<TokenResponse>(response.Body);
        return new TokenExchangeAttempt(response, payload);
    }

    private static bool IsTokenExchangeSuccessful(TokenExchangeAttempt attempt)
    {
        return attempt.Response.IsSuccess &&
               attempt.Payload != null &&
               string.IsNullOrWhiteSpace(attempt.Payload.error);
    }

    private static bool IsInvalidClientUnauthorized(TokenExchangeAttempt attempt)
    {
        if (attempt.Payload == null)
        {
            return false;
        }

        return string.Equals(attempt.Payload.error, "invalid_client", StringComparison.OrdinalIgnoreCase) &&
               (attempt.Payload.error_description ?? string.Empty).IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsClientSecretMissing(TokenExchangeAttempt attempt)
    {
        if (attempt.Payload == null)
        {
            return false;
        }

        return string.Equals(attempt.Payload.error, "invalid_request", StringComparison.OrdinalIgnoreCase) &&
               (attempt.Payload.error_description ?? string.Empty).IndexOf("client_secret is missing", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string MaskClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return "(empty)";
        }

        string normalized = clientId.Trim();
        if (normalized.Length <= 18)
        {
            return normalized;
        }

        int headLength = Mathf.Min(10, normalized.Length);
        int tailLength = Mathf.Min(12, normalized.Length - headLength);
        return $"{normalized[..headLength]}...{normalized[^tailLength..]}";
    }

    #endregion

    #region Helper Methods

    private static DesktopOAuthCredentials LoadDesktopGoogleCredentials()
    {
        string overrideClientId = ResolveClientIdOverride();
        string overrideClientSecret = ResolveClientSecretOverride();
        string overrideRedirectUri = ResolveRedirectUriOverride();
        ValidateOverrideInputs(overrideClientId, overrideClientSecret);

        if (TryBuildOverrideCredentials(overrideClientId, overrideClientSecret, overrideRedirectUri, out DesktopOAuthCredentials overrideCredentials))
        {
            return overrideCredentials;
        }

        string configPath = Path.Combine(Application.streamingAssetsPath, DESKTOP_GOOGLE_CONFIG_FILE_NAME);
        if (!File.Exists(configPath))
        {
            throw new NotSupportedException($"Desktop OAuth 설정 파일을 찾을 수 없습니다: {configPath}");
        }

        string json = File.ReadAllText(configPath, Encoding.UTF8);
        GoogleServicesConfig config = ParseJson<GoogleServicesConfig>(json);
        InstalledDesktopOAuthRootConfig installedConfig = ParseJson<InstalledDesktopOAuthRootConfig>(json);
        if (config == null)
        {
            throw new NotSupportedException("Desktop OAuth 설정 파일 형식이 올바르지 않습니다.");
        }

        if (TryBuildInstalledCredentials(installedConfig, overrideClientSecret, overrideRedirectUri, out DesktopOAuthCredentials installedCredentials))
        {
            return installedCredentials;
        }

        if (TryBuildDesktopOauthCredentials(config, overrideClientSecret, overrideRedirectUri, out DesktopOAuthCredentials desktopOauthCredentials))
        {
            return desktopOauthCredentials;
        }

        if (TryBuildLegacyClientCredentials(config, overrideClientSecret, overrideRedirectUri, out DesktopOAuthCredentials legacyClientCredentials))
        {
            return legacyClientCredentials;
        }

        throw new NotSupportedException(
            "Desktop OAuth client_id를 찾지 못했습니다. " +
            $"환경 변수 '{DESKTOP_CLIENT_ID_ENV_KEY}'를 설정하거나 " +
            $"'{DESKTOP_GOOGLE_CONFIG_FILE_NAME}' 파일에 desktop_google_oauth.client_id를 추가해 주세요.");
    }

    private static bool TryBuildOverrideCredentials(
        string overrideClientId,
        string overrideClientSecret,
        string overrideRedirectUri,
        out DesktopOAuthCredentials credentials)
    {
        credentials = null;
        if (string.IsNullOrWhiteSpace(overrideClientId))
        {
            return false;
        }

        credentials = new DesktopOAuthCredentials
        {
            ClientId = overrideClientId,
            ClientSecret = overrideClientSecret,
            RedirectUri = overrideRedirectUri ?? string.Empty,
            Source = "Override(args/env)"
        };

        return true;
    }

    private static bool TryBuildInstalledCredentials(
        InstalledDesktopOAuthRootConfig installedConfig,
        string overrideClientSecret,
        string overrideRedirectUri,
        out DesktopOAuthCredentials credentials)
    {
        credentials = null;
        if (installedConfig?.installed == null || string.IsNullOrWhiteSpace(installedConfig.installed.client_id))
        {
            return false;
        }

        credentials = new DesktopOAuthCredentials
        {
            ClientId = installedConfig.installed.client_id.Trim(),
            ClientSecret = FirstNonEmpty(overrideClientSecret, installedConfig.installed.client_secret),
            RedirectUri = FirstNonEmpty(overrideRedirectUri, FirstNonEmpty(installedConfig.installed.redirect_uris)),
            Source = $"{DESKTOP_GOOGLE_CONFIG_FILE_NAME}.installed"
        };

        return true;
    }

    private static bool TryBuildDesktopOauthCredentials(
        GoogleServicesConfig config,
        string overrideClientSecret,
        string overrideRedirectUri,
        out DesktopOAuthCredentials credentials)
    {
        credentials = null;
        if (config?.desktop_google_oauth == null)
        {
            return false;
        }

        string configClientId = FirstNonEmpty(config.desktop_google_oauth.client_id, config.desktop_google_oauth.device_client_id);
        if (string.IsNullOrWhiteSpace(configClientId))
        {
            return false;
        }

        credentials = new DesktopOAuthCredentials
        {
            ClientId = configClientId.Trim(),
            ClientSecret = FirstNonEmpty(
                overrideClientSecret,
                config.desktop_google_oauth.client_secret,
                config.desktop_google_oauth.device_client_secret),
            RedirectUri = FirstNonEmpty(overrideRedirectUri, config.desktop_google_oauth.redirect_uri),
            Source = $"{DESKTOP_GOOGLE_CONFIG_FILE_NAME}.desktop_google_oauth"
        };

        return true;
    }

    private static bool TryBuildLegacyClientCredentials(
        GoogleServicesConfig config,
        string overrideClientSecret,
        string overrideRedirectUri,
        out DesktopOAuthCredentials credentials)
    {
        credentials = null;
        if (config?.client == null)
        {
            return false;
        }

        for (int i = 0; i < config.client.Length; i++)
        {
            GoogleClientConfig client = config.client[i];
            if (client == null || client.oauth_client == null)
            {
                continue;
            }

            for (int j = 0; j < client.oauth_client.Length; j++)
            {
                GoogleOAuthClientConfig oauth = client.oauth_client[j];
                if (oauth == null || string.IsNullOrWhiteSpace(oauth.client_id))
                {
                    continue;
                }

                if (oauth.client_type != 3)
                {
                    continue;
                }

                credentials = new DesktopOAuthCredentials
                {
                    ClientId = oauth.client_id.Trim(),
                    ClientSecret = overrideClientSecret,
                    RedirectUri = overrideRedirectUri ?? string.Empty,
                    Source = $"{DESKTOP_GOOGLE_CONFIG_FILE_NAME}.client.oauth_client(type=3)"
                };

                return true;
            }
        }

        return false;
    }

    private static void ValidateOverrideInputs(string overrideClientId, string overrideClientSecret)
    {
        bool hasClientIdOverride = !string.IsNullOrWhiteSpace(overrideClientId);
        bool hasClientSecretOverride = !string.IsNullOrWhiteSpace(overrideClientSecret);

        if (!hasClientIdOverride && hasClientSecretOverride)
        {
            throw new NotSupportedException(
                "Google Desktop OAuth override 설정이 잘못되었습니다: client_secret만 설정되어 있습니다. " +
                $"'{DESKTOP_CLIENT_SECRET_ENV_KEY}'/ '{DESKTOP_CLIENT_SECRET_ARG_PREFIX}'를 제거하거나 " +
                $"client_id도 함께 설정해 주세요.");
        }
    }

    private static string ResolveClientIdOverride()
    {
        return FirstNonEmpty(
            ResolveArgumentValue(DESKTOP_CLIENT_ID_ARG_PREFIX),
            Environment.GetEnvironmentVariable(DESKTOP_CLIENT_ID_ENV_KEY));
    }

    private static string ResolveClientSecretOverride()
    {
        return FirstNonEmpty(
            ResolveArgumentValue(DESKTOP_CLIENT_SECRET_ARG_PREFIX),
            Environment.GetEnvironmentVariable(DESKTOP_CLIENT_SECRET_ENV_KEY));
    }

    private static string ResolveRedirectUriOverride()
    {
        return FirstNonEmpty(
            ResolveArgumentValue(DESKTOP_REDIRECT_URI_ARG_PREFIX),
            Environment.GetEnvironmentVariable(DESKTOP_REDIRECT_URI_ENV_KEY));
    }

    private static string ResolveArgumentValue(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return string.Empty;
        }

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            return arg.Substring(prefix.Length).Trim();
        }

        return string.Empty;
    }

    private static string ResolveRedirectUri(string configuredRedirectUri, int fallbackPort)
    {
        string redirectUri = string.IsNullOrWhiteSpace(configuredRedirectUri)
            ? $"http://127.0.0.1:{fallbackPort}/"
            : configuredRedirectUri.Trim();

        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out Uri parsedUri))
        {
            throw new NotSupportedException($"Desktop OAuth redirect_uri 형식이 잘못되었습니다: {redirectUri}");
        }

        bool isLoopbackHost = string.Equals(parsedUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(parsedUri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        if (!isLoopbackHost || !string.Equals(parsedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Desktop OAuth redirect_uri는 http://127.0.0.1 또는 http://localhost 루프백 주소만 지원합니다.");
        }

        string normalized = parsedUri.GetLeftPart(UriPartial.Path);
        if (!normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized += "/";
        }

        return normalized;
    }

    private static string CreateCodeVerifier()
    {
        byte[] randomBytes = new byte[64];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        return Base64UrlEncode(randomBytes);
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        byte[] verifierBytes = Encoding.ASCII.GetBytes(codeVerifier ?? string.Empty);
        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(verifierBytes);
        return Base64UrlEncode(hash);
    }

    private static string CreateRandomUrlSafeString(int byteLength)
    {
        byte[] randomBytes = new byte[Mathf.Max(16, byteLength)];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }

        return Base64UrlEncode(randomBytes);
    }

    private static async Task<HttpFormResponse> PostFormAsync(string url, List<KeyValuePair<string, string>> fields)
    {
        byte[] payload = Encoding.UTF8.GetBytes(BuildFormBody(fields));

        using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(payload);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = REQUEST_TIMEOUT_SECONDS;
        request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");

        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        while (!operation.isDone)
        {
            await Task.Yield();
        }

        string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
        bool isSuccess = request.result == UnityWebRequest.Result.Success &&
                         request.responseCode >= 200 &&
                         request.responseCode < 300;

        return new HttpFormResponse(isSuccess, request.responseCode, body, request.error);
    }

    private static string BuildFormBody(List<KeyValuePair<string, string>> fields)
    {
        StringBuilder builder = new StringBuilder(256);

        for (int i = 0; i < fields.Count; i++)
        {
            KeyValuePair<string, string> field = fields[i];
            if (i > 0)
            {
                builder.Append('&');
            }

            builder.Append(UnityWebRequest.EscapeURL(field.Key ?? string.Empty));
            builder.Append('=');
            builder.Append(UnityWebRequest.EscapeURL(field.Value ?? string.Empty));
        }

        return builder.ToString();
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        string encoded = Convert.ToBase64String(bytes ?? Array.Empty<byte>());
        return encoded.Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return parameters;
        }

        string trimmedQuery = query.TrimStart('?');
        string[] pairs = trimmedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < pairs.Length; i++)
        {
            string[] parts = pairs[i].Split('=', 2);
            string key = UrlDecode(parts.Length > 0 ? parts[0] : string.Empty);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            string value = UrlDecode(parts.Length > 1 ? parts[1] : string.Empty);
            parameters[key] = value;
        }

        return parameters;
    }

    private static string UrlDecode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Uri.UnescapeDataString(value.Replace("+", "%20"));
    }

    private static string GetQueryValue(Dictionary<string, string> query, string key)
    {
        if (query == null || string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return query.TryGetValue(key, out string value) ? value ?? string.Empty : string.Empty;
    }

    private static async Task WriteBrowserResponseAsync(NetworkStream stream, AuthorizationCallback callback)
    {
        if (stream == null)
        {
            return;
        }

        bool isSuccess = callback != null && string.IsNullOrWhiteSpace(callback.Error) && !string.IsNullOrWhiteSpace(callback.Code);
        string html = isSuccess
            ? "<html><body><h2>로그인이 완료되었습니다.</h2><p>게임으로 돌아가 주세요.</p></body></html>"
            : "<html><body><h2>로그인에 실패했습니다.</h2><p>게임으로 돌아가 다시 시도해 주세요.</p></body></html>";

        byte[] bodyBytes = Encoding.UTF8.GetBytes(html);
        string statusLine = isSuccess ? "HTTP/1.1 200 OK" : "HTTP/1.1 400 Bad Request";
        string head =
            $"{statusLine}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";
        byte[] headBytes = Encoding.ASCII.GetBytes(head);

        try
        {
            await stream.WriteAsync(headBytes, 0, headBytes.Length);
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length);
        }
        catch
        {
            // Why: 브라우저가 먼저 닫힌 경우 응답 쓰기 예외는 무시합니다.
        }
    }

    private static async Task<string> ReadHttpRequestHeadAsync(NetworkStream stream)
    {
        const int maxReadBytes = 16 * 1024;
        byte[] buffer = new byte[1024];
        int totalRead = 0;
        StringBuilder builder = new StringBuilder(2048);

        while (totalRead < maxReadBytes)
        {
            int read = await stream.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            totalRead += read;
            builder.Append(Encoding.ASCII.GetString(buffer, 0, read));

            if (builder.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal) >= 0)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static string ExtractRequestTarget(string requestHead)
    {
        if (string.IsNullOrWhiteSpace(requestHead))
        {
            return string.Empty;
        }

        int lineEnd = requestHead.IndexOf("\r\n", StringComparison.Ordinal);
        string requestLine = lineEnd >= 0 ? requestHead.Substring(0, lineEnd) : requestHead;
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return string.Empty;
        }

        string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return string.Empty;
        }

        return parts[1];
    }

    private static string ExtractQueryStringFromRequestTarget(string requestTarget, int fallbackPort)
    {
        if (string.IsNullOrWhiteSpace(requestTarget))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(requestTarget, UriKind.Absolute, out Uri absolute))
        {
            return absolute.Query ?? string.Empty;
        }

        string normalizedTarget = requestTarget.StartsWith("/", StringComparison.Ordinal)
            ? requestTarget
            : $"/{requestTarget}";

        string absoluteUri = $"http://127.0.0.1:{fallbackPort}{normalizedTarget}";
        if (Uri.TryCreate(absoluteUri, UriKind.Absolute, out Uri parsed))
        {
            return parsed.Query ?? string.Empty;
        }

        int queryIndex = requestTarget.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? requestTarget.Substring(queryIndex) : string.Empty;
    }

    private static T ParseJson<T>(string json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<T>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildOAuthErrorMessage(string prefix, string oauthError, string oauthDescription, string transportError, long statusCode)
    {
        string safePrefix = string.IsNullOrWhiteSpace(prefix) ? "OAuth 요청이 실패했습니다." : prefix.Trim();
        string safeOauthError = string.IsNullOrWhiteSpace(oauthError) ? string.Empty : oauthError.Trim();
        string safeOauthDescription = string.IsNullOrWhiteSpace(oauthDescription)
            ? string.Empty
            : WebUtility.HtmlDecode(oauthDescription.Trim());
        string safeTransportError = string.IsNullOrWhiteSpace(transportError) ? string.Empty : transportError.Trim();

        StringBuilder builder = new StringBuilder(safePrefix);
        if (!string.IsNullOrWhiteSpace(safeOauthError))
        {
            builder.Append($" Error={safeOauthError}");
        }

        if (!string.IsNullOrWhiteSpace(safeOauthDescription))
        {
            builder.Append($", Description={safeOauthDescription}");
        }
        else if (!string.IsNullOrWhiteSpace(safeTransportError))
        {
            builder.Append($", Transport={safeTransportError}");
        }

        if (statusCode > 0)
        {
            builder.Append($", HttpStatus={statusCode}");
        }

        return builder.ToString();
    }

    private static bool IsLoopbackBindConflict(Exception exception)
    {
        if (exception == null)
        {
            return false;
        }

        Exception current = exception;
        while (current != null)
        {
            if (current is HttpListenerException httpListenerException)
            {
                if (httpListenerException.ErrorCode == 183 || httpListenerException.ErrorCode == 10048)
                {
                    return true;
                }
            }

            if (current is SocketException socketException &&
                socketException.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                return true;
            }

            if (ContainsAddressInUseMessage(current.Message))
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    private static bool ContainsAddressInUseMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        return message.IndexOf("address already in use", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("Cannot create a file when that file already exists", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null)
        {
            return string.Empty;
        }

        for (int i = 0; i < values.Length; i++)
        {
            string normalized = NormalizeSettingValue(values[i]);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return string.Empty;
    }

    private static string NormalizeSettingValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            trimmed[0] == '"' &&
            trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    #endregion
}
