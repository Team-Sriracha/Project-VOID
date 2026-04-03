using FishNet.Object.Prediction;
using UnityEngine;

/// <summary>
/// 플레이어 이동 입력 데이터 (CSP Replicate용)
/// Fish-Net Prediction V2 클라이언트 입력 전송용
/// </summary>
public struct MoveReplicateData : IReplicateData
{
    /// <summary>
    /// 이동 방향 (WASD 입력)
    /// </summary>
    public Vector3 MoveDirection;
    
    /// <summary>
    /// 조준 방향 (마우스/터치 위치)
    /// </summary>
    public Vector3 AimDirection;
    
    /// <summary>
    /// 대시 입력 여부
    /// </summary>
    public bool DashPressed;
    
    /// <summary>
    /// 조준 버튼 홀드 여부
    /// </summary>
    public bool AimHeld;
    
    /// <summary>
    /// 발사 버튼 홀드 여부 (발사 중 회전용)
    /// </summary>
    public bool FireHeld;

    private uint _tick;

    public MoveReplicateData(Vector3 moveDirection, Vector3 aimDirection, bool dashPressed, bool aimHeld, bool fireHeld)
    {
        MoveDirection = moveDirection;
        AimDirection = aimDirection;
        DashPressed = dashPressed;
        AimHeld = aimHeld;
        FireHeld = fireHeld;
        _tick = 0;
    }

    public void Dispose() { }
    public uint GetTick() => _tick;
    public void SetTick(uint value) => _tick = value;
}

