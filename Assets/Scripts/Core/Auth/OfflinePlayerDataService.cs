using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 오프라인 플레이어 데이터 서비스입니다.
/// </summary>
public class OfflinePlayerDataService : IPlayerDataService
{
    #region Constants

    private const string DEFAULT_EMAIL_DISPLAY_NAME_PREFIX = "Player";
    private const string DEFAULT_GUEST_DISPLAY_NAME_PREFIX = "Guest";

    #endregion

    #region Private Fields

    private static readonly Dictionary<string, PlayerProfile> _profilesByUid = new();
    private static readonly Dictionary<string, string> _uidByDisplayNameKey = new();
    private static readonly object _lock = new();

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public Task<PlayerProfile> GetPlayerProfileAsync(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return Task.FromResult<PlayerProfile>(null);
        }

        lock (_lock)
        {
            _profilesByUid.TryGetValue(uid, out PlayerProfile profile);
            return Task.FromResult(profile);
        }
    }

    /// <inheritdoc />
    public Task<PlayerProfile> CreatePlayerProfileAsync(string uid, string displayName, string email, bool isGuest, string guestId = "")
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return Task.FromResult<PlayerProfile>(null);
        }

        string normalizedName = NormalizeDisplayName(displayName);
        if (string.IsNullOrEmpty(normalizedName))
        {
            normalizedName = isGuest
                ? $"{DEFAULT_GUEST_DISPLAY_NAME_PREFIX}_{GetSafeSuffix(uid, 4)}"
                : $"{DEFAULT_EMAIL_DISPLAY_NAME_PREFIX}_{GetSafeSuffix(uid, 4)}";
        }

        lock (_lock)
        {
            if (_profilesByUid.TryGetValue(uid, out PlayerProfile existing))
            {
                return Task.FromResult(existing);
            }

            string displayKey = NormalizeDisplayNameKey(normalizedName);
            if (_uidByDisplayNameKey.TryGetValue(displayKey, out string existingUid) &&
                !string.Equals(existingUid, uid, StringComparison.Ordinal))
            {
                return Task.FromResult<PlayerProfile>(null);
            }

            string resolvedGuestId = isGuest
                ? ResolveGuestId(guestId)
                : string.Empty;

            DateTime nowUtc = DateTime.UtcNow;
            PlayerProfile profile = new PlayerProfile
            {
                Uid = uid,
                GuestId = resolvedGuestId,
                IsGuest = isGuest,
                DisplayName = normalizedName,
                IsDisplayNameConfirmed = isGuest,
                Email = email ?? string.Empty,
                CreatedAtUtc = nowUtc,
                LastLoginAtUtc = nowUtc
            };

            _profilesByUid[uid] = profile;
            _uidByDisplayNameKey[displayKey] = uid;

            return Task.FromResult(profile);
        }
    }

    /// <inheritdoc />
    public async Task<PlayerProfile> EnsureGuestProfileAsync(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return null;
        }

        PlayerProfile profile = await GetPlayerProfileAsync(uid);
        if (profile != null)
        {
            if (string.IsNullOrWhiteSpace(profile.GuestId))
            {
                profile.GuestId = GenerateGuestId();
            }

            if (string.IsNullOrWhiteSpace(profile.DisplayName))
            {
                profile.DisplayName = $"{DEFAULT_GUEST_DISPLAY_NAME_PREFIX}_{GetSafeSuffix(profile.GuestId, 4)}";
            }

            profile.IsGuest = true;
            profile.IsDisplayNameConfirmed = true;
            return profile;
        }

        string newGuestId = GenerateGuestId();
        string guestDisplayName = $"{DEFAULT_GUEST_DISPLAY_NAME_PREFIX}_{GetSafeSuffix(newGuestId, 4)}";
        return await CreatePlayerProfileAsync(uid, guestDisplayName, string.Empty, true, newGuestId);
    }

    /// <inheritdoc />
    public Task UpdateLastLoginAtAsync(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return Task.CompletedTask;
        }

        lock (_lock)
        {
            if (_profilesByUid.TryGetValue(uid, out PlayerProfile profile))
            {
                profile.LastLoginAtUtc = DateTime.UtcNow;
            }
        }

        return Task.CompletedTask;
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

        Debug.LogWarning("[OfflinePlayerDataService] IServerMatchResultService가 없어 매치 결과를 커밋하지 못했습니다.");
        return false;
    }

    /// <inheritdoc />
    public Task<bool> UpdateDisplayNameAsync(string uid, string newName)
    {
        if (string.IsNullOrWhiteSpace(uid))
        {
            return Task.FromResult(false);
        }

        string normalizedName = NormalizeDisplayName(newName);
        if (string.IsNullOrEmpty(normalizedName))
        {
            return Task.FromResult(false);
        }

        lock (_lock)
        {
            if (!_profilesByUid.TryGetValue(uid, out PlayerProfile profile))
            {
                return Task.FromResult(false);
            }

            string nextKey = NormalizeDisplayNameKey(normalizedName);
            if (_uidByDisplayNameKey.TryGetValue(nextKey, out string occupiedUid) &&
                !string.Equals(occupiedUid, uid, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            string prevKey = NormalizeDisplayNameKey(profile.DisplayName);
            if (!string.IsNullOrEmpty(prevKey))
            {
                _uidByDisplayNameKey.Remove(prevKey);
            }

            profile.DisplayName = normalizedName;
            profile.IsDisplayNameConfirmed = true;
            _uidByDisplayNameKey[nextKey] = uid;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> IsDisplayNameAvailableAsync(string name)
    {
        string key = NormalizeDisplayNameKey(name);
        if (string.IsNullOrEmpty(key))
        {
            return Task.FromResult(false);
        }

        lock (_lock)
        {
            return Task.FromResult(!_uidByDisplayNameKey.ContainsKey(key));
        }
    }

    #endregion

    #region Helper Methods

    private static string NormalizeDisplayName(string displayName)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : displayName.Trim();
    }

    private static string NormalizeDisplayNameKey(string displayName)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : displayName.Trim().ToLowerInvariant();
    }

    private static string ResolveGuestId(string guestId)
    {
        return string.IsNullOrWhiteSpace(guestId) ? GenerateGuestId() : guestId.Trim().ToUpperInvariant();
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

    /// <summary>
    /// 사용자 표시용 GuestId를 생성합니다.
    /// </summary>
    public static string GenerateGuestId()
    {
        string value = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8].ToUpperInvariant();
        return $"G-{value}";
    }

    #endregion
}
