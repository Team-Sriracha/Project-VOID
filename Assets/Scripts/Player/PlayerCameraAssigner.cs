using Unity.Cinemachine;
using Fusion;
using UnityEngine;

/// <summary>
/// 로컬 플레이어에게 Cinemachine Virtual Camera를 할당하는 컴포넌트.
/// Fusion Server 모드에서 Input Authority를 가진 클라이언트만 카메라를 생성합니다.
/// </summary>
public class PlayerCameraAssigner : NetworkBehaviour
{
    #region Serialized Fields

    [Header("카메라 설정")]
    [Tooltip("플레이어에 할당할 Virtual Camera 프리팹")]
    [SerializeField] private CinemachineCamera _virtualCameraPrefab;

    #endregion

    #region Private Fields

    private CinemachineCamera _localCamera;
    private GameObject _cameraTarget;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        // Why: Input Authority를 가진 클라이언트(로컬 플레이어)만 카메라 생성
        if (!HasInputAuthority)
            return;

        CreateCamera();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        DestroyCamera();
    }

    #endregion

    #region Camera Management

    /// <summary>
    /// Virtual Camera를 생성하고 타겟을 설정합니다.
    /// </summary>
    private void CreateCamera()
    {
        if (_virtualCameraPrefab == null)
        {
            Debug.LogError("[PlayerCameraAssigner] Virtual Camera 프리팹이 할당되지 않았습니다.");
            return;
        }

        // Why: 높은 followSpeed로 즉각 반응 + 미세 스무딩
        _cameraTarget = new GameObject($"CameraTarget_{Object.InputAuthority}");
        CameraTarget smoothTarget = _cameraTarget.AddComponent<CameraTarget>();
        smoothTarget.SetTarget(transform);
        smoothTarget.SetFollowSpeed(50f);

        // Virtual Camera 인스턴스 생성
        _localCamera = Instantiate(_virtualCameraPrefab);
        _localCamera.name = $"PlayerCamera_{Object.InputAuthority}";

        // Why: 카메라는 플레이어를 직접 추적하지 않고 스무스 타겟을 추적
        // 이렇게 하면 네트워크 보간과 상관없이 항상 부드러움
        _localCamera.Target.TrackingTarget = _cameraTarget.transform;
        _localCamera.Target.LookAtTarget = _cameraTarget.transform;

        Debug.Log($"[PlayerCameraAssigner] 로컬 플레이어 카메라 생성 완료: {_localCamera.name}");
    }

    /// <summary>
    /// Virtual Camera를 제거합니다.
    /// </summary>
    private void DestroyCamera()
    {
        if (_localCamera != null)
        {
            Destroy(_localCamera.gameObject);
            _localCamera = null;
        }

        if (_cameraTarget != null)
        {
            Destroy(_cameraTarget);
            _cameraTarget = null;
        }

        Debug.Log("[PlayerCameraAssigner] 로컬 플레이어 카메라 제거 완료");
    }

    #endregion

    #region Public API

    /// <summary>
    /// 현재 로컬 플레이어의 Virtual Camera를 반환합니다.
    /// </summary>
    public CinemachineCamera LocalCamera => _localCamera;

    /// <summary>
    /// 카메라가 할당되었는지 확인합니다.
    /// </summary>
    public bool HasCamera => _localCamera != null;

    #endregion
}
