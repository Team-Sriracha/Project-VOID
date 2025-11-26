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
    [SerializeField] private PlayerInventory _inventory;

    #endregion

    #region Networked Properties

    [Networked] public NetworkBool IsAiming { get; set; }
    [Networked] public Vector3 AimDirection { get; set; }

    // Why: 발사 시점의 방향이 불안정할 수 있어 마지막 유효한 방향을 저장
    [Networked] private Vector3 LastValidAimDirection { get; set; }

    #endregion

    #region Private Fields

    private Rigidbody _rb;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_aimVisualizer == null) _aimVisualizer = GetComponent<AimVisualizer>();
        if (_weapon == null) _weapon = GetComponent<NetworkedWeapon>();
        if (_inventory == null) _inventory = GetComponent<PlayerInventory>();
    }

    #endregion

    #region Fusion Lifecycle

    public override void FixedUpdateNetwork()
    {
        // Why: State Authority(서버)만 조준 상태 처리
        if (!HasStateAuthority) return;

        // Why: 벽 충돌로 생긴 각속도를 항상 0으로 (계속 회전하는 현상 방지)
        _rb.angularVelocity = Vector3.zero;

        bool isAttacking = false;

        if (GetInput(out NetworkInputData input))
        {
            // Why: 우클릭 홀드로 조준 모드 진입
            IsAiming = input.AimPressed;

            // Why: 우클릭 홀드 중일 때만 조준 방향 저장 (조준선 표시용)
            if (IsAiming)
            {
                AimDirection = input.AimDirection;
            }
            else
            {
                AimDirection = Vector3.zero;
            }

            // Why: 조준 중이거나 공격 중일 때 마우스 방향 저장 (발사/회전용)
            if ((input.AimPressed || input.AttackHeld || input.FirePressed) && input.AimDirection.sqrMagnitude > 0.01f)
            {
                LastValidAimDirection = input.AimDirection;
            }

            // 재장전 입력 처리
            if (input.ReloadPressed && _weapon != null)
            {
                _weapon.Reload();
            }

            // 무기 드랍 입력 처리 (G키)
            // Why: Server 모드에서는 서버가 모든 입력을 처리하므로 직접 호출
            // Why: ItemSlots[0]이 비어있으면 기본 무기이므로 드랍 불가
            if (input.DropWeaponPressed && _inventory != null && HasStateAuthority)
            {
                if (_inventory.ItemSlots[0] != default)
                {
                    _inventory.DropItem(0); // SLOT_WEAPON = 0
                }
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

            // Why: 조준도 공격도 안 할 때는 방향 리셋
            if (!IsAiming && !isAttacking)
            {
                LastValidAimDirection = Vector3.zero;
            }
        }

        // Why: 조준 중이거나 공격 중일 때만 마우스 방향으로 회전
        if ((IsAiming || isAttacking) && LastValidAimDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(LastValidAimDirection);
            Quaternion newRotation = Quaternion.Slerp(_rb.rotation, targetRotation, _rotationSpeed * Runner.DeltaTime);
            _rb.MoveRotation(newRotation);
        }
    }

    public override void Render()
    {
        // Why: Input Authority(자신의 플레이어)만 조준선 표시
        if (!HasInputAuthority || _aimVisualizer == null) return;

        // Why: 조준 중일 때만 조준선 표시, 무기 타입에 따라 조준선 타입이 바뀜
        if (IsAiming && AimDirection.sqrMagnitude > 0.01f && _weapon != null && _weapon.CurrentWeaponData != null)
        {
            Debug.Log($"[PlayerAimController] ShowAimIndicator - WeaponType: {_weapon.CurrentWeaponData.GetType().Name}");
            _aimVisualizer.ShowAimIndicator(_weapon.CurrentWeaponData, AimDirection);
        }
        else
        {
            _aimVisualizer.HideAimIndicator();
        }
    }

    #endregion
}