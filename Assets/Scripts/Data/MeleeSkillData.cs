using UnityEngine;

/// <summary>
/// 근접무기 스킬 데이터를 정의합니다.
/// 각 근접무기에 고유 스킬을 할당할 수 있습니다.
/// </summary>
[CreateAssetMenu(fileName = "New Melee Skill", menuName = "Project VOID/Skills/Melee Skill Data")]
public class MeleeSkillData : ScriptableObject
{
    #region Serialized Fields

    [Header("기본 정보")]
    [SerializeField] private string _skillName;
    [Tooltip("스킬 아이콘 (UI 표시용)")]
    [SerializeField] private Sprite _skillIcon;
    [Tooltip("스킬 쿨다운 (초)")]
    [SerializeField] private float _cooldown = 10f;

    [Header("이동")]
    [Tooltip("스킬 사용 시 앞으로 이동하는 거리")]
    [SerializeField] private float _forwardDistance = 2f;
    [Tooltip("이동에 걸리는 시간")]
    [SerializeField] private float _moveDuration = 0.5f;

    [Header("히트박스 (스킬 전용)")]
    [Tooltip("스킬 공격 반경 (기본 무기 Range와 별도)")]
    [SerializeField] private float _attackRadius = 2.5f;
    [Tooltip("스킬 공격 각도 (360 = 전방위)")]
    [Range(30f, 360f)]
    [SerializeField] private float _attackAngle = 360f;
    [SerializeField] private LayerMask _hitLayers;

    [Header("데미지")]
    [Tooltip("1회 히트당 데미지 배율 (기본 무기 데미지 기준)")]
    [SerializeField] private float _damageMultiplier = 0.4f;

    [Header("시각 효과")]
    [Tooltip("스킬 시작 시 재생할 VFX")]
    [SerializeField] private GameObject _skillVFXPrefab;
    [Tooltip("VFX 지속 시간")]
    [SerializeField] private float _vfxDuration = 1f;
    [Tooltip("스킬 히트 시 재생할 이펙트 (기본 무기 HitEffect와 별도)")]
    [SerializeField] private GameObject _skillHitEffectPrefab;

    [Header("사운드")]
    [SerializeField] private AudioCue _castAudioCue;
    [SerializeField] private AudioCue _hitAudioCue;
    [SerializeField] private AudioCue _readyAudioCue;

    #endregion

    #region Properties

    public string SkillName => _skillName;
    public Sprite SkillIcon => _skillIcon;
    public float Cooldown => _cooldown;
    public float ForwardDistance => _forwardDistance;
    public float MoveDuration => _moveDuration;
    public float AttackRadius => _attackRadius;
    public float AttackAngle => _attackAngle;
    public LayerMask HitLayers => _hitLayers;
    public float DamageMultiplier => _damageMultiplier;
    public GameObject SkillVFXPrefab => _skillVFXPrefab;
    public float VFXDuration => _vfxDuration;
    public GameObject SkillHitEffectPrefab => _skillHitEffectPrefab;
    public AudioCue CastAudioCue => _castAudioCue;
    public AudioCue HitAudioCue => _hitAudioCue;
    public AudioCue ReadyAudioCue => _readyAudioCue;

    #endregion
}
