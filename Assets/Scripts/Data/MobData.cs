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

    [Tooltip("공격 애니메이션 지속 시간 (초) - 이 시간 후에 데미지 적용")]
    [SerializeField] private float _attackAnimationDuration = 0.5f;

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
    public float AttackAnimationDuration => _attackAnimationDuration;
    public float MoveSpeed => _moveSpeed;
    public float XPReward => _xpReward;
    public float AlertDuration => _alertDuration;
    public float RespawnTime => _respawnTime;

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
    }

    #endregion
}
