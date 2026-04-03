using FishNet.Object;
using UnityEngine;
using System.Collections;

/// <summary>
/// FOV 시스템의 메인 컨트롤러. 플레이어에 부착되어 시야를 관리합니다.
/// </summary>
[ExecuteAlways]
public class FOVController : NetworkBehaviour
{
    #region Constants

    private const float CIRCULAR_ANGLE = 360f;
    private const float ANGLE_THRESHOLD = 359f;
    private const int MAX_SHADER_HIT_DISTANCE_COUNT = 181;

    #endregion
    
    // ... [Note: SerializeFields are skipped here for brevity, assume they exist] ...
    
    #region Serialized Fields

    [Header("시야 범위")]
    [SerializeField] private float _baseViewRange = 10f;

    [Header("성능")]
    [Range(60, 180)]
    [SerializeField] private int _rayCount = 120;

    [Header("시각 효과")]
    [SerializeField] private bool _enableSoftEdge = true;

    [Range(0.5f, 5.0f)]
    [SerializeField] private float _edgeSoftness = 2.0f;

    [Range(0.1f, 5.0f)]
    [SerializeField] private float _falloffExp = 1.0f;

    [Range(0.1f, 0.5f)]
    [SerializeField] private float _transitionDuration = 0.25f;

    [Header("장애물 감지")]
    [SerializeField] private LayerMask _obstacleLayers;

    [Header("재질")]
    [SerializeField] private Material _fovMaterial;

    [Range(0.5f, 5.0f)]
    [SerializeField] private float _fovHeight = 2.5f;

    [Header("컴포넌트 참조")]
    [SerializeField] private PlayerAimController _aimController;
    [SerializeField] private NetworkedWeapon _weapon;

    [Header("로컬 플레이어 시야")]
    [Range(1f, 5f)]
    [SerializeField] private float _localPlayerViewRange = 2.5f;

    #endregion

    #region Properties

    public static FOVController LocalInstance { get; private set; }
    public float CurrentRange => _currentRange;
    public float CurrentAngle => _currentAngle;
    public LayerMask ObstacleLayers => _obstacleLayers;

    #endregion

    #region Private Fields

    private FOVMeshGenerator _meshGenerator;
    private FOVCalculator _calculator;
    private float _currentRange;
    private float _currentAngle;
    private bool _isTransitioning;
    private Vector3 _smoothedForward;
    private readonly float[] _shaderHitDistances = new float[MAX_SHADER_HIT_DISTANCE_COUNT];

    private float[] _lastHitDistances;
    private float _lastStartAngle;
    private float _lastEndAngle;

    #endregion

