using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// 플레이어 애니메이션을 관리합니다.
/// Counter + OnChange 패턴으로 네트워크 동기화합니다.
/// </summary>
public class PlayerAnimationController : NetworkBehaviour
{
    #region Constants

    private static readonly int HASH_IS_MOVING = Animator.StringToHash("isMoving");
    private static readonly int HASH_MOVE_DIRECTION_X = Animator.StringToHash("MoveDirectionX");
    private static readonly int HASH_MOVE_DIRECTION_Y = Animator.StringToHash("MoveDirectionY");
    private static readonly int HASH_MELEE_ATTACK = Animator.StringToHash("MeleeAttack");
    private static readonly int HASH_HIT = Animator.StringToHash("Hit");
    private static readonly int HASH_RELOAD = Animator.StringToHash("Reload");
    private static readonly int HASH_RELOAD_SPEED = Animator.StringToHash("ReloadSpeed");
    private static readonly int HASH_DIE = Animator.StringToHash("Die");
    private static readonly int HASH_VICTORY = Animator.StringToHash("Victory");
    private static readonly int HASH_SKILL = Animator.StringToHash("Skill");

    private const string LAYER_UPPER_BODY = "Upper Body Layer";

    #endregion

    #region Serialized Fields

    [Header("애니메이터")]
    [SerializeField] private Animator _animator;

    [Header("애니메이션 설정")]
    [SerializeField] private float _reloadAnimationLength = 1f;

    #endregion

    #region SyncVars

    public readonly SyncVar<bool> IsMoving = new();
    public readonly SyncVar<float> MoveDirectionX = new();
    public readonly SyncVar<float> MoveDirectionY = new();
    public readonly SyncVar<int> MeleeAttackCounter = new();
    public readonly SyncVar<int> HitCounter = new();
    public readonly SyncVar<int> ReloadCounter = new();
    public readonly SyncVar<float> ReloadSpeed = new();
    public readonly SyncVar<int> DieCounter = new();
    public readonly SyncVar<int> VictoryCounter = new();
    public readonly SyncVar<int> SkillCounter = new();

    #endregion

    #region Private Fields

    private int _upperBodyLayerIndex;
    private RuntimeAnimatorController _defaultController;
    private NetworkedWeapon _networkedWeapon;

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        
        _networkedWeapon = GetComponent<NetworkedWeapon>();
        
        if (_animator == null)
        {
            _animator = GetComponentInChildren<Animator>();
        }

        if (_animator == null)
        {
            Debug.LogError("[PlayerAnimationController] Animator를 찾을 수 없습니다!");
            return;
        }

        // 기본 컨트롤러 백업
        _defaultController = _animator.runtimeAnimatorController;
        _upperBodyLayerIndex = _animator.GetLayerIndex(LAYER_UPPER_BODY);

