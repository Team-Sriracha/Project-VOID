using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어 애니메이션을 관리합니다.
/// Counter + Trigger 패턴으로 네트워크 동기화합니다.
/// </summary>
public class PlayerAnimationController : NetworkBehaviour
{
    #region Constants

    private static readonly int HASH_IS_MOVING = Animator.StringToHash("isMoving");
    private static readonly int HASH_MOVE_DIRECTION_X = Animator.StringToHash("MoveDirectionX");
    private static readonly int HASH_MOVE_DIRECTION_Y = Animator.StringToHash("MoveDirectionY");
    private static readonly int HASH_PUNCH = Animator.StringToHash("Punch");
    private static readonly int HASH_HIT = Animator.StringToHash("Hit");
    private static readonly int HASH_RELOAD = Animator.StringToHash("Reload");
    private static readonly int HASH_RELOAD_SPEED = Animator.StringToHash("ReloadSpeed");
    private static readonly int HASH_DIE = Animator.StringToHash("Die");
    private static readonly int HASH_VICTORY = Animator.StringToHash("Victory");

    private const string LAYER_UPPER_BODY = "Upper Body Layer";

    #endregion

    #region Serialized Fields

    [Header("애니메이터")]
    [SerializeField] private Animator _animator;

    [Header("애니메이션 설정")]
    [SerializeField] private float _reloadAnimationLength = 1f;

    #endregion

    #region Networked Properties

    [Networked] public NetworkBool IsMoving { get; set; }
    [Networked] public float MoveDirectionX { get; set; }
    [Networked] public float MoveDirectionY { get; set; }

    [Networked, OnChangedRender(nameof(OnPunchCounterChanged))]
    public int PunchCounter { get; set; }

    [Networked, OnChangedRender(nameof(OnHitCounterChanged))]
    public int HitCounter { get; set; }

    [Networked, OnChangedRender(nameof(OnReloadCounterChanged))]
    public int ReloadCounter { get; set; }

    [Networked] public float ReloadSpeed { get; set; }

    [Networked, OnChangedRender(nameof(OnDieCounterChanged))]
    public int DieCounter { get; set; }

    [Networked, OnChangedRender(nameof(OnVictoryCounterChanged))]
    public int VictoryCounter { get; set; }

    #endregion

    #region Private Fields

    private int _upperBodyLayerIndex;

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
            Debug.LogError("[PlayerAnimationController] Animator를 찾을 수 없습니다!");
            return;
        }

        _upperBodyLayerIndex = _animator.GetLayerIndex(LAYER_UPPER_BODY);
    }

    public override void Render()
    {
        if (_animator == null) return;

        UpdateAnimatorParameters();
    }

    #endregion

    #region Movement Animation

    public void SetMovement(bool isMoving, float directionX, float directionY)
    {
        if (!HasStateAuthority) return;

        IsMoving = isMoving;
        MoveDirectionX = directionX;
        MoveDirectionY = directionY;
    }

    #endregion

    #region Combat Animation

    public void PlayPunch()
    {
        if (!HasStateAuthority) return;
        PunchCounter++;
    }

    public void PlayHit()
    {
        if (!HasStateAuthority) return;
        HitCounter++;
    }

    public void PlayDie()
    {
        if (!HasStateAuthority) return;
        DieCounter++;
    }

    public void PlayVictory()
    {
        if (!HasStateAuthority) return;
        VictoryCounter++;
    }

    public void PlayReload(float reloadTime)
    {
        if (!HasStateAuthority) return;

        ReloadSpeed = _reloadAnimationLength / reloadTime;
        ReloadCounter++;
    }

    #endregion

    #region OnChangedRender Callbacks

    private void OnPunchCounterChanged()
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_PUNCH);
    }

    private void OnHitCounterChanged()
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_HIT);
    }

    private void OnReloadCounterChanged()
    {
        if (_animator == null) return;
        _animator.SetFloat(HASH_RELOAD_SPEED, ReloadSpeed);
        _animator.SetTrigger(HASH_RELOAD);
    }

    private void OnDieCounterChanged()
    {
        if (_animator == null) return;
        _animator.SetLayerWeight(_upperBodyLayerIndex, 0f);
        _animator.SetTrigger(HASH_DIE);
    }

    private void OnVictoryCounterChanged()
    {
        if (_animator == null) return;
        _animator.SetLayerWeight(_upperBodyLayerIndex, 0f);
        _animator.SetTrigger(HASH_VICTORY);
    }

    #endregion

    #region Helper Methods

    private void UpdateAnimatorParameters()
    {
        _animator.SetBool(HASH_IS_MOVING, IsMoving);
        _animator.SetFloat(HASH_MOVE_DIRECTION_X, MoveDirectionX);
        _animator.SetFloat(HASH_MOVE_DIRECTION_Y, MoveDirectionY);
    }

    #endregion
}
