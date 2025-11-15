using Fusion;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 공격받으면 열리고 아이템을 드랍하는 LootBox (서버 권한)
/// </summary>
public class LootBox : NetworkBehaviour, IDamageable
{
    #region Serialized Fields

    [Header("체력 설정")]
    [Tooltip("LootBox의 최대 체력")]
    [SerializeField] private float _maxHealth = 50f;

    [Header("아이템 설정")]
    [Tooltip("최소 드랍 개수")]
    [SerializeField] private int _minItemCount = 1;

    [Tooltip("최대 드랍 개수")]
    [SerializeField] private int _maxItemCount = 3;

    [Tooltip("앞으로 날아가는 속도")]
    [SerializeField] private float _forwardSpeed = 3f;

    [Tooltip("위로 튀어오르는 속도")]
    [SerializeField] private float _upwardSpeed = 5f;

    [Tooltip("아이템이 퍼지는 범위 (반지름)")]
    [SerializeField] private float _spreadRadius = .5f;

    [Header("초기화 설정")]
    [Tooltip("LootBox가 다시 사용 가능해지는 시간 (초)")]
    private float _resetTime = 30f;

    #endregion

    #region Private Fields

    private Animator _animator;
    private Collider _collider;

    #endregion

    #region Networked Properties

    [Networked]
    public float Health { get; set; }

    [Networked]
    public NetworkBool IsOpened { get; set; }

    [Networked]
    private TickTimer ResetTimer { get; set; }

    #endregion

    #region Properties

    public bool IsAlive => Health > 0 && !IsOpened;

    /// <summary>
    /// 리셋 시간을 설정합니다 (LootBoxSpawnManager에서 호출)
    /// </summary>
    public void SetResetTime(float time)
    {
        _resetTime = time;
    }

    #endregion

    #region Animation Hashes

    private static readonly int ANIM_HIT = Animator.StringToHash("Hit");
    private static readonly int ANIM_IS_OPEN = Animator.StringToHash("isOpen");

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        // Why: 컴포넌트 참조 가져오기
        _animator = GetComponent<Animator>();
        _collider = GetComponent<Collider>();

        if (HasStateAuthority)
        {
            Health = _maxHealth;
            IsOpened = false;
            Debug.Log($"[LootBox] Spawned - Health: {Health}, IsOpened: {IsOpened}");
        }

        // Why: 초기 상태는 닫힌 상태 (Collider 활성화, isOpen = false)
        if (_collider != null)
        {
            _collider.enabled = !IsOpened;
        }

        if (_animator != null)
        {
            _animator.SetBool(ANIM_IS_OPEN, IsOpened);
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Why: 서버만 초기화 타이머 체크
        if (!HasStateAuthority) return;

        // Why: 초기화 타이머가 만료되면 박스 리셋
        if (ResetTimer.Expired(Runner))
        {
            ResetBox();
        }
    }

    #endregion

    #region IDamageable Implementation

    public void TakeDamage(float damage, PlayerRef attacker)
    {
        if (!HasStateAuthority || IsOpened)
            return;

        Health -= damage;
        RPC_PlayHitAnimation();

        Debug.Log($"[LootBox] 데미지 {damage} 받음, 남은 체력: {Health}");

        if (Health <= 0)
        {
            OpenBox();
        }
    }

    #endregion

    #region Box Opening

    private void OpenBox()
    {
        if (!HasStateAuthority || IsOpened)
            return;

        IsOpened = true;
        Debug.Log("[LootBox] 박스 열림!");

        // Why: Collider 비활성화 (더 이상 공격받지 않음)
        RPC_SetColliderState(false);

        // Why: 애니메이션 상태 변경 (isOpen = true)
        RPC_PlayOpenAnimation();

        SpawnLoot();

        // Why: 초기화 타이머 시작
        ResetTimer = TickTimer.CreateFromSeconds(Runner, _resetTime);
        Debug.Log($"[LootBox] {_resetTime}초 후 초기화 예정");
    }

