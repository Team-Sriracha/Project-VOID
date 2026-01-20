using FishNet.Object.Prediction;
using UnityEngine;

/// <summary>
/// 플레이어 이동 상태 데이터 (CSP Reconcile용)
/// 서버가 검증한 상태를 클라이언트에 전송하여 동기화합니다.
/// </summary>
public struct MoveReconcileData : IReconcileData
{
    /// <summary>
    /// 서버가 검증한 위치
    /// </summary>
    public Vector3 Position;
    
    /// <summary>
    /// 서버가 검증한 회전
    /// </summary>
    public Quaternion Rotation;
    
    /// <summary>
    /// 현재 이동 속도
    /// </summary>
    public Vector3 CurrentVelocity;
    
    /// <summary>
    /// 대시 쿨다운 종료 시간 (Tick 기준)
    /// </summary>
    public uint DashCooldownEndTick;
    
    /// <summary>
    /// 남은 대시 거리
    /// </summary>
    public float DashRemainingDistance;
    
    /// <summary>
    /// 대시 방향
    /// </summary>
    public Vector3 DashDirection;

    private uint _tick;

    public MoveReconcileData(
        Vector3 position, 
        Quaternion rotation, 
        Vector3 currentVelocity, 
        uint dashCooldownEndTick,
        float dashRemainingDistance,
        Vector3 dashDirection)
    {
        Position = position;
        Rotation = rotation;
        CurrentVelocity = currentVelocity;
        DashCooldownEndTick = dashCooldownEndTick;
        DashRemainingDistance = dashRemainingDistance;
        DashDirection = dashDirection;
        _tick = 0;
    }

    public void Dispose() { }
    public uint GetTick() => _tick;
    public void SetTick(uint value) => _tick = value;
}
