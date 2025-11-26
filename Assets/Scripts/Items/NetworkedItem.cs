using Fusion;
using UnityEngine;

/// <summary>
/// 월드에 드랍된 아이템 (서버 권한)
/// </summary>
public class NetworkedItem : NetworkBehaviour
{
    #region Networked Properties

    [Networked]
    public NetworkString<_32> ItemID { get; set; }

    /// <summary>
    /// 아이템 소유자 (None이면 월드에 드랍된 상태)
    /// </summary>
    [Networked]
    public PlayerRef Owner { get; set; }

    /// <summary>
    /// 드랍된 위치 (떠다니기 기준점)
    /// </summary>
    [Networked]
    public Vector3 DroppedPosition { get; set; }

    /// <summary>
    /// 초기 발사 속도 (LootBox에서 튀어오를 때 사용)
    /// </summary>
    [Networked]
    public Vector3 InitialVelocity { get; set; }

    #endregion

    #region Serialized Fields

    [Header("드랍 효과 설정")]
    [Tooltip("위아래로 떠다니는 속도")]
    [SerializeField] private float _bobbingSpeed = 1f;

    [Tooltip("떠다니는 높이")]
    [SerializeField] private float _bobbingHeight = 0.3f;

    [Tooltip("회전 속도 (도/초)")]
    [SerializeField] private float _rotationSpeed = 30f;

    #endregion

    #region Private Fields

    private ItemData _itemData;
    private Collider _collider;
    private PlayerRef _previousOwner;
    private float _bobbingTime;
    private GameObject _dropEffectInstance;
    private Rigidbody _rigidbody;
    private bool _hasLanded = false;
    private Transform _currentAttachPoint;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        // ItemID로 ItemData 로드
        string itemIDStr = ItemID.ToString();
        _itemData = ItemDatabase.GetItem(itemIDStr);

        if (_itemData == null)
        {
            Debug.LogError($"[NetworkedItem] ItemID '{itemIDStr}'에 해당하는 ItemData를 찾을 수 없습니다!");
            return;
        }

        // Why: Rigidbody 가져오기
        _rigidbody = GetComponent<Rigidbody>();

        // Why: Collider는 이미 프리팹에 설정되어 있음
        _collider = GetComponent<Collider>();

        // Why: Owner가 이미 설정되어 있으면 즉시 위치 설정
        _previousOwner = Owner;
        if (Owner != PlayerRef.None)
        {
            UpdateAttachment();

            // Why: 소유된 상태면 Rigidbody 비활성화
            if (_rigidbody != null)
            {
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;
            }

            // Why: 소유된 상태면 Collider 비활성화
            if (_collider != null)
            {
                _collider.enabled = false;
            }
        }
        else
        {
            // Why: 드랍된 상태로 스폰 - Collider는 물리 충돌 가능하도록 설정
            if (_collider != null)
            {
                _collider.enabled = true;
                _collider.isTrigger = false; // 바닥과 충돌해야 함
            }

            // Why: 드랍된 상태로 스폰
            if (HasStateAuthority)
            {
                // Why: LootBox에서 튀어오를 때 초기 속도 적용
                if (InitialVelocity != Vector3.zero && _rigidbody != null)
                {
                    _rigidbody.isKinematic = false;
                    _rigidbody.useGravity = true;
                    _rigidbody.linearVelocity = InitialVelocity;
                    Debug.Log($"[NetworkedItem] 초기 속도 적용: {InitialVelocity}, 현재 위치: {transform.position}");
                }

                DroppedPosition = transform.position;
            }
            else
            {
                // Why: 클라이언트는 서버의 Transform 동기화를 기다림
                if (_rigidbody != null)
                {
                    _rigidbody.isKinematic = true; // 클라이언트는 서버 위치 따라감
                    _rigidbody.useGravity = false;
                }
                Debug.Log($"[NetworkedItem] 클라이언트 스폰, 서버 Transform 기다림");
            }
        }