    private void ResetBox()
    {
        if (!HasStateAuthority || !IsOpened)
            return;

        // Why: 박스 상태 초기화
        Health = _maxHealth;
        IsOpened = false;

        Debug.Log("[LootBox] 박스 초기화 완료!");

        // Why: Collider 다시 활성화 (공격 가능)
        RPC_SetColliderState(true);

        // Why: 모든 클라이언트에 Idle 애니메이션 재생 (isOpen = false)
        RPC_PlayIdleAnimation();
    }

    private void SpawnLoot()
    {
        if (!HasStateAuthority)
            return;

        // Why: LootBox에서 드랍 가능한 아이템만 가져오기 (기본 무기 제외)
        List<ItemData> itemList = ItemDatabase.GetLootableItems();
        if (itemList == null || itemList.Count == 0)
        {
            Debug.LogWarning("[LootBox] 드랍 가능한 아이템이 없습니다!");
            return;
        }

        // Why: 최소~최대 개수 사이의 랜덤 개수
        int spawnCount = Random.Range(_minItemCount, _maxItemCount + 1);

        // Why: 스폰 위치 (박스 위쪽)
        Vector3 spawnCenter = transform.position + Vector3.up * 1f;

        // Why: 박스의 정면 방향
        Vector3 forwardDir = transform.forward;

        for (int i = 0; i < spawnCount; i++)
        {
            ItemData randomItem = itemList[Random.Range(0, itemList.Count)];

            if (randomItem == null || randomItem.ModelPrefab == null)
                continue;

            // Why: 앞쪽 90도 범위로만 퍼지도록 (-45도 ~ +45도)
            float spreadAngle = Random.Range(-45f, 45f);
            Vector3 launchDirection = Quaternion.Euler(0, spreadAngle, 0) * forwardDir;

            // Why: 포물선 궤적을 위한 초기 속도 (앞으로 + 위로)
            Vector3 initialVelocity = launchDirection * _forwardSpeed + Vector3.up * _upwardSpeed;

            // Why: onBeforeSpawned 콜백에서 ItemID와 InitialVelocity 설정
            NetworkObject itemObj = Runner.Spawn(
                randomItem.ModelPrefab,
                spawnCenter,
                Quaternion.Euler(0, -90, 0),
                onBeforeSpawned: (runner, obj) =>
                {
                    NetworkedItem networkedItem = obj.GetComponent<NetworkedItem>();
                    if (networkedItem != null)
                    {
                        networkedItem.ItemID = randomItem.ItemID;
                        networkedItem.InitialVelocity = initialVelocity;
                    }
                }
            );

            if (itemObj != null)
            {
                Debug.Log($"[LootBox] 아이템 스폰: {randomItem.ItemName}, InitialVelocity: {initialVelocity}");
            }
        }
    }

    #endregion

    #region RPC Methods

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayHitAnimation(RpcInfo info = default)
    {
        if (_animator != null)
        {
            _animator.SetTrigger(ANIM_HIT);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayOpenAnimation(RpcInfo info = default)
    {
        if (_animator != null)
        {
            // Why: isOpen = true로 설정 → Open 애니메이션으로 전환
            _animator.SetBool(ANIM_IS_OPEN, true);

            Debug.Log("[LootBox] RPC_PlayOpenAnimation - Open 상태로 전환 (isOpen = true)");
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayIdleAnimation(RpcInfo info = default)
    {
        if (_animator != null)
        {
            // Why: Hit Trigger 리셋
            _animator.ResetTrigger(ANIM_HIT);
            // Why: isOpen = false로 설정 → Idle 애니메이션으로 복귀
            _animator.SetBool(ANIM_IS_OPEN, false);

            Debug.Log("[LootBox] RPC_PlayIdleAnimation - Idle 상태로 전환 (isOpen = false)");
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SetColliderState(bool enabled, RpcInfo info = default)
    {
        if (_collider != null)
        {
            _collider.enabled = enabled;
            Debug.Log($"[LootBox] RPC_SetColliderState - Collider.enabled = {enabled}");
        }
    }

    #endregion
}
