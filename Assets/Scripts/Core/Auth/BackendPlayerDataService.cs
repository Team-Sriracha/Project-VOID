using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 프로필 쓰기 작업을 자체 백엔드로 우선 위임하는 플레이어 데이터 서비스입니다.
/// </summary>
public class BackendPlayerDataService : IPlayerDataService
{
    #region Constants

    private const string PROFILE_SYNC_ENDPOINT_ENV_KEY = "PROJECTVOID_PROFILE_SYNC_URL";
    private const string PROFILE_SYNC_ENDPOINT_ARG_PREFIX = "--profile-sync-url=";
    private const string PROFILE_DISPLAY_NAME_ENDPOINT_ENV_KEY = "PROJECTVOID_PROFILE_DISPLAY_NAME_URL";
    private const string PROFILE_DISPLAY_NAME_ENDPOINT_ARG_PREFIX = "--profile-display-name-url=";
    private const string BACKEND_BASE_URL_ENV_KEY = "PROJECTVOID_BACKEND_BASE_URL";
    private const string BACKEND_BASE_URL_ARG_PREFIX = "--backend-base-url=";
    private const string VERIFY_ENDPOINT_ENV_KEY = "PROJECTVOID_VERIFY_TOKEN_URL";
    private const string VERIFY_ENDPOINT_ARG_PREFIX = "--verify-token-url=";
    private const string PROFILE_SYNC_PATH = "/profile/sync";
    private const string PROFILE_DISPLAY_NAME_PATH = "/profile/display-name";
    private const int REQUEST_TIMEOUT_SECONDS = 10;

    #endregion

    #region Private Types

    [Serializable]
    private sealed class ProfileSyncRequest
    {
        public string PreferredDisplayName;
        public bool EnsureProfile;
        public bool UpdateLastLogin;
    }

    [Serializable]
    private sealed class DisplayNameUpdateRequest
    {
        public string DisplayName;
    }

    [Serializable]
    private sealed class BackendProfileResponse
    {
        public bool Success;
        public string ErrorMessage;
        public ProfilePayload Profile;
    }

    [Serializable]
    private sealed class ProfilePayload
    {
        public string Uid;
        public string GuestId;
        public bool IsGuest;
        public string DisplayName;
        public bool IsDisplayNameConfirmed;
        public string Email;
        public string CreatedAtUtc;
        public string LastLoginAtUtc;
        public int TotalMatches;
        public int Wins;
        public int Draws;
        public int Kills;
        public int Deaths;
        public ModeStatsPayload NormalModeStats;
        public ModeStatsPayload RankedModeStats;
        public int RankedMmr;
        public int RankedSeasonBestMmr;
        public string RankedTier;
        public RecentMatchPayload[] RecentMatches;
        public string[] ProcessedMatchIds;
        public string LastMatchAtUtc;
    }

    [Serializable]
    private sealed class ModeStatsPayload
    {
        public int TotalMatches;
        public int Wins;
        public int Draws;
        public int Kills;
        public int Deaths;
    }

    [Serializable]
    private sealed class RecentMatchPayload
    {
        public string MatchId;
        public string Mode;
        public int Rank;
        public int Kills;
        public int Deaths;
        public int MmrDelta;
        public bool IsWin;
        public bool IsDraw;
        public string EndedAtUtc;
    }

    #endregion

    #region Private Fields

    private readonly FirestorePlayerDataService _firestoreFallback = new();

    #endregion

    #region Profile

    /// <inheritdoc />
    public async Task<PlayerProfile> GetPlayerProfileAsync(string uid)
    {
        if (TryResolveCurrentAuthenticatedUser(uid, requireDisplayNameEndpoint: false, out IAuthService authService))
        {
            PlayerProfile profile = await RequestProfileSyncAsync(
                authService,
                ensureProfile: false,
                updateLastLogin: false,
                preferredDisplayName: string.Empty);

            if (profile != null)
            {
                return profile;
            }
        }

        return await _firestoreFallback.GetPlayerProfileAsync(uid);
    }

