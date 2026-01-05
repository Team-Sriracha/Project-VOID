using UnityEngine;

/// <summary>
/// 조건부 컴파일을 사용한 디버그 로그 유틸리티.
/// Development 빌드에서만 로그를 출력하여 프로덕션 성능을 향상시킵니다.
/// </summary>
public static class DebugLogger
{
    #region Constants

    // Why: 개발 중 로그 활성화 여부 (프로덕션 빌드에서는 자동으로 비활성화)
    private const bool ENABLE_LOGS = true;

    #endregion

    #region Public Methods

    /// <summary>
    /// 일반 로그를 출력합니다 (Development 빌드에서만).
    /// </summary>
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Log(object message)
    {
        if (ENABLE_LOGS)
        {
            Debug.Log(message);
        }
    }

    /// <summary>
    /// 일반 로그를 출력합니다 (컨텍스트 포함, Development 빌드에서만).
    /// </summary>
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void Log(object message, Object context)
    {
        if (ENABLE_LOGS)
        {
            Debug.Log(message, context);
        }
    }

    /// <summary>
    /// 경고 로그를 출력합니다 (Development 빌드에서만).
    /// </summary>
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void LogWarning(object message)
    {
        if (ENABLE_LOGS)
        {
            Debug.LogWarning(message);
        }
    }

    /// <summary>
    /// 경고 로그를 출력합니다 (컨텍스트 포함, Development 빌드에서만).
    /// </summary>
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void LogWarning(object message, Object context)
    {
        if (ENABLE_LOGS)
        {
            Debug.LogWarning(message, context);
        }
    }

    /// <summary>
    /// 에러 로그를 출력합니다 (항상 출력됨).
    /// Why: 에러는 프로덕션에서도 기록되어야 함
    /// </summary>
    public static void LogError(object message)
    {
        Debug.LogError(message);
    }

    /// <summary>
    /// 에러 로그를 출력합니다 (컨텍스트 포함, 항상 출력됨).
    /// </summary>
    public static void LogError(object message, Object context)
    {
        Debug.LogError(message, context);
    }

    /// <summary>
    /// 네트워크 관련 로그를 출력합니다 (카테고리 태그 포함).
    /// </summary>
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void LogNetwork(string tag, object message)
    {
        if (ENABLE_LOGS)
        {
            Debug.Log($"<color=cyan>[{tag}]</color> {message}");
        }
    }

    /// <summary>
    /// 성능 관련 로그를 출력합니다 (카테고리 태그 포함).
    /// </summary>
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void LogPerformance(string tag, object message)
    {
        if (ENABLE_LOGS)
        {
            Debug.Log($"<color=yellow>[{tag}]</color> {message}");
        }
    }

    /// <summary>
    /// 게임플레이 관련 로그를 출력합니다 (카테고리 태그 포함).
    /// </summary>
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    public static void LogGameplay(string tag, object message)
    {
        if (ENABLE_LOGS)
        {
            Debug.Log($"<color=green>[{tag}]</color> {message}");
        }
    }

    #endregion
}
