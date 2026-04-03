using FishNet;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// 디버그 UI: FPS, RTT, 서버 세션 정보를 화면에 표시
/// F3 키로 토글 가능
/// </summary>
public class DebugUIManager : MonoBehaviour
{
    #region Singleton

    private static DebugUIManager _instance;
    public static DebugUIManager Instance => _instance;

    #endregion

    #region Serialized Fields

    [Header("UI 설정")]
    [SerializeField] private GameObject _debugPanel;
    [SerializeField] private TextMeshProUGUI _debugText;
    [SerializeField] private Button _toggleDebugButton;
    [SerializeField] private Key _toggleKey = Key.F3;

    [Header("업데이트 설정")]
    [SerializeField] private float _updateInterval = 0.1f;

    [Header("사운드")]
    [SerializeField] private AudioCue _toggleAudioCue;

    #endregion

    #region Private Fields

    private bool _isVisible = false;
    private float _lastUpdateTime = 0f;
    
    // FPS 계산용
    private int _frameCount = 0;
    private float _fpsAccumulatedTime = 0f;
    private float _currentFPS = 0f;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            // 초기 상태: 숨김
            if (_debugPanel != null)
            {
                _debugPanel.SetActive(false);
            }

            // 버튼 리스너 연결
            if (_toggleDebugButton != null)
            {
                _toggleDebugButton.onClick.AddListener(ToggleDebugPanel);
            }
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Update()
    {
        // 토글 키 처리
        if (_toggleKey != Key.None && Keyboard.current != null && Keyboard.current[_toggleKey].wasPressedThisFrame)
        {
            ToggleDebugPanel();
        }

        // FPS 계산 (항상)
        CalculateFPS();

        // UI가 보일 때만 업데이트
        if (_isVisible && Time.time - _lastUpdateTime >= _updateInterval)
        {
            _lastUpdateTime = Time.time;
            UpdateDebugText();
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 디버그 패널 토글
    /// </summary>
    public void ToggleDebugPanel()
    {
        AudioManager.Instance?.PlayUi(_toggleAudioCue);
        _isVisible = !_isVisible;
        if (_debugPanel != null)
        {
            _debugPanel.SetActive(_isVisible);
        }
        
        if (_isVisible)
        {
            UpdateDebugText();
        }
    }

    /// <summary>
    /// 디버그 패널 표시
    /// </summary>
    public void ShowDebugPanel()
    {
        _isVisible = true;
        if (_debugPanel != null)
        {
            _debugPanel.SetActive(true);
        }
        UpdateDebugText();
    }

    /// <summary>
    /// 디버그 패널 숨김
    /// </summary>
    public void HideDebugPanel()
    {
        _isVisible = false;
        if (_debugPanel != null)
        {
            _debugPanel.SetActive(false);
        }
    }

    #endregion

    #region Private Methods

    private void CalculateFPS()
    {
        _frameCount++;
        _fpsAccumulatedTime += Time.unscaledDeltaTime;

        // 0.5초마다 FPS 계산
        if (_fpsAccumulatedTime >= 0.5f)
        {
            _currentFPS = _frameCount / _fpsAccumulatedTime;
            _frameCount = 0;
            _fpsAccumulatedTime = 0f;
        }
    }

    private void UpdateDebugText()
    {
        if (_debugText == null) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        // FPS
        sb.AppendLine($"<b>FPS:</b> {_currentFPS:F1}");

        // RTT (Ping)
        string rttValue = GetRTTString();
        sb.AppendLine($"<b>RTT:</b> {rttValue}");

        // 연결 상태
        string connectionStatus = GetConnectionStatus();
        sb.AppendLine($"<b>연결:</b> {connectionStatus}");

        // 서버 세션 정보
        if (MatchmakingManager.Instance != null)
        {
            // 게임 모드
            var gameMode = MatchmakingManager.Instance.CurrentGameMode;
            sb.AppendLine($"<b>모드:</b> {GetGameModeString(gameMode)}");

            // 방 코드 (있을 경우)
            string roomCode = MatchmakingManager.Instance.RoomCode;
            if (!string.IsNullOrEmpty(roomCode))
            {
                sb.AppendLine($"<b>방코드:</b> {roomCode}");
            }

            // 플레이어 수
            int currentPlayers = MatchmakingManager.Instance.CurrentPlayers;
            int maxPlayers = MatchmakingManager.Instance.MaxPlayers;
            if (maxPlayers > 0)
            {
                sb.AppendLine($"<b>플레이어:</b> {currentPlayers}/{maxPlayers}");
            }
        }

        // 로컬 플레이어 스탯 정보
        if (InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Connection != null)
        {
            var localPlayer = InstanceFinder.ClientManager.Connection.FirstObject;
            if (localPlayer != null)
            {
                sb.AppendLine();
                sb.AppendLine("<b>[Player Stats]</b>");

                var combat = localPlayer.GetComponent<PlayerCombat>();
                if (combat != null)
                {
                    sb.AppendLine($"<b>HP:</b> {Mathf.CeilToInt(combat.HP.Value)} / {Mathf.CeilToInt(combat.MaxHP)}");
                    sb.AppendLine($"<b>LV:</b> {combat.Level.Value}");
                    sb.AppendLine($"<b>XP:</b> {Mathf.FloorToInt(combat.CurrentXP.Value)} / {Mathf.FloorToInt(combat.XPToNextLevel)}");
                }

                var controller = localPlayer.GetComponent<PlayerController>();
                if (controller != null)
                {
                    sb.AppendLine($"<b>Move Speed:</b> {controller.GetCurrentMoveSpeed():F1}");
                    sb.AppendLine($"<b>Dash Speed:</b> {controller.GetDashSpeed():F1}");
                    sb.AppendLine($"<b>Dash Cooldown:</b> {controller.GetDashCooldown():F2}s");
                }

                var weapon = localPlayer.GetComponent<NetworkedWeapon>();
                if (weapon != null)
                {
                    if (weapon.CurrentWeaponData != null)
                    {
                        sb.AppendLine($"<b>Weapon:</b> {weapon.CurrentWeaponData.ItemName}");
                        
                        // GunData인 경우 발사 방식 표시
                        if (weapon.CurrentWeaponData is GunData gun)
                        {
                            // Reload Speed만 표시
                            sb.AppendLine($" - Reload Speed: {weapon.EffectiveReloadTime:F2}s");
                        }
                        
                        sb.AppendLine($" - Dmg: {weapon.GetFinalDamage():F0}");
                        sb.AppendLine($" - AtkDelay: {weapon.EffectiveAttackDelay:F2}s");
                    }
                }

                var armor = localPlayer.GetComponent<NetworkedArmor>();
                if (armor != null)
                {
                    // [Fix] 단순 기본 방어력이 아닌 카드 보너스가 적용된 EffectiveDefense 사용
                    if (combat != null)
                    {
                        sb.AppendLine($"<b>Defense:</b> {combat.EffectiveDefense:F0}");
                    }
                    else
                    {
                        sb.AppendLine($"<b>Defense:</b> {armor.CurrentDefense:F0}");
                    }
                }
            }
        }

        _debugText.text = sb.ToString();
    }

    private string GetRTTString()
    {
        if (!InstanceFinder.IsClientStarted)
        {
            return "-";
        }

        try
        {
            // Fish-Net RTT (밀리초 단위)
            var timeManager = InstanceFinder.TimeManager;
            if (timeManager != null)
            {
                // RoundTripTime은 이미 밀리초 단위입니다 (FishNet 내부에서 계산됨)
                // 버그 수정: 기존에는 Ticks로 착각하여 * TickDelta * 1000을 해서 값이 뻥튀기되었습니다.
                long rttMs = timeManager.RoundTripTime;
                
                // 핑(Ping)은 보통 RTT의 절반으로 간주합니다.
                long pingMs = rttMs / 2;

                return $"{rttMs} ms (Ping: {pingMs} ms)";
            }
        }
        catch
        {
            // RTT 접근 실패 시
        }

        return "-";
    }

    private string GetConnectionStatus()
    {
        bool isServer = InstanceFinder.IsServerStarted;
        bool isClient = InstanceFinder.IsClientStarted;

        if (isServer && isClient)
        {
            return "Host";
        }
        else if (isServer)
        {
            return "Server";
        }
        else if (isClient)
        {
            return "Client";
        }
        else
        {
            return "Offline";
        }
    }

    private string GetGameModeString(GameMode mode)
    {
        return mode switch
        {
            GameMode.FourPlayer => "4인전",
            GameMode.EightPlayer => "8인전",
            GameMode.Ranked => "랭크 8인",
            GameMode.Custom => "커스텀",
            GameMode.PracticeRange => "연습장",
            _ => "없음"
        };
    }

    #endregion
}