    #region Unity Lifecycle

#if UNITY_EDITOR
    // 에디터 로드/컴파일 직후 즉시 실행되어 Scene View에서 보이게 함 (기본값 0 방지)
    [UnityEditor.InitializeOnLoadMethod]
    private static void InitGlobalStencil()
    {
        Shader.SetGlobalFloat("_GlobalStencilComp", 8.0f); // Always
    }
#endif

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            Shader.SetGlobalFloat("_GlobalStencilComp", 8.0f); // Always
        }
    }

    private void Awake()
    {
        if (_aimController == null) _aimController = GetComponent<PlayerAimController>();
        if (_weapon == null) _weapon = GetComponent<NetworkedWeapon>();

        // [FIX] 초기 FOV 셰이더 프로퍼티 설정
        Shader.SetGlobalVector("_FOVCenter", transform.position);
        Shader.SetGlobalFloat("_FOVRange", 999f); 
        Shader.SetGlobalFloat("_FOVEdgeSoftness", 0f);

        // [FIX] Global Stencil Comp
        if (Application.isPlaying)
        {
            Shader.SetGlobalFloat("_GlobalStencilComp", 3.0f); // Equal (Game)
        }
        else
        {
            Shader.SetGlobalFloat("_GlobalStencilComp", 8.0f); // Always (Editor)
        }

        // [FIX] Early initialization of state to prevent zero-vector errors
        _smoothedForward = transform.forward;
        if (_smoothedForward == Vector3.zero) _smoothedForward = Vector3.forward;
        _currentRange = _baseViewRange > 0 ? _baseViewRange : 15f;
        _currentAngle = CIRCULAR_ANGLE;
        _lastStartAngle = -180f;
        _lastEndAngle = 180f;

        // [DEBUG] Pre-initialize MeshGenerator to avoid data gap
        if (Application.isPlaying && (Application.isEditor || UnityEngine.SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null))
        {
             _meshGenerator = new FOVMeshGenerator();
             _meshGenerator.Initialize(transform, _fovMaterial);
             _calculator = new FOVCalculator();
             _calculator.Initialize(_obstacleLayers);
             
             // 초기 더미 데이터 전송
             UpdateFOVMesh(); 
        }
    }
    
    private void OnDestroy()
    {
        _meshGenerator?.Destroy();

        if (LocalInstance == this || IsOwner)
        {
            Shader.SetGlobalFloat("_FOVEnabled", 0.0f);
            Shader.SetGlobalFloat("_FOVHitDistanceCount", 0.0f);
        }

        // Editor 가시성 복구: 게임 종료 시 8 (Always)로 되돌려 Scene View에서 다시 보이게 함
        Shader.SetGlobalFloat("_GlobalStencilComp", 8.0f);
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying) return; // Don't run logic in Editor

        if (!IsOwner) return;

        _smoothedForward = Vector3.Lerp(_smoothedForward, transform.forward, 25f * Time.deltaTime).normalized;
        DetermineFOVShape();
        UpdateFOVMesh();
    }



    #endregion

    #region Fishnet Lifecycle

    public override void OnStartClient()
    {
        base.OnStartClient();

        if (!IsOwner)
        {
            enabled = false;
            // 만약 Owner가 아니더라도 MeshGenerator가 있다면 정리 (Awake에서 생성된 것)
            _meshGenerator?.Destroy();
            _meshGenerator = null;
            return;
        }
        
        // 로컬 인스턴스 설정
        if (LocalInstance != null && LocalInstance != this)
        {
            Debug.LogWarning("[FOVController] Multiple LocalInstances detected! Overwriting.");
        }
        LocalInstance = this;

        // Awake에서 생성되지 않았을 경우에만 생성
        if (_meshGenerator == null)
        {
            _meshGenerator = new FOVMeshGenerator();
            _meshGenerator.Initialize(transform, _fovMaterial);
        }

        if (_calculator == null)
        {
            _calculator = new FOVCalculator();
            _calculator.Initialize(_obstacleLayers);
        }

        // Re-affirm initial state
        _currentRange = _baseViewRange;
        _currentAngle = CIRCULAR_ANGLE;
        // _smoothedForward is already safe from Awake or LateUpdate, but sync with transform just in case
        _smoothedForward = transform.forward;
        if (_smoothedForward == Vector3.zero) _smoothedForward = Vector3.forward;

        UpdateFOVMesh();
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        if (LocalInstance == this) LocalInstance = null;
    }

    #endregion

    #region FOV Shape

    private void DetermineFOVShape()
    {
        bool isAiming = _aimController != null && _aimController.IsAiming.Value;
        var weaponData = _weapon?.CurrentWeaponData;

        float targetRange;
        float targetAngle;

        if (weaponData is GunData gunData)
        {
            if (isAiming)
            {
                targetRange = gunData.AimFOVRange;
                targetAngle = gunData.AimFOVAngle;
            }
            else
            {
                targetRange = gunData.CircularFOVRange;
                targetAngle = CIRCULAR_ANGLE;
            }
        }
        else if (weaponData is MeleeWeaponData meleeData)
        {
            targetRange = meleeData.CircularFOVRange;
            targetAngle = CIRCULAR_ANGLE;
        }
        else
        {
            targetRange = _baseViewRange;
            targetAngle = CIRCULAR_ANGLE;
        }

        if (!_isTransitioning && NeedsTransition(targetRange, targetAngle))
        {
            StartCoroutine(TransitionFOV(targetRange, targetAngle));
        }
    }

    private bool NeedsTransition(float targetRange, float targetAngle)
    {
        return Mathf.Abs(_currentRange - targetRange) > 0.1f || Mathf.Abs(_currentAngle - targetAngle) > 1f;
    }

    private IEnumerator TransitionFOV(float targetRange, float targetAngle)
    {
        _isTransitioning = true;

        float startRange = _currentRange;
        float startAngle = _currentAngle;
        bool isFromCircular = startAngle >= ANGLE_THRESHOLD;
        bool isToCircular = targetAngle >= ANGLE_THRESHOLD;
        float halfDuration = _transitionDuration * 0.5f;

        if (isFromCircular && !isToCircular)
        {
            yield return AnimateRange(startRange, _localPlayerViewRange, halfDuration, CIRCULAR_ANGLE);
            _currentAngle = targetAngle;
            yield return AnimateRange(_localPlayerViewRange, targetRange, halfDuration, targetAngle);
        }
        else if (!isFromCircular && isToCircular)
        {
            // 부채꼴 형태를 유지하며 줄어들기 → 원형으로 전환 후 확대
            yield return AnimateRange(startRange, _localPlayerViewRange, halfDuration, startAngle);
            _currentAngle = CIRCULAR_ANGLE;
            yield return AnimateRange(_localPlayerViewRange, targetRange, halfDuration, CIRCULAR_ANGLE);
        }
        else
        {
            yield return AnimateTransition(startRange, targetRange, startAngle, targetAngle, _transitionDuration);
        }

        _currentRange = targetRange;
        _currentAngle = targetAngle;
        _isTransitioning = false;
    }

    private IEnumerator AnimateRange(float fromRange, float toRange, float duration, float fixedAngle)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            _currentRange = Mathf.Lerp(fromRange, toRange, t);
            _currentAngle = fixedAngle;
            yield return null;
        }
    }

    private IEnumerator AnimateTransition(float fromRange, float toRange, float fromAngle, float toAngle, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            _currentRange = Mathf.Lerp(fromRange, toRange, t);
            _currentAngle = Mathf.Lerp(fromAngle, toAngle, t);
            yield return null;
        }
    }

    #endregion

    #region Mesh Update

    private void UpdateFOVMesh()
    {
        if (_meshGenerator == null || _calculator == null) return;

        Vector3 origin = transform.position;
        float[] hitDistances;

        if (_currentAngle >= ANGLE_THRESHOLD)
        {
            hitDistances = _calculator.CalculateCircularFOV(origin, _smoothedForward, _currentRange, _rayCount, _fovHeight);
            _lastStartAngle = -180f;
            _lastEndAngle = 180f;
        }
        else
        {
            hitDistances = _calculator.CalculateCompositeFOV(
                origin, _smoothedForward, _currentRange, _currentAngle,
                _localPlayerViewRange, _rayCount, _fovHeight);
            _lastStartAngle = -180f;
            _lastEndAngle = 180f;
        }

        _lastHitDistances = hitDistances;
        



        _meshGenerator.UpdateMesh(
            hitDistances, _lastStartAngle, _lastEndAngle,
            origin + Vector3.up * _fovHeight, _smoothedForward,
            _enableSoftEdge, _edgeSoftness);





        UpdateShaderProperties(origin);
    }

    private void UpdateShaderProperties(Vector3 origin)
    {
        if (_fovMaterial != null)
        {
            _fovMaterial.SetFloat("_ViewRadius", _currentRange);
            _fovMaterial.SetFloat("_FalloffExp", _falloffExp);
        }

        Vector2 forwardXZ = new Vector2(_smoothedForward.x, _smoothedForward.z);
        if (forwardXZ.sqrMagnitude < 0.0001f)
        {
            forwardXZ = Vector2.up;
        }
        else
        {
            forwardXZ.Normalize();
        }

        Vector4 projectionOffset = Vector4.zero;
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Vector3 cameraForward = mainCamera.transform.forward;
            float verticalFactor = Mathf.Max(Mathf.Abs(cameraForward.y), 0.001f);
            Vector2 cameraForwardXZ = new Vector2(cameraForward.x, cameraForward.z);
            projectionOffset = new Vector4(
                -cameraForwardXZ.x / verticalFactor,
                -cameraForwardXZ.y / verticalFactor,
                0f,
                0f);
        }

        int hitDistanceCount = PopulateShaderHitDistanceBuffer();

        Shader.SetGlobalVector("_FOVCenter", origin);
        Shader.SetGlobalFloat("_FOVRange", _currentRange);
        Shader.SetGlobalFloat("_FOVEdgeSoftness", _edgeSoftness);
        Shader.SetGlobalFloat("_FOVEnabled", 1.0f); // Enable FOV dimming in game
        Shader.SetGlobalVector("_FOVProjectionOffset", projectionOffset);
        Shader.SetGlobalFloat("_FOVBaseY", origin.y);
        Shader.SetGlobalVector("_FOVForwardXZ", new Vector4(forwardXZ.x, forwardXZ.y, 0f, 0f));
        Shader.SetGlobalFloat("_FOVStartAngle", _lastStartAngle);
        Shader.SetGlobalFloat("_FOVEndAngle", _lastEndAngle);
        Shader.SetGlobalFloat("_FOVHitDistanceCount", hitDistanceCount);
        Shader.SetGlobalFloatArray("_FOVHitDistances", _shaderHitDistances);
    }

    private int PopulateShaderHitDistanceBuffer()
    {
        int hitDistanceCount = _lastHitDistances != null
            ? Mathf.Min(_lastHitDistances.Length, MAX_SHADER_HIT_DISTANCE_COUNT)
            : 0;

        float fallbackDistance = _currentRange > 0.01f ? _currentRange : _baseViewRange;

        if (hitDistanceCount >= 2)
        {
            for (int i = 0; i < hitDistanceCount; i++)
            {
                _shaderHitDistances[i] = _lastHitDistances[i];
            }

            fallbackDistance = _shaderHitDistances[hitDistanceCount - 1];
        }
        else
        {
            hitDistanceCount = 2;
            _shaderHitDistances[0] = fallbackDistance;
            _shaderHitDistances[1] = fallbackDistance;
        }

        for (int i = hitDistanceCount; i < MAX_SHADER_HIT_DISTANCE_COUNT; i++)
        {
            _shaderHitDistances[i] = fallbackDistance;
        }

        return hitDistanceCount;
    }

    #endregion

    #region Public Methods

    public void ShowFOV() => _meshGenerator?.Show();
    public void HideFOV() => _meshGenerator?.Hide();

    /// <summary>
    /// 주어진 월드 좌표가 현재 FOV 내부에 있는지 확인합니다.
    /// </summary>
    public bool IsInsideFOV(Vector3 worldPos)
    {
        if (_lastHitDistances == null || _lastHitDistances.Length == 0) return false;

        Vector3 toTarget = worldPos - transform.position;
        toTarget.y = 0;
        float distance = toTarget.magnitude;

        if (distance > _currentRange + 1f) return false;

        Vector3 forward = transform.forward;
        forward.y = 0;
        if (forward == Vector3.zero) forward = Vector3.forward;

        float angleToTarget = Vector3.SignedAngle(forward, toTarget, Vector3.up);
        if (angleToTarget < _lastStartAngle || angleToTarget > _lastEndAngle) return false;

        float angleRange = _lastEndAngle - _lastStartAngle;
        if (angleRange <= 0) return false;

        float t = (angleToTarget - _lastStartAngle) / angleRange;
        int index = Mathf.Clamp(Mathf.RoundToInt(t * (_lastHitDistances.Length - 1)), 0, _lastHitDistances.Length - 1);

        return distance <= _lastHitDistances[index];
    }

    /// <summary>
    /// 콜라이더의 일부라도 FOV 안에 있는지 확인합니다.
    /// </summary>
    public bool IsColliderInsideFOV(Collider collider)
    {
        if (collider == null) return false;

        Bounds bounds = collider.bounds;
        if (IsInsideFOV(bounds.center)) return true;

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        return IsInsideFOV(new Vector3(min.x, min.y, min.z)) ||
               IsInsideFOV(new Vector3(max.x, min.y, min.z)) ||
               IsInsideFOV(new Vector3(min.x, min.y, max.z)) ||
               IsInsideFOV(new Vector3(max.x, min.y, max.z)) ||
               IsInsideFOV(new Vector3(min.x, max.y, min.z)) ||
               IsInsideFOV(new Vector3(max.x, max.y, min.z)) ||
               IsInsideFOV(new Vector3(min.x, max.y, max.z)) ||
               IsInsideFOV(new Vector3(max.x, max.y, max.z));
    }

    #endregion
}
