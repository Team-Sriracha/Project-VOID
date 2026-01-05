using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// 헤드리스 서버용 HTTP 모니터링 API
/// http://서버IP:8080/status 로 접속하면 서버 상태를 JSON으로 반환합니다.
/// </summary>
public class ServerMonitorHttpServer : MonoBehaviour
{
    #region Serialized Fields

    [Header("HTTP 서버 설정")]
    [SerializeField] private int _port = 8080;
    [SerializeField] private bool _enableOnStart = true;
    [SerializeField] private float _cacheUpdateInterval = 1f;

    #endregion

    #region Private Fields

    private HttpListener _listener;
    private Thread _listenerThread;
    private bool _isRunning = false;
    private MultiPeerServerManager _serverManager;
    
    // Why: HTTP 스레드에서 Unity 객체 접근 불가하므로 캐시 사용
    private ServerStatusCache _statusCache = new ServerStatusCache();
    private readonly object _cacheLock = new object();
    
    // 추가 서버 정보
    private DateTime _serverStartTime;
    private float _currentFps;
    private int _peakPlayers;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        _serverStartTime = DateTime.Now;
        
        _serverManager = GetComponent<MultiPeerServerManager>();
        if (_serverManager == null)
        {
            _serverManager = FindFirstObjectByType<MultiPeerServerManager>();
        }

        if (_serverManager == null)
        {
            Debug.LogWarning("[ServerMonitor] MultiPeerServerManager not found. HTTP monitoring disabled.");
            return;
        }

