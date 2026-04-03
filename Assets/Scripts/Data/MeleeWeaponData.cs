using UnityEngine;

/// <summary>
/// 근거리 무기 데이터를 정의합니다.
/// </summary>
[CreateAssetMenu(fileName = "New Melee Weapon", menuName = "Project VOID/Weapons/Melee Weapon Data")]
public class MeleeWeaponData : WeaponData
{
    #region Serialized Fields

    [Header("근접 공격 설정")]
    [Range(30f, 180f)]
    [SerializeField] private float _attackAngle = 90f;

    [SerializeField] private LayerMask _hitLayers;

    [Header("FOV 설정")]
    [Tooltip("근접 무기 장착 시 원형 시야 범위 (미터)")]
    [SerializeField] private float _circularFOVRange = 8f;

    [Header("시각 효과")]
    [SerializeField] private GameObject _swingEffectPrefab;
    [SerializeField] private GameObject _hitEffectPrefab;

    [Header("사운드")]
    [SerializeField] private AudioCue _swingAudioCue;
    [SerializeField] private AudioCue _hitAudioCue;

    [Header("애니메이션 이벤트")]
    [Tooltip("체크 시, 공격 버튼을 눌렀을 때 즉시 데미지를 주지 않고 애니메이션의 'OnMeleeHit' 이벤트 시점에 데미지를 줍니다.")]
    [SerializeField] private bool _useAnimationEvent;

    #endregion

    #region Properties

    public float AttackAngle => _attackAngle;
    public LayerMask HitLayers => _hitLayers;
    public GameObject SwingEffectPrefab => _swingEffectPrefab;
    public GameObject HitEffectPrefab => _hitEffectPrefab;
    public AudioCue SwingAudioCue => _swingAudioCue;
    public AudioCue HitAudioCue => _hitAudioCue;
    public float CircularFOVRange => _circularFOVRange;
    
    /// <summary>
    /// 애니메이션 이벤트를 사용하여 데미지 타이밍을 제어할지 여부
    /// </summary>
    public bool UseAnimationEvent => _useAnimationEvent;

    #endregion

    #region Skill

    [Header("스킬")]
    [Tooltip("이 무기의 고유 스킬 (없으면 null)")]
    [SerializeField] private MeleeSkillData _skillData;

    public MeleeSkillData SkillData => _skillData;
    public bool HasSkill => _skillData != null;

    #endregion
}
