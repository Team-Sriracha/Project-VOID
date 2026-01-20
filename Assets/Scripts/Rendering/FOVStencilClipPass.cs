using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// FOV 영역 내에서만 특정 레이어의 오브젝트를 렌더링하는 Render Pass입니다.
/// </summary>
public class FOVStencilClipPass : ScriptableRenderPass
{
    #region Constants

    private const string PASS_NAME = "FOV Stencil Clip";
    private const int STENCIL_REF = 1;

    #endregion

    #region Private Fields

    private FilteringSettings _opaqueFilteringSettings;
    private FilteringSettings _transparentFilteringSettings;
    private RenderStateBlock _stencilRenderStateBlock;   // With Stencil Test (Game View)
    private RenderStateBlock _noStencilRenderStateBlock; // Without Stencil Test (Scene View)
    private ShaderTagId[] _shaderTagIds;

    #endregion

    #region Public Properties

    /// <summary>
    /// Scene View에서 Stencil 테스트를 건너뛰기 위한 플래그
    /// </summary>
    public bool BypassStencil { get; set; }

    #endregion

    #region Constructor

    public FOVStencilClipPass(LayerMask clipLayers)
    {
        profilingSampler = new ProfilingSampler(PASS_NAME);

        _opaqueFilteringSettings = new FilteringSettings(RenderQueueRange.opaque, clipLayers);
        _transparentFilteringSettings = new FilteringSettings(RenderQueueRange.transparent, clipLayers);

        // Game View용: Stencil == 1 테스트
        _stencilRenderStateBlock = new RenderStateBlock(RenderStateMask.Stencil)
        {
            stencilState = new StencilState(
                enabled: true,
                readMask: 255,
                writeMask: 255,
                compareFunction: CompareFunction.Equal,
                passOperation: StencilOp.Keep,
                failOperation: StencilOp.Keep,
                zFailOperation: StencilOp.Keep),
            stencilReference = STENCIL_REF
        };

        // Scene View용: Stencil 테스트 없음 (무조건 그림)
        _noStencilRenderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);

        _shaderTagIds = new ShaderTagId[]
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("LightweightForward"),
            new ShaderTagId("SRPDefaultUnlit")
        };
    }

    #endregion

    #region Camera Setup

    [System.Obsolete("Use RecordRenderGraph instead.")]
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        // Configure to render to camera's color and depth targets (includes stencil buffer for stencil test)
        ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle, 
                        renderingData.cameraData.renderer.cameraDepthTargetHandle);
    }

    #endregion

    // [REMOVED] RecordRenderGraph removed to use Legacy Execute (API compatibility)
    // The Execute method below handles the Scene View visibility logic correctly.

    #region Legacy Execute

    [System.Obsolete("Use RecordRenderGraph instead.")]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get(PASS_NAME);

        // BypassStencil이 true면 Stencil 테스트 없이 렌더링 (Scene View 용)
        RenderStateBlock currentRenderStateBlock = BypassStencil ? _noStencilRenderStateBlock : _stencilRenderStateBlock;

        // Opaque
        DrawingSettings opaqueDrawingSettings = CreateDrawingSettings(_shaderTagIds[0], ref renderingData, SortingCriteria.CommonOpaque);
        for (int i = 1; i < _shaderTagIds.Length; i++)
        {
            opaqueDrawingSettings.SetShaderPassName(i, _shaderTagIds[i]);
        }
        
        context.DrawRenderers(renderingData.cullResults, ref opaqueDrawingSettings, ref _opaqueFilteringSettings, ref currentRenderStateBlock);

        // Transparent
        DrawingSettings transparentDrawingSettings = CreateDrawingSettings(_shaderTagIds[0], ref renderingData, SortingCriteria.CommonTransparent);
        for (int i = 1; i < _shaderTagIds.Length; i++)
        {
            transparentDrawingSettings.SetShaderPassName(i, _shaderTagIds[i]);
        }
        context.DrawRenderers(renderingData.cullResults, ref transparentDrawingSettings, ref _transparentFilteringSettings, ref currentRenderStateBlock);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    #endregion
}
