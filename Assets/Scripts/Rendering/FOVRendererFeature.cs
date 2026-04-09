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

        [Header("Shaders")]
        public Shader stencilWriterShader;
        public Shader overlayStencilWriterShader;

        [Header("Stencil Clip")]
        public LayerMask stencilClipLayers;
    }

    public Settings settings = new Settings();

    #endregion

    #region Private Fields

    private Material _stencilWriterMaterial;
    private Material _overlayStencilWriterMaterial;
    private FOVStencilWriterPass _stencilWriterPass;
    private FOVStencilWriterPass _overlayStencilWriterPass;
    private FOVRenderPass _renderPass;
    private FOVOverlayPass _overlayPass;
    private FOVStencilClipPass _stencilClipPass;

    #endregion

    #region ScriptableRendererFeature Implementation

    public override void Create()
    {
        Shader stencilWriterShader = settings.stencilWriterShader != null
            ? settings.stencilWriterShader
            : Shader.Find("Hidden/ProjectVOID/FOVStencilWriter");
        Shader overlayStencilWriterShader = settings.overlayStencilWriterShader != null
            ? settings.overlayStencilWriterShader
            : Shader.Find("Hidden/ProjectVOID/FOVOverlayStencilWriter");

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

            _stencilWriterPass = new FOVStencilWriterPass(
                _stencilWriterMaterial,
                FOVStencilWriterPass.StencilMatrixSource.RevealStencil)
            {
                // Experiment: write reveal stencil before depth/depth-normal prepasses
                // so FOV-aware shaders can clip those passes as well.
                renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses
            };
        }
        else
        {
            Debug.LogError("[FOVRendererFeature] FOVStencilWriter 셰이더를 찾을 수 없습니다. 빌드 설정 또는 Renderer Feature 참조를 확인하세요.");
            _stencilWriterMaterial = null;
            _stencilWriterPass = null;
        }

        if (overlayStencilWriterShader != null)
        {
            if (_overlayStencilWriterMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_overlayStencilWriterMaterial);
                }
                else
                {
                    Object.DestroyImmediate(_overlayStencilWriterMaterial);
                }
            }

            _overlayStencilWriterMaterial = new Material(overlayStencilWriterShader)
            {
                name = "FOV Overlay Stencil Writer"
            };

            _overlayStencilWriterPass = new FOVStencilWriterPass(
                _overlayStencilWriterMaterial,
                FOVStencilWriterPass.StencilMatrixSource.Visual)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques + 1
            };
        }
        else
        {
            Debug.LogError("[FOVRendererFeature] FOVOverlayStencilWriter 셰이더를 찾을 수 없습니다. 빌드 설정 또는 Renderer Feature 참조를 확인하세요.");
            _overlayStencilWriterMaterial = null;
            _overlayStencilWriterPass = null;
        }

        _renderPass = new FOVRenderPass(settings.fovMeshMaterial)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques
        };

        _overlayPass = new FOVOverlayPass(settings.overlayMaterial)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques + 2
        };

        if (settings.stencilClipLayers != 0)
        {
            _stencilClipPass = new FOVStencilClipPass(settings.stencilClipLayers)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingOpaques + 3
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

            if (settings.fovMeshMaterial != null)
            {
                renderer.EnqueuePass(_renderPass);
            }

            if (settings.overlayMaterial != null)
            {
                if (_overlayStencilWriterPass != null)
                {
                    renderer.EnqueuePass(_overlayStencilWriterPass);
                }

                renderer.EnqueuePass(_overlayPass);
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

        if (_overlayStencilWriterMaterial != null)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(_overlayStencilWriterMaterial);
            }
            else
            {
                Object.DestroyImmediate(_overlayStencilWriterMaterial);
            }

            _overlayStencilWriterMaterial = null;
        }

        base.Dispose(disposing);
    }

    #endregion
}
