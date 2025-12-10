using Fusion;
using UnityEngine;

// [TEST MODE] 테스트 완료 후 이 파일 전체를 삭제하세요!
// 설정: 1) Player 프리팩에 컴포넌트 추가  2) GameStateManager의 Test Mode 활성화  3) 씬에 "RespawnPoint" 오브젝트 배치(선택)
// 기능: 플레이어 사망 시 자동/수동 리스폰, 모든 스탯 초기화(HP/Kill/Level/XP), 승리 조건 무시
/// <summary>
/// [테스트용] 플레이어가 죽어도 GameResultPanel 없이 바로 리스폰합니다.
/// Player 프리팩에 추가하세요. 테스트 완료 후 이 스크립트를 제거하세요.
/// GameStateManager의 TestMode도 활성화해야 합니다.
/// </summary>
public class TestRespawn : NetworkBehaviour
{
    [Header("설정")]
    [Tooltip("리스폰 딜레이 (초)")]
    [SerializeField] private float _respawnDelay = 2f;

    [Tooltip("수동 리스폰 키")]
    [SerializeField] private KeyCode _respawnKey = KeyCode.R;

    [Tooltip("자동 리스폰 활성화")]
    [SerializeField] private bool _autoRespawn = true;

    [Header("스폰 위치")]
    [Tooltip("랜덤 오프셋 범위")]
    [SerializeField] private float _randomOffset = 5f;

    private PlayerCombat _playerCombat;
    private Transform _respawnPoint;
    private PlayerAnimationController _animationController;
    private Rigidbody _rigidbody;
    private Collider[] _colliders;

    private bool _isDead = false;
    private float _deathTime;
    private Vector3 _originalPosition;

    public override void Spawned()
    {
        _playerCombat = GetComponent<PlayerCombat>();
        _animationController = GetComponent<PlayerAnimationController>();
        _rigidbody = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>();

        _originalPosition = transform.position;

        // Why: 씬에서 RespawnPoint 태그를 가진 오브젝트 찾기
        GameObject respawnObj = GameObject.FindWithTag("RespawnPoint");
        if (respawnObj != null)
        {
            _respawnPoint = respawnObj.transform;
            Debug.Log($"[TestRespawn] RespawnPoint 찾음: {_respawnPoint.position}");
        }
        else
        {
            // Why: 태그가 없으면 이름으로 찾기
            respawnObj = GameObject.Find("RespawnPoint");
            if (respawnObj != null)
            {
                _respawnPoint = respawnObj.transform;
                Debug.Log($"[TestRespawn] RespawnPoint 찾음 (이름): {_respawnPoint.position}");
            }
        }

        if (Object.HasInputAuthority)
        {
            Debug.Log($"[TestRespawn] 초기화 완료 - 자동리스폰: {_autoRespawn}, 딜레이: {_respawnDelay}초, 키: {_respawnKey}");
        }
    }

    private void Update()
    {
        if (_playerCombat == null) return;

        // Why: 로컬 플레이어만 처리
        if (!Object.HasInputAuthority) return;

        // Why: 사망 감지
        if (!_isDead && !_playerCombat.IsAlive)
        {
            OnDeath();
        }

        // Why: 자동 리스폰
        if (_isDead && _autoRespawn)
        {
            if (Time.time - _deathTime >= _respawnDelay)
            {
                RequestRespawn();
            }
        }

        // Why: 수동 리스폰 (키 입력)
        if (_isDead && Input.GetKeyDown(_respawnKey))
        {
            RequestRespawn();
        }
    }

    private void OnDeath()
    {
        _isDead = true;
        _deathTime = Time.time;
        Debug.Log($"[TestRespawn] 사망 감지! {_respawnDelay}초 후 리스폰...");

        // Why: GameResultPanel 숨기기
        HideGameResultPanel();
    }

    private void RequestRespawn()
    {
        Debug.Log("[TestRespawn] 리스폰 요청!");
        
        // Why: 서버에 리스폰 요청
        RPC_RequestRespawn();
        
        _isDead = false;
    }

    /// <summary>
    /// 클라이언트 → 서버: 리스폰 요청
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_RequestRespawn()
    {
        Debug.Log("[TestRespawn] 서버에서 리스폰 처리!");

        // 1. 모든 스탯 초기화 (HP, KillCount, Level, XP)
        ResetAllStats();

        // 2. 위치 리셋
        Vector3 targetPos = CalculateRespawnPosition();
        transform.position = targetPos;

        // 3. Collider 복구
        EnableColliders();

        // 4. GameStateManager에 리스폰 알림
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnPlayerRespawned();
        }

