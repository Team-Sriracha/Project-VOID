using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// 짧은 방 코드 생성 및 관리 유틸리티
/// 6자리 코드 (대문자 + 숫자) 생성
/// </summary>
public static class RoomCodeGenerator
{
    #region Constants

    private const string CODE_CHARS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // O, I, 0, 1 제외 (혼동 방지)
    private const int CODE_LENGTH = 6;

    #endregion

    #region Private Fields

    private static readonly Random _random = new Random();
    private static readonly HashSet<string> _usedCodes = new HashSet<string>();

    #endregion

    #region Public Methods

    /// <summary>
    /// 새로운 방 코드 생성 (로컬 중복 방지)
    /// </summary>
    /// <returns>6자리 방 코드 (예: "AB3K7M")</returns>
    public static string GenerateCode()
    {
        const int maxAttempts = 100;
        int attempts = 0;

        while (attempts < maxAttempts)
        {
            string code = GenerateRandomCode();

            if (!_usedCodes.Contains(code))
            {
                _usedCodes.Add(code);
                return code;
            }

            attempts++;
        }

        // Why: 100번 시도해도 중복이면 그냥 반환 (확률상 거의 불가능)
        return GenerateRandomCode();
    }

    /// <summary>
    /// 서버에서 사용: 기존 세션 목록과 비교하여 중복되지 않는 방 코드 생성
    /// </summary>
    /// <param name="existingRoomCodes">기존에 사용 중인 방 코드 목록</param>
    /// <param name="maxRetries">최대 재시도 횟수</param>
    /// <returns>중복되지 않는 방 코드</returns>
    public static string GenerateUniqueCodeSync(IEnumerable<string> existingRoomCodes, int maxRetries = 100)
    {
        var existingSet = new HashSet<string>(existingRoomCodes ?? Enumerable.Empty<string>());

        for (int i = 0; i < maxRetries; i++)
        {
            string code = GenerateRandomCode();

            if (!existingSet.Contains(code) && !_usedCodes.Contains(code))
            {
                _usedCodes.Add(code);
                UnityEngine.Debug.Log($"[RoomCodeGenerator] Generated unique code: {code} (attempt {i + 1}/{maxRetries})");
                return code;
            }
        }

        // Fallback: 그냥 랜덤 코드 반환 (32^6 = 10억 개 조합이라 충돌 확률 극히 낮음)
        string fallbackCode = GenerateRandomCode();
        _usedCodes.Add(fallbackCode);
        UnityEngine.Debug.LogWarning($"[RoomCodeGenerator] Using fallback code after {maxRetries} attempts: {fallbackCode}");
        return fallbackCode;
    }

