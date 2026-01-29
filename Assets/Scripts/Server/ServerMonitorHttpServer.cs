using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// 모니터링 전용 HTTP 대시보드
/// --monitor 모드로 실행 시 활성화
/// Unity Sessions API로 모든 활성 세션 조회
/// </summary>
public class ServerMonitorHttpServer : MonoBehaviour
{
    #region Singleton

    private static ServerMonitorHttpServer _instance;
    public static ServerMonitorHttpServer Instance => _instance;

    #endregion

    #region Serialized Fields

    [Header("HTTP 서버 설정")]
    [SerializeField] private int _port = 8080;
    [SerializeField] private float _sessionQueryInterval = 1f;

    #endregion

    #region Private Fields

    private HttpListener _listener;
    private Thread _listenerThread;
    private bool _isRunning = false;
    private bool _isInitialized = false;
    
    private List<SessionData> _sessionsCache = new List<SessionData>();
    private readonly object _cacheLock = new object();
    
    private DateTime _startTime;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        _startTime = DateTime.Now;
        
        // Unity Services 초기화
        await InitializeServicesAsync();
        
        if (_isInitialized)
        {
            StartHttpServer();
            InvokeRepeating(nameof(QuerySessionsLoop), 0f, _sessionQueryInterval);
        }
    }

    private void OnDestroy()
    {
        StopHttpServer();
        CancelInvoke(nameof(QuerySessionsLoop));
        
        if (_instance == this)
            _instance = null;
    }

    private void OnApplicationQuit()
    {
        StopHttpServer();
    }

    #endregion

    #region Initialization

    private async Task InitializeServicesAsync()
    {
        try
        {
            Debug.Log("[Monitor] Unity Services 초기화 중...");
            
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // 고유한 프로필 이름 생성 (프로세스 ID + 타임스탬프)
                string profileName = $"Server_{System.Diagnostics.Process.GetCurrentProcess().Id}_{System.DateTime.Now.Ticks % 10000}";
                var initOptions = new InitializationOptions();
                initOptions.SetProfile(profileName);
                
                Debug.Log($"[Monitor] Unity Services 초기화 - Profile: {profileName}");
                await UnityServices.InitializeAsync(initOptions);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            _isInitialized = true;
            Debug.Log("[Monitor] Unity Services 초기화 완료!");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Monitor] Unity Services 초기화 실패: {ex.Message}");
            _isInitialized = false;
        }
    }

    #endregion

    #region HTTP Server

    public void StartHttpServer()
    {
        if (_isRunning) return;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{_port}/");
            _listener.Start();
            
            _isRunning = true;
            _listenerThread = new Thread(ListenerLoop);
            _listenerThread.IsBackground = true;
            _listenerThread.Start();
            
            Debug.Log($"[Monitor] HTTP 서버 시작됨 - http://localhost:{_port}/");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Monitor] HTTP 서버 시작 실패: {ex.Message}");
        }
    }

    public void StopHttpServer()
    {
        _isRunning = false;
        
        if (_listener != null)
        {
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch { }
            _listener = null;
        }
        
        if (_listenerThread != null && _listenerThread.IsAlive)
        {
            _listenerThread.Join(1000);
            _listenerThread = null;
        }
    }

    private void ListenerLoop()
    {
        while (_isRunning && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = _listener.GetContext();
                ProcessRequest(context);
            }
            catch (HttpListenerException) { }
            catch (Exception ex)
            {
                Debug.LogError($"[Monitor] Request error: {ex.Message}");
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        
        string responseString;
        string contentType = "text/html; charset=utf-8";
        int statusCode = 200;
        
        try
        {
            string path = request.Url.AbsolutePath.ToLower();
            
            switch (path)
            {
                case "/":
                    responseString = GetDashboardHtml();
                    break;
                    
                case "/api/sessions":
                    responseString = GetSessionsJson();
                    contentType = "application/json; charset=utf-8";
                    break;
                    
                case "/health":
                    responseString = "{\"status\":\"ok\"}";
                    contentType = "application/json; charset=utf-8";
                    break;
                    
                default:
                    statusCode = 404;
                    responseString = GetNotFoundHtml();
                    break;
            }
        }
        catch (Exception ex)
        {
            statusCode = 500;
            responseString = $"<h1>Error</h1><p>{ex.Message}</p>";
        }
        
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.ContentType = contentType;
        response.StatusCode = statusCode;
        
        byte[] buffer = Encoding.UTF8.GetBytes(responseString);
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    #endregion

    #region Session Query

    private void QuerySessionsLoop()
    {
        _ = QuerySessionsAsync();
    }

    private async Task QuerySessionsAsync()
    {
        if (!_isInitialized) return;

        try
        {
            var queryOptions = new QuerySessionsOptions();
            var result = await MultiplayerService.Instance.QuerySessionsAsync(queryOptions);
            
            var sessionList = new List<SessionData>();

            foreach (var session in result.Sessions)
            {
                var data = new SessionData
                {
                    SessionId = session.Id,
                    SessionName = session.Name ?? "",  // session.Name 직접 사용
                    CurrentPlayers = session.MaxPlayers - session.AvailableSlots
                };

                // Properties에서 정보 추출
                if (session.Properties != null)
                {
                    if (session.Properties.TryGetValue("ServerIp", out var ip))
                        data.ServerIp = ip.Value;
                    if (session.Properties.TryGetValue("ServerPort", out var port))
                        int.TryParse(port.Value, out data.ServerPort);
                    if (session.Properties.TryGetValue("GameMode", out var mode))
                    {
                        data.GameMode = mode.Value;
                    }
                    
                    // [Fixed] RoomCode 속성에서 코드 읽기 (CreateSession에서 자동 저장됨)
                    if (session.Properties.TryGetValue("RoomCode", out var code))
                    {
                        data.RoomCode = code.Value;
                    }

                    // [Added] Server Identity
                    if (session.Properties.TryGetValue("ServerRegion", out var region))
                        data.ServerRegion = region.Value;
                    if (session.Properties.TryGetValue("ServerHostname", out var hostname))
                        data.ServerHostname = hostname.Value;

                    if (session.Properties.TryGetValue("GameState", out var state))
                        data.GameState = state.Value;
                    if (session.Properties.TryGetValue("IsGameStarted", out var started))
                        data.IsGameStarted = started.Value?.ToLower() == "true";
                    if (session.Properties.TryGetValue("CurrentPhase", out var phase))
                        int.TryParse(phase.Value, out data.CurrentPhase);
                    if (session.Properties.TryGetValue("AlivePlayers", out var alive))
                        int.TryParse(alive.Value, out data.AlivePlayers);
                    if (session.Properties.TryGetValue("TotalPlayers", out var total))
                        int.TryParse(total.Value, out data.TotalPlayers);
                    if (session.Properties.TryGetValue("ElapsedTime", out var elapsed))
                        int.TryParse(elapsed.Value, out data.ElapsedTime);
                    if (session.Properties.TryGetValue("IsGameEnded", out var ended))
                        data.IsGameEnded = ended.Value?.ToLower() == "true";
                    
                    // MaxPlayers 우선순위: TargetPlayers > GameMode > 기본값 8
                    if (session.Properties.TryGetValue("TargetPlayers", out var target) && int.TryParse(target.Value, out int targetVal))
                    {
                         data.MaxPlayers = targetVal;
                    }
                    else
                    {
                         data.MaxPlayers = GetMaxPlayersByGameMode(data.GameMode);
                    }
                }
                else
                {
                     // Properties가 없으면 기본값
                     data.MaxPlayers = 8;
                }

                sessionList.Add(data);
            }

            lock (_cacheLock)
            {
                _sessionsCache = sessionList;
            }

            Debug.Log($"[Monitor] 세션 조회 완료: {sessionList.Count}개");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Monitor] 세션 조회 실패: {ex.Message}");
        }
    }

    #endregion

    #region HTML/JSON Generators

    private string GetDashboardHtml()
    {
        List<SessionData> sessions;
        lock (_cacheLock)
        {
            sessions = new List<SessionData>(_sessionsCache);
        }

        float uptime = (float)(DateTime.Now - _startTime).TotalSeconds;
        string uptimeStr = uptime >= 3600 
            ? $"{(int)(uptime / 3600)}시간 {(int)((uptime % 3600) / 60)}분"
            : $"{(int)(uptime / 60)}분 {(int)(uptime % 60)}초";

        int totalPlayers = 0;
        int waitingCount = 0;
        int playingCount = 0;
        
        foreach (var s in sessions)
        {
            totalPlayers += s.TotalPlayers;
            if (s.IsGameStarted) playingCount++;
            else waitingCount++;
        }

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html lang=""ko"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">

    <title>Project VOID - Server Monitor</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{
            font-family: 'Segoe UI', -apple-system, sans-serif;
            background: linear-gradient(135deg, #0a0f0a 0%, #0d1a0d 50%, #0a0f0a 100%);
            color: #e4e4e7;
            min-height: 100vh;
            padding: 24px;
        }}
        .container {{ max-width: 1400px; margin: 0 auto; }}
        
        .header {{
            text-align: center;
            padding: 40px 20px;
            background: linear-gradient(180deg, rgba(34,197,94,0.15) 0%, transparent 100%);
            border-radius: 20px;
            margin-bottom: 32px;
            border: 1px solid rgba(34,197,94,0.2);
        }}
        .header h1 {{
            font-size: 2.5rem;
            font-weight: 700;
            background: linear-gradient(90deg, #22c55e, #4ade80, #86efac);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            margin-bottom: 8px;
        }}
        .header-sub {{ color: #6ee7b7; font-size: 0.95rem; opacity: 0.8; }}
        
        .stats-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px;
            margin-bottom: 32px;
        }}
        .stat-card {{
            background: rgba(34,197,94,0.08);
            border-radius: 16px;
            padding: 24px;
            text-align: center;
            border: 1px solid rgba(34,197,94,0.15);
            transition: all 0.3s ease;
        }}
        .stat-card:hover {{
            border-color: rgba(34,197,94,0.4);
            transform: translateY(-4px);
            box-shadow: 0 8px 32px rgba(34,197,94,0.1);
        }}
        .stat-icon {{ font-size: 2rem; margin-bottom: 8px; }}
        .stat-value {{
            font-size: 2.2rem;
            font-weight: 700;
            color: #22c55e;
            line-height: 1.2;
        }}
        .stat-label {{
            color: #86efac;
            font-size: 0.85rem;
            margin-top: 8px;
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }}
        
        .section {{
            background: rgba(34,197,94,0.05);
            border-radius: 20px;
            padding: 28px;
            border: 1px solid rgba(34,197,94,0.12);
        }}
        .section-header {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 24px;
        }}
        .section-title {{
            font-size: 1.3rem;
            font-weight: 600;
            color: #4ade80;
            display: flex;
            align-items: center;
            gap: 10px;
        }}
        .refresh-badge {{
            background: rgba(34,197,94,0.2);
            color: #86efac;
            padding: 6px 14px;
            border-radius: 20px;
            font-size: 0.75rem;
        }}
        
        .session-grid {{
            display: grid;
            gap: 16px;
        }}
        .session {{
            background: rgba(0,0,0,0.3);
            border-radius: 14px;
            padding: 20px 24px;
            border-left: 5px solid;
            display: grid;
            grid-template-columns: 1fr auto;
            gap: 20px;
            align-items: center;
            transition: all 0.2s ease;
        }}
        .session:hover {{
            background: rgba(34,197,94,0.1);
            transform: translateX(4px);
        }}
        .session.waiting {{ border-color: #eab308; }}
        .session.playing {{ border-color: #22c55e; }}
        .session.matching {{ border-color: #a855f7; }}
        .session.ended {{ border-color: #ef4444; opacity: 0.7; }}
        
        .session-main {{ display: flex; flex-direction: column; gap: 10px; }}
        .session-name {{
            font-weight: 600;
            font-size: 1.1rem;
            color: #f4f4f5;
            display: flex;
            align-items: center;
            gap: 10px;
        }}
        .room-code-simple {{
            color: #86efac;
            font-size: 0.95rem;
            font-family: monospace;
            margin-left: 8px;
            opacity: 0.9;
        }}
        .session-details {{
            display: flex;
            gap: 20px;
            flex-wrap: wrap;
        }}
        .session-detail {{
            color: #a1a1aa;
            font-size: 0.85rem;
            display: flex;
            align-items: center;
            gap: 6px;
        }}
        
        .session-right {{
            display: flex;
            align-items: center;
            gap: 16px;
        }}
        .player-count {{
            font-size: 1.3rem;
            font-weight: 600;
            color: #4ade80;
        }}
        .status-badge {{
            padding: 8px 18px;
            border-radius: 24px;
            font-size: 0.8rem;
            font-weight: 600;
            text-transform: uppercase;
            letter-spacing: 0.5px;
        }}
        .status-badge.waiting {{
            background: rgba(234,179,8,0.15);
            color: #facc15;
            border: 1px solid rgba(234,179,8,0.3);
        }}
        .status-badge.matching {{
            background: rgba(168,85,247,0.15);
            color: #c084fc;
            border: 1px solid rgba(168,85,247,0.3);
        }}
        .status-badge.playing {{
            background: rgba(34,197,94,0.15);
            color: #4ade80;
            border: 1px solid rgba(34,197,94,0.3);
        }}
        .status-badge.ended {{
            background: rgba(239,68,68,0.15);
            color: #f87171;
            border: 1px solid rgba(239,68,68,0.3);
        }}
        
        .empty-state {{
            text-align: center;
            padding: 60px 20px;
            color: #52525b;
        }}
        .empty-state-icon {{ font-size: 4rem; margin-bottom: 16px; opacity: 0.5; }}
        .empty-state-text {{ font-size: 1.1rem; }}
        
        .footer {{
            text-align: center;
            color: #3f3f46;
            margin-top: 32px;
            font-size: 0.8rem;
            padding: 20px;
        }}
        .server-identity {{
            font-size: 0.85rem;
            color: #71717a;
            margin-top: 4px;
            display: flex;
            align-items: center;
            gap: 6px;
        }}
        .region-tag {{
            background: rgba(34,197,94,0.1);
            color: #4ade80;
            padding: 2px 6px;
            border-radius: 4px;
            font-size: 0.75rem;
            font-weight: 600;
        }}
        .hostname-tag {{
            color: #a1a1aa;
            font-family: monospace;
            background: rgba(255,255,255,0.05);
            padding: 2px 6px;
            border-radius: 4px;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>🎮 Project VOID</h1>
            <div class=""header-sub"">Server Monitor Dashboard</div>
        </div>

        <div class=""stats-grid"">
            <div class=""stat-card"">
                <div class=""stat-icon"">🌐</div>
                <div class=""stat-value"" id=""sessionCount"">{sessions.Count}</div>
                <div class=""stat-label"">Active Sessions</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-icon"">👥</div>
                <div class=""stat-value"" id=""totalPlayers"">{totalPlayers}</div>
                <div class=""stat-label"">Total Players</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-icon"">⏳</div>
                <div class=""stat-value"" id=""waitingCount"" style=""color:#facc15"">{waitingCount}</div>
                <div class=""stat-label"">Waiting</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-icon"">🎯</div>
                <div class=""stat-value"" id=""playingCount"">{playingCount}</div>
                <div class=""stat-label"">In Game</div>
            </div>
            <div class=""stat-card"">
                <div class=""stat-icon"">⏱️</div>
                <div class=""stat-value"" id=""uptime"" style=""font-size:1.4rem"">{uptimeStr}</div>
                <div class=""stat-label"">Uptime</div>
            </div>
        </div>

        <div class=""section"">
            <div class=""section-header"">
                <div class=""section-title"">
                    <span>📋</span>
                    <span>Active Sessions</span>
                </div>
                <div class=""refresh-badge"">1초마다 갱신</div>
            </div>
            <div class=""session-grid"">");

        if (sessions.Count == 0)
        {
            sb.Append(@"
                <div class=""empty-state"">
                    <div class=""empty-state-icon"">🔍</div>
                    <div class=""empty-state-text"">활성 세션이 없습니다</div>
                </div>");
        }
        else
        {
            foreach (var session in sessions)
            {
                // 상태 결정
                string statusClass;
                string statusText;
                if (session.IsGameEnded)
                {
                    statusClass = "ended";
                    statusText = "종료";
                }
                else if (session.IsGameStarted)
                {
                    statusClass = "playing";
                    statusText = "게임중";
                }
                else if (session.TotalPlayers > 0)
                {
                    statusClass = "matching";
                    statusText = "매칭중";
                }
                else
                {
                    statusClass = "waiting";
                    statusText = "대기중";
                }

                string displayName = string.IsNullOrEmpty(session.SessionName) 
                    ? session.SessionId.Substring(0, Math.Min(8, session.SessionId.Length)) 
                    : session.SessionName;
                
                string roomCodeHtml = "";
                if (!string.IsNullOrEmpty(session.RoomCode))
                {
                    roomCodeHtml = $@"<span class=""room-code-simple"">[{EscapeHtml(session.RoomCode)}]</span>";
                }

                // [Modified] Server Identity Display (Region + Hostname)
                string identityDisplay = "";
                if (!string.IsNullOrEmpty(session.ServerHostname) || !string.IsNullOrEmpty(session.ServerRegion))
                {
                    string region = string.IsNullOrEmpty(session.ServerRegion) ? "Unknown" : session.ServerRegion;
                    string host = string.IsNullOrEmpty(session.ServerHostname) ? "" : session.ServerHostname;
                    identityDisplay = $@"<div class=""server-identity"">🖥️ <span class=""region-tag"">{EscapeHtml(region)}</span> <span class=""hostname-tag"">{EscapeHtml(host)}</span></div>";
                }

                int minutes = session.ElapsedTime / 60;
                int seconds = session.ElapsedTime % 60;
                string timeStr = $"{minutes}분 {seconds}초";

                string gameStateDisplay = string.IsNullOrEmpty(session.GameState) 
                    ? (session.IsGameStarted ? "Playing" : "대기 중")
                    : session.GameState;

                int playerCount = session.TotalPlayers;

                // ID 추가 for JS diffing
                sb.Append($@"
                <div class=""session {statusClass}"" id=""session-{session.SessionId}"">
                    <div class=""session-main"">
                        <div class=""session-name"">
                            {EscapeHtml(displayName)} {roomCodeHtml}
                        </div>
                        {identityDisplay}
                        <div class=""session-details"">
                            <span class=""session-detail"">🎮 {EscapeHtml(session.GameMode)}</span>
                            <span class=""session-detail"">🎯 {gameStateDisplay}</span>
                            <span class=""session-detail"">⏱️ {timeStr}</span>
                            <span class=""session-detail"">💀 {session.AlivePlayers}/{session.TotalPlayers} 생존</span>
                            <span class=""session-detail"">🔥 Phase {session.CurrentPhase}</span>
                        </div>
                    </div>
                    <div class=""session-right"">
                        <span class=""player-count"">👥 {playerCount}/{session.MaxPlayers}</span>
                        <span class=""status-badge {statusClass}"">{statusText}</span>
                    </div>
                </div>");
            }
        }

        sb.Append($@"
            </div>
        </div>

        <div class=""footer"" id=""lastUpdate"">
            마지막 갱신: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
        </div>
    </div>

    <script>
    function getStatusInfo(session) {{
        if (session.ended) return {{ class: 'ended', text: '종료' }};
        if (session.playing) return {{ class: 'playing', text: '게임중' }};
        if (session.players > 0) return {{ class: 'matching', text: '매칭중' }};
        return {{ class: 'waiting', text: '대기중' }};
    }}
    
    function formatTime(seconds) {{
        const mins = Math.floor(seconds / 60);
        const secs = seconds % 60;
        return mins + '분 ' + secs + '초';
    }}
    
    function escapeHtml(text) {{
        if (!text) return '';
        return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }}
    
    function renderSessionCard(s) {{
        const status = getStatusInfo(s);
        const displayName = s.name || s.id.substring(0, 8);
        
        let roomCodeHtml = '';
        if (s.roomCode) {{
            // Simple format matching C# code
            roomCodeHtml = `<span class=""room-code-simple"">[${{escapeHtml(s.roomCode)}}]</span>`;
        }}
        
        // [Modified] Server Identity Display (JS - Region + Hostname)
        let identityDisplay = '';
        if (s.serverHostname || s.serverRegion) {{
            const region = s.serverRegion || 'Unknown';
            const host = s.serverHostname || '';
            identityDisplay = `<div class=""server-identity"">🖥️ <span class=""region-tag"">${{escapeHtml(region)}}</span> <span class=""hostname-tag"">${{escapeHtml(host)}}</span></div>`;
        }}
        
        const gameStateDisplay = s.gameState || (s.playing ? 'Playing' : '대기 중');
        
        return `
        <div class=""session ${{status.class}}"" id=""session-${{s.id}}"">
            <div class=""session-main"">
                <div class=""session-name"">
                    ${{escapeHtml(displayName)}} ${{roomCodeHtml}}
                </div>
                ${{identityDisplay}}
                <div class=""session-details"">
                    <span class=""session-detail"">🎮 ${{escapeHtml(s.gameMode)}}</span>
                    <span class=""session-detail"">🎯 ${{gameStateDisplay}}</span>
                    <span class=""session-detail"">⏱️ ${{formatTime(s.elapsedTime)}}</span>
                    <span class=""session-detail"">💀 ${{s.alivePlayers}}/${{s.players}} 생존</span>
                    <span class=""session-detail"">🔥 Phase ${{s.currentPhase}}</span>
                </div>
            </div>
            <div class=""session-right"">
                <span class=""player-count"">👥 ${{s.players}}/${{s.maxPlayers}}</span>
                <span class=""status-badge ${{status.class}}"">${{status.text}}</span>
            </div>
        </div>`;
    }}
    
    async function updateDashboard() {{
        try {{
            const response = await fetch('/api/sessions');
            const data = await response.json();
            
            // Update stats
            document.getElementById('sessionCount').textContent = data.count;
            
            let totalPlayers = 0;
            let waitingCount = 0;
            let playingCount = 0;
            
            data.sessions.forEach(s => {{
                totalPlayers += s.players;
                if (s.playing) playingCount++;
                else waitingCount++;
            }});
            
            document.getElementById('totalPlayers').textContent = totalPlayers;
            document.getElementById('waitingCount').textContent = waitingCount;
            document.getElementById('playingCount').textContent = playingCount;
            
            // DOM Patching for Session List
            const sessionGrid = document.querySelector('.session-grid');
            const currentIds = new Set(data.sessions.map(s => 'session-' + s.id));
            
            // 1. Remove old sessions
            Array.from(sessionGrid.children).forEach(child => {{
                if (!child.classList.contains('empty-state') && !currentIds.has(child.id)) {{
                    child.remove();
                }}
            }});

            if (data.sessions.length === 0) {{
                if (!sessionGrid.querySelector('.empty-state')) {{
                    sessionGrid.innerHTML = `
                        <div class=""empty-state"">
                            <div class=""empty-state-icon"">🔍</div>
                            <div class=""empty-state-text"">활성 세션이 없습니다</div>
                        </div>`;
                }}
            }} else {{
                // Remove empty state if exists
                const emptyState = sessionGrid.querySelector('.empty-state');
                if (emptyState) emptyState.remove();

                // 2. Update or Add sessions
                data.sessions.forEach(s => {{
                    const elId = 'session-' + s.id;
                    const existingEl = document.getElementById(elId);
                    
                    const newHtml = renderSessionCard(s);
                    
                    if (existingEl) {{
                        // Compare simplified update to avoid flickering
                        // Note: A real virtual DOM would be better, but simple HTML string comparison works here
                        // Clean whitespace for robustness before comparing? 
                        // For now simply: direct replace if string changed. 
                        // Browser handles outerHTML replacement fairly well, but inner text change is smoother.
                        // However, to guarantee structure match, replacing outerHTML is safest for now.
                        if (existingEl.outerHTML.replace(/\s/g,'') !== newHtml.replace(/\s/g,'')) {{
                            existingEl.outerHTML = newHtml;
                        }}
                    }} else {{
                        sessionGrid.insertAdjacentHTML('beforeend', newHtml);
                    }}
                }});
            }}
            
            // Update uptime & timestamp
            const uptime = data.uptime || 0;
            let uptimeStr;
            if (uptime >= 3600) {{
                uptimeStr = Math.floor(uptime / 3600) + '시간 ' + Math.floor((uptime % 3600) / 60) + '분';
            }} else {{
                uptimeStr = Math.floor(uptime / 60) + '분 ' + Math.floor(uptime % 60) + '초';
            }}
            document.getElementById('uptime').textContent = uptimeStr;
            document.getElementById('lastUpdate').textContent = '마지막 갱신: ' + new Date().toLocaleString('ko-KR');
        }} catch (e) {{
            console.error('Update failed:', e);
        }}
    }}
    
    setInterval(updateDashboard, 1000);
    </script>
</body>
</html>");

        return sb.ToString();
    }

    private string GetSessionsJson()
    {
        List<SessionData> sessions;
        lock (_cacheLock)
        {
            sessions = new List<SessionData>(_sessionsCache);
        }

        var sb = new StringBuilder();
        sb.Append("{\"sessions\":[");
        
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (i > 0) sb.Append(",");
            sb.Append("{");
            sb.Append($"\"id\":\"{EscapeJson(s.SessionId)}\",");
            sb.Append($"\"name\":\"{EscapeJson(s.SessionName)}\",");
            sb.Append($"\"ip\":\"{EscapeJson(s.ServerIp)}\",");
            sb.Append($"\"port\":{s.ServerPort},");
            sb.Append($"\"players\":{s.TotalPlayers},");
            sb.Append($"\"maxPlayers\":{s.MaxPlayers},");
            sb.Append($"\"gameMode\":\"{EscapeJson(s.GameMode)}\",");
            sb.Append($"\"roomCode\":\"{EscapeJson(s.RoomCode)}\",");
            sb.Append($"\"gameState\":\"{EscapeJson(s.GameState)}\",");
            sb.Append($"\"alivePlayers\":{s.AlivePlayers},");
            sb.Append($"\"elapsedTime\":{s.ElapsedTime},");
            sb.Append($"\"currentPhase\":{s.CurrentPhase},");
            sb.Append($"\"playing\":{s.IsGameStarted.ToString().ToLower()},");
            sb.Append($"\"ended\":{s.IsGameEnded.ToString().ToLower()},");
            sb.Append($"\"serverRegion\":\"{EscapeJson(s.ServerRegion)}\",");
            sb.Append($"\"serverHostname\":\"{EscapeJson(s.ServerHostname)}\"");
            sb.Append("}");
        }
        
        float uptime = (float)(DateTime.Now - _startTime).TotalSeconds;
        sb.Append($"],\"count\":{sessions.Count},\"uptime\":{(int)uptime},\"timestamp\":\"{DateTime.Now:O}\"}}");
        return sb.ToString();
    }

    private string GetNotFoundHtml()
    {
        return @"<!DOCTYPE html>
<html><head><title>404</title></head>
<body style=""background:#0a0f0a;color:#fff;font-family:sans-serif;display:flex;align-items:center;justify-content:center;height:100vh"">
<div style=""text-align:center"">
<h1 style=""font-size:4rem;color:#22c55e"">404</h1>
<p>Page not found</p>
<a href=""/"" style=""color:#4ade80"">← Back to Dashboard</a>
</div></body></html>";
    }

    private string EscapeJson(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }

    private int GetMaxPlayersByGameMode(string gameMode)
    {
        if (string.IsNullOrEmpty(gameMode)) return 8;
        
        return gameMode.ToLower() switch
        {
            "fourplayer" => 4,
            "eightplayer" => 8,
            "twoplayer" => 2,
            _ => 8
        };
    }

    private string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    #endregion

    #region Data Classes

    private class SessionData
    {
        public string SessionId = "";
        public string SessionName = "";
        public string ServerIp = "";
        public int ServerPort = 0;
        public int CurrentPlayers = 0;
        public int MaxPlayers = 0;
        public string GameMode = "";
        public string RoomCode = "";
        public string ServerRegion = "";
        public string ServerHostname = "";
        public string GameState = "";
        public bool IsGameStarted = false;
        public int CurrentPhase = 0;
        public int AlivePlayers = 0;
        public int TotalPlayers = 0;
        public int ElapsedTime = 0;
        public bool IsGameEnded = false;
    }

    #endregion
}
