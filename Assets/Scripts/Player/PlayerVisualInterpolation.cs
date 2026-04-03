using FishNet.Object;
using UnityEngine;

/// <summary>
/// 플레이어 비주얼 보간 (네트워크 떨림 제거)
/// CSP(Client-Side Prediction) 환경에서 사용
/// 별도 자식 비주얼 오브젝트만 보간 필요
/// </summary>
/// <remarks>
/// 중요: CSP가 루트 Transform을 제어하므로, 이 스크립트는
/// 별도의 자식 비주얼 오브젝트만 보간해야 합니다.
/// </remarks>
public class PlayerVisualInterpolation : NetworkBehaviour
{
    #region Serialized Fields

    [Header("보간 설정")]
    [Tooltip("비주얼 보간 속도 (높을수록 빠르게 따라감)")]
    [SerializeField] private float _interpolationSpeed = 20f;

    [Tooltip("로컬 플레이어 스무딩 시간")]
    [SerializeField] private float _localPlayerSmoothTime = 0.02f;

    [Header("참조")]
    [Tooltip("보간할 비주얼 Transform (반드시 자식 오브젝트여야 함)")]
    [SerializeField] private Transform _visualTransform;

    #endregion

    #region Private Fields

    private Vector3 _positionVelocity;
    private Vector3 _rotationVelocity;
    private bool _isValidSetup;

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        
        // CSP가 루트 Transform을 제어하므로, 
        // _visualTransform이 null이거나 자기 자신이면 비활성화
        if (_visualTransform == null || _visualTransform == transform)
        {
            _isValidSetup = false;
            Debug.LogWarning($"[PlayerVisualInterpolation] {gameObject.name}: " +
                "CSP 환경에서는 별도의 자식 비주얼 오브젝트가 필요합니다. 보간 비활성화됨.");
            return;
        }
        
        _isValidSetup = true;
    }

    private void LateUpdate()
    {
        // CSP와 충돌 방지: 유효한 설정이 아니면 아무것도 하지 않음
        if (!_isValidSetup || _visualTransform == null) return;

        // 비주얼을 루트 Transform을 향해 보간
        // (CSP가 루트 Transform을 직접 제어함)
        if (IsOwner)
        {
            // 로컬 플레이어: 짧은 스무딩으로 마이크로 떨림 제거
            _visualTransform.localPosition = Vector3.SmoothDamp(
                _visualTransform.localPosition,
                Vector3.zero,
                ref _positionVelocity,
                _localPlayerSmoothTime
            );

            Vector3 currentEuler = _visualTransform.localRotation.eulerAngles;
            Vector3 smoothedEuler = new Vector3(
                Mathf.SmoothDampAngle(currentEuler.x, 0, ref _rotationVelocity.x, _localPlayerSmoothTime),
                Mathf.SmoothDampAngle(currentEuler.y, 0, ref _rotationVelocity.y, _localPlayerSmoothTime),
                Mathf.SmoothDampAngle(currentEuler.z, 0, ref _rotationVelocity.z, _localPlayerSmoothTime)
            );
            _visualTransform.localRotation = Quaternion.Euler(smoothedEuler);
        }
        else
        {
            // 원격 플레이어: Lerp로 네트워크 보간
            _visualTransform.localPosition = Vector3.Lerp(
                _visualTransform.localPosition,
                Vector3.zero,
                _interpolationSpeed * Time.deltaTime
            );

            _visualTransform.localRotation = Quaternion.Slerp(
                _visualTransform.localRotation,
                Quaternion.identity,
                _interpolationSpeed * Time.deltaTime
            );
        }
    }

    #endregion
}