        Debug.Log($"[NetworkedItem] Spawned: {_itemData.ItemName}, Position: {transform.position}, HasStateAuthority: {HasStateAuthority}, HasRigidbody: {_rigidbody != null}");
    }

    public override void FixedUpdateNetwork()
    {
        // Why: Owner가 변경되었을 때 즉시 위치 업데이트 
        if (Owner != _previousOwner)
        {
            _previousOwner = Owner;
            if (Owner != PlayerRef.None)
            {
                UpdateAttachment();

                // Why: 소유자가 있으면 Rigidbody 비활성화 (플레이어 따라다님)
                if (_rigidbody != null)
                {
                    _rigidbody.isKinematic = true;
                    _rigidbody.useGravity = false;
                }

                // Why: 소유자가 있으면 Collider 비활성화 (픽업 불가)
                if (_collider != null)
                {
                    _collider.enabled = false;
                }
            }
            else if (HasStateAuthority)
            {
                // Why: 드랍 시 현재 위치를 기준점으로 설정
                DroppedPosition = transform.position;

                // Why: Rigidbody 활성화 (물리 적용)
                if (_rigidbody != null)
                {
                    _rigidbody.isKinematic = false;
                    _rigidbody.useGravity = true;
                }

                // Why: Collider를 물리 충돌 가능하도록 설정
                if (_collider != null)
                {
                    _collider.enabled = true;
                    _collider.isTrigger = false; // 바닥과 충돌해야 함
                }

                // Why: 드랍 시 착지 상태 초기화
                _hasLanded = false;

                // Why: 드랍 이펙트 스폰
                SpawnDropEffect();
            }
        }

        // Why: 상태에 따라 Collider 설정
        if (_collider != null)
        {
            if (Owner != PlayerRef.None)
            {
                // Why: 소유된 상태 - Collider 비활성화
                _collider.enabled = false;
            }
            else
            {
                // Why: 드랍된 상태 - Collider 활성화
                _collider.enabled = true;

                // Why: 떨어지는 중에는 바닥과 충돌해야 함 (isTrigger = false)
                //      착지 후에는 픽업 가능해야 함 (isTrigger = true)
                _collider.isTrigger = _hasLanded;
            }
        }
    }

    public override void Render()
    {
        // Why: Render()는 모든 클라이언트에서 매 프레임 실행됨
        if (Owner != PlayerRef.None)
        {
            UpdateAttachment();

            // Why: 픽업되면 드랍 이펙트 제거
            if (_dropEffectInstance != null)
            {
                Destroy(_dropEffectInstance);
                _dropEffectInstance = null;
            }

            _hasLanded = false;
        }
        else
        {
            // Why: Owner가 None이면 부모 해제 (드랍된 상태)
            if (transform.parent != null)
            {
                transform.SetParent(null);
                _currentAttachPoint = null;
                Debug.Log($"[NetworkedItem] Owner가 None으로 변경됨, 부모 해제 (HasStateAuthority: {HasStateAuthority})");
            }

            // Why: 드랍 상태일 때 드랍 이펙트가 없으면 생성
            if (_dropEffectInstance == null && _itemData != null && _itemData.DropEffectPrefab != null)
            {
                _dropEffectInstance = Instantiate(_itemData.DropEffectPrefab, transform);
                _dropEffectInstance.transform.localPosition = Vector3.zero;
                Debug.Log($"[NetworkedItem] Render에서 드랍 이펙트 생성: {_itemData.ItemName}");
            }

            // Why: 아이템이 착지한 후에만 떠다니기 효과 적용
            CheckLanding();
            if (_hasLanded)
            {
                UpdateDroppedEffects();
            }
        }
    }

    #endregion

    #region Attachment

    /// <summary>
    /// 소유자 상태에 따라 아이템을 부착하거나 월드에 배치합니다.
    /// </summary>
    private void UpdateAttachment()
    {
        if (Owner == PlayerRef.None)
        {
            // Why: 소유자가 없으면 월드에 배치 (이미 배치되어 있으면 변경 없음)
            _currentAttachPoint = null;
            return;
        }

        // Why: 소유자가 있으면 플레이어의 AttachPoint에 부착
        Transform attachPoint = GetPlayerAttachPoint(Owner);

        if (attachPoint == null)
        {
            _currentAttachPoint = null;
            return;
        }

        // Why: 아직 부착되지 않았으면 부착
        if (_currentAttachPoint != attachPoint)
        {
            transform.SetParent(attachPoint, true);
            transform.localPosition = new Vector3(0, 0, 0);
            transform.localRotation = Quaternion.Euler(0, 0, 0);

            _currentAttachPoint = attachPoint;
        }
        else
        {
            // Why: 이미 부착되어 있으면 로컬 위치만 고정 (네트워크 동기화로 인한 떨림 방지)
            if (transform.parent != attachPoint)
            {
                transform.SetParent(attachPoint, true);
            }

            transform.localPosition = new Vector3(0, 0, 0);
            transform.localRotation = Quaternion.Euler(0, 0, 0);
        }
    }

    /// <summary>
    /// 플레이어의 아이템 부착 지점을 찾습니다.
    /// </summary>
    private Transform GetPlayerAttachPoint(PlayerRef player)
    {
        if (Runner == null)
        {
            Debug.LogError("[NetworkedItem] Runner가 null입니다!");
            return null;
        }

        NetworkObject playerObj = Runner.GetPlayerObject(player);
        if (playerObj == null)
        {
            Debug.LogError($"[NetworkedItem] Player {player}의 NetworkObject를 찾을 수 없습니다! (HasStateAuthority: {HasStateAuthority})");
            return null;
        }

        // Why: NetworkedWeapon의 WeaponAttachPoint 사용
        NetworkedWeapon weapon = playerObj.GetComponent<NetworkedWeapon>();
        if (weapon == null)
        {
            Debug.LogError($"[NetworkedItem] Player {playerObj.name}에 NetworkedWeapon이 없습니다!");
            return playerObj.transform;
        }

        if (weapon.WeaponAttachPoint != null)
        {
            return weapon.WeaponAttachPoint;
        }

        // Why: AttachPoint를 찾지 못하면 플레이어 자체에 부착
        Debug.LogWarning($"[NetworkedItem] Player {player}에 WeaponAttachPoint가 없습니다! 플레이어에 직접 부착합니다.");
        return playerObj.transform;
    }

    #endregion

    #region Dropped Effects

    /// <summary>
    /// 바닥과 충돌했을 때 호출됩니다.
    /// </summary>
    private void OnCollisionEnter(Collision collision)
    {
        // Why: 드롭된 상태에서만 착지 처리
        if (_hasLanded || Owner != PlayerRef.None || !HasStateAuthority)
            return;

        // Why: 바닥과 충돌 감지 (아래쪽 충돌만 착지로 인정)
        foreach (ContactPoint contact in collision.contacts)
        {
            // Why: 충돌 노말 벡터가 위쪽을 향하면 바닥과 충돌
            if (contact.normal.y > 0.5f)
            {
                _hasLanded = true;
                // Why: 충돌 지점에서 0.5유닛 위로 DroppedPosition 설정
                DroppedPosition = contact.point + Vector3.up * 0.5f;

                // Why: 즉시 DroppedPosition으로 이동
                transform.position = DroppedPosition;

                // Why: 착지 후 Rigidbody 완전히 비활성화
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.isKinematic = true;
                _rigidbody.useGravity = false;

                // Why: 클라이언트에도 착지 알림
                RPC_NotifyLanded(DroppedPosition);

                Debug.Log($"[NetworkedItem] 바닥 충돌 착지! Contact Y: {contact.point.y}, DroppedPosition Y: {DroppedPosition.y}");
                break;
            }
        }
    }

    /// <summary>
    /// 아이템이 착지했는지 확인합니다.
    /// </summary>
    private void CheckLanding()
    {
        // Why: OnCollisionEnter에서 처리하므로 빈 메서드로 유지
    }

    /// <summary>
    /// 드랍된 상태에서 떠다니기 + 회전 효과를 적용합니다.
    /// </summary>
    private void UpdateDroppedEffects()
    {
        // Why: 떠다니기 효과 (Sine 파형)
        _bobbingTime += Time.deltaTime * _bobbingSpeed;
        float yOffset = Mathf.Sin(_bobbingTime) * _bobbingHeight;

        // Why: 기준점에서 Y축 오프셋 적용
        Vector3 targetPosition = DroppedPosition + Vector3.up * yOffset;
        transform.position = targetPosition;

        // Why: Y축 회전 효과
        transform.Rotate(Vector3.up, _rotationSpeed * Time.deltaTime);
    }

    /// <summary>
    /// 드랍 이펙트를 스폰합니다.
    /// </summary>
    private void SpawnDropEffect()
    {
        if (_itemData == null)
        {
            Debug.LogWarning("[NetworkedItem] SpawnDropEffect - _itemData가 null입니다!");
            return;
        }

        if (_itemData.DropEffectPrefab == null)
        {
            Debug.LogWarning($"[NetworkedItem] SpawnDropEffect - {_itemData.ItemName}의 DropEffectPrefab이 null입니다!");
            return;
        }

        Debug.Log($"[NetworkedItem] 드랍 이펙트 스폰 시도: {_itemData.ItemName}");

        // Why: RPC로 모든 클라이언트에 이펙트 표시
        RPC_SpawnDropEffect(transform.position);
    }

    /// <summary>
    /// 서버 → 모든 클라이언트: 드랍 이펙트 생성
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnDropEffect(Vector3 position, RpcInfo info = default)
    {
        if (_itemData == null || _itemData.DropEffectPrefab == null)
        {
            Debug.LogWarning($"[NetworkedItem] RPC_SpawnDropEffect - 이펙트 생성 실패 (HasStateAuthority: {HasStateAuthority})");
            return;
        }

        // Why: 기존 이펙트가 있으면 먼저 제거
        if (_dropEffectInstance != null)
        {
            Destroy(_dropEffectInstance);
        }

        Debug.Log($"[NetworkedItem] RPC_SpawnDropEffect 실행 - 위치: {position} (HasStateAuthority: {HasStateAuthority})");

        // Why: 아이템의 자식으로 생성 (아이템과 함께 이동하고 픽업 시 함께 사라짐)
        _dropEffectInstance = Instantiate(_itemData.DropEffectPrefab, transform);
        _dropEffectInstance.transform.localPosition = Vector3.zero;
    }

    #endregion

    #region RPC Methods

    /// <summary>
    /// 서버 → 모든 클라이언트: 착지 알림
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyLanded(Vector3 landedPosition, RpcInfo info = default)
    {
        _hasLanded = true;
        DroppedPosition = landedPosition;

        if (_rigidbody != null)
        {
            // Why: 이미 kinematic이면 velocity를 설정할 수 없으므로 먼저 확인
            if (!_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
            }
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
        }

        Debug.Log($"[NetworkedItem] RPC_NotifyLanded - 위치: {landedPosition}");
    }

    /// <summary>
    /// 클라이언트 → 서버: 아이템 픽업 요청
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestPickup(PlayerRef player, RpcInfo info = default)
    {
        if (!HasStateAuthority || Owner != PlayerRef.None)
            return;

        // PlayerInventory 찾기
        PlayerInventory inventory = GetPlayerInventory(player);
        if (inventory == null)
        {
            Debug.LogWarning($"[NetworkedItem] PlayerRef {player}의 PlayerInventory를 찾을 수 없습니다!");
            return;
        }

        // 인벤토리에 추가 시도
        if (inventory.TryAddItem(this))
        {
            Debug.Log($"[NetworkedItem] Player {player}가 {_itemData.ItemName} 픽업");
            // Why: Owner는 PlayerInventory에서 설정함
        }
        else
        {
            Debug.Log($"[NetworkedItem] Player {player} 인벤토리가 가득 차거나 이미 소지중인 아이템");
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// PlayerRef로부터 PlayerInventory를 찾습니다.
    /// </summary>
    private PlayerInventory GetPlayerInventory(PlayerRef player)
    {
        // Why: Runner에서 해당 플레이어의 NetworkObject 찾기
        foreach (var obj in Runner.ActivePlayers)
        {
            if (obj == player)
            {
                NetworkObject playerNetObj = Runner.GetPlayerObject(player);
                if (playerNetObj != null)
                {
                    return playerNetObj.GetComponent<PlayerInventory>();
                }
            }
        }

        return null;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// ItemData를 반환합니다.
    /// </summary>
    public ItemData GetItemData() => _itemData;

    #endregion
}
