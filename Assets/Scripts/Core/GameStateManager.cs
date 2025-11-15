using System.Linq;
using Fusion;
using UnityEngine;

/// <summary>
/// 게임 전체 상태를 관리합니다 (서버 권한).
/// </summary>
public class GameStateManager : NetworkBehaviour
{
    #region Singleton

    private static GameStateManager _instance;
    public static GameStateManager Instance => _instance;

    #endregion

    #region Serialized Fields

    [Header("게임 설정")]
    [Tooltip("전체 게임 시간 (초)")]
    [SerializeField] private float _totalGameTime = 600f; // 10분

    [Tooltip("페이즈 전환 간격 (초)")]
    [SerializeField] private float _phaseInterval = 120f; // 2분

    #endregion

    #region Networked Properties

    [Networked]
    public int AlivePlayers { get; set; }

    [Networked]
    public int CurrentPhase { get; set; }

    [Networked]
    public float GameElapsedTime { get; set; }

    [Networked]
    public NetworkBool IsGameEnded { get; set; }

    [Networked]
    public PlayerRef Winner { get; set; }

    #endregion

    #region Properties

    /// <summary>
    /// 전체 게임 시간을 반환합니다.
    /// </summary>
    public float TotalGameTime => _totalGameTime;

    /// <summary>
    /// 남은 시간을 반환합니다.
    /// </summary>
    public float RemainingTime => Mathf.Max(0f, _totalGameTime - GameElapsedTime);

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        if (_instance == null)
        {
            _instance = this;
        }

        if (HasStateAuthority)
        {
            // Why: 실제 접속한 플레이어 수로 초기화
            AlivePlayers = Runner.ActivePlayers.Count();
            CurrentPhase = 1;
            GameElapsedTime = 0f;
            IsGameEnded = false;

            Debug.Log($"[GameStateManager] 게임 시작! 생존자: {AlivePlayers}명");
        }
    }

    /// <summary>
    /// 플레이어가 참가했을 때 생존자 수 증가
    /// </summary>
    public void OnPlayerJoined()
    {
        if (!HasStateAuthority) return;

        AlivePlayers++;
        Debug.Log($"[GameStateManager] 플레이어 참가! 생존자: {AlivePlayers}명");
    }

    /// <summary>
    /// 플레이어가 떠났을 때 생존자 수 감소
    /// </summary>
    public void OnPlayerLeft()
    {
        if (!HasStateAuthority) return;

        AlivePlayers--;
        Debug.Log($"[GameStateManager] 플레이어 퇴장! 생존자: {AlivePlayers}명");
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || IsGameEnded) return;

        // 시간 증가
        GameElapsedTime += Runner.DeltaTime;

        // Why: 경과 시간에 따라 페이즈 자동 변경
        int calculatedPhase = Mathf.FloorToInt(GameElapsedTime / _phaseInterval) + 1;
        if (calculatedPhase != CurrentPhase)
        {
            CurrentPhase = calculatedPhase;
            Debug.Log($"[GameStateManager] Phase {CurrentPhase} 시작!");
        }

        // Why: 시간 종료 체크
        if (GameElapsedTime >= _totalGameTime)
        {
            EndGame();
        }
    }

    #endregion

    #region Game Control

    /// <summary>
    /// 게임을 종료합니다 (서버만).
    /// </summary>
    private void EndGame()
    {
        if (!HasStateAuthority || IsGameEnded) return;

        IsGameEnded = true;

        // Why: 승자 결정 (생존자가 1명이면 그 플레이어, 아니면 None)
        Winner = FindLastSurvivor();

        Debug.Log($"[GameStateManager] 게임 종료! Phase {CurrentPhase}, 생존자: {AlivePlayers}명, 승자: {Winner}");

        // Why: 모든 클라이언트에 게임 종료 알림
        RPC_BroadcastGameEnd(Winner);
    }

    /// <summary>
    /// 마지막 생존자를 찾습니다.
    /// </summary>
    private PlayerRef FindLastSurvivor()
    {
        PlayerCombat[] allCombats = FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None);
        foreach (var combat in allCombats)
        {
            if (combat.IsAlive)
            {
                Fusion.NetworkObject netObj = combat.GetComponent<Fusion.NetworkObject>();
                if (netObj != null)
                {
                    return netObj.InputAuthority;
                }
            }
        }
        return PlayerRef.None;
    }

    #endregion

    #region Player Death

    /// <summary>
    /// 플레이어 사망 처리 (서버만).
    /// </summary>
    public void OnPlayerDied(PlayerRef victim, PlayerRef killer)
    {
        if (!HasStateAuthority) return;

        AlivePlayers--;

        // 킬로그 브로드캐스트
        string killerName = $"Player{killer.PlayerId}";
        string victimName = $"Player{victim.PlayerId}";

        RPC_BroadcastKillLog(killerName, victimName);

        Debug.Log($"[GameStateManager] {killerName} killed {victimName}. Alive: {AlivePlayers}");

        // Why: 생존자가 1명 이하면 게임 종료
        if (AlivePlayers <= 1)
        {
            EndGame();
        }
    }

    #endregion

    #region RPC Methods

    /// <summary>
    /// 모든 클라이언트에 킬로그 전송
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastKillLog(string killerName, string victimName, RpcInfo info = default)
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.AddKillLog(killerName, victimName);
        }
    }

    /// <summary>
    /// 모든 클라이언트에 게임 종료 알림
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastGameEnd(PlayerRef winner, RpcInfo info = default)
    {
        Debug.Log($"[GameStateManager] 게임 종료 알림 받음! 승자: {winner}");

        // Why: 로컬 플레이어의 정보 가져오기
        PlayerCombat localCombat = FindLocalPlayerCombat();
        if (localCombat == null) return;

        Fusion.NetworkObject localNetObj = localCombat.GetComponent<Fusion.NetworkObject>();
        if (localNetObj == null) return;

        int killCount = localCombat.KillCount;
        float survivalTime = GameElapsedTime;

        // Why: 승자인지 확인하고 적절한 UI 표시
        if (localNetObj.InputAuthority == winner)
        {
            // 승리 UI
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowVictory(killCount, survivalTime);
            }
        }
        else
        {
            // 패배 UI (순위는 일단 AlivePlayers + 1로 계산)
            int rank = AlivePlayers + 1;
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowDefeat(killCount, survivalTime, rank);
            }
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 로컬 플레이어의 PlayerCombat을 찾습니다.
    /// </summary>
    private PlayerCombat FindLocalPlayerCombat()
    {
        PlayerCombat[] allCombats = FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None);
        foreach (var combat in allCombats)
        {
            Fusion.NetworkObject netObj = combat.GetComponent<Fusion.NetworkObject>();
            if (netObj != null && netObj.HasInputAuthority)
            {
                return combat;
            }
        }
        return null;
    }

    #endregion
}
