using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// FOV 대응 셰이더가 메인 렌더 경로에서 stencil clip을 사용하도록
/// 공유 머티리얼 기반 reveal 머티리얼을 적용합니다.
/// </summary>
public static class FOVStencilMaterialRuntimeApplier
{
    #region Constants

    private const float STENCIL_COMP_EQUAL = 3f;
    private const string REVEAL_MATERIAL_SUFFIX = " (FOV Reveal)";

    #endregion

    #region Private Fields

    private static readonly Dictionary<Material, Material> s_cachedRevealMaterials = new();

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

            Material[] materials = renderer.sharedMaterials;
            bool hasChanges = false;

            for (int j = 0; j < materials.Length; j++)
            {
                Material material = materials[j];
                if (material == null || !material.HasProperty("_GlobalStencilComp"))
                {
                    continue;
                }

                Material revealMaterial = GetOrCreateRevealMaterial(material);
                if (revealMaterial == material)
                {
                    continue;
                }

                materials[j] = revealMaterial;
                hasChanges = true;
            }

            if (hasChanges)
            {
                renderer.sharedMaterials = materials;
            }
        }
    }

    #endregion

    #region Helper Methods

    private static Material GetOrCreateRevealMaterial(Material sourceMaterial)
    {
        if (sourceMaterial == null)
        {
            return null;
        }

        if (s_cachedRevealMaterials.TryGetValue(sourceMaterial, out Material cachedMaterial) && cachedMaterial != null)
        {
            return cachedMaterial;
        }

        Material revealMaterial = new Material(sourceMaterial)
        {
            name = sourceMaterial.name + REVEAL_MATERIAL_SUFFIX,
            hideFlags = HideFlags.DontSave
        };
        revealMaterial.SetFloat("_GlobalStencilComp", STENCIL_COMP_EQUAL);

        s_cachedRevealMaterials[sourceMaterial] = revealMaterial;
        return revealMaterial;
    }

    #endregion
}
