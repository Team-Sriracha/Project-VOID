using UnityEngine;

/// <summary>
/// FOV 스냅샷을 기준으로 월드 좌표와 콜라이더의 가시성을 판정합니다.
/// </summary>
public static class FOVVisibilityQuery
{
    #region Constants

    private const float DEFAULT_DISTANCE_PADDING = 0f;

    #endregion

    #region Public Methods

    /// <summary>
    /// 주어진 좌표에서 경계 샘플 거리와 현재 거리를 계산합니다.
    /// </summary>
    public static bool TryGetBoundaryMetrics(FOVStateSnapshot snapshot, Vector3 worldPos,
        out float currentDistance, out float boundaryDistance)
    {
        currentDistance = 0f;
        boundaryDistance = 0f;

        if (snapshot == null || !snapshot.HasData)
        {
            return false;
        }

        Vector3 toTarget = worldPos - snapshot.Origin;
        toTarget.y = 0f;
        currentDistance = toTarget.magnitude;

        Vector3 forward = snapshot.Forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }
        else
        {
            forward.Normalize();
        }

        if (toTarget.sqrMagnitude < 0.0001f)
        {
            boundaryDistance = snapshot.GetHitDistance(0);
            return true;
        }

        float angleToTarget = Vector3.SignedAngle(forward, toTarget, Vector3.up);
        if (angleToTarget < snapshot.StartAngle || angleToTarget > snapshot.EndAngle)
        {
            return false;
        }

        float angleRange = snapshot.EndAngle - snapshot.StartAngle;
        if (angleRange <= 0f)
        {
            return false;
        }

        float t = (angleToTarget - snapshot.StartAngle) / angleRange;
        int index = Mathf.Clamp(Mathf.RoundToInt(t * (snapshot.HitDistanceCount - 1)), 0, snapshot.HitDistanceCount - 1);
        boundaryDistance = snapshot.GetHitDistance(index);
        return true;
    }

    /// <summary>
    /// 주어진 월드 좌표가 현재 FOV 내부에 있는지 확인합니다.
    /// </summary>
    public static bool IsInside(FOVStateSnapshot snapshot, Vector3 worldPos, float distancePadding = DEFAULT_DISTANCE_PADDING)
    {
        if (!TryGetBoundaryMetrics(snapshot, worldPos, out float distance, out float boundaryDistance))
        {
            return false;
        }

        return distance <= boundaryDistance + distancePadding;
    }

    /// <summary>
    /// 콜라이더의 일부라도 FOV 안에 있는지 확인합니다.
    /// </summary>
    public static bool IsColliderInside(FOVStateSnapshot snapshot, Collider collider, float distancePadding = DEFAULT_DISTANCE_PADDING)
    {
        if (collider == null)
        {
            return false;
        }

        Bounds bounds = collider.bounds;
        if (IsInside(snapshot, bounds.center, distancePadding))
        {
            return true;
        }

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        return IsInside(snapshot, new Vector3(min.x, min.y, min.z), distancePadding) ||
               IsInside(snapshot, new Vector3(max.x, min.y, min.z), distancePadding) ||
               IsInside(snapshot, new Vector3(min.x, min.y, max.z), distancePadding) ||
               IsInside(snapshot, new Vector3(max.x, min.y, max.z), distancePadding) ||
               IsInside(snapshot, new Vector3(min.x, max.y, min.z), distancePadding) ||
               IsInside(snapshot, new Vector3(max.x, max.y, min.z), distancePadding) ||
               IsInside(snapshot, new Vector3(min.x, max.y, max.z), distancePadding) ||
               IsInside(snapshot, new Vector3(max.x, max.y, max.z), distancePadding);
    }

    #endregion
}