    /// <inheritdoc />
    public async Task<PlayerProfile> CreatePlayerProfileAsync(string uid, string displayName, string email, bool isGuest, string guestId = "")
    {
        if (isGuest)
        {
            return await _firestoreFallback.CreatePlayerProfileAsync(uid, displayName, email, true, guestId);
        }

        if (TryResolveCurrentAuthenticatedUser(uid, requireDisplayNameEndpoint: false, out IAuthService authService))
        {
            return await RequestProfileSyncAsync(
                authService,
                ensureProfile: true,
                updateLastLogin: true,
                preferredDisplayName: displayName);
        }

        return await _firestoreFallback.CreatePlayerProfileAsync(uid, displayName, email, false, guestId);
    }

    /// <inheritdoc />
    public async Task<PlayerProfile> EnsureGuestProfileAsync(string uid)
    {
        return await _firestoreFallback.EnsureGuestProfileAsync(uid);
    }

    /// <inheritdoc />
    public async Task UpdateLastLoginAtAsync(string uid)
    {
        if (TryResolveCurrentAuthenticatedUser(uid, requireDisplayNameEndpoint: false, out IAuthService authService))
        {
            await RequestProfileSyncAsync(
                authService,
                ensureProfile: true,
                updateLastLogin: true,
                preferredDisplayName: string.Empty);
            return;
        }

        await _firestoreFallback.UpdateLastLoginAtAsync(uid);
    }

    /// <inheritdoc />
    public async Task<bool> ApplyMatchResultAsync(ServerMatchResult matchResult)
    {
        return await _firestoreFallback.ApplyMatchResultAsync(matchResult);
    }

    #endregion

    #region Display Name

    /// <inheritdoc />
    public async Task<bool> UpdateDisplayNameAsync(string uid, string newName)
    {
        if (TryResolveCurrentAuthenticatedUser(uid, requireDisplayNameEndpoint: true, out IAuthService authService))
        {
            return await RequestDisplayNameUpdateAsync(authService, newName);
        }

        return await _firestoreFallback.UpdateDisplayNameAsync(uid, newName);
    }

    /// <inheritdoc />
    public async Task<bool> IsDisplayNameAvailableAsync(string name)
    {
        return await _firestoreFallback.IsDisplayNameAvailableAsync(name);
    }

    #endregion

    #region Backend Requests

    private async Task<PlayerProfile> RequestProfileSyncAsync(
        IAuthService authService,
        bool ensureProfile,
        bool updateLastLogin,
        string preferredDisplayName)
    {
        if (authService == null)
        {
            return null;
        }

        string endpoint = ResolveProfileSyncEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        string idToken = await authService.GetIdTokenAsync();
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        ProfileSyncRequest requestBody = new ProfileSyncRequest
        {
            PreferredDisplayName = string.IsNullOrWhiteSpace(preferredDisplayName)
                ? string.Empty
                : preferredDisplayName.Trim(),
            EnsureProfile = ensureProfile,
            UpdateLastLogin = updateLastLogin
        };

        BackendProfileResponse response = await SendAuthorizedJsonRequestAsync<ProfileSyncRequest>(endpoint, idToken, requestBody);
        if (response == null)
        {
            return null;
        }

        if (!response.Success)
        {
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
            {
                Debug.LogWarning($"[BackendPlayerDataService] 프로필 동기화 실패: {response.ErrorMessage}");
            }

            return null;
        }

        return ConvertToPlayerProfile(response.Profile);
    }

    private async Task<bool> RequestDisplayNameUpdateAsync(IAuthService authService, string displayName)
    {
        if (authService == null)
        {
            return false;
        }

        string endpoint = ResolveProfileDisplayNameEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        string idToken = await authService.GetIdTokenAsync();
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return false;
        }

