using Fusion;
using UnityEngine;

/// <summary>
/// 네트워크를 통해 전송되는 플레이어 입력 데이터입니다.
/// </summary>
public struct NetworkInputData : INetworkInput
{
    // 이동
    public Vector3 MoveDirection;
    public NetworkBool DashPressed;

    // 조준 & 발사
    public Vector3 AimDirection;
    public NetworkBool AimPressed;     // 우클릭 홀드 (조준 모드)
    public NetworkBool FirePressed;    // 좌클릭 프레스 (단발)
    public NetworkBool AttackHeld;     // 좌클릭 홀드 (연사)

    // 재장전
    public NetworkBool ReloadPressed;  // R키 프레스

    // 아이템 드랍
    public NetworkBool DropWeaponPressed;  // G키 프레스
}
