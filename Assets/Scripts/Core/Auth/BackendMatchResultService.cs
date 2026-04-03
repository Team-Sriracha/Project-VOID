using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 서버 전적 반영 백엔드 서비스입니다.
/// </summary>
public class BackendMatchResultService : IServerMatchResultService
{
    #region Constants

    private const string MATCH_RESULT_ENDPOINT_ENV_KEY = "PROJECTVOID_MATCH_RESULT_URL";
    private const string MATCH_RESULT_ENDPOINT_ARG_PREFIX = "--match-result-url=";
    private const string BACKEND_SHARED_SECRET_ENV_KEY = "PROJECTVOID_BACKEND_SHARED_SECRET";
    private const string BACKEND_SHARED_SECRET_ARG_PREFIX = "--backend-shared-secret=";
    private const string BACKEND_SHARED_SECRET_HEADER_NAME = "X-ProjectVoid-Server-Key";
    private const int REQUEST_TIMEOUT_SECONDS = 10;

    #endregion

    #region Private Types

    [Serializable]
    private sealed class MatchResultPayload
    {
        public string MatchId;
        public string Mode;
        public string StartedAtUtc;
        public string EndedAtUtc;
        public string WinnerUid;
        public List<MatchPlayerPayload> Players = new();
    }

    [Serializable]
    private sealed class MatchPlayerPayload
    {
        public string Uid;
        public int Kills;
        public int Deaths;
        public int Rank;
        public bool IsGuest;
        public bool IsDraw;
    }

    [Serializable]
    private sealed class MatchCommitResponse
    {
        public bool Success;
        public string ErrorMessage;
    }

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public async Task<bool> CommitMatchResultAsync(ServerMatchResult result)
    {
        if (result == null)
        {
            return false;
        }

        string endpoint = ResolveMatchResultEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            Debug.LogWarning("[BackendMatchResultService] Endpoint 미설정으로 전적 커밋 실패 처리합니다. PROJECTVOID_MATCH_RESULT_URL 또는 --match-result-url= 설정이 필요합니다.");
            return false;
        }

        try
        {
            string payloadJson = JsonUtility.ToJson(CreatePayload(result));

            using UnityWebRequest request = new UnityWebRequest(endpoint, "POST");
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(payloadJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = REQUEST_TIMEOUT_SECONDS;
            request.SetRequestHeader("Content-Type", "application/json");

            string sharedSecret = ResolveBackendSharedSecret();
            if (!string.IsNullOrWhiteSpace(sharedSecret))
            {
                request.SetRequestHeader(BACKEND_SHARED_SECRET_HEADER_NAME, sharedSecret);
            }

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[BackendMatchResultService] 요청 실패: {request.error}");
                return false;
            }

            if (request.responseCode < 200 || request.responseCode >= 300)
            {
                Debug.LogWarning($"[BackendMatchResultService] HTTP 실패: {request.responseCode}");
                return false;
            }

            string responseJson = request.downloadHandler != null
                ? request.downloadHandler.text
                : string.Empty;
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return true;
            }

            MatchCommitResponse response = JsonUtility.FromJson<MatchCommitResponse>(responseJson);
            if (response == null)
            {
                return true;
            }

            if (!response.Success && !string.IsNullOrWhiteSpace(response.ErrorMessage))
            {
                Debug.LogWarning($"[BackendMatchResultService] 백엔드 실패 응답: {response.ErrorMessage}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[BackendMatchResultService] 전적 커밋 예외: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Helper Methods

    private static MatchResultPayload CreatePayload(ServerMatchResult result)
    {
        MatchResultPayload payload = new MatchResultPayload
        {
            MatchId = result.MatchId,
            Mode = result.Mode,
            StartedAtUtc = result.StartedAtUtc.ToUniversalTime().ToString("O"),
            EndedAtUtc = result.EndedAtUtc.ToUniversalTime().ToString("O"),
            WinnerUid = result.WinnerUid ?? string.Empty
        };

        if (result.Players != null)
        {
            for (int i = 0; i < result.Players.Count; i++)
            {
                ServerMatchPlayerResult player = result.Players[i];
                if (player == null)
                {
                    continue;
                }

                payload.Players.Add(new MatchPlayerPayload
                {
                    Uid = player.Uid ?? string.Empty,
                    Kills = player.Kills,
                    Deaths = player.Deaths,
                    Rank = player.Rank,
                    IsGuest = player.IsGuest,
                    IsDraw = player.IsDraw
                });
            }
        }

        return payload;
    }

    private static string ResolveMatchResultEndpoint()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith(MATCH_RESULT_ENDPOINT_ARG_PREFIX, StringComparison.Ordinal))
            {
                return arg.Substring(MATCH_RESULT_ENDPOINT_ARG_PREFIX.Length).Trim();
            }
        }

        return Environment.GetEnvironmentVariable(MATCH_RESULT_ENDPOINT_ENV_KEY)?.Trim();
    }

    private static string ResolveBackendSharedSecret()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith(BACKEND_SHARED_SECRET_ARG_PREFIX, StringComparison.Ordinal))
            {
                return arg.Substring(BACKEND_SHARED_SECRET_ARG_PREFIX.Length).Trim();
            }
        }

        return Environment.GetEnvironmentVariable(BACKEND_SHARED_SECRET_ENV_KEY)?.Trim();
    }

    #endregion
}
