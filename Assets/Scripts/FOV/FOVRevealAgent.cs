using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 게임플레이 오브젝트가 어떤 FOV reveal 정책을 사용할지 선언하는 런타임 에이전트입니다.
/// 현재 단계에서는 stencil 기반 부분 가시성과 anchor 기반 경계 그림자 입력만 담당합니다.
/// </summary>
[DisallowMultipleComponent]
public class FOVRevealAgent : MonoBehaviour
{
    #region Constants

    private static readonly int FOV_REVEAL_ANCHOR_WS_ID = Shader.PropertyToID("_FOVRevealAnchorWS");

    #endregion

    #region Serialized Fields

    [Header("Reveal 정책")]
    [SerializeField] private FOVRevealMode _revealMode = FOVRevealMode.StencilOnly;

    [Header("Reveal 기준")]
    [SerializeField] private Transform _anchorTransform;
    [SerializeField] private Vector3 _anchorOffset = new(0f, 0.5f, 0f);

    #endregion

    #region Private Fields

    private static readonly HashSet<FOVRevealAgent> s_activeAgents = new();

    private bool _isApplied;
    private MaterialPropertyBlock _propertyBlock;
    private Renderer[] _renderers;
    private Collider _targetCollider;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        EnsurePropertyBlock();
        CacheComponents();
    }

    private void OnEnable()
    {
        s_activeAgents.Add(this);
        ApplyRevealPolicy();
        UpdateRevealAnchor();
    }

    private void OnDisable()
    {
        s_activeAgents.Remove(this);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 현재 reveal 정책을 갱신합니다.
    /// </summary>
    public void Configure(FOVRevealMode revealMode)
    {
        _revealMode = revealMode;
        _isApplied = false;
        ApplyRevealPolicy();
    }

    /// <summary>
    /// reveal 정책을 즉시 다시 적용합니다.
    /// </summary>
    public void RefreshRevealState()
    {
        _isApplied = false;
        ApplyRevealPolicy();
    }

    /// <summary>
    /// 현재 활성화된 모든 reveal 에이전트의 anchor world position을 갱신합니다.
    /// </summary>
    public static void UpdateAll()
    {
        foreach (FOVRevealAgent agent in s_activeAgents)
        {
            if (agent == null || !agent.isActiveAndEnabled)
            {
                continue;
            }

            agent.UpdateRevealAnchor();
        }
    }

    /// <summary>
    /// 루트 오브젝트에 reveal 에이전트를 보장합니다.
    /// </summary>
    public static FOVRevealAgent Ensure(GameObject root, FOVRevealMode revealMode = FOVRevealMode.StencilOnly)
    {
        if (root == null)
        {
            return null;
        }

        FOVRevealAgent agent = root.GetComponent<FOVRevealAgent>();
        if (agent == null)
        {
            agent = root.AddComponent<FOVRevealAgent>();
        }

        agent.Configure(revealMode);
        return agent;
    }

    #endregion

    #region Helper Methods

    private void ApplyRevealPolicy()
    {
        if (_isApplied || !isActiveAndEnabled)
        {
            return;
        }

        switch (_revealMode)
        {
            case FOVRevealMode.None:
                break;
            case FOVRevealMode.StencilOnly:
                FOVStencilMaterialRuntimeApplier.ApplyToHierarchy(gameObject);
                break;
        }

        _isApplied = true;
    }

    private void UpdateRevealAnchor()
    {
        EnsurePropertyBlock();
        CacheComponents();

        if (_renderers == null || _propertyBlock == null)
        {
            return;
        }

        Vector3 anchorPosition = GetAnchorPosition();

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer renderer = _renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetVector(FOV_REVEAL_ANCHOR_WS_ID, new Vector4(anchorPosition.x, anchorPosition.y, anchorPosition.z, 1f));
            renderer.SetPropertyBlock(_propertyBlock);
        }
    }

    private void EnsurePropertyBlock()
    {
        if (_propertyBlock == null)
        {
            _propertyBlock = new MaterialPropertyBlock();
        }
    }

    private Vector3 GetAnchorPosition()
    {
        if (_anchorTransform != null)
        {
            return _anchorTransform.position + _anchorOffset;
        }

        CacheComponents();

        if (_renderers != null && _renderers.Length > 0 && _renderers[0] != null)
        {
            Bounds bounds = _renderers[0].bounds;
            for (int i = 1; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null)
                {
                    continue;
                }

                bounds.Encapsulate(_renderers[i].bounds);
            }

            return bounds.center + _anchorOffset;
        }

        if (_targetCollider != null)
        {
            return _targetCollider.bounds.center + _anchorOffset;
        }

        return transform.position + _anchorOffset;
    }

    private void CacheComponents()
    {
        if (_renderers == null || _renderers.Length == 0)
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        if (_targetCollider == null)
        {
            _targetCollider = GetComponentInChildren<Collider>();
        }
    }

    #endregion
}
