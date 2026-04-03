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

    private Material _stencilWriterMaterial;
    private FOVStencilWriterPass _stencilWriterPass;
    private FOVRenderPass _renderPass;
    private FOVStencilClipPass _stencilClipPass;

    #endregion

    #region ScriptableRendererFeature Implementation

    public override void Create()
    {
        Shader stencilWriterShader = Shader.Find("Hidden/ProjectVOID/FOVStencilWriter");
        if (stencilWriterShader != null)
        {
            if (_stencilWriterMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_stencilWriterMaterial);
                }
                else
                {
                    Object.DestroyImmediate(_stencilWriterMaterial);
                }
            }

            _stencilWriterMaterial = new Material(stencilWriterShader)
            {
                name = "FOV Stencil Writer"
            };

            _stencilWriterPass = new FOVStencilWriterPass(_stencilWriterMaterial)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
            };
        }
        else
        {
            _stencilWriterMaterial = null;
            _stencilWriterPass = null;
        }

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
        var cam = renderingData.cameraData.camera;
        var camType = renderingData.cameraData.cameraType;
        
        bool isSceneView = camType == CameraType.SceneView;
        bool isMainCamera = cam.CompareTag("MainCamera");
        
        // Game View (MainCamera): FOV 메시 + Stencil 클리핑 적용
        if (isMainCamera)
        {
            if (_stencilWriterPass != null)
            {
                renderer.EnqueuePass(_stencilWriterPass);
            }

            if (settings.fovMeshMaterial != null && settings.overlayMaterial != null)
            {
                renderer.EnqueuePass(_renderPass);
            }

            if (_stencilClipPass != null)
            {
                _stencilClipPass.BypassStencil = false; // Stencil 테스트 활성화
                renderer.EnqueuePass(_stencilClipPass);
            }
        }
        // Scene View: FOV 없이 Stencil Clip 레이어만 렌더링 (BypassStencil)
        else if (isSceneView)
        {
            if (_stencilClipPass != null)
            {
                _stencilClipPass.BypassStencil = true; // Stencil 테스트 비활성화
                renderer.EnqueuePass(_stencilClipPass);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_stencilWriterMaterial != null)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(_stencilWriterMaterial);
            }
            else
            {
                Object.DestroyImmediate(_stencilWriterMaterial);
            }

            _stencilWriterMaterial = null;
        }

        base.Dispose(disposing);
    }

    #endregion
}
