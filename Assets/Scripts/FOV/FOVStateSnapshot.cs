using System;
using UnityEngine;

/// <summary>
/// 현재 FOV 계산 결과를 보관하는 읽기 중심 데이터 컨테이너입니다.
/// 렌더링과 질의가 동일한 스냅샷을 참조하도록 단일 기준점을 제공합니다.
/// </summary>
public class FOVStateSnapshot
{
    #region Properties

    public Vector3 Origin { get; private set; }
    public Vector3 Forward { get; private set; } = Vector3.forward;
    public float CurrentRange { get; private set; }
    public float CurrentAngle { get; private set; }
    public float StartAngle { get; private set; } = -180f;
    public float EndAngle { get; private set; } = 180f;
    public int HitDistanceCount { get; private set; }
    public bool HasData => HitDistanceCount > 1;

    #endregion

    #region Private Fields

    private float[] _hitDistances = Array.Empty<float>();

    #endregion

    #region Public Methods

    /// <summary>
    /// 스냅샷 데이터를 현재 계산 결과로 갱신합니다.
    /// </summary>
    public void Update(Vector3 origin, Vector3 forward, float currentRange, float currentAngle,
        float startAngle, float endAngle, float[] hitDistances, int hitDistanceCount)
    {
        Origin = origin;
        Forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        CurrentRange = currentRange;
        CurrentAngle = currentAngle;
        StartAngle = startAngle;
        EndAngle = endAngle;

        int clampedCount = hitDistances != null
            ? Mathf.Clamp(hitDistanceCount, 0, hitDistances.Length)
            : 0;

        EnsureBufferSize(clampedCount);

        if (clampedCount > 0)
        {
            Array.Copy(hitDistances, _hitDistances, clampedCount);
        }

        HitDistanceCount = clampedCount;
    }

    /// <summary>
    /// 지정한 인덱스의 시야 경계 거리를 반환합니다.
    /// </summary>
    public float GetHitDistance(int index)
    {
        if (HitDistanceCount <= 0)
        {
            return 0f;
        }

        int clampedIndex = Mathf.Clamp(index, 0, HitDistanceCount - 1);
        return _hitDistances[clampedIndex];
    }

    /// <summary>
    /// 셰이더 전달용 버퍼에 현재 경계 거리를 복사합니다.
    /// </summary>
    public int CopyHitDistancesTo(float[] destination, int maxCount, float fallbackDistance)
    {
        if (destination == null || destination.Length == 0 || maxCount <= 0)
        {
            return 0;
        }

        int copyLimit = Mathf.Min(destination.Length, maxCount);
        int copiedCount = Mathf.Min(HitDistanceCount, copyLimit);
        float fillDistance = fallbackDistance;

        if (copiedCount >= 2)
        {
            Array.Copy(_hitDistances, destination, copiedCount);
            fillDistance = destination[copiedCount - 1];
        }
        else
        {
            copiedCount = Mathf.Min(2, copyLimit);
            for (int i = 0; i < copiedCount; i++)
            {
                destination[i] = fillDistance;
            }
        }

        for (int i = copiedCount; i < copyLimit; i++)
        {
            destination[i] = fillDistance;
        }

        return copiedCount;
    }

    #endregion

    #region Helper Methods

    private void EnsureBufferSize(int count)
    {
        if (_hitDistances.Length < count)
        {
            _hitDistances = new float[count];
        }
    }

    #endregion
}
