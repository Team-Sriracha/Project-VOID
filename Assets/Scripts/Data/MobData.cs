using UnityEngine;

/// <summary>
/// 몬스터 상태 열거형
/// </summary>
public enum MonsterState
{
    Idle,      // 스폰 위치에서 대기
    Alert,     // 피격 후 놀라는 애니메이션, 회전
    Chase,     // NavMesh로 플레이어 추적
    Attack,    // 공격 범위 내에서 공격
    Return,    // 스폰 위치로 복귀
    Dead       // 사망, 리스폰 대기
}

/// <summary>
/// 몬스터 공격 패턴 데이터입니다.
/// </summary>
[System.Serializable]
public class MobAttackPatternData
{
    #region Serialized Fields

    [Tooltip("패턴 이름")]
    [SerializeField] private string _patternName = "Attack 01";

    [Tooltip("Animator에서 사용할 공격 패턴 인덱스")]
    [SerializeField] private int _animationIndex = 0;

    [Tooltip("이 패턴의 공격 범위 (미터)")]
    [SerializeField] private float _attackRange = 2f;

    [Tooltip("이 패턴의 공격 데미지")]
    [SerializeField] private float _attackDamage = 10f;

    [Tooltip("이 패턴의 공격 쿨다운 (초)")]
    [SerializeField] private float _attackCooldown = 1.5f;

    #endregion

    #region Properties

    public string PatternName => _patternName;
    public int AnimationIndex => _animationIndex;
    public float AttackRange => _attackRange;
    public float AttackDamage => _attackDamage;
    public float AttackCooldown => _attackCooldown;

    #endregion

    #region Validation

    public void Validate(float fallbackRange, float fallbackDamage, float fallbackCooldown)
    {
        if (string.IsNullOrWhiteSpace(_patternName))
        {
            _patternName = $"Attack {_animationIndex + 1:00}";
        }

        if (_animationIndex < 0)
        {
            _animationIndex = 0;
        }

        if (_attackRange <= 0f)
        {
            _attackRange = fallbackRange > 0f ? fallbackRange : 2f;
        }

        if (_attackDamage <= 0f)
        {
            _attackDamage = fallbackDamage > 0f ? fallbackDamage : 10f;
        }

        if (_attackCooldown <= 0f)
        {
            _attackCooldown = fallbackCooldown > 0f ? fallbackCooldown : 1.5f;
        }
    }

    #endregion
}

/// <summary>
/// Mob 스탯 및 설정 데이터 (ScriptableObject)
/// </summary>
[CreateAssetMenu(fileName = "New Mob Data", menuName = "Project VOID/Mob Data")]
public class MobData : ScriptableObject
{
    #region Serialized Fields

    [Header("기본 정보")]
    [Tooltip("Mob ID (고유 식별자, 예: goblin_basic)")]
    [SerializeField] private string _mobID = "goblin_basic";

    [Tooltip("Mob 이름 (UI에 표시됨)")]
    [SerializeField] private string _mobName = "Goblin";

    [Header("체력")]
    [Tooltip("최대 HP")]
    [SerializeField] private float _maxHP = 100f;

    [Header("전투")]
    [Tooltip("감지 범위 (미터) - 플레이어가 이 범위에 들어오면 바로 추격")]
    [SerializeField] private float _detectionRange = 8f;

    [Tooltip("공격 범위 (미터)")]
    [SerializeField] private float _attackRange = 2f;

    [Tooltip("추격 범위 (미터) - 이 범위 밖으로 플레이어가 나가면 귀환")]
    [SerializeField] private float _chaseRange = 15f;

    [Tooltip("공격 데미지")]
    [SerializeField] private float _attackDamage = 10f;

    [Tooltip("방어력 (추후 확장용)")]
    [SerializeField] private float _defense = 0f;

    [Tooltip("공격 쿨다운 (초)")]
    [SerializeField] private float _attackCooldown = 1.5f;

    [Header("전투 - 패턴 분기")]
    [Tooltip("패턴별 공격 설정. 비어 있으면 기본 공격 설정을 사용합니다.")]
    [SerializeField] private MobAttackPatternData[] _attackPatterns;

    [Tooltip("공격 패턴 실행 순서. 예: 0,0,1 => 1번, 1번, 2번 반복")]
    [SerializeField] private int[] _attackSequence;



    [Header("이동")]
    [Tooltip("이동 속도 (미터/초)")]
    [SerializeField] private float _moveSpeed = 3.5f;

    [Header("보상")]
    [Tooltip("처치 시 지급할 XP")]
    [SerializeField] private float _xpReward = 20f;

    [Header("경계 애니메이션")]
    [Tooltip("피격 후 경계(Alert) 지속 시간 (초)")]
    [SerializeField] private float _alertDuration = 1.5f;

    [Header("리스폰")]
    [Tooltip("죽은 후 리스폰까지 시간 (초)")]
    [SerializeField] private float _respawnTime = 30f;

