using UnityEngine;

/// <summary>
/// 캐주얼 탑다운 슈터 최적화 카메라 타겟.
/// 빠른 반응 + 약간의 부드러움으로 즉각적이면서도 자연스러운 느낌 제공.
/// </summary>
public class CameraTarget : MonoBehaviour
{
    #region Serialized Fields

    [Header("타겟 설정")]
    [Tooltip("따라갈 플레이어 Transform")]
    [SerializeField] private Transform _targetPlayer;

    [Header("스무딩 설정")]
    [Tooltip("추적 스무딩 시간 (0 = Hard Lock, 0.1-0.3 = 부드러움)")]
    [SerializeField] private float _smoothTime = 0.1f;

    [Tooltip("Y축 고정 여부 (탑다운은 보통 true)")]
    [SerializeField] private bool _lockYAxis = true;

    [Header("디버그")]
    [SerializeField] private bool _showDebugInfo = false;

    #endregion

    #region Private Fields

    private Vector3 _currentVelocity;
    private float _fixedY;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        if (_targetPlayer != null)
        {
            _fixedY = _targetPlayer.position.y;
            transform.position = _targetPlayer.position;
        }
    }

    private void LateUpdate()
    {
        if (_targetPlayer == null) return;

        Vector3 targetPosition = _targetPlayer.position;

        if (_lockYAxis)
        {
            targetPosition.y = _fixedY;
        }

        if (_smoothTime > 0f)
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref _currentVelocity,
                _smoothTime
            );
        }
        else
        {
            transform.position = targetPosition;
        }

        if (_showDebugInfo)
        {
            float distance = Vector3.Distance(transform.position, targetPosition);
            Debug.Log($"[CameraTarget] Distance to player: {distance:F3}");
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// 따라갈 타겟 플레이어를 설정합니다.
    /// </summary>
    public void SetTarget(Transform target)
    {
        _targetPlayer = target;

        if (_targetPlayer != null)
        {
            _fixedY = _targetPlayer.position.y;
            transform.position = _targetPlayer.position;
        }
    }

    /// <summary>
    /// 추적 스무딩 시간을 동적으로 조정합니다.
    /// </summary>
    public void SetSmoothTime(float smoothTime)
    {
        _smoothTime = smoothTime;
    }

    #endregion
}
