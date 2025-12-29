using Fusion;
using UnityEngine;

/// <summary>
/// Networked Projectile 풀을 관리합니다 (서버 전용).
/// </summary>
public class ProjectilePool : MonoBehaviour
{
    #region Serialized Fields

    [Header("Pool Settings")]
    [SerializeField] private NetworkPrefabRef _projectilePrefab;

    #endregion

    #region Private Fields

    private NetworkRunner _runner;

    #endregion

    #region Initialization

    /// <summary>
    /// 풀을 초기화합니다. 서버에서만 호출하세요.
    /// </summary>
    public void Initialize(NetworkRunner runner, NetworkPrefabRef prefab)
    {
        if (runner == null || !runner.IsServer)
        {
            Debug.LogWarning("[ProjectilePool] Initialize는 서버에서만 호출할 수 있습니다.");
            return;
        }

        _runner = runner;
        _projectilePrefab = prefab;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 발사체를 스폰합니다.
    /// </summary>
    public NetworkObject Get(Vector3 position, Quaternion rotation)
    {
        if (_runner == null || !_runner.IsServer)
        {
            Debug.LogWarning("[ProjectilePool] 서버에서만 Projectile을 가져올 수 있습니다.");
            return null;
        }

        return SpawnNew(position, rotation);
    }

    /// <summary>
    /// 발사체를 반환/정리합니다.
    /// </summary>
    public void Return(NetworkObject obj)
    {
        if (_runner == null || obj == null) return;

        if (obj.IsValid)
        {
            _runner.Despawn(obj);
        }
    }

    #endregion

    #region Helper Methods

    private NetworkObject SpawnNew(Vector3 position, Quaternion rotation)
    {
        if (_runner == null || !_runner.IsServer)
        {
            return null;
        }

        if (!_projectilePrefab.IsValid)
        {
            Debug.LogWarning("[ProjectilePool] Prefab이 설정되지 않았습니다.");
            return null;
        }

        return _runner.Spawn(_projectilePrefab, position, rotation, inputAuthority: PlayerRef.None);
    }

    #endregion
}
