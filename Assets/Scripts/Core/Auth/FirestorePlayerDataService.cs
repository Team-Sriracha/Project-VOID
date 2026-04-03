using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Firestore 플레이어 데이터 서비스입니다.
/// </summary>
public class FirestorePlayerDataService : IPlayerDataService
{
    #region Constants

    private const string FIRESTORE_TYPE_NAME = "Firebase.Firestore.FirebaseFirestore, Firebase.Firestore";
    private const string FIRESTORE_SETTINGS_TYPE_NAME = "Firebase.Firestore.FirebaseFirestoreSettings, Firebase.Firestore";
    private const string PLAYERS_COLLECTION = "players";
    private const string DISPLAY_NAMES_COLLECTION = "displayNames";
    private const int DEFAULT_RANKED_MMR = 0;
    private const int RECENT_MATCH_LIMIT = 20;
    private const int PROCESSED_MATCH_LIMIT = 50;

    #endregion

    #region Private Fields

    private readonly OfflinePlayerDataService _fallbackService = new();
    private readonly object _firestore;
    private readonly bool _isFirestoreReady;

    #endregion

    #region Constructor

    /// <summary>
    /// 서비스 인스턴스를 생성합니다.
    /// </summary>
    public FirestorePlayerDataService()
    {
        _firestore = TryGetFirestoreInstance();
        _isFirestoreReady = _firestore != null;

        if (_isFirestoreReady)
        {
            TryConfigureDesktopFirestoreSettings(_firestore);
        }

        if (!_isFirestoreReady)
        {
            Debug.LogWarning("[FirestorePlayerDataService] Firebase Firestore를 찾지 못해 OfflinePlayerDataService로 폴백합니다.");
        }
    }

    #endregion

    #region Profile

    /// <inheritdoc />
    public async Task<PlayerProfile> GetPlayerProfileAsync(string uid)
    {
        if (!_isFirestoreReady)
        {
            return await _fallbackService.GetPlayerProfileAsync(uid);
        }

        if (string.IsNullOrWhiteSpace(uid))
        {
            return null;
        }

        object playerDoc = GetDocumentReference(PLAYERS_COLLECTION, uid.Trim());
        if (playerDoc == null)
        {
            return null;
        }

        object snapshot = await GetDocumentSnapshotAsync(playerDoc);
        if (!SnapshotExists(snapshot))
        {
            return null;
        }

        Dictionary<string, object> data = SnapshotToDictionary(snapshot);
        if (data == null || data.Count == 0)
        {
            return null;
        }

        return ParseProfile(data, uid.Trim());
    }

