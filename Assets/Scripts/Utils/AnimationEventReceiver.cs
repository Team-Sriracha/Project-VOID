using UnityEngine;

/// <summary>
/// 자식 오브젝트(Animator가 있는 모델)에 부착하여,
/// Animation Event를 부모 오브젝트(Controller가 있는 루트)로 전달하는 역할
/// </summary>
public class AnimationEventReceiver : MonoBehaviour
{
    private MobAnimationController _mobController;

    private void Awake()
    {
        // 부모에서 컨트롤러 찾기
        _mobController = GetComponentInParent<MobAnimationController>();
    }

    // Animator Event에서 호출될 함수 이름
    public void OnMobAttackHit()
    {
        if (_mobController != null)
        {
            _mobController.OnMobAttackHit();
        }
        else
        {
            // 부모를 다시 찾아봄 (혹시 Awake 시점에 실패했을 경우)
            _mobController = GetComponentInParent<MobAnimationController>();
            if (_mobController != null)
            {
                _mobController.OnMobAttackHit();
            }
        }
    }
}