    [Header("사운드")]
    [SerializeField] private AudioCue _alertAudioCue;
    [SerializeField] private AudioCue _attackAudioCue;
    [SerializeField] private AudioCue _hitAudioCue;
    [SerializeField] private AudioCue _deathAudioCue;

    [Header("시각 효과")]
    [Tooltip("몬스터 공격 시작 시 재생할 이펙트 프리팹")]
    [SerializeField] private GameObject _attackEffectPrefab;

    [Tooltip("공격 이펙트를 자동 제거할 시간(초)")]
    [SerializeField] private float _attackEffectDuration = 1f;

    #endregion

    #region Properties

    public string MobID => _mobID;
    public string MobName => _mobName;
    public float MaxHP => _maxHP;
    public float DetectionRange => _detectionRange;
    public float AttackRange => _attackRange;
    public float ChaseRange => _chaseRange;
    public float AttackDamage => _attackDamage;
    public float Defense => _defense;
    public float AttackCooldown => _attackCooldown;
    public int AttackPatternCount => _attackPatterns?.Length ?? 0;
    public int AttackSequenceLength => _attackSequence?.Length ?? 0;

    public float MoveSpeed => _moveSpeed;
    public float XPReward => _xpReward;
    public float AlertDuration => _alertDuration;
    public float RespawnTime => _respawnTime;
    public AudioCue AlertAudioCue => _alertAudioCue;
    public AudioCue AttackAudioCue => _attackAudioCue;
    public AudioCue HitAudioCue => _hitAudioCue;
    public AudioCue DeathAudioCue => _deathAudioCue;
    public GameObject AttackEffectPrefab => _attackEffectPrefab;
    public float AttackEffectDuration => _attackEffectDuration;

    #endregion

    #region Attack Pattern Helpers

    /// <summary>
    /// 지정된 인덱스의 공격 패턴을 반환합니다.
    /// </summary>
    /// <param name="patternIndex">패턴 인덱스</param>
    public MobAttackPatternData GetAttackPattern(int patternIndex)
    {
        if (_attackPatterns == null || _attackPatterns.Length == 0)
        {
            return null;
        }

        if (patternIndex < 0 || patternIndex >= _attackPatterns.Length)
        {
            return null;
        }

        return _attackPatterns[patternIndex];
    }

    /// <summary>
    /// 지정된 시퀀스 스텝에서 사용할 공격 패턴 인덱스를 반환합니다.
    /// </summary>
    /// <param name="sequenceStep">시퀀스 스텝</param>
    public int GetAttackPatternIndexForSequenceStep(int sequenceStep)
    {
        if (_attackPatterns == null || _attackPatterns.Length == 0)
        {
            return 0;
        }

        if (_attackSequence == null || _attackSequence.Length == 0)
        {
            return 0;
        }

        int normalizedStep = sequenceStep % _attackSequence.Length;
        if (normalizedStep < 0)
        {
            normalizedStep += _attackSequence.Length;
        }

        return Mathf.Clamp(_attackSequence[normalizedStep], 0, _attackPatterns.Length - 1);
    }

    /// <summary>
    /// 지정된 시퀀스 스텝에서 사용할 공격 패턴 데이터를 반환합니다.
    /// </summary>
    /// <param name="sequenceStep">시퀀스 스텝</param>
    public MobAttackPatternData GetAttackPatternForSequenceStep(int sequenceStep)
    {
        if (_attackPatterns == null || _attackPatterns.Length == 0)
        {
            return null;
        }

        int patternIndex = GetAttackPatternIndexForSequenceStep(sequenceStep);
        return GetAttackPattern(patternIndex);
    }

    #endregion

    #region Validation

    private void OnValidate()
    {
        // 기본값 검증
        if (_maxHP <= 0f) _maxHP = 100f;
        if (_detectionRange <= 0f) _detectionRange = 8f;
        if (_attackRange <= 0f) _attackRange = 2f;
        if (_chaseRange <= _detectionRange) _chaseRange = _detectionRange * 2f;
        if (_detectionRange <= _attackRange) _detectionRange = _attackRange * 2f;
        if (_moveSpeed <= 0f) _moveSpeed = 3.5f;
        if (_attackCooldown <= 0f) _attackCooldown = 1.5f;
        if (_xpReward < 0f) _xpReward = 0f;
        if (_alertDuration <= 0f) _alertDuration = 1.5f;
        if (_respawnTime <= 0f) _respawnTime = 30f;
        if (_attackEffectDuration <= 0f) _attackEffectDuration = 1f;

        if (_attackPatterns != null)
        {
            for (int i = 0; i < _attackPatterns.Length; i++)
            {
                _attackPatterns[i]?.Validate(_attackRange, _attackDamage, _attackCooldown);
            }
        }

        if (_attackSequence != null && _attackPatterns != null && _attackPatterns.Length > 0)
        {
            for (int i = 0; i < _attackSequence.Length; i++)
            {
                _attackSequence[i] = Mathf.Clamp(_attackSequence[i], 0, _attackPatterns.Length - 1);
            }
        }
    }

    #endregion
}
