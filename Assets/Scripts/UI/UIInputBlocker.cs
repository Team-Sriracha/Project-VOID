using UnityEngine;

/// <summary>
/// 이 컴포넌트가 붙은 UI 요소는 플레이어의 입력(조준, 발사)을 차단합니다.
/// </summary>
public class UIInputBlocker : MonoBehaviour, IInputBlocker
{
    [SerializeField] private bool _isBlocking = true;

    public bool ShouldBlockInput()
    {
        return _isBlocking && gameObject.activeInHierarchy;
    }
    
    public void SetBlocking(bool isBlocking)
    {
        _isBlocking = isBlocking;
    }
}
