using System;
using System.Collections.Generic;

/// <summary>
/// 전역 서비스 등록/조회용 단순 Service Locator.
/// Fishnet에서는 Per-Session 프로세스 아키텍처를 사용하므로 전역 서비스로 충분합니다.
/// </summary>
public static class ServiceLocator
{
    #region Private Fields

    private static readonly Dictionary<Type, object> _services = new();
    private static readonly object _lock = new object();

    /// <summary>
    ///  씬 로드 시 서비스 목록을 초기화합니다.
    /// </summary>
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        ClearAll();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 서비스를 등록합니다.
    /// </summary>
    /// <typeparam name="T">등록할 서비스 타입</typeparam>
    /// <param name="service">서비스 인스턴스</param>
    public static void Register<T>(T service) where T : class
    {
        if (service == null) return;

        lock (_lock)
        {
            _services[typeof(T)] = service;
        }
    }

    /// <summary>
    /// 서비스를 조회합니다.
    /// </summary>
    /// <typeparam name="T">조회할 서비스 타입</typeparam>
    /// <param name="service">출력 서비스 인스턴스</param>
    /// <returns>서비스 조회 성공 여부</returns>
    public static bool TryGet<T>(out T service) where T : class
    {
        service = null;

        lock (_lock)
        {
            if (_services.TryGetValue(typeof(T), out var obj))
            {
                service = obj as T;
                return service != null;
            }
        }

        return false;
    }

    /// <summary>
    /// 서비스를 조회합니다.
    /// </summary>
    /// <typeparam name="T">조회할 서비스 타입</typeparam>
    /// <returns>서비스 인스턴스 또는 null</returns>
    public static T Get<T>() where T : class
    {
        TryGet(out T service);
        return service;
    }

    /// <summary>
    /// 서비스 등록을 해제합니다.
    /// </summary>
    /// <typeparam name="T">해제할 서비스 타입</typeparam>
    public static void Unregister<T>() where T : class
    {
        lock (_lock)
        {
            _services.Remove(typeof(T));
        }
    }

    /// <summary>
    /// 모든 서비스를 해제합니다.
    /// </summary>
    public static void ClearAll()
    {
        lock (_lock)
        {
            _services.Clear();
        }
    }

    #endregion
}
