using UnityEngine;

/// <summary>
/// 플레이어의 이동 방향에 따라 Ground Ring(Quad)을 회전시킵니다.
/// FOV 스텐실 클리핑이 적용됩니다.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class GroundRingRotator : MonoBehaviour
{
    #region Constants

    private const float HEIGHT_OFFSET = 0.01f;

    #endregion

    #region Serialized Fields

    [Header("설정")]
    [Tooltip("PlayerController의 _moveRotationSpeed와 동일하게")]
    [SerializeField] private float _rotationSpeed = 8f;

    [Header("색상")]
    [SerializeField] private Color _localPlayerColor = Color.yellow;
    [SerializeField] private Color _enemyPlayerColor = Color.red;

    #endregion

    #region Private Fields

    private PlayerController _playerController;
    private Quaternion _targetRotation;
    private MeshRenderer _meshRenderer;
    private Material _materialInstance;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _meshRenderer = GetComponent<MeshRenderer>();

        // Material 인스턴스 생성
        if (_meshRenderer.sharedMaterial != null)
        {
            _materialInstance = Instantiate(_meshRenderer.sharedMaterial);
            _meshRenderer.material = _materialInstance;
        }
    }

    private void LateUpdate()
    {
        if (_playerController == null) return;

        // 위치 동기화 (Z-fighting 방지)
        Vector3 pos = _playerController.transform.position;
        pos.y += HEIGHT_OFFSET;
        transform.position = pos;

        // 회전 동기화
        Vector3 moveDir = _playerController.MoveDirection;

        if (moveDir.sqrMagnitude > 0.01f)
        {
            float yAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            _targetRotation = Quaternion.Euler(90f, yAngle, 0f);
        }

        transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, _rotationSpeed * Time.deltaTime);
    }

    private void OnDestroy()
    {
        _playerController = null;

        if (_materialInstance != null)
        {
            Destroy(_materialInstance);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 플레이어 스폰 시 호출하여 초기화합니다.
    /// </summary>
    public void Initialize(PlayerController playerController, bool isLocalPlayer)
    {
        _playerController = playerController;
        _targetRotation = Quaternion.Euler(90f, 0f, 0f);
        transform.rotation = _targetRotation;

        SetRingColor(isLocalPlayer ? _localPlayerColor : _enemyPlayerColor);
    }

    #endregion

    #region Private Methods

    private void SetRingColor(Color color)
    {
        if (_materialInstance == null) return;

        // FOVClippedDecal 셰이더의 _Tint 프로퍼티
        _materialInstance.SetColor("_Tint", color);
    }

    #endregion
}
