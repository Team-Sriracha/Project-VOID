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

    private static readonly int HASH_IS_MOVING = Animator.StringToHash("isMoving");
    private static readonly int HASH_ATTACK = Animator.StringToHash("Attack");
    private static readonly int HASH_ATTACK_TYPE = Animator.StringToHash("AttackType");
    private static readonly int HASH_HIT = Animator.StringToHash("Hit");
    private static readonly int HASH_DIE = Animator.StringToHash("Die");
    private static readonly int HASH_ALERT = Animator.StringToHash("Alert");
    private const string HIT_LAYER_NAME = "HitLayer";

    #endregion

    #region Serialized Fields

    [Header("애니메이터")]
    [Tooltip("몹의 Animator 컴포넌트")]
    [SerializeField] private Animator _animator;

    #endregion

    #region SyncVars

    public readonly SyncVar<bool> IsMoving = new();
    public readonly SyncVar<int> AttackPatternIndex = new();
    public readonly SyncVar<int> AttackCounter = new();
    public readonly SyncVar<int> HitCounter = new();
    public readonly SyncVar<int> DieCounter = new();
    public readonly SyncVar<int> AlertCounter = new();

    #endregion

    #region Fishnet Lifecycle

    private MobCombat _mobCombat;
    private MobAI _mobAI;
    private bool _hasMoveBoolParameter;
    private bool _hasAttackTypeParameter;
    private int _hitLayerIndex = -1;

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        _mobCombat = GetComponent<MobCombat>();
        _mobAI = GetComponent<MobAI>();
        
        if (_animator == null)
        {
            _animator = GetComponentInChildren<Animator>();
        }

        if (_animator == null)
        {
            Debug.LogError($"[MobAnimationController] {gameObject.name}: Animator를 찾을 수 없습니다!");
        }
        else
        {
            CacheAnimatorParameters();
            UpdateHitLayerWeight();
        }

        // OnChange 이벤트 구독
        AttackPatternIndex.OnChange += OnAttackPatternIndexChanged;
        AttackCounter.OnChange += OnAttackCounterChanged;
        HitCounter.OnChange += OnHitCounterChanged;
        DieCounter.OnChange += OnDieCounterChanged;
        AlertCounter.OnChange += OnAlertCounterChanged;
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        
        // OnChange 이벤트 구독 해제
        AttackPatternIndex.OnChange -= OnAttackPatternIndexChanged;
        AttackCounter.OnChange -= OnAttackCounterChanged;
        HitCounter.OnChange -= OnHitCounterChanged;
        DieCounter.OnChange -= OnDieCounterChanged;
        AlertCounter.OnChange -= OnAlertCounterChanged;
    }

    private void Update()
    {
        if (_animator == null) return;

        UpdateAnimatorParameters();
        UpdateHitLayerWeight();
    }

    #endregion

    #region Movement Animation

    public void SetMovement(bool isMoving)
    {
        if (!IsServerInitialized) return;

        IsMoving.Value = isMoving;
    }

    #endregion

    #region Combat Animation

    public void PlayAttack(int attackPatternIndex = 0)
    {
        if (!IsServerInitialized) return;
        AttackPatternIndex.Value = attackPatternIndex;
        AttackCounter.Value++;
    }

    public void PlayHit()
    {
        if (!IsServerInitialized) return;
        if (ShouldSuppressHitDuringAttack()) return;
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
        
        if (_mobAI != null)
        {
            _mobAI.OnAnimationEvent_AttackHit();
        }
    }

    #endregion

    #region OnChange Callbacks

    private void OnAttackPatternIndexChanged(int prev, int next, bool asServer)
    {
        if (_animator == null || !_hasAttackTypeParameter) return;
        _animator.SetInteger(HASH_ATTACK_TYPE, next);
    }

    private void OnAttackCounterChanged(int prev, int next, bool asServer)
    {
        if (_animator == null) return;

        if (_hasAttackTypeParameter)
        {
            _animator.SetInteger(HASH_ATTACK_TYPE, AttackPatternIndex.Value);
        }

        bool isHostClientCallback = !asServer && IsServerInitialized;
        if (!isHostClientCallback)
        {
            _animator.SetTrigger(HASH_ATTACK);
        }

        if (!asServer)
        {
            SpawnAttackEffect();
            AudioManager.Instance?.PlayAttachedOneShot(_mobCombat?.GetMobData()?.AttackAudioCue, transform, Vector3.up);
        }
    }

    private void OnHitCounterChanged(int prev, int next, bool asServer)
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_HIT);
    }

    private void OnDieCounterChanged(int prev, int next, bool asServer)
    {
        if (_animator == null) return;
        // Hit 트리거 리셋 후 Die 재생 (Hit 중에도 Die가 우선)
        _animator.ResetTrigger(HASH_HIT);
        _animator.ResetTrigger(HASH_ATTACK);
        _animator.SetTrigger(HASH_DIE);
    }

    private void OnAlertCounterChanged(int prev, int next, bool asServer)
    {
        if (_animator == null) return;
        _animator.SetTrigger(HASH_ALERT);

        if (!asServer)
        {
            AudioManager.Instance?.PlayAttachedOneShot(_mobCombat?.GetMobData()?.AlertAudioCue, transform, Vector3.up);
        }
    }

    #endregion

    #region Helper Methods

    private bool ShouldSuppressHitDuringAttack()
    {
        // 공격 판정이 애니메이션 이벤트에 묶여 있으므로,
        // Attack 상태에서는 Hit가 공격 애니메이션을 끊지 않도록 차단합니다.
        return _mobAI != null && _mobAI.CurrentState == MonsterState.Attack;
    }

    private void UpdateAnimatorParameters()
    {
        if (_hasMoveBoolParameter)
        {
            _animator.SetBool(HASH_IS_MOVING, IsMoving.Value);
        }

        if (_hasAttackTypeParameter)
        {
            _animator.SetInteger(HASH_ATTACK_TYPE, AttackPatternIndex.Value);
        }
    }

    private void SpawnAttackEffect()
    {
        MobData mobData = _mobCombat?.GetMobData();
        if (mobData == null || mobData.AttackEffectPrefab == null)
        {
            return;
        }

        Transform effectAnchor = GetAttackEffectAnchor();
        GameObject effectInstance = Instantiate(
            mobData.AttackEffectPrefab,
            effectAnchor.position,
            effectAnchor.rotation,
            effectAnchor);

        Destroy(effectInstance, mobData.AttackEffectDuration);
    }

    private Transform GetAttackEffectAnchor()
    {
        return _mobCombat != null ? _mobCombat.AttackEffectPoint : transform;
    }

    public void ResetAnimator()
    {
        if (IsServerInitialized)
        {
            IsMoving.Value = false;
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

    private void CacheAnimatorParameters()
    {
        _hasMoveBoolParameter = false;
        _hasAttackTypeParameter = false;
        _hitLayerIndex = -1;

        if (_animator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = _animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            int parameterHash = parameters[i].nameHash;
            if (parameterHash == HASH_IS_MOVING)
            {
                _hasMoveBoolParameter = true;
            }
            else if (parameterHash == HASH_ATTACK_TYPE)
            {
                _hasAttackTypeParameter = true;
            }
        }

        for (int i = 0; i < _animator.layerCount; i++)
        {
            if (_animator.GetLayerName(i) == HIT_LAYER_NAME)
            {
                _hitLayerIndex = i;
                break;
            }
        }
    }

    private void UpdateHitLayerWeight()
    {
        if (_animator == null || _hitLayerIndex < 0)
        {
            return;
        }

        float layerWeight = ShouldEnableHitLayer() ? 1f : 0f;
        _animator.SetLayerWeight(_hitLayerIndex, layerWeight);
    }

    private bool ShouldEnableHitLayer()
    {
        if (_hitLayerIndex < 0)
        {
            return false;
        }

        if (_animator.IsInTransition(_hitLayerIndex))
        {
            if (_animator.GetCurrentAnimatorClipInfoCount(_hitLayerIndex) > 0)
            {
                return true;
            }

            if (_animator.GetNextAnimatorClipInfoCount(_hitLayerIndex) > 0)
            {
                return true;
            }
        }

        return _animator.GetCurrentAnimatorClipInfoCount(_hitLayerIndex) > 0;
    }

    #endregion
}