        if (_enableOnStart)
        {
            StartHttpServer();
            InvokeRepeating(nameof(UpdateCache), 0f, _cacheUpdateInterval);
        }
    }

    private void OnDestroy()
    {
        StopHttpServer();
        CancelInvoke(nameof(UpdateCache));
    }

    private void OnApplicationQuit()
    {
        StopHttpServer();
    }

    #endregion

    #region Public Methods

    public void StartHttpServer()
    {
        if (_isRunning)
        {
            Debug.LogWarning("[ServerMonitor] HTTP server already running.");
            return;
        }

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://*:{_port}/");
            _listener.Start();
            
            _isRunning = true;
            _listenerThread = new Thread(ListenerLoop);
            _listenerThread.IsBackground = true;
            _listenerThread.Start();
            
            Debug.Log($"[ServerMonitor] HTTP 서버 시작됨 - http://localhost:{_port}/status");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ServerMonitor] HTTP 서버 시작 실패: {ex.Message}");
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
        
        Debug.Log("[ServerMonitor] HTTP 서버 중지됨");
    }

    #endregion

    #region Cache Update (Main Thread)

    private void UpdateCache()
    {
        if (_serverManager == null) return;

        // FPS 계산
        _currentFps = 1f / Time.unscaledDeltaTime;
        
        int currentPlayers = _serverManager.GetTotalPlayerCount();
        if (currentPlayers > _peakPlayers)
        {
            _peakPlayers = currentPlayers;
        }

        var sessions = _serverManager.GetSessionStatusList();
        int waitingCount = sessions.Count(s => s.Status == "Waiting");
        int inGameCount = sessions.Count(s => s.Status == "InGame");

        var newCache = new ServerStatusCache
        {
            ServerTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ServerStartTime = _serverStartTime.ToString("yyyy-MM-dd HH:mm:ss"),
            Uptime = Time.realtimeSinceStartup,
            TotalPlayers = currentPlayers,
            MaxPlayers = _serverManager.MaxTotalPlayers,
            PeakPlayers = _peakPlayers,
            Sessions = sessions,
            MemoryMB = System.GC.GetTotalMemory(false) / (1024 * 1024),
            ServerId = _serverManager.ServerId,
            Fps = _currentFps,
            WaitingSessionCount = waitingCount,
            InGameSessionCount = inGameCount
        };

        lock (_cacheLock)
        {
            _statusCache = newCache;
        }
    }

    #endregion

    #region HTTP Listener Thread

    private void ListenerLoop()
    {
        while (_isRunning && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = _listener.GetContext();
                ProcessRequest(context);
            }
            catch (HttpListenerException)
            {
                // 서버 종료 시 발생, 무시
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ServerMonitor] Request error: {ex.Message}");
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        
        string responseString;
        string contentType = "application/json; charset=utf-8";
        int statusCode = 200;
        
        try
        {
            string path = request.Url.AbsolutePath.ToLower();
            
            switch (path)
            {
                case "/":
                case "/dashboard":
                    responseString = GetDashboardHtml();
                    contentType = "text/html; charset=utf-8";
                    break;
                    
                case "/status":
                    responseString = GetStatusJson();
                    break;
                    
                case "/sessions":
                    responseString = GetSessionsJson();
                    break;
                    
                case "/health":
                    responseString = "{\"status\":\"ok\"}";
                    break;
                    
                default:
                    statusCode = 404;
                    responseString = "{\"error\":\"Not Found\",\"endpoints\":[\"/dashboard\",\"/status\",\"/sessions\",\"/health\"]}";
                    break;
            }
        }
        catch (Exception ex)
        {
            statusCode = 500;
            responseString = $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}";
        }
        
        // CORS 허용
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET");
        response.ContentType = contentType;
        response.StatusCode = statusCode;
        
        byte[] buffer = Encoding.UTF8.GetBytes(responseString);
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    private string GetStatusJson()
    {
        ServerStatusCache cache;
        lock (_cacheLock)
        {
            cache = _statusCache;
        }

        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"serverTime\":\"{cache.ServerTime}\",");
        sb.Append($"\"uptime\":{cache.Uptime:F0},");
        sb.Append($"\"totalPlayers\":{cache.TotalPlayers},");
        sb.Append($"\"maxPlayers\":{cache.MaxPlayers},");
        sb.Append($"\"sessionCount\":{cache.Sessions?.Count ?? 0}");
        sb.Append("}");
        
        return sb.ToString();
    }

    private string GetSessionsJson()
    {
        ServerStatusCache cache;
        lock (_cacheLock)
        {
            cache = _statusCache;
        }

        var sb = new StringBuilder();
        sb.Append("{");
        sb.Append($"\"serverTime\":\"{cache.ServerTime}\",");
        sb.Append("\"sessions\":[");
        
        if (cache.Sessions != null)
        {
            for (int i = 0; i < cache.Sessions.Count; i++)
            {
                var session = cache.Sessions[i];
                if (i > 0) sb.Append(",");
                sb.Append("{");
                sb.Append($"\"name\":\"{EscapeJson(session.Name)}\",");
                sb.Append($"\"players\":{session.PlayerCount},");
                sb.Append($"\"maxPlayers\":{session.MaxPlayers},");
                sb.Append($"\"status\":\"{session.Status}\",");
                sb.Append($"\"roomCode\":\"{EscapeJson(session.RoomCode ?? "")}\"");
                sb.Append("}");
            }
        }
        
        sb.Append("]}");
        return sb.ToString();
    }

    private string EscapeJson(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }

    private string GetDashboardHtml()
    {
        ServerStatusCache cache;
        lock (_cacheLock)
        {
            cache = _statusCache;
        }

        // Uptime 포맷팅
        TimeSpan uptime = TimeSpan.FromSeconds(cache.Uptime);
        string uptimeStr = uptime.TotalHours >= 1 
            ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m" 
            : $"{uptime.Minutes}m {uptime.Seconds}s";

        // 점유율 계산
        float loadPercent = cache.MaxPlayers > 0 ? (float)cache.TotalPlayers / cache.MaxPlayers * 100 : 0;
        string loadColor = loadPercent < 50 ? "#4ade80" : loadPercent < 80 ? "#facc15" : "#f87171";

        var sb = new StringBuilder();
        sb.Append($@"<!DOCTYPE html>
<html lang=""ko"">
<head>
    <meta charset=""UTF-8"">
    <meta http-equiv=""refresh"" content=""1"">
    <title>Project VOID - Server Monitor</title>
    <style>
        * {{ margin: 0; padding: 0; box-sizing: border-box; }}
        body {{ 
            font-family: 'Segoe UI', Arial, sans-serif; 
            background: linear-gradient(135deg, #0f0f1a 0%, #1a1a2e 50%, #16213e 100%);
            color: #e4e4e7; 
            min-height: 100vh;
            padding: 20px;
        }}
        .container {{ max-width: 1200px; margin: 0 auto; }}
        .header {{ 
            text-align: center; 
            margin-bottom: 30px;
            padding: 20px;
            background: rgba(255,255,255,0.03);
            border-radius: 16px;
            border: 1px solid rgba(255,255,255,0.1);
        }}
        h1 {{ 
            font-size: 2.2rem;
            background: linear-gradient(90deg, #818cf8, #c084fc, #f472b6);
            -webkit-background-clip: text;
            -webkit-text-fill-color: transparent;
            margin-bottom: 10px;
        }}
        .server-id {{ color: #71717a; font-size: 0.9rem; }}
        .server-start {{ color: #52525b; font-size: 0.8rem; margin-top: 5px; }}
        
        .section {{ margin-bottom: 25px; }}
        .section-title {{ 
            font-size: 1.1rem; 
            color: #a1a1aa; 
            margin-bottom: 15px;
            display: flex;
            align-items: center;
            gap: 8px;
        }}
        
        .stats {{ 
            display: grid; 
            grid-template-columns: repeat(4, 1fr); 
            gap: 15px; 
        }}
        .stat-card {{ 
            background: rgba(255,255,255,0.05); 
            border-radius: 12px; 
            padding: 18px;
            border: 1px solid rgba(255,255,255,0.08);
            transition: transform 0.2s;
        }}
        .stat-card:hover {{ transform: translateY(-2px); }}
        .stat-label {{ color: #71717a; font-size: 0.8rem; margin-bottom: 8px; }}
        .stat-value {{ font-size: 1.6rem; font-weight: 600; }}
        .stat-sub {{ color: #52525b; font-size: 0.75rem; margin-top: 4px; }}
        
        .session-stats {{
            display: grid;
            grid-template-columns: repeat(3, 1fr);
            gap: 10px;
            margin-bottom: 15px;
        }}
        .session-stat {{
            background: rgba(255,255,255,0.03);
            border-radius: 8px;
            padding: 12px;
            text-align: center;
        }}
        .session-stat-value {{ font-size: 1.4rem; font-weight: 600; }}
        .session-stat-label {{ color: #71717a; font-size: 0.75rem; }}
        
        .session {{ 
            background: rgba(255,255,255,0.03); 
            border-radius: 10px; 
            padding: 16px 20px;
            margin-bottom: 12px;
            border-left: 4px solid;
            display: grid;
            grid-template-columns: 1fr auto;
            gap: 20px;
            align-items: center;
        }}
        .session.waiting {{ border-color: #facc15; }}
        .session.ingame {{ border-color: #4ade80; }}
        
        .session-main {{ display: flex; flex-direction: column; gap: 6px; }}
        .session-name {{ font-weight: 600; font-size: 1rem; }}
        .session-details {{ display: flex; gap: 15px; flex-wrap: wrap; }}
        .session-detail {{ 
            color: #a1a1aa; 
            font-size: 0.8rem;
            display: flex;
            align-items: center;
            gap: 4px;
        }}
        
        .session-right {{ display: flex; align-items: center; gap: 15px; }}
        .session-players {{ 
            font-size: 1.1rem; 
            font-weight: 500;
            color: #e4e4e7;
        }}
        .session-status {{ 
            padding: 5px 14px; 
            border-radius: 20px; 
            font-size: 0.8rem;
            font-weight: 500;
        }}
        .session-status.waiting {{ background: rgba(250,204,21,0.15); color: #facc15; }}
        .session-status.ingame {{ background: rgba(74,222,128,0.15); color: #4ade80; }}
        
        .no-sessions {{ 
            color: #52525b; 
            text-align: center; 
            padding: 50px;
            background: rgba(255,255,255,0.02);
            border-radius: 10px;
        }}
        .footer {{ 
            text-align: center; 
            color: #3f3f46; 
            margin-top: 30px; 
            font-size: 0.75rem;
            padding: 15px;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <div class=""header"">
            <h1>🎮 Project VOID Server Monitor</h1>
            <div class=""server-id"">서버 ID: {EscapeHtml(cache.ServerId)}</div>
            <div class=""server-start"">시작 시간: {cache.ServerStartTime}</div>
        </div>
        
        <div class=""section"">
            <div class=""section-title"">📊 서버 상태</div>
            <div class=""stats"">
                <div class=""stat-card"">
                    <div class=""stat-label"">👥 플레이어</div>
                    <div class=""stat-value"">{cache.TotalPlayers} / {cache.MaxPlayers}</div>
                    <div class=""stat-sub"">최대 {cache.PeakPlayers}명 기록</div>
                </div>
                <div class=""stat-card"">
                    <div class=""stat-label"">📊 점유율</div>
                    <div class=""stat-value"" style=""color:{loadColor}"">{loadPercent:F0}%</div>
                </div>
                <div class=""stat-card"">
                    <div class=""stat-label"">⏱️ Uptime</div>
                    <div class=""stat-value"">{uptimeStr}</div>
                </div>
                <div class=""stat-card"">
                    <div class=""stat-label"">💾 메모리</div>
                    <div class=""stat-value"">{cache.MemoryMB} MB</div>
                    <div class=""stat-sub"">FPS: {cache.Fps:F0}</div>
                </div>
            </div>
        </div>
        
        <div class=""section"">
            <div class=""section-title"">📦 세션 ({cache.Sessions?.Count ?? 0}개)</div>
            <div class=""session-stats"">
                <div class=""session-stat"">
                    <div class=""session-stat-value"" style=""color:#facc15"">{cache.WaitingSessionCount}</div>
                    <div class=""session-stat-label"">대기 중</div>
                </div>
                <div class=""session-stat"">
                    <div class=""session-stat-value"" style=""color:#4ade80"">{cache.InGameSessionCount}</div>
                    <div class=""session-stat-label"">게임 중</div>
                </div>
                <div class=""session-stat"">
                    <div class=""session-stat-value"">{cache.Sessions?.Count ?? 0}</div>
                    <div class=""session-stat-label"">전체</div>
                </div>
            </div>");

        if (cache.Sessions == null || cache.Sessions.Count == 0)
        {
            sb.Append(@"<div class=""no-sessions"">활성 세션이 없습니다</div>");
        }
        else
        {
            foreach (var session in cache.Sessions)
            {
                string statusClass = session.Status == "InGame" ? "ingame" : "waiting";
                string statusText = session.Status == "InGame" ? "게임중" : "대기중";
                string roomCodeDisplay = string.IsNullOrEmpty(session.RoomCode) ? "" : $" [{session.RoomCode}]";
                
                // 세션 경과 시간 포맷
                TimeSpan sessionDuration = TimeSpan.FromSeconds(session.Duration);
                string durationStr = sessionDuration.TotalMinutes >= 1 
                    ? $"{(int)sessionDuration.TotalMinutes}분 {sessionDuration.Seconds}초"
                    : $"{sessionDuration.Seconds}초";

                sb.Append($@"
            <div class=""session {statusClass}"">
                <div class=""session-main"">
                    <span class=""session-name"">{EscapeHtml(session.Name)}{roomCodeDisplay}</span>
                    <div class=""session-details"">
                        <span class=""session-detail"">🎮 {session.GameMode}</span>
                        <span class=""session-detail"">📍 {session.Phase}</span>
                        <span class=""session-detail"">⏱️ {durationStr}</span>
                        <span class=""session-detail"">🔗 {session.ObjectCount} 객체</span>
                    </div>
                </div>
                <div class=""session-right"">
                    <span class=""session-players"">👥 {session.PlayerCount}/{session.MaxPlayers}</span>
                    <span class=""session-status {statusClass}"">{statusText}</span>
                </div>
            </div>");
            }
        }

        sb.Append($@"
        </div>
        
        <div class=""footer"">
            마지막 업데이트: {cache.ServerTime} · 1초마다 자동 갱신
        </div>
    </div>
</body>
</html>");

        return sb.ToString();
    }

    private string EscapeHtml(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    #endregion

    #region Data Classes

    private class ServerStatusCache
    {
        public string ServerTime = "";
        public string ServerStartTime = "";
        public string ServerId = "";
        public float Uptime = 0;
        public float Fps = 0;
        public int TotalPlayers = 0;
        public int MaxPlayers = 0;
        public int PeakPlayers = 0;
        public long MemoryMB = 0;
        public int WaitingSessionCount = 0;
        public int InGameSessionCount = 0;
        public List<SessionStatus> Sessions = new List<SessionStatus>();
    }

    #endregion
}

/// <summary>
/// 세션 상태 정보 (외부에서도 사용 가능하도록 public)
/// </summary>
[System.Serializable]
public class SessionStatus
{
    public string Name;
    public int PlayerCount;
    public int MaxPlayers;
    public string Status; // "Waiting", "InGame", "Ending"
    public string RoomCode;
    
    // 추가 정보
    public string GameMode;     // "4인", "8인", "연습", "커스텀"
    public string Phase;        // "Preparing", "Playing", "Ended"
    public float Duration;      // 세션 경과 시간 (초)
    public int ObjectCount;     // NetworkObject 수
}
