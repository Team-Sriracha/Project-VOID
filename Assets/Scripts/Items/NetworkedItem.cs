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
    /// 총기 아이템의 현재 탄약 (드랍/픽업 간 유지용)
    /// </summary>
    [Networked]
    public int CurrentAmmo { get; set; }

    /// <summary>
    /// 총기 아이템의 보유 탄약 (드랍/픽업 간 유지용)
    /// </summary>
    [Networked]
    public int TotalAmmo { get; set; }

    /// <summary>
    /// 드랍된 위치 (떠다니기 기준점)
    /// </summary>
    [Networked]
    public Vector3 DroppedPosition { get; set; }

    /// <summary>
    /// 현재 속도 (서버에서 수동 물리 계산용)
    /// </summary>
    [Networked]
    public Vector3 CurrentVelocity { get; set; }

    #endregion

    #region Serialized Fields

    [Header("드랍 효과 설정")]
    [Tooltip("위아래로 떠다니는 속도")]
    [SerializeField] private float _bobbingSpeed = 1f;

    [Tooltip("떠다니는 높이")]
    [SerializeField] private float _bobbingHeight = 0.3f;

    [Tooltip("회전 속도 (도/초)")]
    [SerializeField] private float _rotationSpeed = 30f;

    [Header("물리 설정")]
    [Tooltip("중력 가속도")]
    [SerializeField] private float _gravity = 9.81f;

    [Tooltip("바닥 감지 거리")]
    [SerializeField] private float _groundCheckDistance = 0.1f;
    
    [Tooltip("바닥 감지 레이어 마스크 (예: Ground)")]
    [SerializeField] private LayerMask _groundLayerMask = 1; // Default 레이어

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
    private bool _needsParentSetup = false;
    
    /// <summary>
    /// LootBox에서 스폰 시 설정되는 부모 Transform (같은 씬에 배치)
    /// </summary>
    [HideInInspector]
    public Transform TargetParent;

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

        // Why: 총기 아이템이면 탄약 초기값을 설정하거나 저장된 탄약을 클램프
        if (_itemData is GunData gunData)
        {
            int initCurrent = CurrentAmmo > 0 ? CurrentAmmo : gunData.MagazineSize;
            int initTotal = TotalAmmo > 0 ? TotalAmmo : gunData.MaxAmmo;
            CurrentAmmo = Mathf.Clamp(initCurrent, 0, gunData.MagazineSize);
            TotalAmmo = Mathf.Clamp(initTotal, 0, gunData.MaxAmmo);
        }

        // Why: Rigidbody 가져오기 (항상 kinematic으로 설정)
        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
        }

        // Why: Collider는 이미 프리팹에 설정되어 있음
        _collider = GetComponent<Collider>();

        // Why: Owner가 이미 설정되어 있으면 즉시 위치 설정
        _previousOwner = Owner;
        if (Owner != PlayerRef.None)
        {
            UpdateAttachment();

            // Why: 소유된 상태면 Collider 비활성화
            if (_collider != null)
            {
                _collider.enabled = false;
            }
        }
        else
        {
            // Why: 드랍된 상태로 스폰
            if (_collider != null)
            {
                _collider.enabled = true;
                _collider.isTrigger = true; // Trigger로 픽업 감지
            }

            // Why: Fusion이 스폰 프로세스를 마무리하면 부모를 다시 null로 초기화하므로
            // Render() 첨 프레임에서 부모를 설정하도록 플래그 설정
            _needsParentSetup = true;

            if (HasStateAuthority)
            {
                // Why: 초기 속도가 설정되어 있으면 적용 (LootBox에서 튀어오를 때)
                if (CurrentVelocity != Vector3.zero)
                {
                    Debug.Log($"[NetworkedItem] 초기 속도 적용: {CurrentVelocity}, 현재 위치: {transform.position}");
                }
                else
                {
                    // Why: 초기 속도가 없으면 바로 착지 상태
                    _hasLanded = true;
                    DroppedPosition = transform.position;
                }
            }
        }

        Debug.Log($"[NetworkedItem] Spawned: {_itemData.ItemName}, Position: {transform.position}, Parent: {transform.parent?.name}, HasStateAuthority: {HasStateAuthority}");
    }

    /// <summary>
    /// DroppedItemsParent 또는 [GamePlay] 오브젝트를 찾아 부모로 설정합니다.
    /// </summary>
    private void SetupParent()
    {
        Transform targetParent = TargetParent;
        if (targetParent == null && Runner != null && Runner.SimulationUnityScene.IsValid())
        {
            // Why: 클라이언트에서는 TargetParent가 전달되지 않으므로 Runner 씬에서 찾기
            foreach (GameObject rootObj in Runner.SimulationUnityScene.GetRootGameObjects())
            {
                // Why: DroppedItemsParent 또는 [GamePlay] 오브젝트를 찾기
                if (rootObj.name == "DroppedItemsParent")
                {
                    targetParent = rootObj.transform;
                    break;
                }
                else if (rootObj.name == "[GamePlay]" || rootObj.name == "GamePlay")
                {
                    // Why: [GamePlay] 안에서 DroppedItemsParent 찾기
                    Transform droppedItemsParent = rootObj.transform.Find("DroppedItemsParent");
                    if (droppedItemsParent != null)
                    {
                        targetParent = droppedItemsParent;
                    }
                    else
                    {
                        // Why: DroppedItemsParent가 없으면 [GamePlay] 직접 사용
                        targetParent = rootObj.transform;
                    }
                    break;
                }
            }
        }
        
        if (targetParent != null)
        {
            transform.SetParent(targetParent, true);
            Debug.Log($"[NetworkedItem] {targetParent.name} 하위로 이동: {gameObject.name}");
        }
        else
        {
            Debug.LogWarning($"[NetworkedItem] DroppedItemsParent/[GamePlay]를 찾지 못함: {gameObject.name}");
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Why: Runner가 종료 중이거나 실행 중이 아니면 처리하지 않음
        if (Runner == null || !Runner.IsRunning) return;

        // Why: Owner가 변경되었을 때 즉시 위치 업데이트
        if (Owner != _previousOwner)
        {
            _previousOwner = Owner;
            if (Owner != PlayerRef.None)
            {
                UpdateAttachment();

                // Why: 소유자가 있으면 Collider 비활성화 (픽업 불가)
                if (_collider != null)
                {
                    _collider.enabled = false;
                }
            }
            else if (HasStateAuthority)
            {
                // Why: 플레이어가 드랍한 경우, 현재 위치에서 즉시 착지 처리
                // (LootBox에서 스폰되어 튀어오르는 경우와 다름)
                CurrentVelocity = Vector3.zero;
                
                // Why: 드랍 위치를 현재 transform 위치로 설정하고 즉시 착지
                DroppedPosition = transform.position;
                _hasLanded = true;
                
                // Why: 클라이언트에도 착지 위치 동기화
                RPC_NotifyLanded(DroppedPosition);

                // Why: Collider 활성화
                if (_collider != null)
                {
                    _collider.enabled = true;
                    _collider.isTrigger = true; // Trigger로 픽업 감지
                }

                // Why: 드랍 이펙트 스폰
                SpawnDropEffect();
                
                Debug.Log($"[NetworkedItem] 플레이어가 드랍함 - 위치: {DroppedPosition}");
            }
        }

        // Why: 서버에서만 물리 계산 (kinematic Rigidbody로 수동 계산)
        if (HasStateAuthority && Owner == PlayerRef.None && !_hasLanded)
        {
            SimulatePhysics();
        }

        // Why: 상태에 따라 Collider 설정
        if (_collider != null)
        {
            if (Owner != PlayerRef.None)
            {
                _collider.enabled = false;
            }
            else
            {
                _collider.enabled = true;
                _collider.isTrigger = true; // 항상 Trigger (픽업 감지용)
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
            // Why: Fusion 스폰 후 부모 설정 필요
            if (_needsParentSetup)
            {
                _needsParentSetup = false;
                SetupParent();
            }
            
            // Why: 플레이어 손에서 떨어진 경우 attach point 참조만 해제
            if (_currentAttachPoint != null)
            {
                _currentAttachPoint = null;
            }

            // Why: 드랍 상태일 때 드랍 이펙트가 없으면 생성
            if (_dropEffectInstance == null && _itemData != null && _itemData.DropEffectPrefab != null)
            {
                _dropEffectInstance = Instantiate(_itemData.DropEffectPrefab, transform);
                _dropEffectInstance.transform.localPosition = Vector3.zero;
                Debug.Log($"[NetworkedItem] Render에서 드랍 이펙트 생성: {_itemData.ItemName}");
            }

            // Why: 아이템이 착지한 후에만 떠다니기 효과 적용
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

    #region Physics Simulation

    /// <summary>
    /// 서버에서 수동으로 물리를 시뮬레이션합니다 (kinematic Rigidbody).
    /// </summary>
    private void SimulatePhysics()
    {
        if (!HasStateAuthority) return;

        float deltaTime = Runner.DeltaTime;

        // Why: 중력 적용
        CurrentVelocity += Vector3.down * _gravity * deltaTime;

        // Why: 위치 업데이트
        Vector3 newPosition = transform.position + CurrentVelocity * deltaTime;

        // Why: 바닥 충돌 감지 (Raycast)
        if (CheckGroundCollision(newPosition, out Vector3 hitPoint))
        {
            // Why: 착지 처리
            _hasLanded = true;
            DroppedPosition = hitPoint + Vector3.up * 0.5f; // 바닥에서 0.5유닛 위
            transform.position = DroppedPosition;
            CurrentVelocity = Vector3.zero;

            // Why: 클라이언트에도 착지 알림
            RPC_NotifyLanded(DroppedPosition);

            Debug.Log($"[NetworkedItem] 착지! 위치: {DroppedPosition}");
        }
        else
        {
            // Why: 아직 떨어지는 중
            transform.position = newPosition;
        }
    }

    /// <summary>
/// 바닥 충돌을 감지합니다 (Raycast).
/// </summary>
private bool CheckGroundCollision(Vector3 position, out Vector3 hitPoint)
{
    hitPoint = position;

    // Why: Multi-Peer 환경에서 올바른 Physics 씬에서 Raycast 수행
    RaycastHit hit;
    bool hasHit = false;
    float rayDistance = _groundCheckDistance + 0.5f;

    if (Runner != null && Runner.SceneManager != null && 
        Runner.SceneManager.TryGetPhysicsScene3D(out var physicsScene) && physicsScene.IsValid())
    {
        // Multi-Peer: 해당 Runner의 PhysicsScene에서 Raycast (레이어 마스크 적용)
        hasHit = physicsScene.Raycast(position, Vector3.down, out hit, rayDistance, _groundLayerMask);
    }
    else
    {
        // Fallback: 기본 Physics.Raycast (레이어 마스크 적용)
        hasHit = Physics.Raycast(position, Vector3.down, out hit, rayDistance, _groundLayerMask);
    }

    if (hasHit)
    {
        hitPoint = hit.point;
        return true;
    }

    return false;
}
    #endregion

    #region Dropped Effects

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
        CurrentVelocity = Vector3.zero;

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
        // Why: Runner에서 직접 플레이어 NetworkObject 가져오기
        NetworkObject playerNetObj = Runner.GetPlayerObject(player);
        if (playerNetObj != null)
        {
            return playerNetObj.GetComponent<PlayerInventory>();
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
