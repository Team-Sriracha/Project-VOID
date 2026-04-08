using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 모바일 우측 조준 스틱의 터치 유지 상태를 추적합니다.
/// 조준 해제는 손가락을 떼는 순간에만 발생하도록 입력 상태를 분리합니다.
/// </summary>
public class MobileAimStickTouchTracker : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IBeginDragHandler, IEndDragHandler
{
    #region Private Fields

    private static int _activePointerId = int.MinValue;

    #endregion

    #region Unity Lifecycle

    private void OnEnable()
    {
        MobileAimInputState.HasTracker = true;
        ResetState();
    }

    private void OnDisable()
    {
        MobileAimInputState.HasTracker = false;
        ResetState();
    }

    #endregion

    #region Event Handlers

    public void OnPointerDown(PointerEventData eventData)
    {
        if (MobileAimInputState.IsAimStickPressed)
        {
            return;
        }

        MobileAimInputState.IsAimStickPressed = true;
        _activePointerId = eventData.pointerId;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        ReleaseIfActive(eventData.pointerId);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (MobileAimInputState.IsAimStickPressed)
        {
            return;
        }

        MobileAimInputState.IsAimStickPressed = true;
        _activePointerId = eventData.pointerId;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        ReleaseIfActive(eventData.pointerId);
    }

    #endregion

    #region Helper Methods

    private static void ReleaseIfActive(int pointerId)
    {
        if (pointerId != _activePointerId)
        {
            return;
        }

        ResetState();
    }

    private static void ResetState()
    {
        MobileAimInputState.IsAimStickPressed = false;
        _activePointerId = int.MinValue;
    }

    #endregion
}
