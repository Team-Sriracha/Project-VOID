using System;
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

    [Header("사운드")]
    [SerializeField] private AudioCue _phaseCountdownAudioCue;
    [SerializeField] private AudioCue _phaseChangedAudioCue;
    [SerializeField] [Min(1f)] private float _phaseWarningLeadTime = 10f;

    #endregion

    #region Private Fields (GameModeConfig에서 로드)

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
    public readonly SyncVar<GameMode> CurrentGameModeSync = new();
    
    /// <summary>
    /// 전체 게임 시간 (초) - GameModeConfig에서 로드, 클라이언트 동기화
    /// </summary>
    public readonly SyncVar<float> SyncTotalGameTime = new(600f);

    // Why: 기존 코드 호환성을 위한 프로퍼티
    public bool IsGameStarted => CurrentGameState.Value == GameState.Playing;
    public bool IsGameEnded => CurrentGameState.Value == GameState.Ended;
    public GameMode CurrentGameMode => CurrentGameModeSync.Value;

    #endregion

    #region Private Fields

    private const float MIN_SESSION_TIME_BEFORE_AUTO_SHUTDOWN = 5f;
    private const int MIN_CUSTOM_PLAYER_COUNT = 2;
    private const int MAX_CUSTOM_PLAYER_COUNT = 8;
    private const int MIN_CUSTOM_GAME_TIME_SECONDS = 180;
    private const int MAX_CUSTOM_GAME_TIME_SECONDS = 900;
    private const float DEFAULT_TOTAL_GAME_TIME_SECONDS = 600f;
    private const float DEFAULT_IDENTITY_VERIFICATION_TIMEOUT_SECONDS = 15f;
    private float _sessionStartTime;
    
    private GameMode _pendingGameMode;
    private int _pendingPlayerCount;
    private string _pendingMapTemplateName = string.Empty;

    /// <summary>
    /// 현재 적용된 게임모드 설정
    /// </summary>
    private GameModeConfig _currentGameModeConfig;
    private DateTime _matchStartedAtUtc = DateTime.UtcNow;
    private bool _isMatchResultCommitInProgress;
    private bool _isMatchResultCommitted;
    private TaskCompletionSource<bool> _identityVerificationTaskSource;
    private string _lastIdentityVerificationErrorMessage = string.Empty;
    private int _lastObservedPhase = -1;
    private int _lastPhaseCountdownSecond = -1;
    private bool _hasInitializedPhaseAudioState;

    #endregion

    #region Properties

    public float TotalGameTime => SyncTotalGameTime.Value;
    public float RemainingTime => Mathf.Max(0f, SyncTotalGameTime.Value - GameElapsedTime.Value);
    /// <summary>
    /// 마지막 서버 신원 검증 실패 메시지를 반환합니다.
    /// </summary>
    public string LastIdentityVerificationErrorMessage => _lastIdentityVerificationErrorMessage;

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
            _matchStartedAtUtc = DateTime.UtcNow;
            _isMatchResultCommitInProgress = false;
            _isMatchResultCommitted = false;

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
        UpdatePhaseAudio();

        // 서버에서만 처리
        if (!IsServerInitialized) return;

        // 게임 시작 후에만 시간 업데이트
        if (IsGameStarted)
        {
            GameElapsedTime.Value += Time.deltaTime;
            CalculateCurrentPhase();

            if (GameElapsedTime.Value >= SyncTotalGameTime.Value)
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

    private void UpdatePhaseAudio()
    {
        if (!IsClientInitialized || Application.isBatchMode)
        {
            return;
        }

        if (CurrentGameState.Value != GameState.Playing)
        {
            _lastObservedPhase = CurrentPhase.Value;
            _lastPhaseCountdownSecond = -1;
            _hasInitializedPhaseAudioState = false;
            return;
        }

        if (!_hasInitializedPhaseAudioState)
        {
            _lastObservedPhase = CurrentPhase.Value;
            _lastPhaseCountdownSecond = Mathf.CeilToInt(TimeToNextPhase);
            _hasInitializedPhaseAudioState = true;
            return;
        }

        if (CurrentPhase.Value > _lastObservedPhase)
        {
            AudioManager.Instance?.PlayUi(_phaseChangedAudioCue);
            _lastObservedPhase = CurrentPhase.Value;
            _lastPhaseCountdownSecond = -1;
        }

        if (IsLastPhase)
        {
            return;
        }

        int countdownSecond = Mathf.CeilToInt(TimeToNextPhase);
        int warningLeadTime = Mathf.Max(1, Mathf.CeilToInt(_phaseWarningLeadTime));

        if (countdownSecond > 0 && countdownSecond <= warningLeadTime && countdownSecond != _lastPhaseCountdownSecond)
        {
            AudioManager.Instance?.PlayUi(_phaseCountdownAudioCue);
            _lastPhaseCountdownSecond = countdownSecond;
        }
        else if (countdownSecond > warningLeadTime)
        {
            _lastPhaseCountdownSecond = countdownSecond;
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
            Debug.Log($"[SESSION_STATUS] MATCHING port={ServerRuntimeInfo.ServerPort} players={ConnectedPlayers.Value}");
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

        bool isMatchActiveOrEnded = IsGameStarted || IsGameEnded;
        if (isMatchActiveOrEnded)
        {
            // 실제 스폰 플레이어 기준으로 카운트 재동기화 (중복 이벤트/순서 이슈 보정)
            SyncRuntimePlayerCountsFromSpawnedPlayers();

            bool noClientsRemaining = ConnectedPlayers.Value <= 0;
            if (noClientsRemaining)
            {
                // 경기 진행 중/종료 후 모든 플레이어 나감 → 세션 종료
                Debug.Log("[GameStateManager] All players left during/after match. Shutting down session...");
                Debug.Log($"[SESSION_STATUS] ENDED port={ServerRuntimeInfo.ServerPort} reason=all_players_left");
                _ = ShutdownSessionImmediately();
                return;
            }

            if (ShouldEndByElimination())
            {
                EndGame();
            }

            return;
        }

        bool noClientsBeforeStart = ConnectedPlayers.Value <= 0;
        if (noClientsBeforeStart)
        {
            // 게임 시작 전 모든 플레이어 나감 → 대기 상태로 리셋 (Matching → Idle)
            Debug.Log("[GameStateManager] All players left before game started. Resetting to waiting state...");
            Debug.Log($"[SESSION_STATUS] IDLE port={ServerRuntimeInfo.ServerPort} reason=all_players_left_before_game");
            ResetToWaitingState();
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

        AlivePlayers.Value = Mathf.Max(AlivePlayers.Value - 1, 0);

        // 실제 스폰 플레이어 기준으로 카운트 재동기화 (조기 종료 방지)
        SyncRuntimePlayerCountsFromSpawnedPlayers();

        string killerName = $"Player{killer?.ClientId ?? 0}";
        string victimName = $"Player{victim?.ClientId ?? 0}";

        RPC_BroadcastKillLog(killerName, victimName, weaponID ?? "");

        if (ShouldEndByElimination())
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

        // 연습장 모드는 모드 정의를 기준으로 판별 (하위 호환으로 TargetPlayerCount=1도 허용)
        bool isPracticeMode = IsPracticeModeSession();
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

        if (IsPracticeModeSession())
        {
            Debug.Log("[GameStateManager] Practice mode started immediately without countdown.");
            CompleteGameStart();
            return;
        }

        Debug.Log("[GameStateManager] All players ready! Starting 5-second countdown...");
        
        CurrentGameState.Value = GameState.Countdown;
        RPC_StartGameCountdown();
        _ = StartGameAfterCountdown();
    }

    private async Task StartGameAfterCountdown()
    {
        await Task.Delay(5000);

        if (!IsServerInitialized || CurrentGameState.Value != GameState.Countdown) return;

        CompleteGameStart();
    }

    private void CompleteGameStart()
    {
        if (!IsServerInitialized || CurrentGameState.Value == GameState.Playing)
        {
            return;
        }

        CurrentGameState.Value = GameState.Playing;
        Debug.Log("[GameStateManager] Game started!");
        Debug.Log($"[SESSION_STATUS] PLAYING port={ServerRuntimeInfo.ServerPort} players={ConnectedPlayers.Value}");
        _matchStartedAtUtc = DateTime.UtcNow;
        _isMatchResultCommitInProgress = false;
        _isMatchResultCommitted = false;
        
        // NetworkManager에 게임 시작 알림
        if (CustomNetworkManager.Instance != null)
        {
            CustomNetworkManager.Instance.SetGameStarted(true);
        }

        if (UnityLobbyManager.Instance != null)
        {
            _ = UnityLobbyManager.Instance.MarkGameStarted();
        }

        UpdateSessionStatusAsync();
        RPC_NotifyGameStarted();
    }

    private void EndGame()
    {
        if (!IsServerInitialized || CurrentGameState.Value == GameState.Ended) return;

        SyncRuntimePlayerCountsFromSpawnedPlayers();

        bool isTimeLimitTie = IsTimeLimitTie();
        CurrentGameState.Value = GameState.Ended;
        Winner.Value = isTimeLimitTie ? null : FindLastSurvivor();
        
        Debug.Log($"[SESSION_STATUS] ENDED port={ServerRuntimeInfo.ServerPort} reason=game_finished");

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

        bool isPracticeMode = IsPracticeModeSession();
        if (!isPracticeMode)
        {
            _ = CommitMatchResultAsync(isTimeLimitTie);
        }

        UpdateSessionStatusAsync();
        RPC_BroadcastGameEnd(Winner.Value, isTimeLimitTie);
        
        // 연습장 모드는 즉시 종료
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

        NetworkConnection lastAlive = null;
        int aliveCount = 0;

        foreach (var kvp in players)
        {
            var combat = kvp.Value?.GetComponent<PlayerCombat>();
            if (combat != null && combat.IsAlive)
            {
                aliveCount++;
                if (aliveCount > 1)
                {
                    return null;
                }

                lastAlive = kvp.Key;
            }
        }

        return aliveCount == 1 ? lastAlive : null;
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

        if (CustomNetworkManager.Instance != null)
        {
            CustomNetworkManager.Instance.ResetSessionRuntimeObjects();
        }

        // 상태 초기화
        CurrentGameState.Value = GameState.WaitingForPlayers;
        CurrentPhase.Value = 1;
        GameElapsedTime.Value = 0f;
        SyncTotalGameTime.Value = DEFAULT_TOTAL_GAME_TIME_SECONDS;
        AlivePlayers.Value = 0;
        ConnectedPlayers.Value = 0;
        TargetPlayerCount.Value = 0;
        Winner.Value = null;
        CurrentGameModeSync.Value = GameMode.None;
        _phaseConfigs = new PhaseConfig[]
        {
            new PhaseConfig { Duration = 120f, ShrinkRate = 0f },
            new PhaseConfig { Duration = 120f, ShrinkRate = 0.25f },
            new PhaseConfig { Duration = 120f, ShrinkRate = 0.40f },
            new PhaseConfig { Duration = 0f, ShrinkRate = 1.0f },
        };
        _pendingGameMode = GameMode.None;
        _pendingPlayerCount = 0;
        _pendingMapTemplateName = string.Empty;
        _currentGameModeConfig = null;
        _matchStartedAtUtc = DateTime.UtcNow;
        _isMatchResultCommitInProgress = false;
        _isMatchResultCommitted = false;

        // 세션 시작 시간 리셋
        _sessionStartTime = Time.time;
        _lastSessionUpdateTime = 0f;

        // Unity Sessions 메타데이터와 상태를 대기 세션 기본값으로 복원
        _ = ResetUnitySessionToWaitingStateAsync();

        // TODO: 맵 정리 (NetworkMapManager.ResetMap 등)

        Debug.Log("[GameStateManager] Session reset complete. Ready for new players.");
    }

    private async Task ResetUnitySessionToWaitingStateAsync()
    {
        if (UnityLobbyManager.Instance == null || !UnityLobbyManager.Instance.IsInSession)
        {
            return;
        }

        // 대기 세션은 GameMode를 빈 값으로 두어 다음 클라이언트가 모드를 재설정하도록 합니다.
        await UpdateSessionGameModeAsync(string.Empty, 0, 0, string.Empty);

        await UnityLobbyManager.Instance.UpdateSessionStatus(
            GameState.WaitingForPlayers.ToString(),
            1,
            0,
            0,
            0f,
            false,
            false);
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

    #region Identity Verification

    /// <summary>
    /// 서버에 ID Token 검증을 요청하고 결과를 반환합니다.
    /// </summary>
    /// <param name="idToken">검증할 ID Token</param>
    /// <param name="timeoutSeconds">검증 타임아웃(초)</param>
    /// <returns>신원 검증 성공 여부</returns>
    public async Task<bool> VerifyIdentityOnServerAsync(string idToken, float timeoutSeconds = DEFAULT_IDENTITY_VERIFICATION_TIMEOUT_SECONDS)
    {
        _lastIdentityVerificationErrorMessage = string.Empty;

        if (!IsClientInitialized)
        {
            _lastIdentityVerificationErrorMessage = "클라이언트 네트워크가 초기화되지 않았습니다.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(idToken))
        {
            _lastIdentityVerificationErrorMessage = "ID Token이 비어 있습니다.";
            return false;
        }

        _identityVerificationTaskSource?.TrySetCanceled();
        _identityVerificationTaskSource = new TaskCompletionSource<bool>();

        ServerRpc_RequestIdentityVerification(idToken);

        int timeoutMilliseconds = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds * 1000f));
        Task completedTask = await Task.WhenAny(_identityVerificationTaskSource.Task, Task.Delay(timeoutMilliseconds));
        if (completedTask != _identityVerificationTaskSource.Task)
        {
            _lastIdentityVerificationErrorMessage = "신원 검증 응답 시간 초과";
            _identityVerificationTaskSource = null;
            return false;
        }

        bool isSuccess = await _identityVerificationTaskSource.Task;
        _identityVerificationTaskSource = null;
        if (!isSuccess && string.IsNullOrWhiteSpace(_lastIdentityVerificationErrorMessage))
        {
            _lastIdentityVerificationErrorMessage = "신원 검증에 실패했습니다.";
        }

        return isSuccess;
    }

    #endregion

    #region RPC Methods

    [ServerRpc(RequireOwnership = false)]
    private void ServerRpc_RequestIdentityVerification(string idToken, NetworkConnection sender = null)
    {
        _ = VerifyIdentityForConnectionAsync(sender, idToken);
    }

    private async Task VerifyIdentityForConnectionAsync(NetworkConnection sender, string idToken)
    {
        if (sender == null || !sender.IsValid)
        {
            return;
        }

        if (CustomNetworkManager.Instance == null)
        {
            TargetRpc_IdentityVerificationResult(sender, false, "NetworkManager 인스턴스를 찾을 수 없습니다.");
            return;
        }

        NetworkManager.IdentityVerificationResult verificationResult =
            await CustomNetworkManager.Instance.TryVerifyAndRegisterIdentityWithResultAsync(sender, idToken, false);
        TargetRpc_IdentityVerificationResult(
            sender,
            verificationResult.IsSuccess,
            verificationResult.ErrorMessage);
    }

    [TargetRpc]
    private void TargetRpc_IdentityVerificationResult(NetworkConnection conn, bool isSuccess, string errorMessage)
    {
        _lastIdentityVerificationErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? string.Empty
            : errorMessage.Trim();
        _identityVerificationTaskSource?.TrySetResult(isSuccess);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RPC_SetGameModeInfo(int gameMode, int targetPlayerCount, int customGameTimeSeconds, string customMapTemplateName)
    {
        Debug.Log($"[GameStateManager] RPC_SetGameModeInfo received - GameMode: {(GameMode)gameMode}, TargetPlayerCount: {targetPlayerCount}, CustomTime: {customGameTimeSeconds}, CustomMap: {customMapTemplateName}");

        if (TargetPlayerCount.Value == 0)
        {
            GameMode mode = (GameMode)gameMode;
            int resolvedTargetPlayerCount = Mathf.Max(1, targetPlayerCount);
            int resolvedCustomGameTimeSeconds = 0;
            string resolvedCustomMapTemplateName = string.Empty;

            if (mode == GameMode.Custom)
            {
                resolvedTargetPlayerCount = Mathf.Clamp(resolvedTargetPlayerCount, MIN_CUSTOM_PLAYER_COUNT, MAX_CUSTOM_PLAYER_COUNT);

                if (customGameTimeSeconds > 0)
                {
                    resolvedCustomGameTimeSeconds = Mathf.Clamp(customGameTimeSeconds, MIN_CUSTOM_GAME_TIME_SECONDS, MAX_CUSTOM_GAME_TIME_SECONDS);
                }

                resolvedCustomMapTemplateName = string.IsNullOrWhiteSpace(customMapTemplateName)
                    ? string.Empty
                    : customMapTemplateName.Trim();
            }

            TargetPlayerCount.Value = resolvedTargetPlayerCount;
            Debug.Log($"[GameStateManager] TargetPlayerCount set to: {resolvedTargetPlayerCount} via RPC");
            
            // SyncVar 업데이트
            if (IsServerInitialized)
            {
                CurrentGameModeSync.Value = mode;
            }

            // GameModeConfig 로드 (tier drop rates 등)
            LoadGameModeConfig(mode, resolvedTargetPlayerCount);

            if (mode == GameMode.Custom && resolvedCustomGameTimeSeconds > 0)
            {
                SyncTotalGameTime.Value = resolvedCustomGameTimeSeconds;
                Debug.Log($"[GameStateManager] Custom game time override applied: {resolvedCustomGameTimeSeconds}초");
            }
            
            if (CustomNetworkManager.Instance != null)
            {
                CustomNetworkManager.Instance.SetPendingMapInit(mode, resolvedTargetPlayerCount, resolvedCustomMapTemplateName);
            }
            Debug.Log($"[GameStateManager] Game mode info saved to NetworkManager.");

            // 첫 번째 클라이언트의 GameMode로 Unity Session 업데이트
            string modeId = GameModeCatalog.GetModeId(mode);
            _ = UpdateSessionGameModeAsync(modeId, resolvedTargetPlayerCount, resolvedCustomGameTimeSeconds, resolvedCustomMapTemplateName);

            CheckAndTransitionToGamePlay();
        }
    }

    /// <summary>
    /// Unity Session의 GameMode 업데이트 (서버 전용)
    /// </summary>
    private async System.Threading.Tasks.Task UpdateSessionGameModeAsync(string gameMode, int targetPlayers, int customGameTimeSeconds, string customMapTemplateName)
    {
        if (UnityLobbyManager.Instance != null && UnityLobbyManager.Instance.IsInSession)
        {
            bool success = await UnityLobbyManager.Instance.UpdateSessionGameModeAsync(
                gameMode,
                targetPlayers,
                customGameTimeSeconds,
                customMapTemplateName);
            if (success)
            {
                Debug.Log($"[GameStateManager] Unity Session GameMode updated: {gameMode}, TargetPlayers: {targetPlayers}, CustomTime: {customGameTimeSeconds}, CustomMap: {customMapTemplateName}");
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
    private void RPC_NotifyGameStarted()
    {
        if (MatchmakingManager.Instance != null)
        {
            MatchmakingManager.Instance.HandleGameStartedImmediate();
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
    private void RPC_BroadcastGameEnd(NetworkConnection winner, bool isTimeLimitTie)
    {
        PlayerCombat localCombat = FindLocalPlayerCombat();
        if (localCombat == null) return;

        NetworkObject localNetObj = localCombat.GetComponent<NetworkObject>();
        if (localNetObj == null) return;
        
        int killCount = localCombat.KillCount.Value;

        // 사망자는 사망 시점의 패배 UI(순위)를 이미 받았으므로 종료 시 다시 덮어쓰지 않음
        if (!localCombat.IsAlive)
        {
            return;
        }

        if (isTimeLimitTie)
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowTie(killCount, 1);
            }
            return;
        }

        if (localNetObj.Owner == winner)
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowVictory(killCount);
            }
        }
        else
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowDefeat(killCount, 2);
            }
        }
    }

    #endregion

    #region Helper Methods

    private async Task CommitMatchResultAsync(bool isTimeLimitTie)
    {
        if (!IsServerInitialized || _isMatchResultCommitted || _isMatchResultCommitInProgress)
        {
            return;
        }

        ServerMatchResult result = BuildServerMatchResult(isTimeLimitTie);
        if (result == null || result.Players.Count == 0)
        {
            Debug.LogWarning("[GameStateManager] 커밋할 매치 결과 데이터가 없어 전적 커밋을 건너뜁니다.");
            return;
        }

        if (!ServiceLocator.TryGet<IServerMatchResultService>(out IServerMatchResultService matchResultService) ||
            matchResultService == null)
        {
            Debug.LogWarning("[GameStateManager] IServerMatchResultService가 없어 백엔드 전적 커밋에 실패했습니다.");
            return;
        }

        _isMatchResultCommitInProgress = true;
        bool isSuccess = await matchResultService.CommitMatchResultAsync(result);
        _isMatchResultCommitInProgress = false;

        if (!isSuccess)
        {
            Debug.LogWarning($"[GameStateManager] 매치 결과 커밋 실패: MatchId={result.MatchId}");
            return;
        }

        _isMatchResultCommitted = true;
        Debug.Log($"[GameStateManager] 매치 결과 커밋 성공: MatchId={result.MatchId}, Players={result.Players.Count}");
    }

    private ServerMatchResult BuildServerMatchResult(bool isTimeLimitTie)
    {
        IReadOnlyDictionary<NetworkConnection, NetworkObject> players = CustomNetworkManager.Instance?.GetSpawnedPlayers();
        if (players == null || players.Count == 0)
        {
            return null;
        }

        NetworkConnection winnerConnection = isTimeLimitTie ? null : Winner.Value;

        List<MatchEntry> entries = new List<MatchEntry>();
        foreach (KeyValuePair<NetworkConnection, NetworkObject> pair in players)
        {
            NetworkConnection connection = pair.Key;
            NetworkObject playerObject = pair.Value;
            if (connection == null || playerObject == null)
            {
                continue;
            }

            if (!playerObject.TryGetComponent<PlayerCombat>(out PlayerCombat combat))
            {
                continue;
            }

            if (!TryResolveMatchIdentity(connection, out string uid, out bool isGuest))
            {
                continue;
            }

            entries.Add(new MatchEntry
            {
                Connection = connection,
                Combat = combat,
                Uid = uid,
                IsGuest = isGuest
            });
        }

        // Why: 게스트 플레이 데이터는 영구 저장 정책에서 제외합니다.
        entries.RemoveAll(entry => entry == null || entry.IsGuest);
        if (entries.Count == 0)
        {
            return null;
        }

        if (winnerConnection != null && entries.All(entry => entry.Connection != winnerConnection))
        {
            winnerConnection = null;
        }

        string winnerUid = string.Empty;
        if (winnerConnection != null)
        {
            MatchEntry winnerEntry = entries.Find(entry => entry.Connection == winnerConnection);
            winnerUid = winnerEntry != null ? winnerEntry.Uid : string.Empty;
        }

        entries.Sort((left, right) =>
        {
            bool leftIsWinner = winnerConnection != null && left.Connection == winnerConnection;
            bool rightIsWinner = winnerConnection != null && right.Connection == winnerConnection;
            if (leftIsWinner != rightIsWinner)
            {
                return rightIsWinner.CompareTo(leftIsWinner);
            }

            int aliveCompare = right.Combat.IsAlive.CompareTo(left.Combat.IsAlive);
            if (aliveCompare != 0)
            {
                return aliveCompare;
            }

            int killCompare = right.Combat.KillCount.Value.CompareTo(left.Combat.KillCount.Value);
            if (killCompare != 0)
            {
                return killCompare;
            }

            return left.Connection.ClientId.CompareTo(right.Connection.ClientId);
        });

        ServerMatchResult result = new ServerMatchResult
        {
            MatchId = GenerateMatchId(),
            Mode = CurrentGameModeSync.Value.ToString(),
            StartedAtUtc = _matchStartedAtUtc,
            EndedAtUtc = DateTime.UtcNow,
            WinnerUid = winnerUid
        };

        bool hasExplicitWinner = winnerConnection != null;
        int nextRank = 2;
        for (int i = 0; i < entries.Count; i++)
        {
            MatchEntry entry = entries[i];
            bool isWinner = hasExplicitWinner && entry.Connection == winnerConnection;
            bool isTieWinner = isTimeLimitTie && entry.Combat.IsAlive;
            bool isFallbackWinner = !hasExplicitWinner && !isTimeLimitTie && i == 0;

            int rank;
            if (isWinner || isTieWinner || isFallbackWinner)
            {
                rank = 1;
            }
            else
            {
                rank = nextRank;
                nextRank++;
            }

            result.Players.Add(new ServerMatchPlayerResult
            {
                Uid = entry.Uid,
                Kills = entry.Combat.KillCount.Value,
                Deaths = entry.Combat.IsAlive ? 0 : 1,
                Rank = rank,
                IsGuest = entry.IsGuest,
                IsDraw = isTimeLimitTie && rank == 1
            });
        }

        return result;
    }

    private bool TryResolveMatchIdentity(NetworkConnection connection, out string uid, out bool isGuest)
    {
        uid = string.Empty;
        isGuest = false;

        if (connection == null)
        {
            return false;
        }

        if (CustomNetworkManager.Instance != null &&
            CustomNetworkManager.Instance.TryGetPlayerIdentity(connection, out PlayerIdentity identity) &&
            identity != null)
        {
            isGuest = identity.IsAnonymous;
            if (!string.IsNullOrWhiteSpace(identity.FirebaseUid))
            {
                uid = identity.FirebaseUid.Trim();
                return true;
            }

            Debug.LogWarning($"[GameStateManager] FirebaseUid가 비어 있어 전적 집계에서 제외합니다. ClientId={connection.ClientId}, Guest={isGuest}");
            return false;
        }

        Debug.LogWarning($"[GameStateManager] PlayerIdentity가 없어 전적 집계에서 제외합니다. ClientId={connection.ClientId}");
        return false;
    }

    private static string GenerateMatchId()
    {
        return $"match_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
    }

    private sealed class MatchEntry
    {
        public NetworkConnection Connection;
        public PlayerCombat Combat;
        public string Uid;
        public bool IsGuest;
    }

    private void SyncRuntimePlayerCountsFromSpawnedPlayers()
    {
        var players = CustomNetworkManager.Instance?.GetSpawnedPlayers();
        if (players == null)
        {
            return;
        }

        int connectedCount = players.Count;
        int aliveCount = 0;

        foreach (var kvp in players)
        {
            if (kvp.Value == null) continue;

            if (kvp.Value.TryGetComponent<PlayerCombat>(out var combat) && combat.IsAlive)
            {
                aliveCount++;
            }
        }

        if (ConnectedPlayers.Value != connectedCount)
        {
            Debug.LogWarning($"[GameStateManager] ConnectedPlayers corrected: {ConnectedPlayers.Value} -> {connectedCount}");
            ConnectedPlayers.Value = connectedCount;
        }

        if (AlivePlayers.Value != aliveCount)
        {
            Debug.LogWarning($"[GameStateManager] AlivePlayers corrected: {AlivePlayers.Value} -> {aliveCount}");
            AlivePlayers.Value = aliveCount;
        }
    }

    private bool ShouldEndByElimination()
    {
        return IsGameStarted &&
               !IsGameEnded &&
               TargetPlayerCount.Value > 1 &&
               AlivePlayers.Value <= 1;
    }

    private bool IsTimeLimitReached()
    {
        return GameElapsedTime.Value >= SyncTotalGameTime.Value - 0.01f;
    }

    private bool IsTimeLimitTie()
    {
        return IsTimeLimitReached() && AlivePlayers.Value > 1;
    }

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

    private bool IsPracticeModeSession()
    {
        return GameModeCatalog.IsPracticeMode(CurrentGameModeSync.Value) ||
               (CurrentGameModeSync.Value == GameMode.None && TargetPlayerCount.Value == 1);
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
                phaseDuration = Mathf.Max(0f, SyncTotalGameTime.Value - accumulated);
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

    public void SetPendingMapInit(GameMode gameMode, int playerCount, string mapTemplateName = "")
    {
        _pendingGameMode = gameMode;
        _pendingPlayerCount = playerCount;
        _pendingMapTemplateName = string.IsNullOrWhiteSpace(mapTemplateName) ? string.Empty : mapTemplateName.Trim();
        Debug.Log($"[GameStateManager] SetPendingMapInit - Mode: {_pendingGameMode}, Players: {_pendingPlayerCount}, MapTemplate: {_pendingMapTemplateName}");
        
        // TargetPlayerCount SyncVar 설정 (서버에서만 호출됨)
        if (IsServerInitialized && TargetPlayerCount.Value == 0)
        {
            TargetPlayerCount.Value = playerCount;
        }
        
        // 게임모드 설정 로드
        LoadGameModeConfig(gameMode, playerCount);
        
        // SyncVar 업데이트
        if (IsServerInitialized)
        {
            CurrentGameModeSync.Value = gameMode;
        }
    }

    /// <summary>
    /// 게임모드에 해당하는 설정을 로드합니다.
    /// </summary>
    private void LoadGameModeConfig(GameMode mode, int playerCount = 0)
    {
        if (_gameModeConfigs == null || _gameModeConfigs.Length == 0)
        {
            Debug.LogWarning($"[GameStateManager] GameModeConfigs가 설정되지 않았습니다!");
            return;
        }

        _currentGameModeConfig = null;

        if (playerCount > 0)
        {
            _currentGameModeConfig = System.Array.Find(_gameModeConfigs,
                c => c != null && c.GameMode == mode && c.SupportsPlayerCount(playerCount));
        }

        if (_currentGameModeConfig == null)
        {
            _currentGameModeConfig = System.Array.Find(_gameModeConfigs, c => c != null && c.GameMode == mode);
        }

        // 랭크 전용 설정이 없으면 일반 8인 설정으로 폴백
        if (_currentGameModeConfig == null && mode == GameMode.Ranked)
        {
            if (playerCount > 0)
            {
                _currentGameModeConfig = System.Array.Find(_gameModeConfigs,
                    c => c != null && c.GameMode == GameMode.EightPlayer && c.SupportsPlayerCount(playerCount));
            }

            if (_currentGameModeConfig == null)
            {
                _currentGameModeConfig = System.Array.Find(_gameModeConfigs, c => c != null && c.GameMode == GameMode.EightPlayer);
            }
            if (_currentGameModeConfig != null)
            {
                Debug.LogWarning("[GameStateManager] Ranked 설정이 없어 EightPlayer 설정으로 대체합니다.");
            }
        }
        
        if (_currentGameModeConfig != null)
        {
            SyncTotalGameTime.Value = _currentGameModeConfig.TotalGameTime;
            
            if (_currentGameModeConfig.PhaseConfigs != null && _currentGameModeConfig.PhaseConfigs.Length > 0)
            {
                _phaseConfigs = _currentGameModeConfig.PhaseConfigs;
            }
            
            Debug.Log($"[GameStateManager] {mode} 모드 설정 로드: TotalGameTime={SyncTotalGameTime.Value}s, Phases={_phaseConfigs?.Length}");
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