    /// <inheritdoc />
    public async Task<PlayerProfile> CreatePlayerProfileAsync(string uid, string displayName, string email, bool isGuest, string guestId = "")
    {
        if (isGuest)
        {
            // Why: 게스트는 영구 저장 정책에서 제외되어 Firestore에 프로필을 만들지 않습니다.
            return await _fallbackService.CreatePlayerProfileAsync(uid, displayName, email, true, guestId);
        }

        if (!_isFirestoreReady)
        {
            return await _fallbackService.CreatePlayerProfileAsync(uid, displayName, email, isGuest, guestId);
        }

        if (string.IsNullOrWhiteSpace(uid))
        {
            return null;
        }

        string trimmedUid = uid.Trim();
        PlayerProfile existingProfile = await GetPlayerProfileAsync(trimmedUid);
        if (existingProfile != null)
        {
            return existingProfile;
        }

        string resolvedDisplayName = ResolveDisplayName(displayName, trimmedUid);
        string normalizedName = NormalizeDisplayNameKey(resolvedDisplayName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        bool reserved = await TryCreateDisplayNameIndexAsync(normalizedName, trimmedUid);
        if (!reserved)
        {
            return null;
        }

        PlayerProfile profile = new PlayerProfile
        {
            Uid = trimmedUid,
            DisplayName = resolvedDisplayName,
            IsDisplayNameConfirmed = false,
            Email = email ?? string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow,
            TotalMatches = 0,
            Wins = 0,
            Draws = 0,
            Kills = 0,
            Deaths = 0,
            RankedMmr = DEFAULT_RANKED_MMR,
            RankedSeasonBestMmr = DEFAULT_RANKED_MMR,
            RankedTier = ResolveRankedTier(DEFAULT_RANKED_MMR),
            LastMatchAtUtc = DateTime.UtcNow
        };

        bool saved = await SetPlayerDocumentAsync(profile);
        if (!saved)
        {
            await DeleteDisplayNameIndexAsync(normalizedName);
            return null;
        }

        return profile;
    }

    /// <inheritdoc />
    public async Task<PlayerProfile> EnsureGuestProfileAsync(string uid)
    {
        // Why: 게스트 세션은 로컬 임시 프로필만 사용하며 Firestore에 저장하지 않습니다.
        return await _fallbackService.EnsureGuestProfileAsync(uid);
    }

    /// <inheritdoc />
    public async Task UpdateLastLoginAtAsync(string uid)
    {
        if (!_isFirestoreReady)
        {
            await _fallbackService.UpdateLastLoginAtAsync(uid);
            return;
        }

        if (string.IsNullOrWhiteSpace(uid))
        {
            return;
        }

        object playerDoc = GetDocumentReference(PLAYERS_COLLECTION, uid.Trim());
        if (playerDoc == null)
        {
            return;
        }

        Dictionary<string, object> updates = new Dictionary<string, object>
        {
            ["LastLoginAtUtc"] = DateTime.UtcNow
        };

        await TryUpdateDocumentAsync(playerDoc, updates);
    }

    /// <inheritdoc />
    public async Task<bool> ApplyMatchResultAsync(ServerMatchResult matchResult)
    {
        if (matchResult == null ||
            matchResult.Players == null ||
            matchResult.Players.Count == 0 ||
            string.IsNullOrWhiteSpace(matchResult.MatchId))
        {
            return false;
        }

        if (ServiceLocator.TryGet<IServerMatchResultService>(out IServerMatchResultService matchResultService) &&
            matchResultService != null)
        {
            return await matchResultService.CommitMatchResultAsync(matchResult);
        }

        Debug.LogWarning("[FirestorePlayerDataService] IServerMatchResultService가 없어 매치 결과를 커밋하지 못했습니다.");
        return false;
    }

    #endregion

    #region Display Name

    /// <inheritdoc />
    public async Task<bool> UpdateDisplayNameAsync(string uid, string newName)
    {
        if (!_isFirestoreReady)
        {
            return await _fallbackService.UpdateDisplayNameAsync(uid, newName);
        }

        if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(newName))
        {
            return false;
        }

        string trimmedUid = uid.Trim();
        string resolvedName = newName.Trim();
        string nextKey = NormalizeDisplayNameKey(resolvedName);
        if (string.IsNullOrWhiteSpace(nextKey))
        {
            return false;
        }

        PlayerProfile profile = await GetPlayerProfileAsync(trimmedUid);
        if (profile == null)
        {
            return false;
        }

        string prevKey = NormalizeDisplayNameKey(profile.DisplayName);
        if (string.Equals(prevKey, nextKey, StringComparison.Ordinal))
        {
            if (profile.IsDisplayNameConfirmed)
            {
                return true;
            }

            object sameNameDoc = GetDocumentReference(PLAYERS_COLLECTION, trimmedUid);
            if (sameNameDoc == null)
            {
                return false;
            }

            Dictionary<string, object> sameNameUpdates = new Dictionary<string, object>
            {
                ["IsDisplayNameConfirmed"] = true
            };

            return await TryUpdateDocumentAsync(sameNameDoc, sameNameUpdates);
        }

        bool reserved = await TryCreateDisplayNameIndexAsync(nextKey, trimmedUid);
        if (!reserved)
        {
            return false;
        }

        object playerDoc = GetDocumentReference(PLAYERS_COLLECTION, trimmedUid);
        if (playerDoc == null)
        {
            await DeleteDisplayNameIndexAsync(nextKey);
            return false;
        }

        Dictionary<string, object> updates = new Dictionary<string, object>
        {
            ["DisplayName"] = resolvedName,
            ["IsDisplayNameConfirmed"] = true
        };

        bool updated = await TryUpdateDocumentAsync(playerDoc, updates);
        if (!updated)
        {
            await DeleteDisplayNameIndexAsync(nextKey);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(prevKey))
        {
            await DeleteDisplayNameIndexAsync(prevKey);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> IsDisplayNameAvailableAsync(string name)
    {
        if (!_isFirestoreReady)
        {
            return await _fallbackService.IsDisplayNameAvailableAsync(name);
        }

        string key = NormalizeDisplayNameKey(name);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        object doc = GetDocumentReference(DISPLAY_NAMES_COLLECTION, key);
        if (doc == null)
        {
            return false;
        }

        object snapshot = await GetDocumentSnapshotAsync(doc);
        return !SnapshotExists(snapshot);
    }

    #endregion

    #region Firestore Helpers

    private static void TryConfigureDesktopFirestoreSettings(object firestore)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (firestore == null)
        {
            return;
        }

        try
        {
            Type firestoreType = firestore.GetType();
            PropertyInfo settingsProperty = firestoreType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Instance);
            if (settingsProperty == null || !settingsProperty.CanRead || !settingsProperty.CanWrite)
            {
                return;
            }

            object currentSettings = settingsProperty.GetValue(firestore);
            if (currentSettings == null)
            {
                Type settingsType = Type.GetType(FIRESTORE_SETTINGS_TYPE_NAME);
                if (settingsType == null)
                {
                    return;
                }

                currentSettings = Activator.CreateInstance(settingsType);
                if (currentSettings == null)
                {
                    return;
                }
            }

            PropertyInfo persistenceEnabledProperty = currentSettings.GetType()
                .GetProperty("PersistenceEnabled", BindingFlags.Public | BindingFlags.Instance);
            if (persistenceEnabledProperty == null || !persistenceEnabledProperty.CanWrite)
            {
                return;
            }

            bool currentEnabled = false;
            if (persistenceEnabledProperty.CanRead)
            {
                object value = persistenceEnabledProperty.GetValue(currentSettings);
                currentEnabled = value is bool boolValue && boolValue;
            }

            if (!currentEnabled)
            {
                return;
            }

            persistenceEnabledProperty.SetValue(currentSettings, false);
            settingsProperty.SetValue(firestore, currentSettings);
            Debug.Log("[FirestorePlayerDataService] 데스크톱 멀티 인스턴스 충돌 완화를 위해 Firestore PersistenceEnabled=false로 설정했습니다.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FirestorePlayerDataService] Firestore Settings 적용 실패(계속 진행): {ex.Message}");
        }
#endif
    }

