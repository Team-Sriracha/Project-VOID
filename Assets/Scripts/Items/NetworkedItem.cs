using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using FishNet.Component.Transforming;
using UnityEngine;

/// <summary>
/// 월드 드랍 아이템 (서버 권한)
/// </summary>
public class NetworkedItem : NetworkBehaviour
{
    #region SyncVars

    public readonly SyncVar<string> ItemID = new();

    /// <summary>
    /// 아이템 소유자 (null이면 월드에 드랍된 상태)
    /// </summary>
    public readonly SyncVar<NetworkConnection> OwnerConnection = new();

    /// <summary>
    /// 드랍 상태 여부 (클라이언트 동기화를 위한 명시적 플래그)
    /// </summary>
    public readonly SyncVar<bool> IsDropped = new(new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.Observers));

    /// <summary>
    /// 총기 아이템의 현재 탄약 (드랍/픽업 간 유지용)
    /// </summary>
    public readonly SyncVar<int> CurrentAmmo = new();

    /// <summary>
    /// 총기 아이템의 보유 탄약 (드랍/픽업 간 유지용)
    /// </summary>
    public readonly SyncVar<int> TotalAmmo = new();

    /// <summary>
    /// 드랍된 위치 (떠다니기 기준점)
    /// </summary>
    public readonly SyncVar<Vector3> DroppedPosition = new();

    /// <summary>
    /// 현재 속도 (서버에서 수동 물리 계산용)
    /// </summary>
    public readonly SyncVar<Vector3> CurrentVelocity = new();

    /// <summary>
    /// 착지 여부 (클라이언트 동기화용)
    /// </summary>
    public readonly SyncVar<bool> HasLanded = new();

    /// <summary>
    /// 아이템 등급 (드랍 시 서버에서 랜덤 결정)
    /// </summary>
    public readonly SyncVar<ItemTier> Tier = new(ItemTier.Normal);

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
    [SerializeField] private LayerMask _groundLayerMask = 1;

    #endregion

    #region Private Fields

    private ItemData _itemData;
    private Collider _collider;
    private NetworkConnection _previousOwner;
    private float _bobbingTime;
    private GameObject _dropEffectInstance;
    private Rigidbody _rigidbody;
    private Transform _currentAttachPoint;
    
    /// <summary>
    /// LootBox에서 스폰 시 설정되는 부모 Transform (같은 씬에 배치)
    /// </summary>
    [HideInInspector]
    public Transform TargetParent;

    private NetworkTransform _networkTransform;

    // 클라이언트 물리 보간용
    private bool _clientSimulating = false;
    private Vector3 _clientVelocity;
    private Vector3 _interpolateFrom;
    private float _interpolateProgress = 1f;
    private const float INTERPOLATE_DURATION = 0.2f;

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartServer()
    {
        base.OnStartServer();
        
        InitializeItem();
        
        TimeManager.OnTick += OnTick;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        
        // Tier 변경 콜백 구독 (클라이언트에서 등급 동기화 시 이펙트 갱신)
        Tier.OnChange += OnTierChanged;
        
        // 착지 상태 변경 콜백 구독 (클라이언트 보간용)
        HasLanded.OnChange += OnHasLandedChanged;
        
        // 클라이언트 물리 시뮬레이션 시작
        if (!IsServerInitialized && IsDropped.Value && !HasLanded.Value)
        {
            _clientSimulating = true;
            _clientVelocity = CurrentVelocity.Value;
        }
        
        InitializeItem();
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        
        if (TimeManager != null)
        {
            TimeManager.OnTick -= OnTick;
        }
    }

    public override void OnStopClient()
    {
        base.OnStopClient();
        
        Tier.OnChange -= OnTierChanged;
        HasLanded.OnChange -= OnHasLandedChanged;
    }

    /// <summary>
    /// Tier 변경 콜백 (클라이언트 동기화 시)
    /// </summary>
    private void OnTierChanged(ItemTier prev, ItemTier next, bool asServer)
    {
        // 클라이언트에서 등급이 변경되면 이펙트 재생성
        if (!asServer && IsDropped.Value)
        {
            RecreateDropEffect();
        }
    }

    /// <summary>
    /// 착지 상태 변경 콜백 (클라이언트 보간용)
    /// </summary>
    private void OnHasLandedChanged(bool prev, bool next, bool asServer)
    {
        // 서버에서는 무시
        if (asServer) return;
        
        // 착지 상태로 변경되면 부드러운 보간 시작
        if (next && !prev)
        {
            _clientSimulating = false;
            _interpolateFrom = transform.position;
            _interpolateProgress = 0f;
        }
    }

    /// <summary>
    /// 드랍 이펙트를 재생성합니다.
    /// </summary>
    private void RecreateDropEffect()
    {
        // 기존 이펙트 제거
        if (_dropEffectInstance != null)
        {
            Destroy(_dropEffectInstance);
            _dropEffectInstance = null;
        }
        
        // 새 이펙트 생성
        EnsureDropEffect();
    }

    private void InitializeItem()
    {
        // ItemID로 ItemData 로드
        _itemData = ItemDatabase.GetItem(ItemID.Value);

        if (_itemData == null)
        {
            Debug.LogError($"[NetworkedItem] ItemID '{ItemID.Value}'에 해당하는 ItemData를 찾을 수 없습니다!");
            return;
        }

        // Why: Rigidbody 가져오기 (항상 kinematic으로 설정)
        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = true;
            _rigidbody.useGravity = false;
        }

        // NetworkTransform 캐싱
        _networkTransform = GetComponent<NetworkTransform>();

        // Collider는 이미 프리팹에 설정됨
        _collider = GetComponent<Collider>();

        // Owner 설정 시 즉시 위치 설정
        _previousOwner = OwnerConnection.Value;
        if (OwnerConnection.Value != null)
        {
            if (_collider != null)
            {
                _collider.enabled = false;
            }
        }
        else
        {
            // 드랍된 상태로 스폰
            if (_collider != null)
            {
                _collider.enabled = true;
                _collider.isTrigger = true;
            }

            // 서버에서 초기 상태 설정
            if (IsServerInitialized)
            {
                // 드랍 상태로 시작
                IsDropped.Value = true;
                
                // 총기 아이템 탄약 초기화
                if (_itemData is GunData gunData)
                {
                    if (CurrentAmmo.Value <= 0) CurrentAmmo.Value = gunData.MagazineSize;
                    if (TotalAmmo.Value <= 0) TotalAmmo.Value = gunData.MaxAmmo;
                }

                if (CurrentVelocity.Value == Vector3.zero)
                {
                    // 초기 속도 없으면 즉시 착지 상태
                    HasLanded.Value = true;
                    DroppedPosition.Value = transform.position;
                }
            }
        }
        
        UpdateNetworkTransformState();
    }

    private void OnTick()
    {
        if (!IsServerInitialized) return;

        // Owner 변경 처리
        if (OwnerConnection.Value != _previousOwner)
        {
            _previousOwner = OwnerConnection.Value;
            
            if (OwnerConnection.Value != null)
            {
                // 픽업됨
                IsDropped.Value = false;
                HasLanded.Value = false;
                
                if (_collider != null)
                {
                    _collider.enabled = false;
                }
            }
            else
            {
                // 드랍됨
                IsDropped.Value = true;
                CurrentVelocity.Value = Vector3.zero;
                DroppedPosition.Value = transform.position;
                HasLanded.Value = true;

                if (_collider != null)
                {
                    _collider.enabled = true;
                    _collider.isTrigger = true;
                }
            }
            
            UpdateNetworkTransformState();
        }

        // 서버 전용 물리 계산 (미착지 시)
        if (IsDropped.Value && !HasLanded.Value)
        {
            SimulatePhysics();
        }

        // 상태 기반 Collider 설정
        if (_collider != null)
        {
            _collider.enabled = IsDropped.Value;
            if (_collider.enabled) _collider.isTrigger = true;
        }
    }

    private void Update()
    {
        // Render() 역할 - 모든 클라이언트 매 프레임 실행
        // IsDropped SyncVar로 드랍 상태 판단 (NetworkConnection보다 안정적)
        bool isEquipped = !IsDropped.Value;
        

        
        if (isEquipped)
        {
            // 픽업된 상태 - 플레이어에게 부착
            UpdateAttachment();

            // 드랍 이펙트 비활성화 (Destroy 대신)
            if (_dropEffectInstance != null && _dropEffectInstance.activeSelf)
            {
                _dropEffectInstance.SetActive(false);
            }
        }
        else
        {
            // 드랍된 상태 - 부모 해제
            if (transform.parent != null)
            {
                transform.SetParent(null);
            }
            
            if (_currentAttachPoint != null)
            {
                _currentAttachPoint = null;
            }

            // 드랍 이펙트 활성화 또는 생성
            EnsureDropEffect();

            // 클라이언트 물리 시뮬레이션 (서버가 아닌 경우)
            if (!IsServerInitialized)
            {
                SimulateClientPhysics();
            }

            // 착지 완료 + 보간 완료 상태면 떠다니기 효과 적용
            if (HasLanded.Value && DroppedPosition.Value != Vector3.zero && _interpolateProgress >= 1f)
            {
                UpdateDroppedEffects();
            }
        }
    }

    /// <summary>
    /// 드랍 이펙트를 생성하거나 활성화합니다.
    /// </summary>
    private void EnsureDropEffect()
    {
        // 이미 존재하면 활성화만
        if (_dropEffectInstance != null)
        {
            if (!_dropEffectInstance.activeSelf)
            {
                _dropEffectInstance.SetActive(true);
            }
            return;
        }
        
        // 없으면 생성
        if (_itemData == null)
        {
            _itemData = ItemDatabase.GetItem(ItemID.Value);
        }
        
        // 등급별 이펙트 사용 (ItemTierConfig에서 관리)
        GameObject effectPrefab = GetTierDropEffectPrefab();
        if (effectPrefab != null)
        {
            _dropEffectInstance = Instantiate(effectPrefab, transform);
            _dropEffectInstance.transform.localPosition = Vector3.zero;
        }
    }

    /// <summary>
    /// 등급에 해당하는 드랍 이펙트 프리팹을 반환합니다.
    /// </summary>
    private GameObject GetTierDropEffectPrefab()
    {
        var config = ItemTierConfig.Instance;
        if (config == null) return null;
        
        var settings = config.GetSettings(Tier.Value);
        return settings.DropEffectPrefab;
    }

    #endregion

    #region Attachment

    private void UpdateAttachment()
    {
        if (OwnerConnection.Value == null)
        {
            _currentAttachPoint = null;
            return;
        }

        Transform attachPoint = GetPlayerAttachPoint(OwnerConnection.Value);

        if (attachPoint == null)
        {
            // attachPoint를 찾지 못하면 DroppedPosition으로 폴백
            _currentAttachPoint = null;
            if (transform.parent != null)
            {
                transform.SetParent(null);
            }
            if (DroppedPosition.Value != Vector3.zero)
            {
                transform.position = DroppedPosition.Value;
            }
            return;
        }

        if (_currentAttachPoint != attachPoint)
        {
            transform.SetParent(attachPoint, true);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            _currentAttachPoint = attachPoint;

            // 부착 시 해당 컴포넌트에 알림
            NotifyAttachment();
        }
        else
        {
            if (transform.parent != attachPoint)
            {
                transform.SetParent(attachPoint, true);
            }

            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
    }

    private Transform GetPlayerAttachPoint(NetworkConnection conn)
    {
        // 드랍 과정에서 Connection/FirstObject null 가능 (정상)
        if (conn == null || conn.FirstObject == null)
        {
            return null;
        }

        NetworkObject playerObj = conn.FirstObject;

        // 아이템 타입에 따라 AttachPoint 결정
        if (_itemData is ArmorItemData)
        {
            NetworkedArmor armor = playerObj.GetComponent<NetworkedArmor>();
            if (armor != null && armor.ArmorAttachPoint != null)
            {
                return armor.ArmorAttachPoint;
            }
            Debug.LogWarning($"[NetworkedItem] Player에 ArmorAttachPoint가 없습니다!");
            return playerObj.transform;
        }

        // 무기 타입 (기본)
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

        Debug.LogWarning($"[NetworkedItem] Player에 WeaponAttachPoint가 없습니다!");
        return playerObj.transform;
    }

    /// <summary>
    /// 부착 시 해당 컴포넌트에 알림
    /// </summary>
    private void NotifyAttachment()
    {
        if (OwnerConnection.Value == null || OwnerConnection.Value.FirstObject == null)
            return;

        var playerObj = OwnerConnection.Value.FirstObject;

        if (_itemData is ArmorItemData)
        {
            var armor = playerObj.GetComponent<NetworkedArmor>();
            armor?.OnArmorAttached(this);
        }
        else if (_itemData is WeaponData)
        {
            var weapon = playerObj.GetComponent<NetworkedWeapon>();
            weapon?.OnWeaponAttached(this);
        }
    }

    #endregion

    #region Physics Simulation

    private void SimulatePhysics()
    {
        if (!IsServerInitialized) return;

        float deltaTime = (float)TimeManager.TickDelta;

        // 중력 적용
        CurrentVelocity.Value += Vector3.down * _gravity * deltaTime;

        Vector3 newPosition = transform.position + CurrentVelocity.Value * deltaTime;

        // 바닥 충돌 감지
        if (CheckGroundCollision(newPosition, out Vector3 hitPoint))
        {
            HasLanded.Value = true;
            DroppedPosition.Value = hitPoint + Vector3.up * 0.5f;
            transform.position = DroppedPosition.Value;
            CurrentVelocity.Value = Vector3.zero;

            Debug.Log($"[NetworkedItem] 착지! 위치: {DroppedPosition.Value}");
        }
        else
        {
            transform.position = newPosition;
        }
    }

    private bool CheckGroundCollision(Vector3 position, out Vector3 hitPoint)
    {
        hitPoint = position;
        float rayDistance = _groundCheckDistance + 0.5f;

        if (Physics.Raycast(position, Vector3.down, out RaycastHit hit, rayDistance, _groundLayerMask))
        {
            hitPoint = hit.point;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 클라이언트 측 물리 시뮬레이션 (서버 물리와 동일한 로직)
    /// 서버에서 HasLanded=true가 오면 보간으로 전환
    /// </summary>
    private void SimulateClientPhysics()
    {
        // 보간 중이면 보간 처리
        if (_interpolateProgress < 1f)
        {
            _interpolateProgress += Time.deltaTime / INTERPOLATE_DURATION;
            _interpolateProgress = Mathf.Clamp01(_interpolateProgress);
            
            Vector3 targetPos = DroppedPosition.Value + Vector3.up * 0.5f;
            transform.position = Vector3.Lerp(_interpolateFrom, targetPos, _interpolateProgress);
            return;
        }
        
        // 클라이언트 시뮬레이션 중이면 물리 계산
        if (_clientSimulating && !HasLanded.Value)
        {
            // 중력 적용
            _clientVelocity += Vector3.down * _gravity * Time.deltaTime;
            transform.position += _clientVelocity * Time.deltaTime;
        }
    }

    #endregion

    #region Dropped Effects

    private void UpdateDroppedEffects()
    {
        _bobbingTime += Time.deltaTime * _bobbingSpeed;
        float yOffset = Mathf.Sin(_bobbingTime) * _bobbingHeight;

        Vector3 targetPosition = DroppedPosition.Value + Vector3.up * yOffset;
        transform.position = targetPosition;

        transform.Rotate(Vector3.up, _rotationSpeed * Time.deltaTime);
    }

    #endregion

    #region RPC Methods

    [ServerRpc(RequireOwnership = false)]
    public void RPC_RequestPickup(NetworkConnection conn = null)
    {
        if (!IsServerInitialized || OwnerConnection.Value != null)
            return;

        if (conn == null || conn.FirstObject == null)
        {
            Debug.LogWarning($"[NetworkedItem] Connection의 FirstObject를 찾을 수 없습니다!");
            return;
        }

        PlayerInventory inventory = conn.FirstObject.GetComponent<PlayerInventory>();
        if (inventory == null)
        {
            Debug.LogWarning($"[NetworkedItem] PlayerInventory를 찾을 수 없습니다!");
            return;
        }

        if (inventory.TryAddItem(this))
        {
            Debug.Log($"[NetworkedItem] Player가 {_itemData.ItemName} 픽업");
        }
        else
        {
            Debug.Log($"[NetworkedItem] Player 인벤토리가 가득 차거나 이미 소지중인 아이템");
        }
    }

    #endregion

    #region Public Methods

    public ItemData GetItemData() => _itemData;

    /// <summary>
    /// 등급 배율이 적용된 최종 데미지를 반환합니다. (반올림)
    /// </summary>
    /// <returns>최종 데미지 (무기가 아니면 0)</returns>
    public float GetFinalDamage()
    {
        if (_itemData is WeaponData weapon)
        {
            var config = ItemTierConfig.Instance;
            float multiplier = config?.GetSettings(Tier.Value).DamageMultiplier ?? 1f;
            return Mathf.Round(weapon.Damage * multiplier);
        }
        return 0f;
    }

    /// <summary>
    /// 등급 배율이 적용된 최종 방어력을 반환합니다. (반올림)
    /// </summary>
    /// <returns>최종 방어력 (방어구가 아니면 0)</returns>
    public float GetFinalDefense()
    {
        if (_itemData is ArmorItemData armor)
        {
            var config = ItemTierConfig.Instance;
            float multiplier = config?.GetSettings(Tier.Value).DefenseMultiplier ?? 1f;
            return Mathf.Round(armor.Defense * multiplier);
        }
        return 0f;
    }

    private void UpdateNetworkTransformState()
    {
        if (_networkTransform != null)
        {
            // 드랍 상태일 때만 NetworkTransform 활성화
            // 장착 시에는 부모(Player)에 따라가므로 비활성화하여 충돌 방지
            bool shouldEnable = IsDropped.Value;
            if (_networkTransform.enabled != shouldEnable)
            {
                _networkTransform.enabled = shouldEnable;
            }
        }
    }

    #endregion
}
