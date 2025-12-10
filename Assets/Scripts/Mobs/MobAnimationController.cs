using Fusion;
using UnityEngine;

/// <summary>
/// 몹 애니메이션을 관리합니다.
/// Counter + Trigger 패턴으로 네트워크 동기화합니다.
/// PlayerAnimationController와 동일한 패턴을 사용합니다.
/// </summary>
public class MobAnimationController : NetworkBehaviour
{
    #region Constants

    private static readonly int HASH_IS_MOVING = Animator.StringToHash("IsMoving");
    private static readonly int HASH_MOVE_SPEED = Animator.StringToHash("MoveSpeed");
    private static readonly int HASH_ATTACK = Animator.StringToHash("Attack");
    private static readonly int HASH_HIT = Animator.StringToHash("Hit");
    private static readonly int HASH_DIE = Animator.StringToHash("Die");
    private static readonly int HASH_ALERT = Animator.StringToHash("Alert");

    #endregion

    #region Serialized Fields

    [Header("애니메이터")]
    [Tooltip("몹의 Animator 컴포넌트")]
    [SerializeField] private Animator _animator;

    #endregion

    #region Networked Properties

    [Networked]
    public NetworkBool IsMoving { get; set; }

    [Networked]
    public float MoveSpeed { get; set; }

    [Networked, OnChangedRender(nameof(OnAttackCounterChanged))]
    public int AttackCounter { get; set; }

    [Networked, OnChangedRender(nameof(OnHitCounterChanged))]
    public int HitCounter { get; set; }

    [Networked, OnChangedRender(nameof(OnDieCounterChanged))]
    public int DieCounter { get; set; }

    [Networked, OnChangedRender(nameof(OnAlertCounterChanged))]
    public int AlertCounter { get; set; }

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        if (_animator == null)
        {
            _animator = GetComponentInChildren<Animator>();
        }

        if (_animator == null)
        {
            Debug.LogError($"[MobAnimationController] {gameObject.name}: Animator를 찾을 수 없습니다!");
        }
    }

    public override void Render()
    {
        if (_animator == null) return;

        UpdateAnimatorParameters();
    }

    #endregion

    #region Movement Animation

    /// <summary>
    /// 이동 상태를 설정합니다.
    /// </summary>
    /// <param name="isMoving">이동 중인지 여부</param>
    /// <param name="speed">이동 속도 (0~1 정규화)</param>
    public void SetMovement(bool isMoving, float speed = 1f)
    {
        if (!HasStateAuthority) return;

        IsMoving = isMoving;
        MoveSpeed = speed;
    }

    #endregion

    #region Combat Animation

    /// <summary>
    /// 공격 애니메이션을 재생합니다.
    /// </summary>
    public void PlayAttack()
    {
        if (!HasStateAuthority) return;
        AttackCounter++;
    }

    /// <summary>
    /// 피격 애니메이션을 재생합니다.
    /// </summary>
    public void PlayHit()
    {
        if (!HasStateAuthority) return;
        HitCounter++;
    }

    /// <summary>
    /// 사망 애니메이션을 재생합니다.
    /// </summary>
    public void PlayDie()
    {
        if (!HasStateAuthority) return;
        DieCounter++;
    }

    /// <summary>
    /// 경계 애니메이션을 재생합니다.
    /// </summary>
    public void PlayAlert()
    {
        if (!HasStateAuthority) return;
        AlertCounter++;
    }

    #endregion

    #region OnChangedRender Callbacks

    private void OnAttackCounterChanged()
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_ATTACK);
    }

    private void OnHitCounterChanged()
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_HIT);
    }

    private void OnDieCounterChanged()
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_DIE);
    }

    private void OnAlertCounterChanged()
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_ALERT);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Animator 파라미터를 업데이트합니다.
    /// </summary>
    private void UpdateAnimatorParameters()
    {
        _animator.SetBool(HASH_IS_MOVING, IsMoving);
        _animator.SetFloat(HASH_MOVE_SPEED, MoveSpeed);
    }

    /// <summary>
    /// Animator를 초기 상태로 리셋합니다.
    /// 리스폰 시 호출됩니다.
    /// </summary>
    public void ResetAnimator()
    {
        if (HasStateAuthority)
        {
            IsMoving = false;
            MoveSpeed = 0f;
            RPC_ResetAnimator();
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ResetAnimator()
    {
        // Why: Animator를 리셋하여 기본 상태로 돌아감
        if (_animator != null)
        {
            _animator.Rebind();
            _animator.Update(0f);
        }
    }

    #endregion
}