    private static object TryGetFirestoreInstance()
    {
        Type firestoreType = Type.GetType(FIRESTORE_TYPE_NAME);
        if (firestoreType == null)
        {
            return null;
        }

        object scopedInstance = TryGetFirestoreInstanceFromRuntimeContext(firestoreType, out bool runtimeContextAttempted);
        if (scopedInstance != null)
        {
            return scopedInstance;
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (runtimeContextAttempted)
        {
            // Why: 멀티 인스턴스 테스트에서 __FIRAPP_DEFAULT 잠금 충돌을 피하기 위해 기본 인스턴스 폴백을 막습니다.
            Debug.LogWarning("[FirestorePlayerDataService] runtimeApp Firestore 인스턴스 생성 실패로 기본 인스턴스 폴백을 건너뜁니다.");
            return null;
        }
#endif

        PropertyInfo defaultInstanceProperty = firestoreType.GetProperty("DefaultInstance", BindingFlags.Public | BindingFlags.Static);
        return defaultInstanceProperty?.GetValue(null);
    }

    private static object TryGetFirestoreInstanceFromRuntimeContext(Type firestoreType, out bool attempted)
    {
        attempted = false;

        if (firestoreType == null)
        {
            return null;
        }

        if (!ServiceLocator.TryGet<FirebaseRuntimeAppContext>(out FirebaseRuntimeAppContext context) ||
            context == null ||
            context.App == null)
        {
            return null;
        }

        attempted = true;

        MethodInfo[] methods = firestoreType.GetMethods(BindingFlags.Public | BindingFlags.Static);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (!string.Equals(method.Name, "GetInstance", StringComparison.Ordinal))
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1)
            {
                continue;
            }

            if (!parameters[0].ParameterType.IsInstanceOfType(context.App))
            {
                continue;
            }

            try
            {
                object firestore = method.Invoke(null, new[] { context.App });
                if (firestore != null)
                {
                    Debug.Log($"[FirestorePlayerDataService] FirebaseFirestore.GetInstance(runtimeApp) 사용: AppName={context.AppName}, ProcessScoped={context.IsProcessScoped}");
                    return firestore;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[FirestorePlayerDataService] runtimeApp Firestore 인스턴스 생성 실패: {ex.Message}");
                return null;
            }
        }

        return null;
    }

