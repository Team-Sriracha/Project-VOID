using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 모드 정의를 중앙에서 조회하는 카탈로그입니다.
/// 외부에서 카탈로그 에셋을 주입받고, 없으면 내장 기본 정의를 사용합니다.
/// </summary>
[CreateAssetMenu(fileName = "GameModeCatalog", menuName = "Game/Mode Catalog")]
public class GameModeCatalog : ScriptableObject
{
    #region Serialized Fields

    [SerializeField] private GameModeDefinition[] _definitions = Array.Empty<GameModeDefinition>();

    #endregion

    #region Static Fields

    private static bool _isInitialized;
    private static GameModeCatalog _activeCatalog;
    private static readonly Dictionary<GameMode, GameModeDefinition> _byMode = new();
    private static readonly Dictionary<string, GameModeDefinition> _byKey = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<GameModeDefinition> _lobbyModes = new();

    #endregion

    #region Runtime Initialization

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RuntimeInitialize()
    {
        _isInitialized = false;
        _activeCatalog = null;
        _byMode.Clear();
        _byKey.Clear();
        _lobbyModes.Clear();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 런타임에서 사용할 카탈로그 에셋을 설정합니다.
    /// </summary>
    public static void SetActiveCatalog(GameModeCatalog catalog)
    {
        _activeCatalog = catalog;
        _isInitialized = false;
        EnsureInitialized();
    }

    public static void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        _byMode.Clear();
        _byKey.Clear();
        _lobbyModes.Clear();

        if (_activeCatalog != null && _activeCatalog._definitions != null && _activeCatalog._definitions.Length > 0)
        {
            RegisterDefinitions(_activeCatalog._definitions);
        }

        if (_byMode.Count == 0)
        {
            RegisterDefinitions(CreateFallbackDefinitions());
            Debug.LogWarning("[GameModeCatalog] 활성 카탈로그가 없어 기본 정의를 사용합니다.");
        }

        BuildLobbyModes();
        ValidateMappings();
    }

    public static bool TryGetByMode(GameMode mode, out GameModeDefinition definition)
    {
        EnsureInitialized();
        return _byMode.TryGetValue(mode, out definition);
    }

    public static GameModeDefinition GetByMode(GameMode mode)
    {
        EnsureInitialized();
        _byMode.TryGetValue(mode, out GameModeDefinition definition);
        return definition;
    }

    public static bool TryGetByModeId(string modeId, out GameModeDefinition definition)
    {
        EnsureInitialized();
        return _byKey.TryGetValue(NormalizeKey(modeId), out definition);
    }

    public static bool TryGetByAlias(string key, out GameModeDefinition definition)
    {
        EnsureInitialized();
        return _byKey.TryGetValue(NormalizeKey(key), out definition);
    }

    public static IReadOnlyList<GameModeDefinition> GetLobbyModes()
    {
        EnsureInitialized();
        return _lobbyModes;
    }

    public static string GetModeId(GameMode mode)
    {
        if (TryGetByMode(mode, out GameModeDefinition definition))
        {
            return definition.ModeId;
        }

        return mode.ToString();
    }

    public static int GetMinPlayers(GameMode mode, int fallback = 1)
    {
        if (mode == GameMode.Custom)
        {
            return 2;
        }

        if (TryGetByMode(mode, out GameModeDefinition definition))
        {
            return definition.MinPlayers;
        }

        return fallback;
    }

    public static int GetMaxPlayers(GameMode mode, int fallback = 8)
    {
        if (mode == GameMode.Custom)
        {
            return 8;
        }

        if (TryGetByMode(mode, out GameModeDefinition definition))
        {
            return definition.MaxPlayers;
        }

        return fallback;
    }

    public static bool IsPracticeMode(GameMode mode)
    {
        if (TryGetByMode(mode, out GameModeDefinition definition))
        {
            return definition.IsPractice;
        }

        return mode == GameMode.PracticeRange;
    }

    public static bool IsQueueMatchmakingMode(GameMode mode)
    {
        if (TryGetByMode(mode, out GameModeDefinition definition))
        {
            return definition.IsQueueMatchMode;
        }

        return mode == GameMode.FourPlayer || mode == GameMode.EightPlayer || mode == GameMode.Ranked;
    }

