using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어 주변 아이템을 감지합니다 (로컬 클라이언트만).
/// </summary>
public class ItemPickupDetector : MonoBehaviour
{
    #region Serialized Fields

    [Header("감지 설정")]
    [Tooltip("아이템 감지 범위 (미터)")]
    [SerializeField] private float _detectionRadius = 2f;

    [Tooltip("아이템 레이어")]
    [SerializeField] private LayerMask _itemLayer;

    [Tooltip("감지 주기 (초)")]
    [SerializeField] private float _detectionInterval = 0.1f;

    #endregion

    #region Events

    /// <summary>
    /// 감지된 아이템 목록이 변경되었을 때 발생합니다.
    /// </summary>
    public event Action<List<NetworkedItem>> OnItemsChanged;

    #endregion

    #region Private Fields

    private List<NetworkedItem> _nearbyItems = new List<NetworkedItem>();
    private List<NetworkedItem> _previousItems = new List<NetworkedItem>();
    private NetworkObject _networkObject;
    private float _detectionTimer;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        _networkObject = GetComponent<NetworkObject>();
    }

    private void Update()
    {
        // Why: 로컬 플레이어만 감지
        if (_networkObject == null || !_networkObject.HasInputAuthority)
            return;

        // Why: 일정 주기마다 감지 (성능 최적화)
        _detectionTimer += Time.deltaTime;
        if (_detectionTimer >= _detectionInterval)
        {
            _detectionTimer = 0f;
            DetectNearbyItems();
        }
    }

    #endregion

    #region Detection

    /// <summary>
    /// 주변 아이템을 감지합니다.
    /// </summary>
    private void DetectNearbyItems()
    {
        _nearbyItems.Clear();

        Collider[] colliders = Physics.OverlapSphere(
            transform.position,
            _detectionRadius,
            _itemLayer
        );

        foreach (var col in colliders)
        {
            NetworkedItem item = col.GetComponent<NetworkedItem>();
            // Why: Owner가 None인 아이템만 픽업 가능 (드랍된 상태)
            if (item != null && item.Owner == PlayerRef.None)
            {
                _nearbyItems.Add(item);
            }
        }

        // Why: 아이템 목록이 실제로 변경되었을 때만 이벤트 발생 (UI 재생성 최소화)
        if (HasItemsChanged())
        {
            OnItemsChanged?.Invoke(_nearbyItems);
            UpdatePreviousItems();
        }
    }

    /// <summary>
    /// 아이템 목록이 변경되었는지 확인합니다.
    /// </summary>
    private bool HasItemsChanged()
    {
        if (_nearbyItems.Count != _previousItems.Count)
            return true;

        // Why: 같은 아이템들인지 확인
        foreach (var item in _nearbyItems)
        {
            if (!_previousItems.Contains(item))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 이전 아이템 목록을 업데이트합니다.
    /// </summary>
    private void UpdatePreviousItems()
    {
        _previousItems.Clear();
        _previousItems.AddRange(_nearbyItems);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 현재 감지된 아이템 목록을 반환합니다.
    /// </summary>
    public List<NetworkedItem> GetNearbyItems() => _nearbyItems;

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected()
    {
        // 감지 범위 표시
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _detectionRadius);
    }

    #endregion
}
