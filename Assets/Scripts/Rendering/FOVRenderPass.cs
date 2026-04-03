using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// FOV 메시와 오버레이를 렌더링합니다. RenderGraph API 지원 (URP 14+).
/// </summary>
public class FOVRenderPass : ScriptableRenderPass
{
    #region Constants

    private const string PASS_NAME = "FOV System";

    #endregion

    #region Static Fields

    private static Mesh s_fovMesh;
    private static Matrix4x4 s_fovMatrix;
    private static bool s_hasFovData;

    #endregion

    #region Private Fields

    private Material _fovMeshMaterial;
    private Material _overlayMaterial;

    #endregion

    #region Constructor

    public FOVRenderPass(Material fovMeshMaterial, Material overlayMaterial)
    {
        _fovMeshMaterial = fovMeshMaterial;
        _overlayMaterial = overlayMaterial;
        profilingSampler = new ProfilingSampler(PASS_NAME);
    }

    #endregion

    #region Camera Setup

    [System.Obsolete("Use RecordRenderGraph instead.")]
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        // Configure to render to camera's color and depth targets (includes stencil buffer)
        ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle, 
                        renderingData.cameraData.renderer.cameraDepthTargetHandle);
    }

    #endregion

    #region Static Methods

    public static bool HasFOVMeshData => s_hasFovData && s_fovMesh != null;
    public static Mesh FOVMesh => s_fovMesh;
    public static Matrix4x4 FOVVisualMatrix => s_fovMatrix;
    public static Matrix4x4 FOVStencilMatrix => s_fovMatrix;

    public static void SetFOVMeshData(Mesh mesh, Matrix4x4 matrix)
    {
        s_fovMesh = mesh;
        s_fovMatrix = matrix;
        s_hasFovData = mesh != null;
    }

    public static void ClearFOVMeshData()
    {
        s_fovMesh = null;
        s_fovMatrix = Matrix4x4.identity;
        s_hasFovData = false;
    }

    #endregion

    #region RenderGraph Implementation

    private class PassData
    {
        public Material FovMeshMaterial;
        public Material OverlayMaterial;
        public Mesh FovMesh;
        public Matrix4x4 FovMatrix;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_fovMeshMaterial == null || _overlayMaterial == null) 
        {
            return;
        }
        if (!s_hasFovData || s_fovMesh == null) 
        {
            return;
        }



        using (var builder = renderGraph.AddRasterRenderPass<PassData>(PASS_NAME, out var passData, profilingSampler))
        {
            passData.FovMeshMaterial = _fovMeshMaterial;
            passData.OverlayMaterial = _overlayMaterial;
            passData.FovMesh = s_fovMesh;
            passData.FovMatrix = s_fovMatrix;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
            builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture);

            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                context.cmd.DrawMesh(data.FovMesh, data.FovMatrix, data.FovMeshMaterial, 0, 0);
                context.cmd.DrawProcedural(Matrix4x4.identity, data.OverlayMaterial, 0, MeshTopology.Triangles, 3);
            });
        }
    }

    #endregion

    #region Legacy Execute

    [System.Obsolete("Use RecordRenderGraph instead.")]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (_fovMeshMaterial == null || _overlayMaterial == null) 
        {
            return;
        }
        if (!s_hasFovData || s_fovMesh == null) 
        {
            return;
        }


        CommandBuffer cmd = CommandBufferPool.Get(PASS_NAME);

        cmd.DrawMesh(s_fovMesh, s_fovMatrix, _fovMeshMaterial, 0, 0);
        cmd.DrawProcedural(Matrix4x4.identity, _overlayMaterial, 0, MeshTopology.Triangles, 3);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    #endregion
}