        DisplayNameUpdateRequest requestBody = new DisplayNameUpdateRequest
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? string.Empty
                : displayName.Trim()
        };

        BackendProfileResponse response = await SendAuthorizedJsonRequestAsync<DisplayNameUpdateRequest>(endpoint, idToken, requestBody);
        if (response == null)
        {
            return false;
        }

        if (!response.Success)
        {
            if (!string.IsNullOrWhiteSpace(response.ErrorMessage))
            {
                Debug.LogWarning($"[BackendPlayerDataService] 닉네임 갱신 실패: {response.ErrorMessage}");
            }

            return false;
        }

        return response.Profile != null;
    }

    private static async Task<BackendProfileResponse> SendAuthorizedJsonRequestAsync<TRequest>(
        string endpoint,
        string idToken,
        TRequest requestBody)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        try
        {
            string requestJson = JsonUtility.ToJson(requestBody ?? Activator.CreateInstance<TRequest>());

            using UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(requestJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = REQUEST_TIMEOUT_SECONDS;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {idToken}");

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[BackendPlayerDataService] 요청 실패: {request.error}");
                return null;
            }

            string responseJson = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return null;
            }

            return JsonUtility.FromJson<BackendProfileResponse>(responseJson);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BackendPlayerDataService] 백엔드 요청 예외: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Mapping

    private static PlayerProfile ConvertToPlayerProfile(ProfilePayload payload)
    {
        if (payload == null)
        {
            return null;
        }

        PlayerProfile profile = new PlayerProfile
        {
            Uid = payload.Uid ?? string.Empty,
            GuestId = payload.GuestId ?? string.Empty,
            IsGuest = payload.IsGuest,
            DisplayName = payload.DisplayName ?? string.Empty,
            IsDisplayNameConfirmed = payload.IsDisplayNameConfirmed,
            Email = payload.Email ?? string.Empty,
            CreatedAtUtc = ParseUtcDateTime(payload.CreatedAtUtc),
            LastLoginAtUtc = ParseUtcDateTime(payload.LastLoginAtUtc),
            TotalMatches = payload.TotalMatches,
            Wins = payload.Wins,
            Draws = payload.Draws,
            Kills = payload.Kills,
            Deaths = payload.Deaths,
            NormalModeStats = ConvertToModeStats(payload.NormalModeStats),
            RankedModeStats = ConvertToModeStats(payload.RankedModeStats),
            RankedMmr = payload.RankedMmr,
            RankedSeasonBestMmr = payload.RankedSeasonBestMmr,
            RankedTier = string.IsNullOrWhiteSpace(payload.RankedTier) ? "Rookie" : payload.RankedTier,
            RecentMatches = ConvertToRecentMatches(payload.RecentMatches),
            ProcessedMatchIds = ConvertToProcessedMatchIds(payload.ProcessedMatchIds),
            LastMatchAtUtc = ParseUtcDateTime(payload.LastMatchAtUtc)
        };

        return profile;
    }

    private static PlayerModeStats ConvertToModeStats(ModeStatsPayload payload)
    {
        if (payload == null)
        {
            return new PlayerModeStats();
        }

        return new PlayerModeStats
        {
            TotalMatches = payload.TotalMatches,
            Wins = payload.Wins,
            Draws = payload.Draws,
            Kills = payload.Kills,
            Deaths = payload.Deaths
        };
    }

    private static List<RecentMatchSummary> ConvertToRecentMatches(RecentMatchPayload[] payloads)
    {
        List<RecentMatchSummary> matches = new();
        if (payloads == null || payloads.Length == 0)
        {
            return matches;
        }

        for (int i = 0; i < payloads.Length; i++)
        {
            RecentMatchPayload payload = payloads[i];
            if (payload == null || string.IsNullOrWhiteSpace(payload.MatchId))
            {
                continue;
            }

            matches.Add(new RecentMatchSummary
            {
                MatchId = payload.MatchId,
                Mode = payload.Mode ?? string.Empty,
                Rank = payload.Rank,
                Kills = payload.Kills,
                Deaths = payload.Deaths,
                MmrDelta = payload.MmrDelta,
                IsWin = payload.IsWin,
                IsDraw = payload.IsDraw,
                EndedAtUtc = ParseUtcDateTime(payload.EndedAtUtc)
            });
        }

        return matches;
    }

    private static List<string> ConvertToProcessedMatchIds(string[] payloads)
    {
        List<string> processedMatchIds = new();
        if (payloads == null || payloads.Length == 0)
        {
            return processedMatchIds;
        }

        for (int i = 0; i < payloads.Length; i++)
        {
            string matchId = payloads[i];
            if (!string.IsNullOrWhiteSpace(matchId))
            {
                processedMatchIds.Add(matchId.Trim());
            }
        }

        return processedMatchIds;
    }

    private static DateTime ParseUtcDateTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.UtcNow;
        }

        if (DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out DateTime parsed))
        {
            return parsed;
        }

        return DateTime.UtcNow;
    }

    #endregion

    #region Endpoint Resolution

    private static bool TryResolveCurrentAuthenticatedUser(
        string uid,
        bool requireDisplayNameEndpoint,
        out IAuthService authService)
    {
        authService = ServiceLocator.Get<IAuthService>();
        if (authService == null ||
            !authService.IsSignedIn ||
            authService.IsAnonymous ||
            string.IsNullOrWhiteSpace(uid) ||
            string.IsNullOrWhiteSpace(authService.UserId))
        {
            return false;
        }

        if (!string.Equals(uid.Trim(), authService.UserId.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        string endpoint = requireDisplayNameEndpoint
            ? ResolveProfileDisplayNameEndpoint()
            : ResolveProfileSyncEndpoint();

        return !string.IsNullOrWhiteSpace(endpoint);
    }

    private static string ResolveProfileSyncEndpoint()
    {
        return ResolveExplicitOrDerivedEndpoint(
            PROFILE_SYNC_ENDPOINT_ARG_PREFIX,
            PROFILE_SYNC_ENDPOINT_ENV_KEY,
            PROFILE_SYNC_PATH);
    }

    private static string ResolveProfileDisplayNameEndpoint()
    {
        return ResolveExplicitOrDerivedEndpoint(
            PROFILE_DISPLAY_NAME_ENDPOINT_ARG_PREFIX,
            PROFILE_DISPLAY_NAME_ENDPOINT_ENV_KEY,
            PROFILE_DISPLAY_NAME_PATH);
    }

    private static string ResolveExplicitOrDerivedEndpoint(
        string explicitArgPrefix,
        string explicitEnvKey,
        string fallbackPath)
    {
        string explicitValue = ResolveArgumentOrEnvironment(explicitArgPrefix, explicitEnvKey);
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        string backendBaseUrl = ResolveArgumentOrEnvironment(BACKEND_BASE_URL_ARG_PREFIX, BACKEND_BASE_URL_ENV_KEY);
        if (!string.IsNullOrWhiteSpace(backendBaseUrl))
        {
            return CombineEndpoint(backendBaseUrl, fallbackPath);
        }

        string verifyEndpoint = ResolveArgumentOrEnvironment(VERIFY_ENDPOINT_ARG_PREFIX, VERIFY_ENDPOINT_ENV_KEY);
        if (!string.IsNullOrWhiteSpace(verifyEndpoint))
        {
            return ReplaceEndpointPath(verifyEndpoint, fallbackPath);
        }

        return string.Empty;
    }

    private static string ResolveArgumentOrEnvironment(string argumentPrefix, string environmentKey)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (!string.IsNullOrWhiteSpace(argumentPrefix) &&
                arg.StartsWith(argumentPrefix, StringComparison.Ordinal))
            {
                return arg.Substring(argumentPrefix.Length).Trim();
            }
        }

        return Environment.GetEnvironmentVariable(environmentKey)?.Trim() ?? string.Empty;
    }

    private static string CombineEndpoint(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri baseUri))
        {
            return string.Empty;
        }

        Uri resolved = new Uri(baseUri, path.TrimStart('/'));
        return resolved.ToString();
    }

    private static string ReplaceEndpointPath(string endpoint, string path)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri verifyUri))
        {
            return string.Empty;
        }

        UriBuilder builder = new UriBuilder(verifyUri)
        {
            Path = path,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString();
    }

    #endregion
}
