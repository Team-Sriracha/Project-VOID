using UnityEngine;

/// <summary>
/// FOV 대응 셰이더가 메인 렌더 경로에서 stencil clip을 사용하도록 런타임 material 인스턴스를 초기화합니다.
/// </summary>
public static class FOVStencilMaterialRuntimeApplier
{
    #region Constants

    private const float STENCIL_COMP_EQUAL = 3f;

    #endregion

    #region Public Methods

    public static void ApplyToHierarchy(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            Material[] materials = renderer.materials;
            for (int j = 0; j < materials.Length; j++)
            {
                Material material = materials[j];
                if (material != null && material.HasProperty("_GlobalStencilComp"))
                {
                    material.SetFloat("_GlobalStencilComp", STENCIL_COMP_EQUAL);
                }
            }
        }
    }

    #endregion
}