    private object GetDocumentReference(string collectionName, string documentId)
    {
        object collection = TryInvokeInstanceMethod(_firestore, "Collection", collectionName);
        if (collection == null)
        {
            return null;
        }

        return TryInvokeInstanceMethod(collection, "Document", documentId);
    }

    private async Task<object> GetDocumentSnapshotAsync(object documentReference)
    {
        if (documentReference == null)
        {
            return null;
        }

        object getTask = TryInvokeInstanceMethod(documentReference, "GetSnapshotAsync");
        return await AwaitTaskResultAsync(getTask);
    }

    private static bool SnapshotExists(object snapshot)
    {
        if (snapshot == null)
        {
            return false;
        }

        return GetBoolProperty(snapshot, "Exists");
    }

    private static Dictionary<string, object> SnapshotToDictionary(object snapshot)
    {
        if (snapshot == null)
        {
            return null;
        }

        object dictionaryObject = TryInvokeInstanceMethod(snapshot, "ToDictionary");
        return ToStringObjectDictionary(dictionaryObject);
    }

    private async Task<bool> SetPlayerDocumentAsync(PlayerProfile profile)
    {
        object playerDoc = GetDocumentReference(PLAYERS_COLLECTION, profile.Uid);
        if (playerDoc == null)
        {
            return false;
        }

        Dictionary<string, object> data = BuildProfileData(profile);
        object setTask = TryInvokeInstanceMethod(playerDoc, "SetAsync", data);
        if (setTask == null)
        {
            return false;
        }

        await AwaitTaskAsync(setTask);
        return true;
    }

    private async Task<bool> TryUpdateDocumentAsync(object documentReference, Dictionary<string, object> updates)
    {
        if (documentReference == null || updates == null || updates.Count == 0)
        {
            return false;
        }

        object updateTask = TryInvokeInstanceMethod(documentReference, "UpdateAsync", updates)
            ?? TryInvokeInstanceMethod(documentReference, "SetAsync", updates);
        if (updateTask == null)
        {
            return false;
        }

        await AwaitTaskAsync(updateTask);
        return true;
    }

