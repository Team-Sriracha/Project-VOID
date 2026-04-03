using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 게임 모드 1개에 대한 정의 데이터입니다.
/// </summary>
[CreateAssetMenu(fileName = "GameModeDefinition", menuName = "Game/Mode Definition")]
public class GameModeDefinition : ScriptableObject
{
    #region Serialized Fields

    [Header("식별")]
    [SerializeField] private GameMode _mode = GameMode.None;
    [SerializeField] private string _modeId = "none";
    [SerializeField] private string _displayName = "None";

    [Header("인원")]
    [SerializeField] private int _minPlayers = 1;
    [SerializeField] private int _maxPlayers = 8;

    [Header("속성")]
    [SerializeField] private bool _isRanked;
    [SerializeField] private bool _isPractice;
    [SerializeField] private bool _isCustom;

    [Header("세션/매칭")]
    [SerializeField] private string _sessionTag = "none";
    [SerializeField] private string _queueBucket = "none";
    [SerializeField] private string[] _legacyAliases = Array.Empty<string>();

    [Header("참조")]
    [SerializeField] private GameModeConfig _configRef;

    #endregion

    #region Properties

    public GameMode Mode => _mode;
    public string ModeId => _modeId;
    public string DisplayName => _displayName;
    public int MinPlayers => _minPlayers;
    public int MaxPlayers => _maxPlayers;
    public bool IsRanked => _isRanked;
    public bool IsPractice => _isPractice;
    public bool IsCustom => _isCustom;
    public string SessionTag => _sessionTag;
    public string QueueBucket => _queueBucket;
    public IReadOnlyList<string> LegacyAliases => _legacyAliases;
    public GameModeConfig ConfigRef => _configRef;
    public bool IsQueueMatchMode => !_isPractice && !_isCustom && _mode != GameMode.None;

    #endregion

    #region Public Methods

    /// <summary>
    /// 입력 키가 현재 모드 정의와 매칭되는지 확인합니다.
    /// </summary>
    public bool MatchesKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (string.Equals(_modeId, key, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(_sessionTag, key, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(_mode.ToString(), key, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_legacyAliases != null)
        {
            foreach (string alias in _legacyAliases)
            {
                if (string.Equals(alias, key, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 해당 모드의 허용 인원 범위인지 확인합니다.
    /// </summary>
    public bool SupportsPlayerCount(int playerCount)
    {
        return playerCount >= _minPlayers && playerCount <= _maxPlayers;
    }

    /// <summary>
    /// 카탈로그 에셋이 없는 경우를 위한 런타임 기본 정의를 생성합니다.
    /// </summary>
    public static GameModeDefinition CreateRuntime(
        GameMode mode,
        string modeId,
        string displayName,
        int minPlayers,
        int maxPlayers,
        bool isRanked = false,
        bool isPractice = false,
        bool isCustom = false,
        string sessionTag = "",
        string queueBucket = "",
        string[] legacyAliases = null)
    {
        GameModeDefinition definition = CreateInstance<GameModeDefinition>();
        definition._mode = mode;
        definition._modeId = modeId;
        definition._displayName = displayName;
        definition._minPlayers = minPlayers;
        definition._maxPlayers = maxPlayers;
        definition._isRanked = isRanked;
        definition._isPractice = isPractice;
        definition._isCustom = isCustom;
        definition._sessionTag = string.IsNullOrWhiteSpace(sessionTag) ? modeId : sessionTag;
        definition._queueBucket = string.IsNullOrWhiteSpace(queueBucket) ? modeId : queueBucket;
        definition._legacyAliases = legacyAliases ?? Array.Empty<string>();
        definition.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
        return definition;
    }

    #endregion
}
