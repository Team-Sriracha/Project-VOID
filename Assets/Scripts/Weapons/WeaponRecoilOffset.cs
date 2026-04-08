using UnityEngine;

/// <summary>
/// 장착된 총기 모델에 로컬 위치/회전 기반 반동을 적용합니다.
/// </summary>
public class WeaponRecoilOffset : MonoBehaviour
{
    #region Private Fields

    private Transform _target;
    private GunData _gunData;
    private Vector3 _baseLocalPosition;
    private Quaternion _baseLocalRotation;
    private Vector3 _targetPositionOffset;
    private Vector3 _currentPositionOffset;
    private Vector2 _targetRotationOffset;
    private Vector2 _currentRotationOffset;

    #endregion

    #region Unity Lifecycle

    private void LateUpdate()
    {
        if (_target == null || _gunData == null)
        {
            return;
        }

        float deltaTime = Time.deltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }

        float applyFactor = 1f - Mathf.Exp(-_gunData.RecoilApplySpeed * deltaTime);
        float returnFactor = 1f - Mathf.Exp(-_gunData.RecoilReturnSpeed * deltaTime);

        _targetPositionOffset = Vector3.Lerp(_targetPositionOffset, Vector3.zero, returnFactor);
        _targetRotationOffset = Vector2.Lerp(_targetRotationOffset, Vector2.zero, returnFactor);

        _currentPositionOffset = Vector3.Lerp(_currentPositionOffset, _targetPositionOffset, applyFactor);
        _currentRotationOffset = Vector2.Lerp(_currentRotationOffset, _targetRotationOffset, applyFactor);

        ApplyPose();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 반동을 적용할 타겟과 총기 데이터를 연결합니다.
    /// </summary>
    /// <param name="target">반동을 적용할 무기 Transform</param>
    /// <param name="gunData">반동 파라미터를 제공할 총기 데이터</param>
    public void Bind(Transform target, GunData gunData)
    {
        ResetPose();

        _target = target;
        _gunData = gunData;

        if (_target == null || _gunData == null)
        {
            ClearState();
            return;
        }

        _baseLocalPosition = _target.localPosition;
        _baseLocalRotation = _target.localRotation;
        _targetPositionOffset = Vector3.zero;
        _currentPositionOffset = Vector3.zero;
        _targetRotationOffset = Vector2.zero;
        _currentRotationOffset = Vector2.zero;
    }

    /// <summary>
    /// 장착 해제 시 반동 상태를 초기화하고 타겟을 정리합니다.
    /// </summary>
    public void ResetAndClear()
    {
        ResetPose();
        ClearState();
    }

    /// <summary>
    /// 발사 반동을 누적합니다.
    /// </summary>
    public void PlayFireRecoil()
    {
        if (_target == null || _gunData == null)
        {
            return;
        }

        _targetPositionOffset += Vector3.back * _gunData.RecoilKickBackDistance;
        _targetPositionOffset.z = Mathf.Max(-_gunData.MaxRecoilKickBackDistance, _targetPositionOffset.z);

        float yawOffset = Random.Range(-_gunData.RecoilYawAngle, _gunData.RecoilYawAngle);
        _targetRotationOffset.x = Mathf.Clamp(
            _targetRotationOffset.x + _gunData.RecoilPitchAngle,
            0f,
            _gunData.MaxRecoilPitchAngle
        );
        _targetRotationOffset.y = Mathf.Clamp(
            _targetRotationOffset.y + yawOffset,
            -_gunData.MaxRecoilYawAngle,
            _gunData.MaxRecoilYawAngle
        );

        ApplyPose();
    }

    #endregion

    #region Private Methods

    private void ApplyPose()
    {
        if (_target == null)
        {
            return;
        }

        _target.localPosition = _baseLocalPosition + _currentPositionOffset;
        _target.localRotation = _baseLocalRotation * Quaternion.Euler(-_currentRotationOffset.x, _currentRotationOffset.y, 0f);
    }

    private void ResetPose()
    {
        if (_target == null)
        {
            return;
        }

        _target.localPosition = _baseLocalPosition;
        _target.localRotation = _baseLocalRotation;
    }

    private void ClearState()
    {
        _target = null;
        _gunData = null;
        _targetPositionOffset = Vector3.zero;
        _currentPositionOffset = Vector3.zero;
        _targetRotationOffset = Vector2.zero;
        _currentRotationOffset = Vector2.zero;
    }

    #endregion
}
