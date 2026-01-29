using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>
/// 몹 애니메이션 관리
/// Counter + Trigger 패턴으로 네트워크 동기화
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

    #region SyncVars

    public readonly SyncVar<bool> IsMoving = new();
    public readonly SyncVar<float> MoveSpeed = new();
    public readonly SyncVar<int> AttackCounter = new();
    public readonly SyncVar<int> HitCounter = new();
    public readonly SyncVar<int> DieCounter = new();
    public readonly SyncVar<int> AlertCounter = new();

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        
        if (_animator == null)
        {
            _animator = GetComponentInChildren<Animator>();
        }

        if (_animator == null)
        {
            Debug.LogError($"[MobAnimationController] {gameObject.name}: Animator를 찾을 수 없습니다!");
        }

        // OnChange 이벤트 구독
        AttackCounter.OnChange += OnAttackCounterChanged;
        HitCounter.OnChange += OnHitCounterChanged;
        DieCounter.OnChange += OnDieCounterChanged;
        AlertCounter.OnChange += OnAlertCounterChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        
        // OnChange 이벤트 구독 해제
        AttackCounter.OnChange -= OnAttackCounterChanged;
        HitCounter.OnChange -= OnHitCounterChanged;
        DieCounter.OnChange -= OnDieCounterChanged;
        AlertCounter.OnChange -= OnAlertCounterChanged;
    }

    private void Update()
    {
        if (_animator == null) return;

        UpdateAnimatorParameters();
    }

    #endregion

    #region Movement Animation

    public void SetMovement(bool isMoving, float speed = 1f)
    {
        if (!IsServerInitialized) return;

        IsMoving.Value = isMoving;
        MoveSpeed.Value = speed;
    }

    #endregion

    #region Combat Animation

    public void PlayAttack()
    {
        if (!IsServerInitialized) return;
        AttackCounter.Value++;
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

    public void PlayAlert()
    {
        if (!IsServerInitialized) return;
        AlertCounter.Value++;
    }

    // Animation Event에서 호출
    public void OnMobAttackHit()
    {
        if (!IsServerInitialized) return;
        
        var ai = GetComponent<MobAI>();
        if (ai != null)
        {
            ai.OnAnimationEvent_AttackHit();
        }
    }

    #endregion

    #region OnChange Callbacks

    private void OnAttackCounterChanged(int prev, int next, bool asServer)
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_ATTACK);
    }

    private void OnHitCounterChanged(int prev, int next, bool asServer)
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_HIT);
    }

    private void OnDieCounterChanged(int prev, int next, bool asServer)
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_DIE);
    }

    private void OnAlertCounterChanged(int prev, int next, bool asServer)
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_ALERT);
    }

    #endregion

    #region Helper Methods

    private void UpdateAnimatorParameters()
    {
        _animator.SetBool(HASH_IS_MOVING, IsMoving.Value);
        _animator.SetFloat(HASH_MOVE_SPEED, MoveSpeed.Value);
    }

    public void ResetAnimator()
    {
        if (IsServerInitialized)
        {
            IsMoving.Value = false;
            MoveSpeed.Value = 0f;
            RPC_ResetAnimator();
        }
    }

    [ObserversRpc]
    private void RPC_ResetAnimator()
    {
        if (_animator != null)
        {
            _animator.Rebind();
            _animator.Update(0f);
        }
    }

    #endregion
}
