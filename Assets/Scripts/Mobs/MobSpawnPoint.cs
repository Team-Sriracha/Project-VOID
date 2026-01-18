using UnityEngine;

/// <summary>
/// 몹 스폰 포인트 컴포넌트
/// 스폰할 몹 지정
/// </summary>
public class MobSpawnPoint : MonoBehaviour
{
    #region Serialized Fields

    [Header("스폰 설정")]
    [Tooltip("이 위치에 스폰할 몹의 데이터")]
    [SerializeField] private MobData _mobData;

    [Tooltip("스폰할 몹 프리팹 (NetworkObject 포함)")]
    [SerializeField] private GameObject _mobPrefab;

    #endregion

    #region Properties

    /// <summary>
    /// 스폰할 몹 데이터
    /// </summary>
    public MobData MobData => _mobData;

    /// <summary>
    /// 스폰할 몹 프리팹
    /// </summary>
    public GameObject MobPrefab => _mobPrefab;

    /// <summary>
    /// 유효한 스폰 포인트인지 확인
    /// </summary>
    public bool IsValid => _mobData != null && _mobPrefab != null;

    #endregion

    #region Validation

    private void OnValidate()
    {
        // 프리팹에 NetworkObject가 있는지 확인
        if (_mobPrefab != null && _mobPrefab.GetComponent<FishNet.Object.NetworkObject>() == null)
        {
            Debug.LogWarning($"[MobSpawnPoint] {gameObject.name}: MobPrefab에 NetworkObject 컴포넌트가 없습니다!");
        }
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmos()
    {
        // 에디터에서 스폰 포인트 시각화
        Gizmos.color = _mobData != null ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
    }

    private void OnDrawGizmosSelected()
    {
        // 선택 시 더 진한 표시
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(transform.position, 0.3f);

        // 몹 이름 표시 (Scene 뷰에서)
        if (_mobData != null)
        {
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.5f, _mobData.MobName);
#endif
        }
    }

    #endregion
}
