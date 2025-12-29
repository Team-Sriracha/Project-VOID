using Fusion;
using UnityEngine;
using System.Collections;

/// <summary>
/// FOV 시스템의 메인 컨트롤러. 플레이어에 부착되어 시야를 관리합니다.
/// </summary>
public class FOVController : NetworkBehaviour
{
    #region Constants

    private const float CIRCULAR_ANGLE = 360f;
    private const float ANGLE_THRESHOLD = 359f;

    #endregion

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

    // IsInsideFOV용 캐시
    private float[] _lastHitDistances;
    private float _lastStartAngle;
    private float _lastEndAngle;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_aimController == null) _aimController = GetComponent<PlayerAimController>();
        if (_weapon == null) _weapon = GetComponent<NetworkedWeapon>();
    }

    private void LateUpdate()
    {
        if (!HasInputAuthority) return;

        _smoothedForward = Vector3.Lerp(_smoothedForward, transform.forward, 25f * Time.deltaTime).normalized;
        DetermineFOVShape();
        UpdateFOVMesh();
    }

    private void OnDestroy()
    {
        _meshGenerator?.Destroy();
    }

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        if (!HasInputAuthority)
        {
            enabled = false;
            return;
        }

        LocalInstance = this;

        _meshGenerator = new FOVMeshGenerator();
        _meshGenerator.Initialize(transform, _fovMaterial);

        _calculator = new FOVCalculator();
        _calculator.Initialize(_obstacleLayers, Runner);

        _currentRange = _baseViewRange;
        _currentAngle = CIRCULAR_ANGLE;
        _smoothedForward = transform.forward;

        UpdateFOVMesh();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (LocalInstance == this) LocalInstance = null;
    }

    #endregion

    #region FOV Shape

    private void DetermineFOVShape()
    {
        bool isAiming = _aimController != null && _aimController.IsAiming;
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
            // 원형 → 부채꼴: 축소 후 부채꼴 확장
            yield return AnimateRange(startRange, _localPlayerViewRange, halfDuration, CIRCULAR_ANGLE);
            _currentAngle = targetAngle;
            yield return AnimateRange(_localPlayerViewRange, targetRange, halfDuration, targetAngle);
        }
        else if (!isFromCircular && isToCircular)
        {
            // 부채꼴 → 원형: 축소하며 원형화 후 확장
            yield return AnimateTransition(startRange, _localPlayerViewRange, startAngle, CIRCULAR_ANGLE, halfDuration);
            yield return AnimateRange(_localPlayerViewRange, targetRange, halfDuration, CIRCULAR_ANGLE);
        }
        else
        {
            // 동일 유형 내 전환
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

        Shader.SetGlobalVector("_FOVCenter", origin);
        Shader.SetGlobalFloat("_FOVRange", _currentRange);
        Shader.SetGlobalFloat("_FOVEdgeSoftness", _edgeSoftness);
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

        // 8개 코너 체크
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
