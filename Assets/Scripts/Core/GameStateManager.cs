using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

/// <summary>
/// Phase 설정 구조체
/// </summary>
[System.Serializable]
public struct PhaseConfig
{
    [Tooltip("Phase 지속 시간 (초)")]
    public float Duration;

    [Tooltip("맵 축소 비율 (0~1, 예: 0.15 = 15%)")]
    [Range(0f, 1f)]
    public float ShrinkRate;
}

/// <summary>
/// 게임 전체 상태를 관리합니다 (서버 권한).
/// </summary>
public class GameStateManager : NetworkBehaviour
{
    #region Singleton (Client Only)

    private static GameStateManager _instance;

    /// <summary>
    /// 클라이언트 전용 싱글톤. 서버(Multi-Peer)에서는 사용하지 마세요!
    /// 서버에서는 NetworkManager.Get(runner).GameStateManager를 사용해야 합니다.
    /// </summary>
    public static GameStateManager Instance
    {
        get
        {
            if (Application.isEditor || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "GamePlay")
            {
                return _instance;
            }
            return _instance;
        }
    }

    #endregion

    #region Serialized Fields

    // [TEST MODE] 테스트 완료 후 _testMode를 false로 설정하거나 이 섹션 제거
    [Header("테스트 모드")]
    [Tooltip("테스트 모드 - 승리 조건 무시, 리스폰 허용")]
    [SerializeField] private bool _testMode = false;

    // [TEST MODE]
    public bool IsTestMode => _testMode;

    [Header("게임 설정")]
    [Tooltip("전체 게임 시간 (초)")]
    [SerializeField] private float _totalGameTime = 600f; // 10분

    [Header("Phase 설정")]
    [Tooltip("각 Phase의 지속 시간 및 맵 축소 비율")]
    [SerializeField] private PhaseConfig[] _phaseConfigs = new PhaseConfig[]
    {
        new PhaseConfig { Duration = 120f, ShrinkRate = 0f },     // Phase 1: 2분, 축소 없음
        new PhaseConfig { Duration = 120f, ShrinkRate = 0.25f },  // Phase 2: 2분, 남은 청크의 25% 축소
        new PhaseConfig { Duration = 120f, ShrinkRate = 0.40f },  // Phase 3: 2분, 남은 청크의 40% 축소
        new PhaseConfig { Duration = 120f, ShrinkRate = 1.0f },   // Phase 4: 2분, 남은 청크의 100% 축소 (Start Chunk만 남음)
    };

    #endregion

    #region Networked Properties

    [Networked]
    public int AlivePlayers { get; set; }

    [Networked]
    public int ConnectedPlayers { get; set; }

    [Networked]
    public int TargetPlayerCount { get; set; }

    [Networked]
    public NetworkBool IsGameStarted { get; set; }

    [Networked]
    public int CurrentPhase { get; set; }

    [Networked]
    public float GameElapsedTime { get; set; }

    [Networked]
    public NetworkBool IsGameEnded { get; set; }

    [Networked]
    public PlayerRef Winner { get; set; }

    #endregion

    #region Private Fields

    private const float MIN_SESSION_TIME_BEFORE_AUTO_SHUTDOWN = 5f; // 5초
    private float _sessionStartTime;

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

    /// <summary>
    /// 다음 Phase까지 남은 시간을 반환합니다.
    /// </summary>
    public float TimeToNextPhase
    {
        get
        {
            if (_phaseConfigs == null || _phaseConfigs.Length == 0)
                return 0f;

            float elapsed = GameElapsedTime;
            float accumulated = 0f;

            // 현재 Phase의 종료 시간 찾기
            for (int i = 0; i < _phaseConfigs.Length; i++)
            {
                accumulated += _phaseConfigs[i].Duration;
                if (elapsed < accumulated)
                {
                    // 다음 Phase까지 남은 시간
                    return accumulated - elapsed;
                }
            }

            // 마지막 Phase면 0 반환
            return 0f;
        }
    }

