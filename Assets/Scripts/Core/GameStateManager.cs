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
    public static GameStateManager Instance => _instance;

    #endregion

    #region Serialized Fields



    [Header("프리팹 설정")]
    [SerializeField] private GameObject _itemDatabasePrefab; // ItemDatabase 자동 생성을 위한 프리팹

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
    
    // Why: GamePlay 씬 로드 후 맵 초기화를 위해 게임 모드 정보를 임시 저장
    private GameMode _pendingGameMode;
    private int _pendingPlayerCount;
    private bool _hasPendingMapInit = false;

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
        // Why: NetworkObject를 씬 전환 시에도 유지하려면 Runner.MakeDontDestroyOnLoad 사용
        // Why: 서버에서만 호출 - 클라이언트에서는 assertion 경고가 발생할 수 있음
        if (HasStateAuthority)
        {
            Runner.MakeDontDestroyOnLoad(gameObject);
        }
        
        _instance = this;
        ServiceLocator.Register(Runner, this);

        // Why: ItemDatabase가 없으면 프리팹으로 생성 (LootBox 문제 해결)
        // Why: ItemDatabase가 없으면 프리팹으로 생성 (LootBox 문제 해결)
        // 주의: ItemDatabase.Instance를 호출하면 없을 때 빈 깡통을 자동 생성하므로, FindFirstObjectByType으로 먼저 체크해야 함
        var existingDb = FindFirstObjectByType<ItemDatabase>();
        if (existingDb == null && _itemDatabasePrefab != null)
        {
            Debug.Log("[GameStateManager] ItemDatabase가 없어서 할당된 프리팹으로 생성합니다.");
            var db = Instantiate(_itemDatabasePrefab);
            db.name = "ItemDatabase";
            DontDestroyOnLoad(db); // 생성된 오브젝트 유지
        }
        else if (existingDb == null)
        {
             Debug.LogWarning("[GameStateManager] ItemDatabase 인스턴스도 없고 GameStateManager에 프리팹 할당도 안 되어 있습니다!");
        }

        if (HasStateAuthority)
        {
            AlivePlayers = 0; // 초기 생존자 수는 0으로 설정
            
            // Why: 이미 접속해 있는 플레이어 수로 초기화 (서버 자신 제외)
            ConnectedPlayers = 0;
            foreach (var player in Runner.ActivePlayers)
            {
                if (Runner.GameMode == Fusion.GameMode.Server && player == Runner.LocalPlayer) continue;
                ConnectedPlayers++;
            }

            // Why: Session Properties에서 게임 모드 정보 복원 (씬 전환 후에도 유지됨)
            RestoreStateFromSessionProperties();

            IsGameStarted = false;
            CurrentPhase = 1;
            GameElapsedTime = 0f;
            IsGameEnded = false;
            _sessionStartTime = Time.time;

            Debug.Log($"[GameStateManager] Spawned - Connected: {ConnectedPlayers}, TargetPlayerCount: {TargetPlayerCount}");
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        // Why: 세션이 종료되는 경우에만 싱글톤 해제
        // hasState가 false면 네트워크 상태가 유효하지 않음 = 세션 종료
        // 다른 플레이어가 나가는 것으로 인해 싱글톤이 해제되면 안 됨
        if (_instance == this && !hasState)
        {
            Debug.Log("[GameStateManager] Despawned - clearing singleton (hasState=false, session ending)");
            _instance = null;
        }

        ServiceLocator.Clear(runner);
    }

    /// <summary>
    /// 플레이어가 세션에 접속했을 때 카운트 증가
    /// </summary>
    public void OnPlayerJoined()
    {
        if (!HasStateAuthority) return;

        // Why: Mode 필터링만으로 충분, IsReserved 제거됨
        ConnectedPlayers++;

        Debug.Log($"[GameStateManager] Player joined - Connected: {ConnectedPlayers}/{TargetPlayerCount}, Alive: {AlivePlayers}, IsGameStarted: {IsGameStarted}");

        // Why: 게임 시작 전이고 목표 인원에 도달했으면 GamePlay 씬으로 전환
        if (!IsGameStarted && TargetPlayerCount > 0 && ConnectedPlayers >= TargetPlayerCount)
        {
            Debug.Log($"[GameStateManager] Target player count reached ({ConnectedPlayers}/{TargetPlayerCount})! Transitioning to GamePlay scene...");
            TransitionToGamePlayScene();
        }
    }

    /// <summary>
    /// Matching 씬에서 GamePlay 씬으로 전환합니다 (서버만).
    /// NetworkSceneManager가 모든 클라이언트에게 자동으로 씬 전환을 동기화합니다.
    /// </summary>
    private void TransitionToGamePlayScene()
    {
        if (!HasStateAuthority) return;

        // Why: 코루틴으로 1초 딜레이 후 씬 전환
        StartCoroutine(TransitionToGamePlaySceneWithDelay());
    }

    private System.Collections.IEnumerator TransitionToGamePlaySceneWithDelay()
    {
        int gamePlayIndex = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/GamePlay.unity");
        if (gamePlayIndex < 0)
        {
            Debug.LogError("[GameStateManager] GamePlay scene not found in Build Settings!");
            yield break;
        }

        Debug.Log($"[GameStateManager] Match complete! Showing '매칭완료' for 1 second before scene transition...");
        
        // Why: 모든 클라이언트에게 매칭 완료 알림 (1초간 "매칭완료" 표시)
        RPC_NotifyMatchComplete();
        
        // Why: 1초 대기 (클라이언트에서 "매칭완료" 표시 시간)
        yield return new WaitForSeconds(1f);
        
        Debug.Log($"[GameStateManager] Loading GamePlay scene (index: {gamePlayIndex}) via NetworkSceneManager...");
        
        // Why: 모든 클라이언트에게 씬 전환 알림 (매칭 패널 닫기 등)
        RPC_NotifySceneTransition();
        
        // Why: NetworkSceneManager를 통해 모든 클라이언트에게 씬 로드 동기화 (Single 모드로 명시하여 이전 씬 언로드)
        Runner.LoadScene(SceneRef.FromIndex(gamePlayIndex), new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
    }

    /// <summary>
    /// GamePlay 씬 로드 완료 시 호출됩니다 (NetworkManager.OnSceneLoadDone에서 호출).
    /// 대기 중인 맵 초기화를 수행합니다.
    /// </summary>
    public void OnGamePlaySceneLoaded()
    {
        if (!HasStateAuthority) return;
        
        if (!_hasPendingMapInit)
        {
            Debug.Log("[GameStateManager] OnGamePlaySceneLoaded - No pending map init");
            return;
        }
        
        Debug.Log($"[GameStateManager] OnGamePlaySceneLoaded - Initializing map: Mode={_pendingGameMode}, Players={_pendingPlayerCount}");
        
        // Why: 이제 맵을 초기화 (GamePlay 씬에서)
        NetworkManager netManager = NetworkManager.GetManager(Runner);
        if (netManager != null && netManager.NetworkMapManager != null && !netManager.NetworkMapManager.IsReady())
        {
            netManager.NetworkMapManager.InitializeMap(_pendingGameMode, _pendingPlayerCount);
            _hasPendingMapInit = false;
        }
        else
        {
            Debug.LogWarning("[GameStateManager] OnGamePlaySceneLoaded - NetworkMapManager not ready!");
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
            // Why: 게임이 시작되지 않았으면 세션을 바로 종료
            if (!IsGameStarted)
            {
                Debug.Log("[GameStateManager] All players left before game started. Terminating session...");
                _ = ShutdownSessionImmediately();
                return;
            }

            // Why: 게임이 시작된 후 모든 플레이어가 나갔을 때만 세션 종료
            float sessionUptime = Time.time - _sessionStartTime;

            bool shouldShutdown = sessionUptime >= MIN_SESSION_TIME_BEFORE_AUTO_SHUTDOWN;

            if (shouldShutdown)
            {
                Debug.Log("[GameStateManager] All players left after game started. Shutting down session...");
                _ = ShutdownSessionImmediately();
            }
        }
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
    /// Multi-Peer 서버에 세션 종료 통지
    /// Why: ServerLauncher 삭제됨, MultiPeerServerManager 사용
    /// </summary>
    private void NotifyServerLauncher(NetworkRunner runner)
    {
        var serverManager = FindFirstObjectByType<MultiPeerServerManager>();
        if (serverManager != null && runner != null && runner.SessionInfo.IsValid)
        {
            _ = serverManager.StopSession(runner.SessionInfo.Name);
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
            if (Runner.GameMode == Fusion.GameMode.Server && player == Runner.LocalPlayer)
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
    /// <param name="victim">사망한 플레이어</param>
    /// <param name="killer">처치한 플레이어</param>
    /// <param name="weaponID">킬러가 사용한 무기의 ItemID (null 가능)</param>
    public void OnPlayerDied(PlayerRef victim, PlayerRef killer, string weaponID = null)
    {
        if (!HasStateAuthority) return;

        AlivePlayers--;

        // 킬로그 브로드캐스트
        string killerName = $"Player{killer.PlayerId}";
        string victimName = $"Player{victim.PlayerId}";

        RPC_BroadcastKillLog(killerName, victimName, weaponID ?? "");



        // Why: 생존자가 1명 이하면 게임 종료 (서버는 플레이어로 카운트되지 않음)
        if (AlivePlayers <= 1 && TargetPlayerCount > 1)
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

        Debug.Log($"[GameStateManager] RPC_SetGameModeInfo received - GameMode: {(GameMode)gameMode}, TargetPlayerCount: {targetPlayerCount}");

        // Why: 첫 번째 클라이언트의 게임 모드 정보로만 설정 (이미 설정되었으면 무시)
        if (TargetPlayerCount == 0)
        {
            TargetPlayerCount = targetPlayerCount;
            Debug.Log($"[GameStateManager] TargetPlayerCount set to: {targetPlayerCount} via RPC");

            // Why: 모드에 맞춰 세션 최대 인원 갱신
            GameMode mode = (GameMode)gameMode;
            int maxPlayers = GetMaxPlayersForMode(mode);
            UpdateSessionMaxPlayers(maxPlayers);

            // Why: 현재 매칭 중인 모드를 세션 프로퍼티에 저장 (같은 모드 매칭자들만 조인하도록)
            UpdateSessionMatchingInfo(gameMode, targetPlayerCount);
            Debug.Log($"[GameStateManager] Session matching info updated - GameMode: {(GameMode)gameMode}, TargetPlayerCount: {targetPlayerCount}");

            // Why: 게임 모드 정보를 NetworkManager에 저장 (씬 전환 시에도 유지됨)
            // InitializeMap은 OnSceneLoadDone에서 GamePlay 씬 로드 완료 후 NetworkManager가 호출
            NetworkManager netManager = NetworkManager.GetManager(Runner);
            if (netManager != null)
            {
                netManager.SetPendingMapInit(mode, targetPlayerCount);
            }
            Debug.Log($"[GameStateManager] Game mode info saved to NetworkManager. Map will initialize after GamePlay scene loads.");

            // Why: TargetPlayerCount가 설정되었으므로 게임 시작 조건을 다시 체크
            CheckAndTransitionToGamePlay();
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
                    { "MatchingTargetPlayers", targetPlayerCount } // 매칭 목표 인원수
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
    /// 모드에 맞는 최대 인원을 반환합니다.
    /// </summary>
    private int GetMaxPlayersForMode(GameMode mode)
    {
        return mode switch
        {
            GameMode.PracticeRange => 1,
            GameMode.FourPlayer => 4,
            GameMode.EightPlayer => 8,
            GameMode.Custom => 8,
            _ => 8
        };
    }

    /// <summary>
    /// 세션 최대 인원을 업데이트합니다.
    /// </summary>
    private void UpdateSessionMaxPlayers(int maxPlayers)
    {
        if (!HasStateAuthority) return;

        try
        {
            if (Runner.SessionInfo.IsValid && Runner.SessionInfo.Properties != null)
            {
                var properties = new Dictionary<string, SessionProperty>
                {
                    { "MaxPlayers", maxPlayers }
                };
                Runner.SessionInfo.UpdateCustomProperties(properties);
                Debug.Log($"[GameStateManager] Session MaxPlayers updated: {maxPlayers}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[GameStateManager] Failed to update MaxPlayers: {ex.Message}");
        }
    }

    /// <summary>
    /// 게임 시작 조건을 체크하고 시작합니다. (Matchmaking -> GamePlay 전환)
    /// </summary>
    private void CheckAndTransitionToGamePlay()
    {
        if (!HasStateAuthority) return;

        Debug.Log($"[GameStateManager] CheckAndTransitionToGamePlay - Started: {IsGameStarted}, Count: {ConnectedPlayers}/{TargetPlayerCount}");

        if (!IsGameStarted && TargetPlayerCount > 0 && ConnectedPlayers >= TargetPlayerCount)
        {
            Debug.Log($"[GameStateManager] Conditions met! Transitioning to GamePlay scene...");
            TransitionToGamePlayScene();
        }
    }

    /// <summary>
    /// 모든 클라이언트에게 GamePlay 씬으로 전환 지시
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifySceneTransition(RpcInfo info = default)
    {
        Debug.Log("[GameStateManager] RPC_NotifySceneTransition received - transitioning to GamePlay scene");

        // Why: MatchmakingManager에게 씬 전환 지시
        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.OnServerRequestedSceneTransition();
        }
        else
        {
            // Why: MatchmakingManager가 없으면 직접 씬 전환 (서버인 경우)
            Debug.Log("[GameStateManager] MatchmakingManager not found, this is likely the server");
        }
    }

    /// <summary>
    /// 모든 클라이언트에게 매칭 완료 알림 (1초간 "매칭완료" 표시)
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_NotifyMatchComplete(RpcInfo info = default)
    {
        Debug.Log("[GameStateManager] RPC_NotifyMatchComplete received - showing '매칭완료' text");

        // Why: MatchmakingManager에게 매칭 완료 알림
        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.HandleMatchComplete();
        }
    }

    /// <summary>
    /// 모든 클라이언트에게 게임 시작 카운트다운 시작 알림
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_StartGameCountdown(RpcInfo info = default)
    {
        Debug.Log("[GameStateManager] RPC_StartGameCountdown received - showing countdown UI");

        // Why: LoadingUIManager에게 5초 카운트다운 시작 알림
        if (LoadingUIManager.Instance != null)
        {
            LoadingUIManager.Instance.ShowCountdown(5f);
        }
        else
        {
            Debug.LogWarning("[GameStateManager] LoadingUIManager.Instance is null! Cannot show countdown.");
        }
    }

    /// <summary>
    /// 모든 클라이언트에 킬로그 전송
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastKillLog(string killerName, string victimName, string weaponID, RpcInfo info = default)
    {
        if (UIManager.Instance != null)
        {
            // Why: 무기 ID로 ItemDatabase에서 무기 데이터 조회하여 킬로그 아이콘 가져오기
            Sprite weaponIcon = null;
            if (!string.IsNullOrEmpty(weaponID))
            {
                var itemData = ItemDatabase.GetItem(weaponID);
                if (itemData is WeaponData weaponData)
                {
                    weaponIcon = weaponData.KillLogIcon;
                }
            }
            
            UIManager.Instance.AddKillLog(killerName, victimName, weaponIcon);
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
    /// Session Properties에서 게임 상태를 복원합니다 (씬 전환 후 호출).
    /// </summary>
    private void RestoreStateFromSessionProperties()
    {
        if (!Runner.SessionInfo.IsValid || Runner.SessionInfo.Properties == null)
        {
            Debug.Log("[GameStateManager] Session Properties not available for restoration");
            return;
        }

        var props = Runner.SessionInfo.Properties;

        // MatchingTargetPlayers 복원
        if (props.TryGetValue("MatchingTargetPlayers", out var targetProp))
        {
            int target = targetProp.IsInt ? (int)targetProp : 0;
            if (target > 0)
            {
                TargetPlayerCount = target;
                Debug.Log($"[GameStateManager] Restored TargetPlayerCount from Session Properties: {target}");
            }
        }

        // CurrentMatchingMode 복원 → NetworkManager에 전달하여 맵 초기화
        if (props.TryGetValue("CurrentMatchingMode", out var modeProp))
        {
            int mode = modeProp.IsInt ? (int)modeProp : 0;
            if (mode > 0 && TargetPlayerCount > 0)
            {
                // Why: NetworkManager에 pending map init 설정
                NetworkManager netManager = NetworkManager.GetManager(Runner);
                if (netManager != null)
                {
                    netManager.SetPendingMapInit((GameMode)mode, TargetPlayerCount);
                    Debug.Log($"[GameStateManager] Restored GameMode from Session Properties: {(GameMode)mode}");
                }
            }
        }
    }

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

        Debug.Log("[GameStateManager] All players ready! Starting 5-second countdown...");

        // Why: 모든 클라이언트에게 카운트다운 시작 알림
        RPC_StartGameCountdown();

        // Why: 5초 후 게임 실제 시작
        _ = StartGameAfterCountdown();
    }

    /// <summary>
    /// 5초 카운트다운 후 게임을 실제로 시작합니다.
    /// </summary>
    private async Task StartGameAfterCountdown()
    {
        // Why: 5초 대기 (카운트다운 시간)
        await Task.Delay(5000);

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

    #endregion
}
