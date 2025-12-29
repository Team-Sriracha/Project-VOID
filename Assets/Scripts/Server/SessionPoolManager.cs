using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GameSessionController 오브젝트 풀
/// </summary>
public class SessionPoolManager : MonoBehaviour
{
    [SerializeField] private GameSessionController _sessionPrefab;
    [SerializeField] private int _initialPoolSize = 2;

    private readonly Queue<GameSessionController> _pool = new();

    private void Awake() => Prewarm();

    public GameSessionController Get() => _pool.Count > 0 ? _pool.Dequeue() : CreateInstance();

    public void Return(GameSessionController controller)
    {
        if (controller != null) _pool.Enqueue(controller);
    }

    private void Prewarm()
    {
        for (int i = 0; i < _initialPoolSize; i++)
        {
            var instance = CreateInstance();
            if (instance != null) _pool.Enqueue(instance);
        }
    }

    private GameSessionController CreateInstance()
    {
        if (_sessionPrefab == null)
        {
            Debug.LogWarning("[SessionPoolManager] 프리팹 미설정");
            return null;
        }
        return Instantiate(_sessionPrefab, transform);
    }
}
