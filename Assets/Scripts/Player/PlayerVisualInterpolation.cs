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

    [Header("참조")]
    [Tooltip("보간할 비주얼 Transform (MeshRenderer가 있는 오브젝트)")]
    [SerializeField] private Transform _visualTransform;

    #endregion

    #region Private Fields

    private Vector3 _targetPosition;
    private Quaternion _targetRotation;

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

    #endregion
}
