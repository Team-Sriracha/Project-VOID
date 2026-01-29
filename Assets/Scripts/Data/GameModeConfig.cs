using UnityEngine;

/// <summary>
/// 시간대별 등급 드랍 확률 설정
/// </summary>
[System.Serializable]
public struct TierDropRateByTime
{
    [Tooltip("이 시간(초) 이후부터 적용")]
    public float TimeThresholdSeconds;

    [Tooltip("노말 등급 확률 (%)")]
    [Range(0f, 100f)]
    public float NormalRate;

    [Tooltip("레어 등급 확률 (%)")]
    [Range(0f, 100f)]
    public float RareRate;

    [Tooltip("에픽 등급 확률 (%)")]
    [Range(0f, 100f)]
    public float EpicRate;

    [Tooltip("유니크 등급 확률 (%)")]
    [Range(0f, 100f)]
    public float UniqueRate;

    [Tooltip("레전더리 등급 확률 (%)")]
    [Range(0f, 100f)]
    public float LegendaryRate;
}

/// <summary>
/// 게임모드별 설정을 담는 ScriptableObject
/// </summary>
[CreateAssetMenu(fileName = "GameModeConfig", menuName = "Game/Game Mode Config")]
public class GameModeConfig : ScriptableObject
{
    #region Serialized Fields

    [Header("기본 설정")]
    [Tooltip("게임 모드")]
    [SerializeField] private GameMode _gameMode;

    [Tooltip("표시 이름 (예: 클래식, 신속)")]
    [SerializeField] private string _displayName;

    [Header("시간 설정")]
    [Tooltip("총 게임 시간 (초)")]
    [SerializeField] private float _totalGameTime = 600f;

    [Tooltip("Phase별 설정")]
    [SerializeField] private PhaseConfig[] _phaseConfigs;

    [Header("시간대별 등급 드랍 확률")]
    [Tooltip("시간 경과에 따른 등급 확률 변화")]
    [SerializeField] private TierDropRateByTime[] _tierDropRates = new TierDropRateByTime[]
    {
        // 0초: 노말 위주
        new TierDropRateByTime { TimeThresholdSeconds = 0f, NormalRate = 75f, RareRate = 20f, EpicRate = 4f, UniqueRate = 0.8f, LegendaryRate = 0.2f },
        // 180초 (3분): 레어 증가
        new TierDropRateByTime { TimeThresholdSeconds = 180f, NormalRate = 60f, RareRate = 25f, EpicRate = 10f, UniqueRate = 4f, LegendaryRate = 1f },
        // 360초 (6분): 고등급 증가
        new TierDropRateByTime { TimeThresholdSeconds = 360f, NormalRate = 45f, RareRate = 25f, EpicRate = 15f, UniqueRate = 10f, LegendaryRate = 5f },
        // 540초 (9분): 레전더리 증가
        new TierDropRateByTime { TimeThresholdSeconds = 540f, NormalRate = 30f, RareRate = 25f, EpicRate = 20f, UniqueRate = 15f, LegendaryRate = 10f }
    };

    #endregion

    #region Properties

    /// <summary>
    /// 게임 모드
    /// </summary>
    public GameMode GameMode => _gameMode;

    /// <summary>
    /// 표시 이름
    /// </summary>
    public string DisplayName => _displayName;

    /// <summary>
    /// 총 게임 시간 (초)
    /// </summary>
    public float TotalGameTime => _totalGameTime;

    /// <summary>
    /// Phase 설정 배열
    /// </summary>
    public PhaseConfig[] PhaseConfigs => _phaseConfigs;

    /// <summary>
    /// 시간대별 등급 드랍 확률 배열
    /// </summary>
    public TierDropRateByTime[] TierDropRates => _tierDropRates;

    #endregion

    #region Public Methods

    /// <summary>
    /// 경과 시간에 해당하는 등급 드랍 확률을 반환합니다.
    /// </summary>
    /// <param name="elapsedSeconds">경과 시간 (초)</param>
    /// <returns>해당 시간대의 등급 드랍 확률</returns>
    public TierDropRateByTime GetTierDropRateForTime(float elapsedSeconds)
    {
        if (_tierDropRates == null || _tierDropRates.Length == 0)
        {
            // 기본값 반환
            return new TierDropRateByTime
            {
                TimeThresholdSeconds = 0f,
                NormalRate = 100f,
                RareRate = 0f,
                UniqueRate = 0f,
                EpicRate = 0f,
                LegendaryRate = 0f
            };
        }

        // 시간대별로 역순 검색 (가장 최근 threshold 찾기)
        TierDropRateByTime result = _tierDropRates[0];
        for (int i = 0; i < _tierDropRates.Length; i++)
        {
            if (elapsedSeconds >= _tierDropRates[i].TimeThresholdSeconds)
            {
                result = _tierDropRates[i];
            }
            else
            {
                break;
            }
        }

        return result;
    }

    #endregion
}
