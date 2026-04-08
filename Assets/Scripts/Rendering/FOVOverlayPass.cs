using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// FOV 바깥 영역을 어둡게 덮는 오버레이 전용 Render Pass입니다.
/// </summary>
public class FOVOverlayPass : ScriptableRenderPass
{
    #region Constants

    private const string PASS_NAME = "FOV Overlay";

    #endregion

    #region Private Fields

    private readonly Material _overlayMaterial;

    #endregion

    #region Constructor

    public FOVOverlayPass(Material overlayMaterial)
    {
        _overlayMaterial = overlayMaterial;
        profilingSampler = new ProfilingSampler(PASS_NAME);
    }

    #endregion

    #region Camera Setup

    [System.Obsolete("Use RecordRenderGraph instead.")]
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        ConfigureTarget(
            renderingData.cameraData.renderer.cameraColorTargetHandle,
            renderingData.cameraData.renderer.cameraDepthTargetHandle);
    }

    #endregion

    #region RenderGraph Implementation

    private class PassData
    {
        public Material OverlayMaterial;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_overlayMaterial == null || !FOVRenderData.HasFOVMeshData)
        {
            return;
        }

        using (var builder = renderGraph.AddRasterRenderPass<PassData>(PASS_NAME, out var passData, profilingSampler))
        {
            passData.OverlayMaterial = _overlayMaterial;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawProcedural(Matrix4x4.identity, data.OverlayMaterial, 0, MeshTopology.Triangles, 3);
            });
        }
    }

    #endregion

    #region Legacy Execute

    [System.Obsolete("Use RecordRenderGraph instead.")]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (_overlayMaterial == null || !FOVRenderData.HasFOVMeshData)
        {
            return;
        }

        CommandBuffer cmd = CommandBufferPool.Get(PASS_NAME);
        cmd.DrawProcedural(Matrix4x4.identity, _overlayMaterial, 0, MeshTopology.Triangles, 3);
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    #endregion
}