        // 5. 모든 클라이언트에 리스폰 알림 (애니메이션 등)
        RPC_OnRespawned(targetPos);
    }

    /// <summary>
    /// 서버 → 모든 클라이언트: 리스폰 완료 알림
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnRespawned(Vector3 position)
    {
        Debug.Log($"[TestRespawn] 리스폰 완료! 위치: {position}");

        // Why: 위치 동기화
        transform.position = position;

        // Why: 애니메이션 리셋
        ResetAnimation();

        // Why: Collider 복구 (클라이언트)
        EnableColliders();

        // Why: UI 숨기기
        if (Object.HasInputAuthority)
        {
            HideGameResultPanel();
            _isDead = false;
        }
    }

    private void ResetAllStats()
    {
        if (_playerCombat == null) return;

        var bindingFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

        // Why: HP 리셋
        var hpProperty = typeof(PlayerCombat).GetProperty("HP", bindingFlags);
        if (hpProperty != null && hpProperty.CanWrite)
        {
            hpProperty.SetValue(_playerCombat, _playerCombat.MaxHP);
        }

        // Why: KillCount 리셋
        var killCountProperty = typeof(PlayerCombat).GetProperty("KillCount", bindingFlags);
        if (killCountProperty != null && killCountProperty.CanWrite)
        {
            killCountProperty.SetValue(_playerCombat, 0);
        }

        // Why: Level 리셋
        var levelProperty = typeof(PlayerCombat).GetProperty("Level", bindingFlags);
        if (levelProperty != null && levelProperty.CanWrite)
        {
            levelProperty.SetValue(_playerCombat, 1);
        }

        // Why: CurrentXP 리셋
        var xpProperty = typeof(PlayerCombat).GetProperty("CurrentXP", bindingFlags);
        if (xpProperty != null && xpProperty.CanWrite)
        {
            xpProperty.SetValue(_playerCombat, 0f);
        }

        Debug.Log($"[TestRespawn] 모든 스탯 초기화 - HP: {_playerCombat.MaxHP}, Kill: 0, Level: 1, XP: 0");
    }

    private Vector3 CalculateRespawnPosition()
    {
        if (_respawnPoint != null)
        {
            return _respawnPoint.position;
        }
        else
        {
            // Why: 원래 위치 + 랜덤 오프셋
            return _originalPosition + new Vector3(
                Random.Range(-_randomOffset, _randomOffset),
                0,
                Random.Range(-_randomOffset, _randomOffset)
            );
        }
    }

    private void EnableColliders()
    {
        if (_colliders != null)
        {
            foreach (var col in _colliders)
            {
                if (col != null) col.enabled = true;
            }
        }

        if (_rigidbody != null)
        {
            _rigidbody.isKinematic = false;
        }
    }

    private void ResetAnimation()
    {
        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            // Why: 트리거 리셋
            animator.ResetTrigger("Die");
            animator.ResetTrigger("Hit");
            
            // Why: Idle로 강제 전환
            animator.Play("Idle", 0, 0f);
            animator.Update(0f);
            
            Debug.Log("[TestRespawn] 애니메이션 Idle로 리셋");
        }
    }

    private void HideGameResultPanel()
    {
        if (UIManager.Instance != null)
        {
            var resultUIField = typeof(UIManager).GetField("_gameResultUI", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (resultUIField != null)
            {
                var resultUI = resultUIField.GetValue(UIManager.Instance) as GameResultUI;
                if (resultUI != null && resultUI.gameObject.activeSelf)
                {
                    resultUI.gameObject.SetActive(false);
                    Debug.Log("[TestRespawn] GameResultUI 숨김");
                }
            }
        }
    }

    private void OnGUI()
    {
        // Why: 로컬 플레이어만 GUI 표시
        if (!Object.HasInputAuthority) return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperRight
        };
        style.normal.textColor = Color.yellow;

        float screenWidth = Screen.width;
        float y = 10;
        
        GUI.Label(new Rect(screenWidth - 310, y, 300, 25), "[테스트 모드 - 무한 리스폰]", style);
        y += 20;

        if (_isDead)
        {
            style.normal.textColor = Color.red;
            float remaining = Mathf.Max(0, _respawnDelay - (Time.time - _deathTime));
            GUI.Label(new Rect(screenWidth - 410, y, 400, 25), $"사망! 리스폰까지: {remaining:F1}초 (또는 {_respawnKey} 키)", style);
        }
        else
        {
            style.normal.textColor = Color.green;
            GUI.Label(new Rect(screenWidth - 310, y, 300, 25), $"생존 중 - HP: {(_playerCombat != null ? _playerCombat.HP : 0):F0}", style);
        }
    }
}