    public static bool AreSameModeKey(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        string leftKey = NormalizeKey(left);
        string rightKey = NormalizeKey(right);

        if (leftKey == rightKey)
        {
            return true;
        }

        EnsureInitialized();

        bool hasLeft = _byKey.TryGetValue(leftKey, out GameModeDefinition leftMode);
        bool hasRight = _byKey.TryGetValue(rightKey, out GameModeDefinition rightMode);

        if (hasLeft && hasRight)
        {
            return leftMode.Mode == rightMode.Mode;
        }

        return false;
    }

    #endregion

    #region Private Methods

    private static void RegisterDefinitions(IEnumerable<GameModeDefinition> definitions)
    {
        foreach (GameModeDefinition definition in definitions)
        {
            if (definition == null)
            {
                continue;
            }

            if (_byMode.ContainsKey(definition.Mode))
            {
                Debug.LogWarning($"[GameModeCatalog] 중복 모드 정의 무시: {definition.Mode}");
                continue;
            }

            _byMode.Add(definition.Mode, definition);
            RegisterKey(definition.ModeId, definition);
            RegisterKey(definition.Mode.ToString(), definition);
            RegisterKey(definition.SessionTag, definition);

            IReadOnlyList<string> aliases = definition.LegacyAliases;
            if (aliases == null)
            {
                continue;
            }

            for (int i = 0; i < aliases.Count; i++)
            {
                RegisterKey(aliases[i], definition);
            }
        }
    }

    private static void RegisterKey(string key, GameModeDefinition definition)
    {
        string normalized = NormalizeKey(key);
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        if (!_byKey.ContainsKey(normalized))
        {
            _byKey.Add(normalized, definition);
        }
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant();
    }

    private static void BuildLobbyModes()
    {
        AddLobbyMode(GameMode.FourPlayer);
        AddLobbyMode(GameMode.EightPlayer);
        AddLobbyMode(GameMode.Ranked);
        AddLobbyMode(GameMode.Custom);
        AddLobbyMode(GameMode.PracticeRange);
    }

    private static void AddLobbyMode(GameMode mode)
    {
        if (TryGetByMode(mode, out GameModeDefinition definition))
        {
            _lobbyModes.Add(definition);
        }
    }

    private static void ValidateMappings()
    {
        foreach (KeyValuePair<GameMode, GameModeDefinition> pair in _byMode)
        {
            GameModeDefinition definition = pair.Value;
            if (definition.MinPlayers > definition.MaxPlayers)
            {
                Debug.LogWarning($"[GameModeCatalog] 인원 설정 오류: {definition.Mode} ({definition.MinPlayers}>{definition.MaxPlayers})");
            }
        }
    }

    private static GameModeDefinition[] CreateFallbackDefinitions()
    {
        return new[]
        {
            GameModeDefinition.CreateRuntime(
                GameMode.FourPlayer,
                "normal_4",
                "일반 4인",
                4,
                4,
                sessionTag: "normal_4",
                queueBucket: "normal",
                legacyAliases: new[] { "fourplayer", "four_player", "four" }),
            GameModeDefinition.CreateRuntime(
                GameMode.EightPlayer,
                "normal_8",
                "일반 8인",
                8,
                8,
                sessionTag: "normal_8",
                queueBucket: "normal",
                legacyAliases: new[] { "eightplayer", "eight_player", "eight" }),
            GameModeDefinition.CreateRuntime(
                GameMode.Ranked,
                "ranked_8",
                "랭크 8인",
                8,
                8,
                isRanked: true,
                sessionTag: "ranked_8",
                queueBucket: "ranked",
                legacyAliases: new[] { "ranked", "ranked8" }),
            GameModeDefinition.CreateRuntime(
                GameMode.Custom,
                "custom",
                "커스텀",
                2,
                8,
                isCustom: true,
                sessionTag: "custom",
                queueBucket: "custom",
                legacyAliases: new[] { "customroom", "custom_room" }),
            GameModeDefinition.CreateRuntime(
                GameMode.PracticeRange,
                "practice",
                "연습장",
                1,
                1,
                isPractice: true,
                sessionTag: "practice",
                queueBucket: "practice",
                legacyAliases: new[] { "practicerange", "practice_range" }),
        };
    }

    #endregion
}
