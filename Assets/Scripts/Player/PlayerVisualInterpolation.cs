using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어 비주얼을 부드럽게 보간하여 네트워크 떨림을 제거합니다.
/// NetworkRigidbody3D의 Interpolation Target으로 사용됩니다.
/// </summary>
public class PlayerVisualInterpolation : NetworkBehaviour
{
    #region Serialized Fields

    [Header("보간 설정")]
    [Tooltip("비주얼 보간 속도 (높을수록 빠르게 따라감)")]
    [SerializeField] private float _interpolationSpeed = 20f;

    [Tooltip("로컬 플레이어 스무딩 시간")]
    [SerializeField] private float _localPlayerSmoothTime = 0.02f;

    [Header("참조")]
    [Tooltip("보간할 비주얼 Transform (MeshRenderer가 있는 오브젝트)")]
    [SerializeField] private Transform _visualTransform;

    #endregion

    #region Private Fields

    private Vector3 _targetPosition;
    private Quaternion _targetRotation;
    private Vector3 _positionVelocity;
    private Vector3 _rotationVelocity;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        if (_visualTransform == null)
        {
            _visualTransform = transform;
        }

        _targetPosition = transform.position;
        _targetRotation = transform.rotation;
    }

    public override void Render()
    {
        if (_visualTransform == null) return;

        _targetPosition = transform.position;
        _targetRotation = transform.rotation;

        // Why: 로컬 플레이어는 매우 짧은 SmoothDamp로 떨림 제거
        // 원격 플레이어는 Lerp로 네트워크 보간
        if (HasInputAuthority)
        {
            _visualTransform.position = Vector3.SmoothDamp(
                _visualTransform.position,
                _targetPosition,
                ref _positionVelocity,
                _localPlayerSmoothTime
            );

            // Why: 회전은 Euler 각도로 변환하여 SmoothDamp 적용
            Vector3 currentEuler = _visualTransform.rotation.eulerAngles;
            Vector3 targetEuler = _targetRotation.eulerAngles;
            Vector3 smoothedEuler = new Vector3(
                Mathf.SmoothDampAngle(currentEuler.x, targetEuler.x, ref _rotationVelocity.x, _localPlayerSmoothTime),
                Mathf.SmoothDampAngle(currentEuler.y, targetEuler.y, ref _rotationVelocity.y, _localPlayerSmoothTime),
                Mathf.SmoothDampAngle(currentEuler.z, targetEuler.z, ref _rotationVelocity.z, _localPlayerSmoothTime)
            );
            _visualTransform.rotation = Quaternion.Euler(smoothedEuler);
        }
        else
        {
            _visualTransform.position = Vector3.Lerp(
                _visualTransform.position,
                _targetPosition,
                _interpolationSpeed * Time.deltaTime
            );

            _visualTransform.rotation = Quaternion.Slerp(
                _visualTransform.rotation,
                _targetRotation,
                _interpolationSpeed * Time.deltaTime
            );
        }
    }

    #endregion
}