    private async Task<bool> TryCreateDisplayNameIndexAsync(string normalizedName, string uid)
    {
        object nameDoc = GetDocumentReference(DISPLAY_NAMES_COLLECTION, normalizedName);
        if (nameDoc == null)
        {
            return false;
        }

        Dictionary<string, object> data = new Dictionary<string, object>
        {
            ["Uid"] = uid,
            ["CreatedAtUtc"] = DateTime.UtcNow
        };

        try
        {
            object createTask = TryInvokeInstanceMethod(nameDoc, "CreateAsync", data);
            if (createTask != null)
            {
                await AwaitTaskAsync(createTask);
                return true;
            }

            object snapshot = await GetDocumentSnapshotAsync(nameDoc);
            if (SnapshotExists(snapshot))
            {
                Dictionary<string, object> current = SnapshotToDictionary(snapshot);
                string occupiedUid = GetStringValue(current, "Uid");
                return string.Equals(occupiedUid, uid, StringComparison.Ordinal);
            }

            object setTask = TryInvokeInstanceMethod(nameDoc, "SetAsync", data);
            if (setTask == null)
            {
                return false;
            }

            await AwaitTaskAsync(setTask);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task DeleteDisplayNameIndexAsync(string normalizedName)
    {
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return;
        }

        object nameDoc = GetDocumentReference(DISPLAY_NAMES_COLLECTION, normalizedName);
        if (nameDoc == null)
        {
            return;
        }

        object deleteTask = TryInvokeInstanceMethod(nameDoc, "DeleteAsync");
        if (deleteTask is Task task)
        {
            await task;
        }
    }

    #endregion

    #region Data Mapping

    private static Dictionary<string, object> BuildProfileData(PlayerProfile profile)
    {
        profile ??= new PlayerProfile();
        return new Dictionary<string, object>
        {
            ["Uid"] = profile.Uid ?? string.Empty,
            ["DisplayName"] = profile.DisplayName ?? string.Empty,
            ["IsDisplayNameConfirmed"] = profile.IsDisplayNameConfirmed,
            ["Email"] = profile.Email ?? string.Empty,
            ["CreatedAtUtc"] = profile.CreatedAtUtc,
            ["LastLoginAtUtc"] = profile.LastLoginAtUtc,
            ["TotalMatches"] = profile.TotalMatches,
            ["Wins"] = profile.Wins,
            ["Draws"] = profile.Draws,
            ["Kills"] = profile.Kills,
            ["Deaths"] = profile.Deaths,
            ["ModeStats"] = BuildModeStatsData(profile),
            ["RankedStats"] = BuildRankedStatsData(profile),
            ["RecentMatches"] = BuildRecentMatchesData(profile.RecentMatches),
            ["ProcessedMatchIds"] = BuildProcessedMatchIdsData(profile.ProcessedMatchIds),
            ["LastMatchAtUtc"] = profile.LastMatchAtUtc
        };
    }

    private static PlayerProfile ParseProfile(Dictionary<string, object> data, string fallbackUid)
    {
        if (data == null)
        {
            return null;
        }

        string storedDisplayName = GetStringValue(data, "DisplayName");
        bool hasDisplayNameConfirmedField = data.ContainsKey("IsDisplayNameConfirmed");

        PlayerProfile profile = new PlayerProfile
        {
            Uid = GetStringValue(data, "Uid", fallbackUid),
            DisplayName = storedDisplayName,
            IsDisplayNameConfirmed = GetBoolValue(data, "IsDisplayNameConfirmed"),
            Email = GetStringValue(data, "Email"),
            CreatedAtUtc = GetDateTimeValue(data, "CreatedAtUtc", DateTime.UtcNow),
            LastLoginAtUtc = GetDateTimeValue(data, "LastLoginAtUtc", DateTime.UtcNow),
            TotalMatches = GetIntValue(data, "TotalMatches"),
            Wins = GetIntValue(data, "Wins"),
            Draws = GetIntValue(data, "Draws"),
            Kills = GetIntValue(data, "Kills"),
            Deaths = GetIntValue(data, "Deaths"),
            RankedMmr = GetIntValue(GetDictionaryValue(data, "RankedStats"), "Mmr"),
            RankedSeasonBestMmr = GetIntValue(GetDictionaryValue(data, "RankedStats"), "SeasonBestMmr"),
            RankedTier = GetStringValue(GetDictionaryValue(data, "RankedStats"), "Tier", "Rookie"),
            LastMatchAtUtc = GetDateTimeValue(data, "LastMatchAtUtc", DateTime.UtcNow)
        };

        if (!hasDisplayNameConfirmedField)
        {
            profile.IsDisplayNameConfirmed = !string.IsNullOrWhiteSpace(storedDisplayName);
        }

        if (string.IsNullOrWhiteSpace(profile.DisplayName))
        {
            profile.DisplayName = ResolveDisplayName(string.Empty, profile.Uid);
        }

        if (profile.RankedMmr <= 0)
        {
            profile.RankedMmr = DEFAULT_RANKED_MMR;
        }

        if (profile.RankedSeasonBestMmr <= 0)
        {
            profile.RankedSeasonBestMmr = Math.Max(DEFAULT_RANKED_MMR, profile.RankedMmr);
        }

        if (string.IsNullOrWhiteSpace(profile.RankedTier))
        {
            profile.RankedTier = ResolveRankedTier(profile.RankedMmr);
        }

        Dictionary<string, object> modeStatsData = GetDictionaryValue(data, "ModeStats");
        profile.NormalModeStats = ParseModeStats(GetDictionaryValue(modeStatsData, "Normal"));
        profile.RankedModeStats = ParseModeStats(GetDictionaryValue(modeStatsData, "Ranked"));
        profile.RecentMatches = ParseRecentMatches(GetListValue(data, "RecentMatches"));
        profile.ProcessedMatchIds = ParseProcessedMatchIds(GetListValue(data, "ProcessedMatchIds"));

        return profile;
    }

    private static Dictionary<string, object> BuildModeStatsData(PlayerProfile profile)
    {
        return new Dictionary<string, object>
        {
            ["Normal"] = BuildSingleModeStatsData(profile?.NormalModeStats),
            ["Ranked"] = BuildSingleModeStatsData(profile?.RankedModeStats)
        };
    }

    private static Dictionary<string, object> BuildSingleModeStatsData(PlayerModeStats stats)
    {
        stats ??= new PlayerModeStats();
        return new Dictionary<string, object>
        {
            ["TotalMatches"] = stats.TotalMatches,
            ["Wins"] = stats.Wins,
            ["Draws"] = stats.Draws,
            ["Kills"] = stats.Kills,
            ["Deaths"] = stats.Deaths
        };
    }

    private static Dictionary<string, object> BuildRankedStatsData(PlayerProfile profile)
    {
        profile ??= new PlayerProfile();
        return new Dictionary<string, object>
        {
            ["Mmr"] = profile.RankedMmr,
            ["SeasonBestMmr"] = profile.RankedSeasonBestMmr,
            ["Tier"] = profile.RankedTier ?? ResolveRankedTier(profile.RankedMmr)
        };
    }

    private static List<object> BuildRecentMatchesData(List<RecentMatchSummary> matches)
    {
        List<object> serialized = new List<object>();
        if (matches == null || matches.Count == 0)
        {
            return serialized;
        }

        int count = Math.Min(matches.Count, RECENT_MATCH_LIMIT);
        for (int i = 0; i < count; i++)
        {
            RecentMatchSummary summary = matches[i];
            if (summary == null || string.IsNullOrWhiteSpace(summary.MatchId))
            {
                continue;
            }

            serialized.Add(new Dictionary<string, object>
            {
                ["MatchId"] = summary.MatchId,
                ["Mode"] = summary.Mode ?? string.Empty,
                ["Rank"] = summary.Rank,
                ["Kills"] = summary.Kills,
                ["Deaths"] = summary.Deaths,
                ["MmrDelta"] = summary.MmrDelta,
                ["IsWin"] = summary.IsWin,
                ["IsDraw"] = summary.IsDraw,
                ["EndedAtUtc"] = summary.EndedAtUtc
            });
        }

        return serialized;
    }

    private static List<object> BuildProcessedMatchIdsData(List<string> processedMatchIds)
    {
        List<object> serialized = new List<object>();
        if (processedMatchIds == null || processedMatchIds.Count == 0)
        {
            return serialized;
        }

        int count = Math.Min(processedMatchIds.Count, PROCESSED_MATCH_LIMIT);
        for (int i = 0; i < count; i++)
        {
            string matchId = processedMatchIds[i];
            if (!string.IsNullOrWhiteSpace(matchId))
            {
                serialized.Add(matchId.Trim());
            }
        }

        return serialized;
    }

    private static PlayerModeStats ParseModeStats(Dictionary<string, object> data)
    {
        if (data == null)
        {
            return new PlayerModeStats();
        }

        return new PlayerModeStats
        {
            TotalMatches = GetIntValue(data, "TotalMatches"),
            Wins = GetIntValue(data, "Wins"),
            Draws = GetIntValue(data, "Draws"),
            Kills = GetIntValue(data, "Kills"),
            Deaths = GetIntValue(data, "Deaths")
        };
    }

    private static List<RecentMatchSummary> ParseRecentMatches(List<object> values)
    {
        List<RecentMatchSummary> result = new List<RecentMatchSummary>();
        if (values == null || values.Count == 0)
        {
            return result;
        }

        for (int i = 0; i < values.Count; i++)
        {
            Dictionary<string, object> row = ToStringObjectDictionary(values[i]);
            if (row == null)
            {
                continue;
            }

            string matchId = GetStringValue(row, "MatchId");
            if (string.IsNullOrWhiteSpace(matchId))
            {
                continue;
            }

            result.Add(new RecentMatchSummary
            {
                MatchId = matchId,
                Mode = GetStringValue(row, "Mode"),
                Rank = Math.Max(1, GetIntValue(row, "Rank")),
                Kills = Math.Max(0, GetIntValue(row, "Kills")),
                Deaths = Math.Max(0, GetIntValue(row, "Deaths")),
                MmrDelta = GetIntValue(row, "MmrDelta"),
                IsWin = GetBoolValue(row, "IsWin"),
                IsDraw = GetBoolValue(row, "IsDraw"),
                EndedAtUtc = GetDateTimeValue(row, "EndedAtUtc", DateTime.UtcNow)
            });
        }

        result.Sort((left, right) => right.EndedAtUtc.CompareTo(left.EndedAtUtc));
        while (result.Count > RECENT_MATCH_LIMIT)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    private static List<string> ParseProcessedMatchIds(List<object> values)
    {
        List<string> result = new List<string>();
        if (values == null || values.Count == 0)
        {
            return result;
        }

        for (int i = 0; i < values.Count; i++)
        {
            string matchId = values[i]?.ToString();
            if (string.IsNullOrWhiteSpace(matchId))
            {
                continue;
            }

            matchId = matchId.Trim();
            if (!result.Contains(matchId))
            {
                result.Add(matchId);
            }

            if (result.Count >= PROCESSED_MATCH_LIMIT)
            {
                break;
            }
        }

        return result;
    }

    private static string GetStringValue(Dictionary<string, object> data, string key, string fallback = "")
    {
        if (data != null && data.TryGetValue(key, out object value) && value != null)
        {
            return value.ToString();
        }

        return fallback ?? string.Empty;
    }

    private static bool GetBoolValue(Dictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out object value) || value == null)
        {
            return false;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        return bool.TryParse(value.ToString(), out bool parsed) && parsed;
    }

    private static int GetIntValue(Dictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out object value) || value == null)
        {
            return 0;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (value is long longValue)
        {
            return (int)longValue;
        }

        if (value is double doubleValue)
        {
            return (int)doubleValue;
        }

        if (int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static float GetFloatValue(Dictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out object value) || value == null)
        {
            return 0f;
        }

        if (value is float floatValue)
        {
            return floatValue;
        }

        if (value is double doubleValue)
        {
            return (float)doubleValue;
        }

        if (value is int intValue)
        {
            return intValue;
        }

        if (float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
        {
            return parsed;
        }

        return 0f;
    }

    private static Dictionary<string, object> GetDictionaryValue(Dictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out object value) || value == null)
        {
            return null;
        }

