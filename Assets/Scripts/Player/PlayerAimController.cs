using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어의 조준, 회전, 발사를 처리합니다.
/// 좌클릭 발사, 우클릭 조준
/// </summary>
public class PlayerAimController : NetworkBehaviour
{
    #region Serialized Fields

    [Header("조준 설정")]
    [SerializeField] private float _rotationSpeed = 10f;

    [Header("컴포넌트 참조")]
    [SerializeField] private AimVisualizer _aimVisualizer;
    [SerializeField] private NetworkedWeapon _weapon;

    #endregion

    #region Networked Properties

    [Networked] public NetworkBool IsAiming { get; set; }
    [Networked] public Vector3 AimDirection { get; set; }

    // Why: 발사 시점의 방향이 불안정할 수 있어 마지막 유효한 방향을 저장
    [Networked] private Vector3 LastValidAimDirection { get; set; }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_aimVisualizer == null) _aimVisualizer = GetComponent<AimVisualizer>();
        if (_weapon == null) _weapon = GetComponent<NetworkedWeapon>();
    }

    #endregion

    #region Fusion Lifecycle

    public override void FixedUpdateNetwork()
    {
        // Why: State Authority(서버)만 조준 상태 처리
        if (!HasStateAuthority) return;

        bool isAttacking = false;

        if (GetInput(out NetworkInputData input))
        {
            // Why: 우클릭 홀드로 조준 모드 진입
            IsAiming = input.AimPressed;

            // Why: 마우스 방향 항상 저장 (좌클릭/우클릭 모두 사용)
            if (input.AimDirection.sqrMagnitude > 0.01f)
            {
                LastValidAimDirection = input.AimDirection;
            }

            // Why: 우클릭 홀드 중일 때만 조준 방향 저장 (조준선 표시용)
            if (IsAiming)
            {
                AimDirection = input.AimDirection;
            }
            else
            {
                AimDirection = Vector3.zero;
            }

            // 재장전 입력 처리
            if (input.ReloadPressed && _weapon != null)
            {
                _weapon.Reload();
            }

            // Why: FireMode에 따라 발사 로직 분기
            if (_weapon != null && LastValidAimDirection.sqrMagnitude > 0.01f)
            {
                EFireMode fireMode = _weapon.CurrentWeaponData?.FireMode ?? EFireMode.SemiAuto;

                bool shouldFire = fireMode switch
                {
                    EFireMode.SemiAuto => input.FirePressed,   // 단발: 클릭할 때마다
                    EFireMode.FullAuto => input.AttackHeld,    // 연사: 홀드 중 계속
                    _ => input.FirePressed
                };

                if (shouldFire)
                {
                    _weapon.Fire(LastValidAimDirection);
                    isAttacking = true;
                }
            }

            // 공격 홀드 중인지도 체크
            if (!isAttacking)
            {
                isAttacking = input.AttackHeld;
            }
        }

        // Why: 조준 중이거나 공격 중일 때만 마우스 방향으로 회전
        if ((IsAiming || isAttacking) && LastValidAimDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(LastValidAimDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, _rotationSpeed * Runner.DeltaTime);
        }
    }

    public override void Render()
    {
        // Why: Input Authority(자신의 플레이어)만 조준선 표시
        if (!HasInputAuthority || _aimVisualizer == null) return;

        if (IsAiming && AimDirection.sqrMagnitude > 0.01f && _weapon != null)
        {
            _aimVisualizer.ShowAimIndicator(_weapon.CurrentWeaponData, AimDirection);
        }
        else
        {
            _aimVisualizer.HideAimIndicator();
        }
    }

    #endregion
}
