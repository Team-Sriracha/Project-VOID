using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 피격 시 개방 및 아이템 드랍 (서버 권한)
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

    [Header("애니메이션 최적화")]
    [Tooltip("Open 애니메이션 길이 (초)")]
    [SerializeField] private float _openAnimationDuration = 1f;

    [Header("오디오 설정")]
    [Tooltip("LootBox가 열릴 때 재생할 사운드")]
    [SerializeField] private AudioCue _openAudioCue;

    [Tooltip("LootBox가 닫힐 때 재생할 사운드")]
    [SerializeField] private AudioCue _closeAudioCue;

    [Header("아이템 풀 설정")]
    [Tooltip("이 LootBox에서 드랍 가능한 아이템 풀")]
    [SerializeField] private LootBoxItemPool _itemPool;

    #endregion

    #region Private Fields

    private Animator _animator;
    private Collider _collider;
    private float _resetEndTime;

    #endregion

    #region SyncVars

    public readonly SyncVar<float> Health = new();
    public readonly SyncVar<bool> IsOpened = new();

    #endregion

    #region Properties

    public bool IsAlive => Health.Value > 0 && !IsOpened.Value;

    public void SetResetTime(float time)
    {
        _resetTime = time;
    }

    #endregion

    #region Animation Hashes

    private static readonly int ANIM_HIT = Animator.StringToHash("Hit");
    private static readonly int ANIM_IS_OPEN = Animator.StringToHash("isOpen");

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartServer()
    {
        base.OnStartServer();
        
        _animator = GetComponent<Animator>();
        _collider = GetComponent<Collider>();

        Health.Value = _maxHealth;
        IsOpened.Value = false;
        
        TimeManager.OnTick += OnTick;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        _animator = GetComponent<Animator>();
        _collider = GetComponent<Collider>();

        if (_collider != null)
        {
            _collider.enabled = !IsOpened.Value;
        }

        if (_animator != null)
        {
            _animator.SetBool(ANIM_IS_OPEN, IsOpened.Value);
        }
    }

    public override void OnStopServer()
    {
        base.OnStopServer();
        
        if (TimeManager != null)
        {
            TimeManager.OnTick -= OnTick;
        }
    }

    private void OnTick()
    {
        if (!IsServerInitialized) return;

        // 초기화 타이머 만료 시 박스 리셋
        if (IsOpened.Value && _resetEndTime > 0 && Time.time >= _resetEndTime)
        {
            ResetBox();
        }
    }

    #endregion

    #region IDamageable Implementation

    public void TakeDamage(float damage, NetworkConnection attacker, Vector3 hitPosition = default)
    {
        if (!IsServerInitialized || IsOpened.Value)
            return;

        Health.Value -= damage;
        RPC_PlayHitAnimation();

        if (Health.Value <= 0)
        {
            OpenBox();
        }
    }

    #endregion

    #region Box Opening

    private void OpenBox()
    {
        if (!IsServerInitialized || IsOpened.Value)
            return;

        IsOpened.Value = true;

        // 서버에서도 Collider 비활성화 (Raycast는 서버에서 실행됨)
        if (_collider != null)
        {
            _collider.enabled = false;
        }

        RPC_SetColliderState(false);
        RPC_PlayOpenAnimation();

        SpawnLoot();

        Invoke(nameof(StopAnimatorAfterOpen), _openAnimationDuration);

        // 초기화 타이머 시작
        _resetEndTime = Time.time + _resetTime;
    }

    private void ResetBox()
    {
        if (!IsServerInitialized || !IsOpened.Value)
            return;

        Health.Value = _maxHealth;
        IsOpened.Value = false;
        _resetEndTime = 0;

        // 서버에서도 Collider 활성화
        if (_collider != null)
        {
            _collider.enabled = true;
        }

        Debug.Log("[LootBox] 박스 초기화 완료!");

        RPC_SetColliderState(true);
        RPC_PlayIdleAnimation();
        RPC_ResumeAnimator();
    }

    private void SpawnLoot()
    {
        if (!IsServerInitialized)
            return;

        // ItemPool 우선, 없으면 ItemDatabase 펴백
        IReadOnlyList<ItemData> itemList = null;
        bool useItemPool = _itemPool != null && !_itemPool.IsEmpty;
        
        if (!useItemPool)
        {
            itemList = ItemDatabase.GetLootableItems();
            if (itemList == null || itemList.Count == 0)
            {
                Debug.LogError($"[LootBox] 드랍 가능한 아이템이 없습니다!");
                return;
            }
        }

        // 시간대별 등급 확률 가져오기
        TierDropRateByTime tierRates = new TierDropRateByTime { NormalRate = 100f };
        if (GameStateManager.Instance != null)
        {
            tierRates = GameStateManager.Instance.GetCurrentTierDropRates();
        }
        else
        {
            Debug.LogWarning("[LootBox] GameStateManager.Instance가 null입니다!");
        }

        int spawnCount = Random.Range(_minItemCount, _maxItemCount + 1);

        Vector3 spawnCenter = transform.position + Vector3.up * 1f;
        Vector3 forwardDir = transform.forward;

        for (int i = 0; i < spawnCount; i++)
        {
            // 등급 랜덤 결정
            ItemTier selectedTier = SelectTierByProbability(tierRates);

            // 아이템 선택
            ItemData randomItem;
            if (useItemPool)
            {
                randomItem = _itemPool.GetRandomItem();
            }
            else
            {
                randomItem = itemList[Random.Range(0, itemList.Count)];
            }

            if (randomItem == null || randomItem.ModelPrefab == null)
            {
                Debug.LogWarning($"[LootBox] 아이템 스폰 실패 (null)");
                continue;
            }

            float spreadAngle = Random.Range(-45f, 45f);
            Vector3 launchDirection = Quaternion.Euler(0, spreadAngle, 0) * forwardDir;

            Vector3 rightDir = transform.right;
            float lateralOffset = Random.Range(-_spreadRadius, _spreadRadius);
            Vector3 spawnOffset = rightDir * lateralOffset;

            Vector3 initialVelocity = launchDirection * _forwardSpeed + Vector3.up * _upwardSpeed;

            try 
            {
                Vector3 spawnPosition = spawnCenter + spawnOffset;
                
                // Fishnet: Instantiate 후 Spawn
                NetworkObject itemPrefab = randomItem.ModelPrefab.GetComponent<NetworkObject>();
                if (itemPrefab == null)
                {
                    Debug.LogError($"[LootBox] {randomItem.ItemName}의 ModelPrefab에 NetworkObject가 없습니다!");
                    continue;
                }

                NetworkObject itemObj = Instantiate(itemPrefab, spawnPosition, Quaternion.Euler(0, -90, 0));
                
                NetworkedItem networkedItem = itemObj.GetComponent<NetworkedItem>();
                if (networkedItem != null)
                {
                    networkedItem.ItemID.Value = randomItem.ItemID;
                    networkedItem.CurrentVelocity.Value = initialVelocity;
                    
                    // 소모 아이템은 타입에 따라 티어 고정
                    if (randomItem is ConsumableItemData consumable)
                    {
                        networkedItem.Tier.Value = consumable.ConsumableType switch
                        {
                            ConsumableType.Health => ItemTier.Normal,
                            ConsumableType.Ammo => ItemTier.Rare,
                            _ => ItemTier.Normal
                        };
                    }
                    else
                    {
                        // 일반 아이템은 랜덤 등급
                        networkedItem.Tier.Value = selectedTier;
                    }
                }

                ServerManager.Spawn(itemObj);


            }
            catch (System.Exception e)
            {
                Debug.LogError($"[LootBox] Exception during Spawn: {e.Message}\n{e.StackTrace}");
            }
        }
    }

    /// <summary>
    /// 확률에 따라 등급을 랜덤 선택합니다.
    /// </summary>
    private ItemTier SelectTierByProbability(TierDropRateByTime rates)
    {
        float roll = Random.Range(0f, 100f);
        float cumulative = 0f;

        // 레전더리부터 역순으로 체크 (높은 등급 우선)
        cumulative += rates.LegendaryRate;
        if (roll < cumulative) return ItemTier.Legendary;

        cumulative += rates.UniqueRate;
        if (roll < cumulative) return ItemTier.Unique;

        cumulative += rates.EpicRate;
        if (roll < cumulative) return ItemTier.Epic;

        cumulative += rates.RareRate;
        if (roll < cumulative) return ItemTier.Rare;

        return ItemTier.Normal;
    }

    #endregion

    #region Animation Optimization

    private void StopAnimatorAfterOpen()
    {
        if (!IsServerInitialized)
            return;

        RPC_DisableAnimator();
    }

    #endregion

    #region RPC Methods

    [ObserversRpc]
    private void RPC_PlayHitAnimation()
    {
        if (_animator != null)
        {
            _animator.SetTrigger(ANIM_HIT);
        }
    }

    [ObserversRpc]
    private void RPC_PlayOpenAnimation()
    {
        if (_animator != null)
        {
            _animator.SetBool(ANIM_IS_OPEN, true);
            Debug.Log("[LootBox] RPC_PlayOpenAnimation - Open 상태로 전환");
        }

        AudioManager.Instance?.PlayAttachedOneShot(_openAudioCue, transform, Vector3.up);
    }

    [ObserversRpc]
    private void RPC_PlayIdleAnimation()
    {
        if (_animator != null)
        {
            _animator.ResetTrigger(ANIM_HIT);
            _animator.SetBool(ANIM_IS_OPEN, false);
            Debug.Log("[LootBox] RPC_PlayIdleAnimation - Idle 상태로 전환");
        }

        AudioManager.Instance?.PlayAttachedOneShot(_closeAudioCue, transform, Vector3.up);
    }

    [ObserversRpc]
    private void RPC_SetColliderState(bool enabled)
    {
        if (_collider != null)
        {
            _collider.enabled = enabled;
            Debug.Log($"[LootBox] RPC_SetColliderState - Collider.enabled = {enabled}");
        }
    }

    [ObserversRpc]
    private void RPC_DisableAnimator()
    {
        if (_animator != null)
        {
            _animator.enabled = false;
            Debug.Log("[LootBox] RPC_DisableAnimator - Animator 비활성화");
        }
    }

    [ObserversRpc]
    private void RPC_ResumeAnimator()
    {
        if (_animator != null)
        {
            _animator.enabled = true;
            Debug.Log("[LootBox] RPC_ResumeAnimator - Animator 활성화");
        }
    }

    #endregion
}
