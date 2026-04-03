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

        // 플레이어 자식이므로 위치는 자동 추적, 회전만 월드 스페이스에서 별도 관리 (부모 회전 영향 제거)
        Vector3 moveDir = _playerController.MoveDirection;

        if (moveDir.sqrMagnitude > 0.01f)
        {
            float yAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            _targetRotation = Quaternion.Euler(90f, yAngle, 0f);
        }

        // transform.rotation은 월드 스페이스 기준이므로 부모 회전에 영향받지 않음
        // RotateTowards 사용 (PlayerController와 일관성 유지)
        float maxDegrees = _rotationSpeed * 60f * Time.deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, _targetRotation, maxDegrees);
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
