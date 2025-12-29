using System;
using System.Collections.Generic;
using Fusion;

/// <summary>
/// Runner 범위 서비스 등록/조회용 단순 Service Locator.
/// </summary>
public static class ServiceLocator
{
    private static readonly Dictionary<NetworkRunner, Dictionary<Type, object>> _servicesByRunner = new();
    private static readonly object _lock = new object();

    public static void Register<T>(NetworkRunner runner, T service) where T : class
    {
        if (runner == null || service == null) return;

        lock (_lock)
        {
            if (!_servicesByRunner.TryGetValue(runner, out var services))
            {
                services = new Dictionary<Type, object>();
                _servicesByRunner[runner] = services;
            }

            services[typeof(T)] = service;
        }
    }

    public static bool TryGet<T>(NetworkRunner runner, out T service) where T : class
    {
        service = null;
        if (runner == null) return false;

        lock (_lock)
        {
            if (_servicesByRunner.TryGetValue(runner, out var services) &&
                services.TryGetValue(typeof(T), out var obj))
            {
                service = obj as T;
                return service != null;
            }
        }

        return false;
    }

    public static T Get<T>(NetworkRunner runner) where T : class
    {
        TryGet(runner, out T service);
        return service;
    }

    public static void Clear(NetworkRunner runner)
    {
        if (runner == null) return;

        lock (_lock)
        {
            _servicesByRunner.Remove(runner);
        }
    }

    public static void ClearAll()
    {
        lock (_lock)
        {
            _servicesByRunner.Clear();
        }
    }
}
