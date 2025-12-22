using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GameSessionController 풀을 관리합니다.
/// </summary>
public class SessionPoolManager : MonoBehaviour
{
    #region Serialized Fields

    [SerializeField] private GameSessionController _sessionPrefab;
    [SerializeField] private int _initialPoolSize = 2;

    #endregion

    #region Private Fields

    private readonly Queue<GameSessionController> _pool = new Queue<GameSessionController>();

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        Prewarm();
    }

    #endregion

    #region Public Methods

    public GameSessionController Get()
    {
        if (_pool.Count > 0)
        {
            return _pool.Dequeue();
        }

        return CreateInstance();
    }

    public void Return(GameSessionController controller)
    {
        if (controller == null) return;
        _pool.Enqueue(controller);
    }

    #endregion

    #region Helper Methods

    private void Prewarm()
    {
        for (int i = 0; i < _initialPoolSize; i++)
        {
            var instance = CreateInstance();
            if (instance != null)
            {
                _pool.Enqueue(instance);
            }
        }
    }

    private GameSessionController CreateInstance()
    {
        if (_sessionPrefab == null)
        {
            Debug.LogWarning("[SessionPoolManager] Session prefab is not set.");
            return null;
        }

        var obj = Instantiate(_sessionPrefab, transform);
        return obj;
    }

    #endregion
}
