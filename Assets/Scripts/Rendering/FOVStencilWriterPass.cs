using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// FOV 메시를 색 출력 없이 stencil 버퍼에 먼저 기록합니다.
/// </summary>
public class FOVStencilWriterPass : ScriptableRenderPass
{
    public enum StencilMatrixSource
    {
        RevealStencil = 0,
        Visual = 1,
    }

    #region Constants

    private const string PASS_NAME = "FOV Stencil Writer";

    #endregion

    #region Private Fields

    private readonly Material _stencilWriterMaterial;
    private readonly StencilMatrixSource _matrixSource;

    #endregion

    #region Constructor

    public FOVStencilWriterPass(Material stencilWriterMaterial, StencilMatrixSource matrixSource)
    {
        _stencilWriterMaterial = stencilWriterMaterial;
        _matrixSource = matrixSource;
        profilingSampler = new ProfilingSampler(PASS_NAME);
    }

    #endregion

    #region Camera Setup

    [System.Obsolete("Use RecordRenderGraph instead.")]
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        ConfigureTarget(renderingData.cameraData.renderer.cameraDepthTargetHandle);
    }

    #endregion

    #region Legacy Execute

    [System.Obsolete("Use RecordRenderGraph instead.")]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (_stencilWriterMaterial == null || !FOVRenderData.HasFOVMeshData)
        {
            return;
        }

        CommandBuffer cmd = CommandBufferPool.Get(PASS_NAME);
        Matrix4x4 matrix = _matrixSource == StencilMatrixSource.RevealStencil
            ? FOVRenderData.FOVRevealStencilMatrix
            : FOVRenderData.FOVVisualMatrix;
        cmd.DrawMesh(FOVRenderData.FOVMesh, matrix, _stencilWriterMaterial, 0, 0);
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    #endregion
}
