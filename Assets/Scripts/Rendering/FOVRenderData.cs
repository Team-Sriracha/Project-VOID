using UnityEngine;

/// <summary>
/// FOV 마스크/시각 메시의 현재 프레임 렌더 데이터를 공유합니다.
/// 패스가 직접 정적 상태를 소유하지 않도록 분리한 저장소입니다.
/// </summary>
public static class FOVRenderData
{
    #region Static Fields

    private static Mesh s_fovMesh;
    private static Matrix4x4 s_fovVisualMatrix;
    private static Matrix4x4 s_fovRevealStencilMatrix;
    private static bool s_hasFovData;

    #endregion

    #region Properties

    public static bool HasFOVMeshData => s_hasFovData && s_fovMesh != null;
    public static Mesh FOVMesh => s_fovMesh;
    public static Matrix4x4 FOVVisualMatrix => s_fovVisualMatrix;
    public static Matrix4x4 FOVRevealStencilMatrix => s_fovRevealStencilMatrix;

    #endregion

    #region Public Methods

    public static void SetFOVMeshData(Mesh mesh, Matrix4x4 visualMatrix, Matrix4x4 revealStencilMatrix)
    {
        s_fovMesh = mesh;
        s_fovVisualMatrix = visualMatrix;
        s_fovRevealStencilMatrix = revealStencilMatrix;
        s_hasFovData = mesh != null;
    }

    public static void ClearFOVMeshData()
    {
        s_fovMesh = null;
        s_fovVisualMatrix = Matrix4x4.identity;
        s_fovRevealStencilMatrix = Matrix4x4.identity;
        s_hasFovData = false;
    }

    #endregion
}