    /// <summary>
    /// 현재 Phase의 총 지속 시간을 반환합니다.
    /// </summary>
    public float CurrentPhaseDuration
    {
        get
        {
            if (_phaseConfigs == null || _phaseConfigs.Length == 0)
                return 0f;

            int phaseIndex = CurrentPhase - 1;
            if (phaseIndex >= 0 && phaseIndex < _phaseConfigs.Length)
            {
                return _phaseConfigs[phaseIndex].Duration;
            }

            return 0f;
        }
    }

    /// <summary>
    /// 현재 Phase가 마지막 Phase인지 확인합니다.
    /// </summary>
    public bool IsLastPhase => _phaseConfigs != null && CurrentPhase >= _phaseConfigs.Length;

    #endregion

    #region Fusion Lifecycle

    public override void Spawned()
    {
        // Client인 경우에만 Singleton 할당
        if (!Runner.IsServer)
        {
            _instance = this;
        }

        // Why: 프리팹에서 _testMode가 true로 설정되어 있을 수 있으므로 강제로 false로 설정
        _testMode = false;

        if (HasStateAuthority)
        {
            AlivePlayers = 0; // 초기 생존자 수는 0으로 설정
            ConnectedPlayers = 0;
            // TargetPlayerCount는 NetworkManager에서 설정됩니다.

            IsGameStarted = false;
            CurrentPhase = 1;
            GameElapsedTime = 0f;
            IsGameEnded = false;
            _sessionStartTime = Time.time;

            Debug.Log($"[GameStateManager] Spawned - TestMode: {_testMode}, Waiting for TargetPlayerCount from NetworkManager.");
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    /// <summary>
    /// 플레이어가 세션에 접속했을 때 카운트 증가
    /// </summary>
    public void OnPlayerJoined()
    {
        if (!HasStateAuthority) return;

        // Why: 첫 클라이언트가 도착해 모드/타겟 정보가 설정되기 전까지 다른 모드 유입을 막기 위해 잠시 예약 상태로 전환
        if (TargetPlayerCount == 0)
        {
            UpdateSessionReservation(true);
        }

        ConnectedPlayers++;

        Debug.Log($"[GameStateManager] Player joined - Connected: {ConnectedPlayers}/{TargetPlayerCount}, Alive: {AlivePlayers}, IsGameStarted: {IsGameStarted}");

        // Why: 게임 시작 전이라면 목표 인원 체크
        if (!IsGameStarted)
        {
            CheckAndStartGame();
        }
    }

    /// <summary>
    /// 플레이어가 떠났을 때 카운트 감소
    /// </summary>
    public void OnPlayerLeft(bool wasAlive)
    {
        if (!HasStateAuthority) return;

        ConnectedPlayers = Mathf.Max(ConnectedPlayers - 1, 0);
        if (wasAlive)
        {
            AlivePlayers = Mathf.Max(AlivePlayers - 1, 0);
        }

        // Why: 접속 중인 클라이언트가 모두 떠난 경우에만 세션 종료 판단
        bool noClientsRemaining = ConnectedPlayers <= 0;
        if (noClientsRemaining)
        {
            // Why: 게임이 시작되지 않았으면 세션을 종료하지 않고 재사용 가능하도록 설정
            if (!IsGameStarted)
            {
                Debug.Log("[GameStateManager] All players left before game started. Resetting session for reuse...");
                ResetSessionForReuse();
                return;
            }

            // Why: 게임이 시작된 후 모든 플레이어가 나갔을 때만 세션 종료
            float sessionUptime = Time.time - _sessionStartTime;

            // [TEST MODE] 테스트 모드에서는 최소 시간 체크 무시
            bool shouldShutdown = _testMode || sessionUptime >= MIN_SESSION_TIME_BEFORE_AUTO_SHUTDOWN;

            if (shouldShutdown)
            {
                Debug.Log("[GameStateManager] All players left after game started. Shutting down session...");
                _ = ShutdownSessionImmediately();
            }
        }
    }

    /// <summary>
    /// 세션을 재사용 가능한 상태로 리셋합니다.
    /// </summary>
    private void ResetSessionForReuse()
    {
        if (!HasStateAuthority) return;

        // Why: 플레이어 카운트 리셋
        AlivePlayers = 0;
        ConnectedPlayers = 0;
        TargetPlayerCount = 0;

        // Why: 세션 속성 업데이트 - IsReserved = false로 설정하여 다른 플레이어가 조인 가능하도록
        try
        {
            if (Runner.SessionInfo.IsValid && Runner.SessionInfo.Properties != null)
            {
                var properties = new Dictionary<string, SessionProperty>
                {
                    { "IsReserved", false }
                };
                Runner.SessionInfo.UpdateCustomProperties(properties);
                Debug.Log("[GameStateManager] Session reset for reuse (IsReserved = false)");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[GameStateManager] Failed to reset session: {ex.Message}");
        }
    }

    // [TEST MODE] 테스트 완료 후 이 메서드 제거
    /// <summary>
    /// 플레이어 리스폰 시 생존자 수 증가 (테스트용)
    /// </summary>
    public void OnPlayerRespawned()
    {
        if (!HasStateAuthority) return;

        AlivePlayers++;
    }

    public override void FixedUpdateNetwork()
    {
        // Why: Runner가 종료 중이거나 실행 중이 아니면 처리하지 않음
        if (Runner == null || !Runner.IsRunning) return;

        if (!HasStateAuthority || IsGameEnded) return;

        // Why: 게임이 시작되지 않았으면 시간을 증가시키지 않음
        if (!IsGameStarted) return;

        // 시간 증가
        GameElapsedTime += Runner.DeltaTime;

        // Why: PhaseConfig 배열 기반으로 Phase 자동 변경
        CalculateCurrentPhase();

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

        Winner = FindLastSurvivor();

        if (Winner != PlayerRef.None)
        {
            Fusion.NetworkObject winnerObj = Runner.GetPlayerObject(Winner);
            if (winnerObj != null)
            {
                PlayerCombat winnerCombat = winnerObj.GetComponent<PlayerCombat>();
                if (winnerCombat != null)
                {
                    winnerCombat.PlayVictory();
                }
            }
        }

        RPC_BroadcastGameEnd(Winner);

        // 게임 종료 후 처리
        OnGameEnded();
    }

    /// <summary>
    /// 게임 종료 후 처리
    /// </summary>
    private void OnGameEnded()
    {
        // 10초 후 세션 종료 (결과 화면 표시 시간)
        _ = ShutdownSessionAfterDelay(10f);
    }

    /// <summary>
    /// 지연 후 세션 종료
    /// </summary>
    private async Task ShutdownSessionAfterDelay(float delay)
    {
        await Task.Delay((int)(delay * 1000));

        // NetworkManager.Get(Runner) 사용 (Singleton 제거)
        NetworkManager netManager = NetworkManager.GetManager(Runner);
        if (netManager != null && netManager.Runner != null)
        {
            // 서버인 경우에만 세션 종료
            if (netManager.Runner.IsServer)
            {
                // ServerLauncher에 세션 종료 통지
                NotifyServerLauncher(netManager.Runner);

                await netManager.Runner.Shutdown();
            }
        }
    }

    /// <summary>
    /// 세션 즉시 종료 (모든 플레이어가 나갔을 때)
    /// </summary>
    private async Task ShutdownSessionImmediately()
    {
        NetworkManager netManager = NetworkManager.GetManager(Runner);
        if (netManager != null && netManager.Runner != null)
        {
            if (netManager.Runner.IsServer)
            {
                // ServerLauncher에 세션 종료 통지
                NotifyServerLauncher(netManager.Runner);

                await netManager.Runner.Shutdown();
            }
        }
    }

    /// <summary>
    /// ServerLauncher에 세션 종료 통지
    /// </summary>
    private void NotifyServerLauncher(NetworkRunner runner)
    {
        ServerLauncher serverLauncher = FindFirstObjectByType<ServerLauncher>();
        if (serverLauncher != null)
        {
            serverLauncher.OnSessionEnded(runner);
        }
    }

    /// <summary>
    /// 마지막 생존자를 찾습니다.
    /// </summary>
    private PlayerRef FindLastSurvivor()
    {
        if (Runner == null)
        {
            return PlayerRef.None;
        }

        // Why: Runner.ActivePlayers를 순회하여 생존자 찾기 (FindObjectsByType 대신)
        foreach (var player in Runner.ActivePlayers)
        {
            // Why: Server 모드에서는 서버 자신(LocalPlayer)을 제외
            if (Runner.GameMode == GameMode.Server && player == Runner.LocalPlayer)
            {
                continue;
            }

            Fusion.NetworkObject netObj = Runner.GetPlayerObject(player);
            if (netObj == null) continue;

            PlayerCombat combat = netObj.GetComponent<PlayerCombat>();
            if (combat != null && combat.IsAlive)
            {
                return player;
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

        // [TEST MODE] 테스트 완료 후 이 if 블록 제거
        if (_testMode)
        {
            return;
        }

        // Why: 생존자가 1명 이하면 게임 종료 (서버는 플레이어로 카운트되지 않음)
        if (AlivePlayers <= 1)
        {
            EndGame();
        }
    }

    #endregion

    #region RPC Methods

    /// <summary>
    /// 클라이언트가 서버에 게임 모드와 목표 플레이어 수를 전달합니다.
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SetGameModeInfo(int gameMode, int targetPlayerCount, RpcInfo info = default)
    {
        if (!HasStateAuthority) return;

        Debug.Log($"[GameStateManager] RPC_SetGameModeInfo received - GameMode: {(EGameMode)gameMode}, TargetPlayerCount: {targetPlayerCount}");

        // Why: 첫 번째 클라이언트의 게임 모드 정보로만 설정 (이미 설정되었으면 무시)
        if (TargetPlayerCount == 0)
        {
            TargetPlayerCount = targetPlayerCount;
            Debug.Log($"[GameStateManager] TargetPlayerCount set to: {targetPlayerCount} via RPC");

            // Why: 현재 매칭 중인 모드를 세션 프로퍼티에 저장 (같은 모드 매칭자들만 조인하도록)
            UpdateSessionMatchingInfo(gameMode, targetPlayerCount);
            Debug.Log($"[GameStateManager] Session matching info updated - GameMode: {(EGameMode)gameMode}, TargetPlayerCount: {targetPlayerCount}");

            // Why: NetworkMapManager에 맵 초기화 요청
            NetworkManager netManager = NetworkManager.GetManager(Runner);
            if (netManager != null && netManager.NetworkMapManager != null && !netManager.NetworkMapManager.IsReady())
            {
                EGameMode mode = (EGameMode)gameMode;
                Debug.Log($"[GameStateManager] Initializing map via RPC - Mode: {mode}, PlayerCount: {targetPlayerCount}");
                netManager.NetworkMapManager.InitializeMap(mode, targetPlayerCount);
            }

            // Why: TargetPlayerCount가 설정되었으므로 게임 시작 조건을 다시 체크
            CheckAndStartGame();
        }
    }

    /// <summary>
    /// 세션 속성을 업데이트합니다 (서버만).
    /// </summary>
    private void UpdateSessionProperty(string key, SessionProperty value)
    {
        if (!HasStateAuthority) return;

        try
        {
            if (Runner.SessionInfo.IsValid && Runner.SessionInfo.Properties != null)
            {
                var properties = new Dictionary<string, SessionProperty> { { key, value } };
                Runner.SessionInfo.UpdateCustomProperties(properties);
                Debug.Log($"[GameStateManager] Session property updated: {key} = {value}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[GameStateManager] Failed to update session property: {ex.Message}");
        }
    }

    /// <summary>
    /// 세션의 현재 매칭 정보를 업데이트합니다 (서버만).
    /// 같은 모드로 매칭하는 플레이어들만 이 세션에 조인할 수 있도록 합니다.
    /// </summary>
    private void UpdateSessionMatchingInfo(int gameMode, int targetPlayerCount)
    {
        if (!HasStateAuthority) return;

        try
        {
            if (Runner.SessionInfo.IsValid && Runner.SessionInfo.Properties != null)
            {
                var properties = new Dictionary<string, SessionProperty>
                {
                    { "CurrentMatchingMode", gameMode }, // 현재 매칭 중인 게임 모드
                    { "MatchingTargetPlayers", targetPlayerCount }, // 매칭 목표 인원수
                    { "IsReserved", false } // 같은 모드 매칭자들이 조인할 수 있도록 false로 설정
                };
                Runner.SessionInfo.UpdateCustomProperties(properties);
                Debug.Log($"[GameStateManager] Session matching info updated - CurrentMatchingMode: {gameMode}, TargetPlayers: {targetPlayerCount}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[GameStateManager] Failed to update session matching info: {ex.Message}");
        }
    }

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
        // Why: 로컬 플레이어의 정보 가져오기
        PlayerCombat localCombat = FindLocalPlayerCombat();
        if (localCombat == null) return;

        Fusion.NetworkObject localNetObj = localCombat.GetComponent<Fusion.NetworkObject>();
        if (localNetObj == null) return;
        
        // Why: PlayerCombat이 아직 Spawned되지 않았으면 기본값 사용
        int killCount = 0;
        if (localCombat.Object != null && localCombat.Object.IsValid)
        {
            killCount = localCombat.KillCount;
        }
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
    /// 목표 플레이어 수에 도달했는지 확인하고 게임을 시작합니다.
    /// </summary>
    private void CheckAndStartGame()
    {
        if (!HasStateAuthority || IsGameStarted) return;

        Debug.Log($"[GameStateManager] CheckAndStartGame - Connected: {ConnectedPlayers}, Alive: {AlivePlayers}, Target: {TargetPlayerCount}");

        // Why: TargetPlayerCount가 0이면 아직 RPC가 도착하지 않은 상태이므로 대기
        if (TargetPlayerCount <= 0)
        {
            Debug.Log("[GameStateManager] TargetPlayerCount not set yet. Waiting for RPC...");
            return;
        }

        // Why: 모든 플레이어가 접속(Connected)했고 실제 스폰되어 살아있는 상태(Alive)가 목표 인원 이상일 때 시작
        bool allConnected = ConnectedPlayers >= TargetPlayerCount;
        bool allAlive = AlivePlayers >= TargetPlayerCount;

        if (allConnected && allAlive)
        {
            Debug.Log("[GameStateManager] Target player count reached! Starting game...");
            StartGame();
        }
        else
        {
            Debug.Log($"[GameStateManager] Waiting for players (Alive {AlivePlayers}/{TargetPlayerCount}, Connected {ConnectedPlayers})...");
        }
    }

    /// <summary>
    /// 게임을 시작합니다 (서버만).
    /// </summary>
    private void StartGame()
    {
        if (!HasStateAuthority || IsGameStarted) return;

        IsGameStarted = true;
        Debug.Log("[GameStateManager] Game started!");

        // Why: 게임 시작 시 세션 상태를 InGame으로 변경
        UpdateSessionInGameState(true);
    }

    /// <summary>
    /// 로컬 플레이어의 PlayerCombat을 찾습니다.
    /// </summary>
    private PlayerCombat FindLocalPlayerCombat()
    {
        if (Runner == null)
        {
            return null;
        }

        // Why: Runner.LocalPlayer를 사용하여 직접 접근 (FindObjectsByType 대신)
        Fusion.NetworkObject localPlayerObj = Runner.GetPlayerObject(Runner.LocalPlayer);
        if (localPlayerObj == null)
        {
            return null;
        }

        return localPlayerObj.GetComponent<PlayerCombat>();
    }

    /// <summary>
    /// 경과 시간 기반으로 현재 Phase를 계산합니다.
    /// </summary>
    private void CalculateCurrentPhase()
    {
        if (_phaseConfigs == null || _phaseConfigs.Length == 0)
        {
            return;
        }

        float elapsed = GameElapsedTime;
        float accumulated = 0f;

        for (int i = 0; i < _phaseConfigs.Length; i++)
        {
            float phaseDuration;

            // Why: 마지막 Phase는 _totalGameTime까지 자동 연장
            if (i == _phaseConfigs.Length - 1)
            {
                phaseDuration = Mathf.Max(0f, _totalGameTime - accumulated);
            }
            else
            {
                phaseDuration = _phaseConfigs[i].Duration;
            }

            accumulated += phaseDuration;

            if (elapsed < accumulated)
            {
                int newPhase = i + 1;
                if (newPhase != CurrentPhase)
                {
                    CurrentPhase = newPhase;
                }
                return;
            }
        }

        // Why: 마지막 Phase를 벗어나면 최대 Phase로 고정
        int finalPhase = _phaseConfigs.Length;
        if (CurrentPhase != finalPhase)
        {
            CurrentPhase = finalPhase;
        }
    }

    /// <summary>
    /// 특정 Phase의 맵 축소 비율을 반환합니다.
    /// </summary>
    /// <param name="phase">Phase 번호 (1부터 시작)</param>
    /// <returns>맵 축소 비율 (0~1)</returns>
    public float GetShrinkRateForPhase(int phase)
    {
        int index = phase - 1;
        if (_phaseConfigs == null || index < 0 || index >= _phaseConfigs.Length)
        {
            return 0f;
        }

        return _phaseConfigs[index].ShrinkRate;
    }

    /// <summary>
    /// 세션의 InGame 상태를 업데이트합니다 (서버만)
    /// </summary>
    private void UpdateSessionInGameState(bool isInGame)
    {
        if (!HasStateAuthority) return;

        try
        {
            // Why: 세션 프로퍼티를 업데이트하여 매칭 시스템이 이 세션을 찾지 않도록 함
            var sessionProperties = Runner.SessionInfo.Properties;
            if (sessionProperties != null && sessionProperties.ContainsKey("IsInGame"))
            {
                Runner.SessionInfo.UpdateCustomProperties(new System.Collections.Generic.Dictionary<string, SessionProperty>
                {
                    { "IsInGame", isInGame }
                });
            }
        }
        catch (System.Exception)
        {
            // 무시
        }
    }

    /// <summary>
    /// 플레이어가 실제로 스폰되어 살아있는 상태가 되었을 때 호출합니다.
    /// </summary>
    public void OnPlayerSpawnedAlive()
    {
        if (!HasStateAuthority) return;
        AlivePlayers++;
        CheckAndStartGame();
    }

    /// <summary>
    /// 세션 예약 상태를 업데이트합니다 (서버만).
    /// </summary>
    private void UpdateSessionReservation(bool isReserved)
    {
        if (!HasStateAuthority) return;

        try
        {
            if (Runner.SessionInfo.IsValid && Runner.SessionInfo.Properties != null)
            {
                Runner.SessionInfo.UpdateCustomProperties(new System.Collections.Generic.Dictionary<string, SessionProperty>
                {
                    { "IsReserved", isReserved }
                });
                Debug.Log($"[GameStateManager] Session reservation updated: IsReserved = {isReserved}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[GameStateManager] Failed to update reservation: {ex.Message}");
        }
    }

    #endregion
}