        return ToStringObjectDictionary(value);
    }

    private static List<object> GetListValue(Dictionary<string, object> data, string key)
    {
        if (data == null || !data.TryGetValue(key, out object value) || value == null)
        {
            return null;
        }

        if (value is List<object> typedList)
        {
            return typedList;
        }

        if (value is IList genericList)
        {
            List<object> converted = new List<object>(genericList.Count);
            for (int i = 0; i < genericList.Count; i++)
            {
                converted.Add(genericList[i]);
            }

            return converted;
        }

        return null;
    }

    private static DateTime GetDateTimeValue(Dictionary<string, object> data, string key, DateTime fallback)
    {
        if (data == null || !data.TryGetValue(key, out object value) || value == null)
        {
            return fallback;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.Kind == DateTimeKind.Utc ? dateTime : dateTime.ToUniversalTime();
        }

        if (DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime parsed))
        {
            return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }

        MethodInfo toDateTimeMethod = value.GetType().GetMethod("ToDateTime", BindingFlags.Public | BindingFlags.Instance);
        if (toDateTimeMethod != null)
        {
            object dateObject = toDateTimeMethod.Invoke(value, null);
            if (dateObject is DateTime fromTimestamp)
            {
                return fromTimestamp.Kind == DateTimeKind.Utc ? fromTimestamp : fromTimestamp.ToUniversalTime();
            }
        }

        PropertyInfo secondsProperty = value.GetType().GetProperty("Seconds", BindingFlags.Public | BindingFlags.Instance);
        if (secondsProperty != null)
        {
            object secondsValue = secondsProperty.GetValue(value);
            if (secondsValue != null && long.TryParse(secondsValue.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            }
        }

        return fallback;
    }

    private static Dictionary<string, object> ToStringObjectDictionary(object source)
    {
        if (source == null)
        {
            return null;
        }

        if (source is Dictionary<string, object> typedDictionary)
        {
            return typedDictionary;
        }

        if (source is IDictionary genericDictionary)
        {
            Dictionary<string, object> converted = new Dictionary<string, object>();
            foreach (DictionaryEntry entry in genericDictionary)
            {
                string key = entry.Key?.ToString();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                converted[key] = entry.Value;
            }

            return converted;
        }

        return null;
    }

    #endregion

    #region Utility

    private static string ResolveDisplayName(string displayName, string uid)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        return $"Player_{GetSafeSuffix(uid, 4)}";
    }

    private static string NormalizeDisplayNameKey(string displayName)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : displayName.Trim().ToLowerInvariant();
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

    private static string ResolveRankedTier(int mmr)
    {
        if (mmr >= 5000)
        {
            return "Void";
        }

        if (mmr >= 2000)
        {
            return "Nova";
        }

        if (mmr >= 1000)
        {
            return "Pro";
        }

        if (mmr >= 600)
        {
            return "Ace";
        }

        if (mmr >= 300)
        {
            return "Elite";
        }

        return "Rookie";
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

        return value != null && bool.TryParse(value.ToString(), out bool parsed) && parsed;
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
                // Why: 오버로드 매칭 실패 시 다음 메서드를 시도합니다.
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

    #endregion
}
