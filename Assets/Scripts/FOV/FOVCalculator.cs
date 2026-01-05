using UnityEngine;
using Fusion;

/// <summary>
/// FOV 레이캐스트를 수행합니다. Multi-Peer 환경에서 올바른 Physics Scene을 사용합니다.
/// </summary>
public class FOVCalculator
{
    #region Constants

    private const int MAX_RAY_COUNT = 180;

    #endregion

    #region Private Fields

    private LayerMask _obstacleLayers;
    private PhysicsScene _physicsScene;
    private bool _hasValidPhysicsScene;
    private float[] _hitDistanceBuffer = new float[MAX_RAY_COUNT + 1];

    #endregion

    #region Initialization

    public void Initialize(LayerMask obstacleLayers, NetworkRunner runner)
    {
        _obstacleLayers = obstacleLayers;
        _hasValidPhysicsScene = false;

        if (runner?.SceneManager != null &&
            runner.SceneManager.TryGetPhysicsScene3D(out var scene) && scene.IsValid())
        {
            _physicsScene = scene;
            _hasValidPhysicsScene = true;
        }
    }

    #endregion

    #region FOV Calculation

    public float[] CalculateCircularFOV(Vector3 origin, Vector3 forward, float radius, int rayCount, float height)
    {
        return CalculateFOV(origin, forward, radius, -180f, 180f, rayCount, height);
    }

    public float[] CalculateFanFOV(Vector3 origin, Vector3 forward, float range, float angle, int rayCount, float height)
    {
        float halfAngle = angle * 0.5f;
        return CalculateFOV(origin, forward, range, -halfAngle, halfAngle, rayCount, height);
    }

    /// <summary>
    /// 복합 FOV를 계산합니다 (주변 원형 시야 + 부채꼴 조준 시야).
    /// </summary>
    public float[] CalculateCompositeFOV(Vector3 origin, Vector3 forward, float fanRange, float fanAngle,
        float peripheralRange, int rayCount, float height)
    {
        EnsureBufferSize(rayCount);

        Vector3 rayStart = origin + Vector3.up * height;
        Quaternion rotation = Quaternion.LookRotation(forward);
        float angleStep = 360f / rayCount;
        float halfFanAngle = fanAngle * 0.5f;

        for (int i = 0; i <= rayCount; i++)
        {
            float currentAngle = -180f + (angleStep * i);
            Vector3 direction = rotation * Quaternion.Euler(0, currentAngle, 0) * Vector3.forward;

            float maxRange = Mathf.Abs(currentAngle) <= halfFanAngle ? fanRange : peripheralRange;
            float hitDistance = PerformRaycast(rayStart, direction, maxRange, origin);
            _hitDistanceBuffer[i] = hitDistance;
        }

        return _hitDistanceBuffer;
    }

    private float[] CalculateFOV(Vector3 origin, Vector3 forward, float maxRange,
        float startAngle, float endAngle, int rayCount, float height)
    {
        EnsureBufferSize(rayCount);

        Vector3 rayStart = origin + Vector3.up * height;
        Quaternion rotation = Quaternion.LookRotation(forward);
        float angleStep = (endAngle - startAngle) / rayCount;

        for (int i = 0; i <= rayCount; i++)
        {
            float currentAngle = startAngle + (angleStep * i);
            Vector3 direction = rotation * Quaternion.Euler(0, currentAngle, 0) * Vector3.forward;
            float hitDistance = PerformRaycast(rayStart, direction, maxRange, origin);
            _hitDistanceBuffer[i] = hitDistance;
        }

        return _hitDistanceBuffer;
    }

    #endregion

    #region Helper Methods

    private void EnsureBufferSize(int rayCount)
    {
        if (_hitDistanceBuffer.Length < rayCount + 1)
        {
            _hitDistanceBuffer = new float[rayCount + 1];
        }
    }

    private float PerformRaycast(Vector3 rayStart, Vector3 direction, float maxRange, Vector3 origin)
    {
        bool hasHit;
        RaycastHit hit;

        if (_hasValidPhysicsScene && _physicsScene.IsValid())
        {
            hasHit = _physicsScene.Raycast(rayStart, direction, out hit, maxRange, _obstacleLayers);
        }
        else
        {
            hasHit = Physics.Raycast(rayStart, direction, out hit, maxRange, _obstacleLayers);
        }

        return hasHit ? Vector3.Distance(origin, hit.point) : maxRange;
    }

    #endregion
}
