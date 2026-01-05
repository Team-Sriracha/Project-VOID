using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// FOV 시스템을 위한 URP Renderer Feature입니다.
/// </summary>
public class FOVRendererFeature : ScriptableRendererFeature
{
    #region Settings

    [System.Serializable]
    public class Settings
    {
        [Header("Materials")]
        public Material fovMeshMaterial;
        public Material overlayMaterial;

        [Header("Stencil Clip")]
        public LayerMask stencilClipLayers;
    }

    public Settings settings = new Settings();

    #endregion

    #region Private Fields

    private FOVRenderPass _renderPass;
    private FOVStencilClipPass _stencilClipPass;

    #endregion

    #region ScriptableRendererFeature Implementation

    public override void Create()
    {
        _renderPass = new FOVRenderPass(settings.fovMeshMaterial, settings.overlayMaterial)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques
        };

        if (settings.stencilClipLayers != 0)
        {
            _stencilClipPass = new FOVStencilClipPass(settings.stencilClipLayers)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques + 1
            };
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!renderingData.cameraData.camera.CompareTag("MainCamera")) return;

        if (settings.fovMeshMaterial != null && settings.overlayMaterial != null)
        {
            renderer.EnqueuePass(_renderPass);
        }

        if (_stencilClipPass != null)
        {
            renderer.EnqueuePass(_stencilClipPass);
        }
    }

    #endregion
}
