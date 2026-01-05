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
    private RenderStateBlock _renderStateBlock;
    private ShaderTagId[] _shaderTagIds;

    #endregion

    #region Constructor

    public FOVStencilClipPass(LayerMask clipLayers)
    {
        profilingSampler = new ProfilingSampler(PASS_NAME);

        _opaqueFilteringSettings = new FilteringSettings(RenderQueueRange.opaque, clipLayers);
        _transparentFilteringSettings = new FilteringSettings(RenderQueueRange.transparent, clipLayers);

        _renderStateBlock = new RenderStateBlock(RenderStateMask.Stencil)
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

        _shaderTagIds = new ShaderTagId[]
        {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("LightweightForward"),
            new ShaderTagId("SRPDefaultUnlit")
        };
    }

    #endregion

    #region RenderGraph Implementation

    private class PassData
    {
        public RendererListHandle OpaqueRendererListHandle;
        public RendererListHandle TransparentRendererListHandle;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        UniversalLightData lightData = frameData.Get<UniversalLightData>();
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(PASS_NAME, out var passData, profilingSampler))
        {
            // Opaque
            DrawingSettings opaqueDrawingSettings = CreateDrawingSettings(renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
            var opaqueRendererListParams = new RendererListParams(renderingData.cullResults, opaqueDrawingSettings, _opaqueFilteringSettings);
            passData.OpaqueRendererListHandle = renderGraph.CreateRendererList(opaqueRendererListParams);
            builder.UseRendererList(passData.OpaqueRendererListHandle);

            // Transparent
            DrawingSettings transparentDrawingSettings = CreateDrawingSettings(renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);
            var transparentRendererListParams = new RendererListParams(renderingData.cullResults, transparentDrawingSettings, _transparentFilteringSettings);
            passData.TransparentRendererListHandle = renderGraph.CreateRendererList(transparentRendererListParams);
            builder.UseRendererList(passData.TransparentRendererListHandle);

            builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawRendererList(data.OpaqueRendererListHandle);
                context.cmd.DrawRendererList(data.TransparentRendererListHandle);
            });
        }
    }

    private DrawingSettings CreateDrawingSettings(UniversalRenderingData renderingData, UniversalCameraData cameraData,
        UniversalLightData lightData, SortingCriteria sortingCriteria)
    {
        var drawingSettings = RenderingUtils.CreateDrawingSettings(_shaderTagIds[0], renderingData, cameraData, lightData, sortingCriteria);
        for (int i = 1; i < _shaderTagIds.Length; i++)
        {
            drawingSettings.SetShaderPassName(i, _shaderTagIds[i]);
        }
        return drawingSettings;
    }

    #endregion

    #region Legacy Execute

    [System.Obsolete("Use RecordRenderGraph instead.")]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        CommandBuffer cmd = CommandBufferPool.Get(PASS_NAME);

        // Opaque
        DrawingSettings opaqueDrawingSettings = CreateDrawingSettings(_shaderTagIds[0], ref renderingData, SortingCriteria.CommonOpaque);
        for (int i = 1; i < _shaderTagIds.Length; i++)
        {
            opaqueDrawingSettings.SetShaderPassName(i, _shaderTagIds[i]);
        }
        context.DrawRenderers(renderingData.cullResults, ref opaqueDrawingSettings, ref _opaqueFilteringSettings, ref _renderStateBlock);

        // Transparent
        DrawingSettings transparentDrawingSettings = CreateDrawingSettings(_shaderTagIds[0], ref renderingData, SortingCriteria.CommonTransparent);
        for (int i = 1; i < _shaderTagIds.Length; i++)
        {
            transparentDrawingSettings.SetShaderPassName(i, _shaderTagIds[i]);
        }
        context.DrawRenderers(renderingData.cullResults, ref transparentDrawingSettings, ref _transparentFilteringSettings, ref _renderStateBlock);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    #endregion
}
