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

    #region SyncVars

    public readonly SyncVar<bool> IsMoving = new();
    public readonly SyncVar<float> MoveDirectionX = new();
    public readonly SyncVar<float> MoveDirectionY = new();
    public readonly SyncVar<int> PunchCounter = new();
    public readonly SyncVar<int> HitCounter = new();
    public readonly SyncVar<int> ReloadCounter = new();
    public readonly SyncVar<float> ReloadSpeed = new();
    public readonly SyncVar<int> DieCounter = new();
    public readonly SyncVar<int> VictoryCounter = new();

    #endregion

    #region Private Fields

    private int _upperBodyLayerIndex;

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
            Debug.LogError("[PlayerAnimationController] Animator를 찾을 수 없습니다!");
            return;
        }

        _upperBodyLayerIndex = _animator.GetLayerIndex(LAYER_UPPER_BODY);

        // OnChange 이벤트 구독
        PunchCounter.OnChange += OnPunchCounterChanged;
        HitCounter.OnChange += OnHitCounterChanged;
        ReloadCounter.OnChange += OnReloadCounterChanged;
        DieCounter.OnChange += OnDieCounterChanged;
        VictoryCounter.OnChange += OnVictoryCounterChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        
        // OnChange 이벤트 구독 해제
        PunchCounter.OnChange -= OnPunchCounterChanged;
        HitCounter.OnChange -= OnHitCounterChanged;
        ReloadCounter.OnChange -= OnReloadCounterChanged;
        DieCounter.OnChange -= OnDieCounterChanged;
        VictoryCounter.OnChange -= OnVictoryCounterChanged;
    }

    private void Update()
    {
        if (_animator == null) return;

        UpdateAnimatorParameters();
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

    public void PlayPunch()
    {
        if (!IsServerInitialized) return;
        PunchCounter.Value++;
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

    #endregion

    #region OnChange Callbacks

    private void OnPunchCounterChanged(int prev, int next, bool asServer)
    {
        if (asServer) return; // 클라이언트에서만 애니메이션 재생
        if (_animator == null) return;
        _animator.SetTrigger(HASH_PUNCH);
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