    /// <summary>
    /// 서버 세션 검색을 통한 중복 체크로 유니크한 방 코드 생성 (클라이언트용)
    /// </summary>
    /// <param name="matchmakingManager">세션 검색을 위한 MatchmakingManager 인스턴스</param>
    /// <param name="maxRetries">최대 재시도 횟수</param>
    /// <returns>중복되지 않는 방 코드</returns>
    public static async Task<string> GenerateUniqueCode(MatchmakingManager matchmakingManager, int maxRetries = 10)
    {
        if (matchmakingManager == null)
        {
            throw new ArgumentNullException(nameof(matchmakingManager));
        }

        for (int i = 0; i < maxRetries; i++)
        {
            string code = GenerateRandomCode();

            // Why: 현재 존재하는 모든 세션의 방 코드와 중복 확인
            var sessions = await matchmakingManager.GetAllGameSessions();
            bool isDuplicate = sessions.Any(s => 
            {
                // Session Properties에서 RoomCode 확인
                if (s.Properties != null && s.Properties.TryGetValue("RoomCode", out var roomCodeProp))
                {
                    string existingCode = roomCodeProp.PropertyValue?.ToString();
                    return string.Equals(existingCode, code, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            });

            if (!isDuplicate && !_usedCodes.Contains(code))
            {
                _usedCodes.Add(code);
                UnityEngine.Debug.Log($"[RoomCodeGenerator] Generated unique code: {code} (attempt {i + 1}/{maxRetries})");
                return code;
            }

            UnityEngine.Debug.LogWarning($"[RoomCodeGenerator] Code {code} already exists, retrying... ({i + 1}/{maxRetries})");
        }

        throw new Exception($"Failed to generate unique room code after {maxRetries} attempts");
    }

    /// <summary>
    /// 세션 리스트와 비교하여 중복되지 않는 방 코드 생성 (서버용)
    /// </summary>
    /// <param name="sessionFetcher">세션 리스트를 반환하는 비동기 함수</param>
    /// <param name="maxRetries">최대 재시도 횟수</param>
    /// <returns>중복되지 않는 방 코드</returns>
    public static async Task<string> GenerateUniqueCodeWithFetcher(System.Func<Task<List<Fusion.SessionInfo>>> sessionFetcher, int maxRetries = 10)
    {
        if (sessionFetcher == null)
        {
            throw new ArgumentNullException(nameof(sessionFetcher));
        }

        for (int i = 0; i < maxRetries; i++)
        {
            string code = GenerateRandomCode();

            // Why: 현재 존재하는 모든 세션의 방 코드와 중복 확인
            var sessions = await sessionFetcher();
            bool isDuplicate = sessions.Any(s => 
            {
                if (s.Properties != null && s.Properties.TryGetValue("RoomCode", out var roomCodeProp))
                {
                    string existingCode = roomCodeProp.PropertyValue?.ToString();
                    return string.Equals(existingCode, code, StringComparison.OrdinalIgnoreCase);
                }
                return false;
            });

            if (!isDuplicate && !_usedCodes.Contains(code))
            {
                _usedCodes.Add(code);
                UnityEngine.Debug.Log($"[RoomCodeGenerator] Generated unique code: {code} (attempt {i + 1}/{maxRetries})");
                return code;
            }

            UnityEngine.Debug.LogWarning($"[RoomCodeGenerator] Code {code} already exists, retrying... ({i + 1}/{maxRetries})");
        }

        throw new Exception($"Failed to generate unique room code after {maxRetries} attempts");
    }

    /// <summary>
    /// 방 코드 사용 완료 (재사용 가능하도록 제거)
    /// </summary>
    /// <param name="code">해제할 방 코드</param>
    public static void ReleaseCode(string code)
    {
        _usedCodes.Remove(code);
    }

    /// <summary>
    /// 방 코드 유효성 검증
    /// </summary>
    /// <param name="code">검증할 코드</param>
    /// <returns>유효 여부</returns>
    public static bool IsValidCode(string code)
    {
        if (string.IsNullOrEmpty(code) || code.Length != CODE_LENGTH)
            return false;

        foreach (char c in code.ToUpper())
        {
            if (!CODE_CHARS.Contains(c.ToString()))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 모든 사용 중인 코드 초기화
    /// </summary>
    public static void ClearAllCodes()
    {
        _usedCodes.Clear();
    }

    /// <summary>
    /// 방 코드 정규화 (ABC-123 → ABC123, 대문자 변환, 공백 제거)
    /// </summary>
    /// <param name="code">정규화할 코드</param>
    /// <returns>정규화된 코드</returns>
    public static string Normalize(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        return code.ToUpper().Replace("-", "").Replace(" ", "").Trim();
    }

    /// <summary>
    /// 방 코드 포맷팅 (ABC123 형식 유지 - 특수문자 없음)
    /// </summary>
    /// <param name="code">포맷팅할 코드</param>
    /// <returns>정규화된 코드 (하이픈 없음)</returns>
    public static string Format(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;

        // Why: 특수문자 없이 ABC123 형식으로 반환
        return Normalize(code);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 랜덤 코드 생성 (중복 체크 없음)
    /// </summary>
    private static string GenerateRandomCode()
    {
        char[] code = new char[CODE_LENGTH];

        for (int i = 0; i < CODE_LENGTH; i++)
        {
            code[i] = CODE_CHARS[_random.Next(CODE_CHARS.Length)];
        }

        return new string(code);
    }

    #endregion
}