        // OnChange 이벤트 구독
        MeleeAttackCounter.OnChange += OnMeleeAttackCounterChanged;
        HitCounter.OnChange += OnHitCounterChanged;
        ReloadCounter.OnChange += OnReloadCounterChanged;
        DieCounter.OnChange += OnDieCounterChanged;
        VictoryCounter.OnChange += OnVictoryCounterChanged;
        SkillCounter.OnChange += OnSkillCounterChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        
        // OnChange 이벤트 구독 해제
        MeleeAttackCounter.OnChange -= OnMeleeAttackCounterChanged;
        HitCounter.OnChange -= OnHitCounterChanged;
        ReloadCounter.OnChange -= OnReloadCounterChanged;
        DieCounter.OnChange -= OnDieCounterChanged;
        VictoryCounter.OnChange -= OnVictoryCounterChanged;
        SkillCounter.OnChange -= OnSkillCounterChanged;
    }

    private void Update()
    {
        if (_animator == null) return;

        UpdateAnimatorParameters();
    }

    #endregion

    #region Weapon Animation Override

    /// <summary>
    /// 무기별 전용 애니메이션 컨트롤러를 적용합니다.
    /// </summary>
    /// <param name="overrideController">적용할 오버라이드 컨트롤러 (null이면 기본값으로 복구)</param>
    public void SetWeaponAnimator(AnimatorOverrideController overrideController)
    {
        if (_animator == null) return;

        if (overrideController != null)
        {
            _animator.runtimeAnimatorController = overrideController;
            // Debug.Log($"[Anim] Override Controller 적용: {overrideController.name}");
        }
        else
        {
            // 기본값으로 복구
            if (_defaultController != null)
            {
                _animator.runtimeAnimatorController = _defaultController;
                // Debug.Log("[Anim] 기본 Controller로 복구");
            }
        }
    }

    #endregion

    #region Movement Animation

    public void SetMovement(bool isMoving, float directionX, float directionY)
    {
        if (!IsServerInitialized) return;

        IsMoving.Value = isMoving;
        MoveDirectionX.Value = directionX;
        MoveDirectionY.Value = directionY;
    }

    #endregion

    #region Combat Animation

    public void PlayMeleeAttack()
    {
        if (!IsServerInitialized) return;
        MeleeAttackCounter.Value++;
    }

    public void PlayHit()
    {
        if (!IsServerInitialized) return;
        HitCounter.Value++;
    }

    public void PlayDie()
    {
        if (!IsServerInitialized) return;
        DieCounter.Value++;
    }

    public void PlayVictory()
    {
        if (!IsServerInitialized) return;
        VictoryCounter.Value++;
    }

    public void PlayReload(float reloadTime)
    {
        if (!IsServerInitialized) return;

        ReloadSpeed.Value = _reloadAnimationLength / reloadTime;
        ReloadCounter.Value++;
    }

    /// <summary>
    /// 스킬 애니메이션 재생 (서버)
    /// </summary>
    public void PlaySkill()
    {
        if (!IsServerInitialized) return;
        SkillCounter.Value++;
    }

    #endregion

    #region OnChange Callbacks

    private void OnMeleeAttackCounterChanged(int prev, int next, bool asServer)
    {
        if (asServer) return; // 클라이언트에서만 애니메이션 재생
        if (_animator == null) return;
        _animator.SetTrigger(HASH_MELEE_ATTACK);
    }

    private void OnHitCounterChanged(int prev, int next, bool asServer)
    {
        if (asServer) return;
        if (_animator == null) return;
        _animator.SetTrigger(HASH_HIT);
    }

    private void OnReloadCounterChanged(int prev, int next, bool asServer)
    {
        if (asServer) return;
        if (_animator == null) return;
        _animator.SetFloat(HASH_RELOAD_SPEED, ReloadSpeed.Value);
        _animator.SetTrigger(HASH_RELOAD);
    }

    private void OnDieCounterChanged(int prev, int next, bool asServer)
    {
        if (asServer) return;
        if (_animator == null) return;
        _animator.SetLayerWeight(_upperBodyLayerIndex, 0f);
        _animator.SetTrigger(HASH_DIE);
    }

    private void OnVictoryCounterChanged(int prev, int next, bool asServer)
    {
        if (asServer) return;
        if (_animator == null) return;
        _animator.SetLayerWeight(_upperBodyLayerIndex, 0f);
        _animator.SetTrigger(HASH_VICTORY);
    }

    private void OnSkillCounterChanged(int prev, int next, bool asServer)
    {
        if (asServer) return;
        if (_animator == null) return;
        // 피격 애니메이션 취소 후 스킬 재생
        _animator.ResetTrigger(HASH_HIT);
        _animator.SetTrigger(HASH_SKILL);
    }

    #endregion

    #region Animation Events

    /// <summary>
    /// 애니메이션 이벤트에서 호출 (근접 공격 타격 시점)
    /// </summary>
    public void OnMeleeHit()
    {
        // 로컬 플레이어(Owner)만 처리하여 서버로 요청
        if (!IsOwner) return;

        if (_networkedWeapon != null)
        {
            _networkedWeapon.OnAnimationEvent_MeleeHit();
        }
    }

    /// <summary>
    /// 애니메이션 이벤트에서 호출 (스킬 전진 시작 시점)
    /// </summary>
    public void OnMeleeSkillMoveStart()
    {
        if (!IsOwner) return;

        if (_networkedWeapon != null)
        {
            _networkedWeapon.OnAnimationEvent_SkillMoveStart();
        }
    }

    /// <summary>
    /// 애니메이션 이벤트에서 호출 (스킬 히트 시점)
    /// </summary>
    public void OnMeleeSkillHit()
    {
        if (!IsOwner) return;

        if (_networkedWeapon != null)
        {
            _networkedWeapon.OnAnimationEvent_SkillHit();
        }
    }

    /// <summary>
    /// 애니메이션 이벤트에서 호출 (스킬 종료 시점)
    /// </summary>
    public void OnMeleeSkillEnd()
    {
        if (!IsOwner) return;

        if (_networkedWeapon != null)
        {
            _networkedWeapon.OnAnimationEvent_SkillEnd();
        }
    }

    #endregion

    #region Helper Methods

    private void UpdateAnimatorParameters()
    {
        _animator.SetBool(HASH_IS_MOVING, IsMoving.Value);
        _animator.SetFloat(HASH_MOVE_DIRECTION_X, MoveDirectionX.Value);
        _animator.SetFloat(HASH_MOVE_DIRECTION_Y, MoveDirectionY.Value);
    }

    #endregion
}
