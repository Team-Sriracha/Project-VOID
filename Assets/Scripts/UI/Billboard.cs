using UnityEngine;

/// <summary>
/// 카메라를 향해 항상 회전하는 빌보드(오버헤드 UI 등)를 구현합니다.
/// 주로 플레이어 오버헤드 UI가 카메라를 항상 바라보도록 할 때 사용합니다.
/// </summary>
public class Billboard : MonoBehaviour
{
    #region Private Fields

    private Camera _mainCamera;

    #endregion

    #region Unity Lifecycle

    /// <summary>
    /// 첫 번째 프레임 업데이트 전에 호출됩니다.
    /// </summary>
    private void Start()
    {
        InitializeCamera();
    }

    /// <summary>
    /// 매 프레임 늦게 호출되어 카메라를 바라보도록 회전시킵니다.
    /// </summary>
    private void LateUpdate()
    {
        UpdateRotation();
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 메인 카메라를 찾아 초기화합니다.
    /// </summary>
    private void InitializeCamera()
    {
        _mainCamera = Camera.main;
    }

    #endregion

    #region Rotation

    /// <summary>
    /// 오브젝트의 회전을 업데이트하여 카메라를 바라보도록 합니다.
    /// </summary>
    private void UpdateRotation()
    {
        if (!IsCameraValid()) return;

        RotateTowardsCamera();
    }

    /// <summary>
    /// 카메라가 유효한지 확인합니다. 없거나 비활성화되면 재탐색합니다.
    /// </summary>
    private bool IsCameraValid()
    {
        // Why: 카메라가 없거나 비활성화되면 재탐색 (씬 전환 시 카메라 변경 대응)
        if (_mainCamera == null || !_mainCamera.isActiveAndEnabled)
        {
            _mainCamera = Camera.main;
        }
        
        return _mainCamera != null;
    }

    /// <summary>
    /// 카메라 방향으로 회전합니다.
    /// </summary>
    private void RotateTowardsCamera()
    {
        transform.rotation = _mainCamera.transform.rotation;
    }

    #endregion
}
