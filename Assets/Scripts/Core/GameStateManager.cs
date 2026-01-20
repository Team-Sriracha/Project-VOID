using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

// Why: FishNet.Managing.NetworkManager와 커스텀 NetworkManager 구분을 위한 alias
using CustomNetworkManager = NetworkManager;

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
/// 게임 상태 enum
/// </summary>
public enum GameState
{
    /// <summary>플레이어 대기 중 (카운트다운 전)</summary>
    WaitingForPlayers,
    /// <summary>카운트다운 중 (5초)</summary>
    Countdown,
    /// <summary>게임 진행 중</summary>
    Playing,
    /// <summary>게임 종료</summary>
    Ended
}

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

    [Header("프리팹 설정")]
    [SerializeField] private GameObject _itemDatabasePrefab;

    [Header("게임 모드 설정")]
    [Tooltip("게임모드별 설정 파일들")]
    [SerializeField] private GameModeConfig[] _gameModeConfigs;

    [Header("등급 설정")]
    [Tooltip("아이템 등급 설정")]
    [SerializeField] private ItemTierConfig _itemTierConfig;

    #endregion

    #region Private Fields (GameModeConfig에서 로드)

    /// <summary>
    /// 전체 게임 시간 (초) - GameModeConfig에서 로드
    /// </summary>
    private float _totalGameTime = 600f;

    /// <summary>
    /// Phase 설정 배열 - GameModeConfig에서 로드
    /// </summary>
    private PhaseConfig[] _phaseConfigs = new PhaseConfig[]
    {
        new PhaseConfig { Duration = 120f, ShrinkRate = 0f },
        new PhaseConfig { Duration = 120f, ShrinkRate = 0.25f },
        new PhaseConfig { Duration = 120f, ShrinkRate = 0.40f },
        new PhaseConfig { Duration = 0f, ShrinkRate = 1.0f },
    };

    #endregion

    #region SyncVars

    public readonly SyncVar<int> AlivePlayers = new();
    public readonly SyncVar<int> ConnectedPlayers = new();
    public readonly SyncVar<int> TargetPlayerCount = new();
    public readonly SyncVar<GameState> CurrentGameState = new(GameState.WaitingForPlayers);
    public readonly SyncVar<int> CurrentPhase = new(1);
    public readonly SyncVar<float> GameElapsedTime = new();
    public readonly SyncVar<NetworkConnection> Winner = new();

    // Why: 기존 코드 호환성을 위한 프로퍼티
    public bool IsGameStarted => CurrentGameState.Value == GameState.Playing;
    public bool IsGameEnded => CurrentGameState.Value == GameState.Ended;

    #endregion

    #region Private Fields

    private const float MIN_SESSION_TIME_BEFORE_AUTO_SHUTDOWN = 5f;
    private float _sessionStartTime;
    
    private GameMode _pendingGameMode;
    private int _pendingPlayerCount;

    /// <summary>
    /// 현재 적용된 게임모드 설정
    /// </summary>
    private GameModeConfig _currentGameModeConfig;

    #endregion

    #region Properties

    public float TotalGameTime => _totalGameTime;
    public float RemainingTime => Mathf.Max(0f, _totalGameTime - GameElapsedTime.Value);

    public float TimeToNextPhase
    {
        get
        {
            if (_phaseConfigs == null || _phaseConfigs.Length == 0)
                return 0f;

            float elapsed = GameElapsedTime.Value;
            float accumulated = 0f;

            for (int i = 0; i < _phaseConfigs.Length; i++)
            {
                accumulated += _phaseConfigs[i].Duration;
                if (elapsed < accumulated)
                {
                    return accumulated - elapsed;
                }
            }

            return 0f;
        }
    }

    public float CurrentPhaseDuration
    {
        get
        {
            if (_phaseConfigs == null || _phaseConfigs.Length == 0)
                return 0f;

            int phaseIndex = CurrentPhase.Value - 1;
            if (phaseIndex >= 0 && phaseIndex < _phaseConfigs.Length)
            {
                return _phaseConfigs[phaseIndex].Duration;
            }

            return 0f;
        }
    }

    public bool IsLastPhase => _phaseConfigs != null && CurrentPhase.Value >= _phaseConfigs.Length;

    /// <summary>
    /// 현재 적용된 게임모드 설정
    /// </summary>
    public GameModeConfig CurrentGameModeConfig => _currentGameModeConfig;

    #endregion

    #region Fishnet Lifecycle

    public override void OnStartNetwork()
    {
        base.OnStartNetwork();
        
        _instance = this;
        
        // ItemTierConfig 초기화
        if (_itemTierConfig != null)
        {
            ItemTierConfig.SetInstance(_itemTierConfig);
        }
        else
        {
            Debug.LogWarning("[GameStateManager] ItemTierConfig가 설정되지 않았습니다!");
        }
        
        // ItemDatabase 초기화 (비 네트워크 오브젝트)
        var existingDb = FindFirstObjectByType<ItemDatabase>();
        if (existingDb == null && _itemDatabasePrefab != null)
        {
            Debug.Log("[GameStateManager] ItemDatabase가 없어서 프리팩으로 생성합니다.");
            var db = Instantiate(_itemDatabasePrefab);
            db.name = "ItemDatabase";
            // Why: NetworkBehaviour에서 DontDestroyOnLoad 직접 호출 금지
            // ItemDatabase는 MonoBehaviour이므로 자체적으로 DontDestroyOnLoad 처리
        }

        if (IsServerInitialized)
        {
            AlivePlayers.Value = 0;
            ConnectedPlayers.Value = 0;
            CurrentGameState.Value = GameState.WaitingForPlayers;
            CurrentPhase.Value = 1;
            GameElapsedTime.Value = 0f;
            _sessionStartTime = Time.time;

            Debug.Log($"[GameStateManager] Spawned - Connected: {ConnectedPlayers.Value}, TargetPlayerCount: {TargetPlayerCount.Value}");
        }
    }

    public override void OnStopNetwork()
    {
        base.OnStopNetwork();
        
        if (_instance == this)
        {
            _instance = null;
        }
    }

    private float _lastSessionUpdateTime = 0f;
    private const float SESSION_UPDATE_INTERVAL = 1f;

    private void Update()
    {
        // 서버에서만 처리
        if (!IsServerInitialized || IsGameEnded) return;

        // 게임 시작 후에만 시간 업데이트
        if (IsGameStarted)
        {
            GameElapsedTime.Value += Time.deltaTime;
            CalculateCurrentPhase();

            if (GameElapsedTime.Value >= _totalGameTime)
            {
                EndGame();
            }
        }

        // 세션 상태 주기적 업데이트 (게임 시작 전/후 모두)
        if (Time.time - _lastSessionUpdateTime >= SESSION_UPDATE_INTERVAL)
        {
            _lastSessionUpdateTime = Time.time;
            UpdateSessionStatusAsync();
        }
    }

    private async void UpdateSessionStatusAsync()
    {
        if (UnityLobbyManager.Instance != null)
        {
            await UnityLobbyManager.Instance.UpdateSessionStatus(
                CurrentGameState.Value.ToString(),  // GameState (WaitingForPlayers, Playing, etc.)
                CurrentPhase.Value,                 // Phase
                AlivePlayers.Value,                 // 생존 플레이어
                ConnectedPlayers.Value,             // 총 연결된 플레이어
                GameElapsedTime.Value,              // 경과 시간
                IsGameStarted,                      // 게임 시작됨
                IsGameEnded                         // 게임 종료됨
            );
        }
    }

    #endregion

    #region Player Events

    public void OnPlayerJoined()
    {
        if (!IsServerInitialized) return;

        int previousCount = ConnectedPlayers.Value;
        ConnectedPlayers.Value++;

        Debug.Log($"[GameStateManager] Player joined - Connected: {ConnectedPlayers.Value}/{TargetPlayerCount.Value}, Alive: {AlivePlayers.Value}, IsGameStarted: {IsGameStarted}");

        // 첫 번째 플레이어 입장 → Idle에서 Matching으로 전환
        if (previousCount == 0 && ConnectedPlayers.Value == 1 && !IsGameStarted)
        {
            Debug.Log($"[SESSION_STATUS] MATCHING port={AutoStartServer.ServerPort} players={ConnectedPlayers.Value}");
        }

        if (!IsGameStarted && TargetPlayerCount.Value > 0 && ConnectedPlayers.Value >= TargetPlayerCount.Value)
        {
            Debug.Log($"[GameStateManager] Target player count reached ({ConnectedPlayers.Value}/{TargetPlayerCount.Value})! Transitioning to GamePlay scene...");
            TransitionToGamePlayScene();
        }
    }

    public void OnPlayerLeft(bool wasAlive)
    {
        if (!IsServerInitialized) return;

        ConnectedPlayers.Value = Mathf.Max(ConnectedPlayers.Value - 1, 0);
        if (wasAlive)
        {
            AlivePlayers.Value = Mathf.Max(AlivePlayers.Value - 1, 0);
        }

        bool noClientsRemaining = ConnectedPlayers.Value <= 0;
        if (noClientsRemaining)
        {
            if (IsGameStarted)
            {
                // 게임 중 모든 플레이어 나감 → 세션 종료
                Debug.Log("[GameStateManager] All players left during game. Shutting down session...");
                Debug.Log($"[SESSION_STATUS] ENDED port={AutoStartServer.ServerPort} reason=all_players_left");
                _ = ShutdownSessionImmediately();
            }
            else
            {
                // 게임 시작 전 모든 플레이어 나감 → 대기 상태로 리셋 (Matching → Idle)
                Debug.Log("[GameStateManager] All players left before game started. Resetting to waiting state...");
                Debug.Log($"[SESSION_STATUS] IDLE port={AutoStartServer.ServerPort} reason=all_players_left_before_game");
                ResetToWaitingState();
            }
        }
    }

    public void OnPlayerSpawnedAlive()
    {
        if (!IsServerInitialized) return;
        AlivePlayers.Value++;
        CheckAndStartGame();
    }

    public void OnPlayerDied(NetworkConnection victim, NetworkConnection killer, string weaponID = null)
    {
        if (!IsServerInitialized) return;

        AlivePlayers.Value--;

        string killerName = $"Player{killer?.ClientId ?? 0}";
        string victimName = $"Player{victim?.ClientId ?? 0}";

        RPC_BroadcastKillLog(killerName, victimName, weaponID ?? "");

        if (AlivePlayers.Value <= 1 && TargetPlayerCount.Value > 1)
        {
            EndGame();
        }
    }

    #endregion

    #region Scene Transition

    private void TransitionToGamePlayScene()
    {
        if (!IsServerInitialized) return;
        StartCoroutine(TransitionToGamePlaySceneWithDelay());
    }

    private IEnumerator TransitionToGamePlaySceneWithDelay()
    {
        int gamePlayIndex = UnityEngine.SceneManagement.SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/GamePlay.unity");
        if (gamePlayIndex < 0)
        {
            Debug.LogError("[GameStateManager] GamePlay scene not found in Build Settings!");
            yield break;
        }

        Debug.Log($"[GameStateManager] Match complete! Showing '매칭완료' for 1 second before scene transition...");
        
        RPC_NotifyMatchComplete();
        
        yield return new WaitForSeconds(1f);
        
        Debug.Log($"[GameStateManager] Loading GamePlay scene (index: {gamePlayIndex})...");
        
        RPC_NotifySceneTransition();
        
        // FishNet 씬 로드 - 기존 씬들을 교체하고 GamePlay만 로드
        var sceneLoadData = new FishNet.Managing.Scened.SceneLoadData("GamePlay")
        {
            ReplaceScenes = FishNet.Managing.Scened.ReplaceOption.All
        };
        InstanceFinder.SceneManager.LoadGlobalScenes(sceneLoadData);
    }

    #endregion

    #region Game Control

    private void CheckAndStartGame()
    {
        if (!IsServerInitialized || IsGameStarted) return;

        Debug.Log($"[GameStateManager] CheckAndStartGame - Connected: {ConnectedPlayers.Value}, Alive: {AlivePlayers.Value}, Target: {TargetPlayerCount.Value}");

        if (TargetPlayerCount.Value <= 0)
        {
            Debug.Log("[GameStateManager] TargetPlayerCount not set yet. Waiting for RPC...");
            return;
        }

        // 연습장 모드 (1인)는 즉시 시작
        bool isPracticeMode = TargetPlayerCount.Value == 1;
        if (isPracticeMode)
        {
            Debug.Log("[GameStateManager] Practice mode detected! Starting immediately...");
            bool hasPlayer = ConnectedPlayers.Value >= 1 && AlivePlayers.Value >= 1;
            if (hasPlayer)
            {
                StartGame();
            }
            return;
        }

        bool allConnected = ConnectedPlayers.Value >= TargetPlayerCount.Value;
        bool allAlive = AlivePlayers.Value >= TargetPlayerCount.Value;

        if (allConnected && allAlive)
        {
            Debug.Log("[GameStateManager] Target player count reached! Starting game...");
            StartGame();
        }
    }

    private void StartGame()
    {
        if (!IsServerInitialized || CurrentGameState.Value != GameState.WaitingForPlayers) return;

        Debug.Log("[GameStateManager] All players ready! Starting 5-second countdown...");
        
        CurrentGameState.Value = GameState.Countdown;
        RPC_StartGameCountdown();
        _ = StartGameAfterCountdown();
    }

    private async Task StartGameAfterCountdown()
    {
        await Task.Delay(5000);

        if (!IsServerInitialized || CurrentGameState.Value != GameState.Countdown) return;

        CurrentGameState.Value = GameState.Playing;
        Debug.Log("[GameStateManager] Game started!");
        Debug.Log($"[SESSION_STATUS] PLAYING port={AutoStartServer.ServerPort} players={ConnectedPlayers.Value}");
        
        // NetworkManager에 게임 시작 알림
        if (CustomNetworkManager.Instance != null)
        {
            CustomNetworkManager.Instance.SetGameStarted(true);
        }
    }

    private void EndGame()
    {
        if (!IsServerInitialized || CurrentGameState.Value == GameState.Ended) return;

        CurrentGameState.Value = GameState.Ended;
        Winner.Value = FindLastSurvivor();
        
        Debug.Log($"[SESSION_STATUS] ENDED port={AutoStartServer.ServerPort} reason=game_finished");

        if (Winner.Value != null)
        {
            // 승자에게 승리 애니메이션 재생
            var players = CustomNetworkManager.Instance?.GetSpawnedPlayers();
            if (players != null && players.TryGetValue(Winner.Value, out var winnerObj))
            {
                var winnerCombat = winnerObj?.GetComponent<PlayerCombat>();
                winnerCombat?.PlayVictory();
            }
        }

        RPC_BroadcastGameEnd(Winner.Value);
        
        // 연습장 모드는 즉시 종료
        bool isPracticeMode = TargetPlayerCount.Value == 1;
        if (isPracticeMode)
        {
            OnGameEndedPracticeMode();
        }
        else
        {
            OnGameEnded();
        }
    }

    private void OnGameEnded()
    {
        _ = ShutdownSessionAfterDelay(10f);
    }

    /// <summary>
    /// 연습장 모드 게임 종료 처리
    /// </summary>
    private void OnGameEndedPracticeMode()
    {
        Debug.Log("[GameStateManager] Practice mode ended. Returning to lobby in 3 seconds...");
        _ = ReturnToLobbyAfterDelay(3f);
    }

    private async Task ReturnToLobbyAfterDelay(float delay)
    {
        await Task.Delay((int)(delay * 1000));

        if (CustomNetworkManager.Instance != null)
        {
            await CustomNetworkManager.Instance.ReturnToLobby();
        }
    }

    private NetworkConnection FindLastSurvivor()
    {
        var players = CustomNetworkManager.Instance?.GetSpawnedPlayers();
        if (players == null) return null;

        foreach (var kvp in players)
        {
            var combat = kvp.Value?.GetComponent<PlayerCombat>();
            if (combat != null && combat.IsAlive)
            {
                return kvp.Key;
            }
        }

        return null;
    }

    #endregion

    #region Session Management

    /// <summary>
    /// 세션을 대기 상태로 리셋합니다 (모든 플레이어 나감 / 게임 종료 시).
    /// Why: 서버는 계속 실행되고 새 플레이어를 받을 수 있음
    /// </summary>
    private void ResetToWaitingState()
    {
        Debug.Log("[GameStateManager] Resetting session to waiting state...");

        // 상태 초기화
        CurrentGameState.Value = GameState.WaitingForPlayers;
        CurrentPhase.Value = 1;
        GameElapsedTime.Value = 0f;
        AlivePlayers.Value = 0;
        ConnectedPlayers.Value = 0;
        TargetPlayerCount.Value = 0;
        Winner.Value = null;

        // 세션 시작 시간 리셋
        _sessionStartTime = Time.time;
        _lastSessionUpdateTime = 0f;

        // Unity Sessions 상태 업데이트 (대기 상태로)
        UpdateSessionStatusAsync();

        // TODO: 맵 정리 (NetworkMapManager.ResetMap 등)

        Debug.Log("[GameStateManager] Session reset complete. Ready for new players.");
    }

    private async Task ShutdownSessionAfterDelay(float delay)
    {
        await Task.Delay((int)(delay * 1000));

        if (InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.StopConnection(true);
        }
    }

    private async Task ShutdownSessionImmediately()
    {
        await Task.Delay(100);
        
        if (InstanceFinder.IsServerStarted)
        {
            InstanceFinder.ServerManager.StopConnection(true);
        }
    }

    #endregion

    #region RPC Methods

    [ServerRpc(RequireOwnership = false)]
    public void RPC_SetGameModeInfo(int gameMode, int targetPlayerCount)
    {
        Debug.Log($"[GameStateManager] RPC_SetGameModeInfo received - GameMode: {(GameMode)gameMode}, TargetPlayerCount: {targetPlayerCount}");

        if (TargetPlayerCount.Value == 0)
        {
            TargetPlayerCount.Value = targetPlayerCount;
            Debug.Log($"[GameStateManager] TargetPlayerCount set to: {targetPlayerCount} via RPC");

            GameMode mode = (GameMode)gameMode;
            
            // GameModeConfig 로드 (tier drop rates 등)
            LoadGameModeConfig(mode);
            
            if (CustomNetworkManager.Instance != null)
            {
                CustomNetworkManager.Instance.SetPendingMapInit(mode, targetPlayerCount);
            }
            Debug.Log($"[GameStateManager] Game mode info saved to NetworkManager.");

            // 첫 번째 클라이언트의 GameMode로 Unity Session 업데이트
            _ = UpdateSessionGameModeAsync(mode.ToString(), targetPlayerCount);

            CheckAndTransitionToGamePlay();
        }
    }

    /// <summary>
    /// Unity Session의 GameMode 업데이트 (서버 전용)
    /// </summary>
    private async System.Threading.Tasks.Task UpdateSessionGameModeAsync(string gameMode, int targetPlayers)
    {
        if (UnityLobbyManager.Instance != null && UnityLobbyManager.Instance.IsInSession)
        {
            bool success = await UnityLobbyManager.Instance.UpdateSessionGameModeAsync(gameMode, targetPlayers);
            if (success)
            {
                Debug.Log($"[GameStateManager] Unity Session GameMode updated: {gameMode}, TargetPlayers: {targetPlayers}");
            }
            else
            {
                Debug.LogWarning("[GameStateManager] Failed to update Unity Session GameMode");
            }
        }
    }

    private void CheckAndTransitionToGamePlay()
    {
        if (!IsServerInitialized) return;

        Debug.Log($"[GameStateManager] CheckAndTransitionToGamePlay - Started: {IsGameStarted}, Count: {ConnectedPlayers.Value}/{TargetPlayerCount.Value}");

        if (!IsGameStarted && TargetPlayerCount.Value > 0 && ConnectedPlayers.Value >= TargetPlayerCount.Value)
        {
            Debug.Log($"[GameStateManager] Conditions met! Transitioning to GamePlay scene...");
            TransitionToGamePlayScene();
        }
    }

    [ObserversRpc]
    private void RPC_NotifySceneTransition()
    {
        Debug.Log("[GameStateManager] RPC_NotifySceneTransition received - transitioning to GamePlay scene");

        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.OnServerRequestedSceneTransition();
        }
    }

    [ObserversRpc]
    private void RPC_NotifyMatchComplete()
    {
        Debug.Log("[GameStateManager] RPC_NotifyMatchComplete received - showing '매칭완료' text");

        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.HandleMatchComplete();
        }
    }

    [ObserversRpc]
    private void RPC_StartGameCountdown()
    {
        Debug.Log("[GameStateManager] RPC_StartGameCountdown received - showing countdown UI");

        if (LoadingUIManager.Instance != null)
        {
            LoadingUIManager.Instance.ShowCountdown(5f);
        }
    }

    [ObserversRpc]
    private void RPC_BroadcastKillLog(string killerName, string victimName, string weaponID)
    {
        if (UIManager.Instance != null)
        {
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

    [ObserversRpc]
    private void RPC_BroadcastGameEnd(NetworkConnection winner)
    {
        PlayerCombat localCombat = FindLocalPlayerCombat();
        if (localCombat == null) return;

        NetworkObject localNetObj = localCombat.GetComponent<NetworkObject>();
        if (localNetObj == null) return;
        
        int killCount = localCombat.KillCount.Value;
        float survivalTime = GameElapsedTime.Value;

        if (localNetObj.Owner == winner)
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowVictory(killCount, survivalTime);
            }
        }
        else
        {
            int rank = AlivePlayers.Value + 1;
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowDefeat(killCount, survivalTime, rank);
            }
        }
    }

    #endregion

    #region Helper Methods

    private PlayerCombat FindLocalPlayerCombat()
    {
        // 로컬 플레이어 찾기
        var combats = FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None);
        foreach (var combat in combats)
        {
            if (combat.IsOwner)
            {
                return combat;
            }
        }
        return null;
    }

    private void CalculateCurrentPhase()
    {
        if (_phaseConfigs == null || _phaseConfigs.Length == 0)
        {
            return;
        }

        float elapsed = GameElapsedTime.Value;
        float accumulated = 0f;

        for (int i = 0; i < _phaseConfigs.Length; i++)
        {
            float phaseDuration;

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
                if (newPhase != CurrentPhase.Value)
                {
                    CurrentPhase.Value = newPhase;
                }
                return;
            }
        }

        int finalPhase = _phaseConfigs.Length;
        if (CurrentPhase.Value != finalPhase)
        {
            CurrentPhase.Value = finalPhase;
        }
    }

    public float GetShrinkRateForPhase(int phase)
    {
        int index = phase - 1;
        if (_phaseConfigs == null || index < 0 || index >= _phaseConfigs.Length)
        {
            return 0f;
        }

        return _phaseConfigs[index].ShrinkRate;
    }

    public void SetPendingMapInit(GameMode gameMode, int playerCount)
    {
        _pendingGameMode = gameMode;
        _pendingPlayerCount = playerCount;
        
        // TargetPlayerCount SyncVar 설정 (서버에서만 호출됨)
        if (IsServerInitialized && TargetPlayerCount.Value == 0)
        {
            TargetPlayerCount.Value = playerCount;
        }
        
        // 게임모드 설정 로드
        LoadGameModeConfig(gameMode);
    }

    /// <summary>
    /// 게임모드에 해당하는 설정을 로드합니다.
    /// </summary>
    private void LoadGameModeConfig(GameMode mode)
    {
        if (_gameModeConfigs == null || _gameModeConfigs.Length == 0)
        {
            Debug.LogWarning($"[GameStateManager] GameModeConfigs가 설정되지 않았습니다!");
            return;
        }

        _currentGameModeConfig = System.Array.Find(_gameModeConfigs, c => c != null && c.GameMode == mode);
        
        if (_currentGameModeConfig != null)
        {
            _totalGameTime = _currentGameModeConfig.TotalGameTime;
            
            if (_currentGameModeConfig.PhaseConfigs != null && _currentGameModeConfig.PhaseConfigs.Length > 0)
            {
                _phaseConfigs = _currentGameModeConfig.PhaseConfigs;
            }
            
            Debug.Log($"[GameStateManager] {mode} 모드 설정 로드: TotalGameTime={_totalGameTime}s, Phases={_phaseConfigs?.Length}");
        }
        else
        {
            Debug.LogWarning($"[GameStateManager] {mode} 모드의 설정을 찾을 수 없습니다. 기본값 사용.");
        }
    }

    /// <summary>
    /// 현재 경과 시간에 해당하는 등급 드랍 확률을 반환합니다.
    /// </summary>
    public TierDropRateByTime GetCurrentTierDropRates()
    {
        // 이미 초 단위로 저장됨
        float elapsedSeconds = GameElapsedTime.Value;

        if (_currentGameModeConfig != null)
        {
            return _currentGameModeConfig.GetTierDropRateForTime(elapsedSeconds);
        }

        // 기본값 반환 (노말 100%)
        return new TierDropRateByTime
        {
            TimeThresholdSeconds = 0f,
            NormalRate = 100f,
            RareRate = 0f,
            UniqueRate = 0f,
            EpicRate = 0f,
            LegendaryRate = 0f
        };
    }

    #endregion
}
