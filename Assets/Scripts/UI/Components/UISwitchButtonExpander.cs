using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

public class UISwitchButtonExpander : MonoBehaviour
{
    [Header("설정")]
    [Tooltip("제어할 대상 SwitchButton")]
    [SerializeField] private UISwitchButton targetSwitch;
    
    [Tooltip("세번째 공간에 표시할 닫기 버튼용 프리팹 (Button 컴포넌트 포함 필수)")]
    [SerializeField] private GameObject closeButtonPrefab;

    [Header("이벤트")]
    public UnityEvent OnExpandEvent;
    public UnityEvent OnCollapseEvent;

    // 내부 관리용
    private List<GameObject> expandedObjects = new List<GameObject>();
    private bool isExpanded = false;
    private LayoutGroup capturedLayoutGroup; // 레이아웃 그룹 임시 저장용

    // 원래 버튼 상태 복구를 위한 정보 저장
    private class OriginalButtonState
    {
        public GameObject buttonObj;
        public int index;
        public Vector2 originalSize;
        public Vector3 originalLocalPos;
        public Transform contentTransform;
        public Vector3 contentOriginalLocalPos;
        public Transform contentOriginalParent;
    }
    private List<OriginalButtonState> buttonStates = new List<OriginalButtonState>();

    /// <summary>
    /// 확장 모드를 활성화합니다.
    /// </summary>
    /// <param name="detailPrefab">두번째 공간에 표시할 프리팹 (외부에서 생성된 것 아님, 프리팹 에셋)</param>
    public void Expand(GameObject detailPrefab)
    {
        if (targetSwitch == null || isExpanded) return;

        isExpanded = true;
        OnExpandEvent?.Invoke(); // 확장 이벤트 발생
        buttonStates.Clear();

        // 1. 레이아웃 계산
        float closeButtonWidth = 0f;
        if (closeButtonPrefab != null)
        {
            // 프리팹의 너비를 가져옴
            closeButtonWidth = closeButtonPrefab.GetComponent<RectTransform>().rect.width;
        }

        // 레이아웃 그룹 비활성화 (수동 제어를 위해)
        capturedLayoutGroup = targetSwitch.ButtonContainer.GetComponent<LayoutGroup>();
        if (capturedLayoutGroup != null)
        {
            capturedLayoutGroup.enabled = false;
        }

        float containerWidth = targetSwitch.ButtonContainer.rect.width;
        float containerHeight = targetSwitch.ButtonContainer.rect.height;
        float remainingWidth = containerWidth - closeButtonWidth;
        float sectionWidth = remainingWidth / 2f;

        // 좌표 계산 (ButtonContainer Pivot 0.5, 0.5 기준)
        float leftStart = -containerWidth * 0.5f;
        float section1CenterX = leftStart + (sectionWidth * 0.5f);
        float section2CenterX = leftStart + sectionWidth + (sectionWidth * 0.5f);
        float section3CenterX = (containerWidth * 0.5f) - (closeButtonWidth * 0.5f);

        // 2. 현재 버튼들의 상태 저장 및 처리
        int currentIndex = targetSwitch.CurrentIndex;
        var buttons = targetSwitch.SpawnedButtons;

        for (int i = 0; i < buttons.Count; i++)
        {
            var btn = buttons[i];
            var rect = btn.GetComponent<RectTransform>();
            
            // 현재 선택된 버튼 내부의 Content (텍스트+아이콘) 찾기
            // SwitchButton 구조상 btn -> ContentPrefab(Clone) 구조임
            Transform content = null;
            if (btn.transform.childCount > 0) content = btn.transform.GetChild(0);

            var state = new OriginalButtonState
            {
                buttonObj = btn,
                index = i,
                originalSize = rect.sizeDelta,
                originalLocalPos = rect.localPosition,
                contentTransform = content,
                contentOriginalParent = content != null ? content.parent : null,
                contentOriginalLocalPos = content != null ? content.localPosition : Vector3.zero
            };
            buttonStates.Add(state);

            if (i == currentIndex)
            {
                // 선택된 버튼의 Content만 이동시킬 것이므로 버튼 껍데기는 숨김 (또는 투명 유지)
                // 여기서는 버튼 객체 자체를 안보이게 하지 않고, 위치만 옮길 수도 있지만
                // "자연스럽게 옮기는 방법"을 위해 Content만 떼와서 새 위치로 이동 애니메이션
                // 혹은 버튼 RectTransform 자체를 이동시키는 것이 가장 자연스러움.
                
                // 버튼을 1번 섹션 위치로 이동 애니메이션
                StartCoroutine(AnimateRectTransform(rect, new Vector3(section1CenterX, 0, 0), new Vector2(sectionWidth, containerHeight)));
            }
            else
            {
                // 선택되지 않은 나머지 버튼은 페이드 아웃 후 비활성화
                CanvasGroup cg = btn.GetComponent<CanvasGroup>();
                if (cg == null) cg = btn.AddComponent<CanvasGroup>();
                cg.alpha = 1f;
                StartCoroutine(FadeOutAndDeactivate(cg, 0.3f));
            }
        }

        // 3. ActiveBg 애니메이션 (1번+2번 영역)
        if (targetSwitch.ActiveBackground != null)
        {
            float targetBgWidth = sectionWidth * 2f;
            float targetBgCenterX = leftStart + sectionWidth; // (sectionWidth*2)/2
            Vector2 targetPos = new Vector2(targetBgCenterX, 0);
            Vector2 targetSize = new Vector2(targetBgWidth, containerHeight);

            // SwitchButton에 추가된 애니메이션 함수 호출
            targetSwitch.AnimateBackgroundTo(targetPos, targetSize);
        }

        // 4. 두번째 공간 (상세 내용) 생성 - 페이드 인?
        CreateSecondSection(section2CenterX, sectionWidth, containerHeight, detailPrefab);

        // 5. 세번째 공간 (닫기 버튼) 생성
        CreateThirdSection(section3CenterX, closeButtonWidth, containerHeight);
    }

    /// <summary>
    /// 확장 모드를 종료하고 원래대로 돌아옵니다.
    /// </summary>
    public void Collapse()
    {
        if (!isExpanded) return;
        isExpanded = false;

        OnCollapseEvent?.Invoke(); // 축소 시작 시 이벤트 발생 (즉시 반응)
        StartCoroutine(CollapseRoutine());
    }

    private IEnumerator CollapseRoutine()
    {
        // 1. 임시 오브젝트 (2번, 3번 섹션) 페이드 아웃
        foreach (var obj in expandedObjects)
        {
            if (obj != null)
            {
                // SetActive(false) 대신 페이드 아웃 처리
                CanvasGroup cg = obj.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    StartCoroutine(FadeCanvasGroup(cg, cg.alpha, 0f, 0.2f));
                }
                else
                {
                    // CanvasGroup이 혹시 없으면 그냥 숨김
                    obj.SetActive(false);
                }
            }
        }

        // 2. 애니메이션 실행 (ActiveBg 및 버튼 복구)
        // AnimateCollapse 내부에서 코루틴을 돌리므로 여기서는 시간만큼 대기
        AnimateCollapse();

        // SwitchButton의 기본 duration과 동일하게 대기
        yield return new WaitForSeconds(0.2f);

        // 3. 임시 오브젝트 완전 삭제
        foreach (var obj in expandedObjects)
        {
            if (obj != null) Destroy(obj);
        }
        expandedObjects.Clear();

        // 4. 레이아웃 그룹 복구
        if (capturedLayoutGroup != null)
        {
            capturedLayoutGroup.enabled = true;
            capturedLayoutGroup = null;
        }

        // 5. 상태 정합성을 위해 레이아웃 완전 복구 호출
        
        targetSwitch.RestoreLayout();
    }

    private void AnimateCollapse()
    {
        // 원본 상태 복구 애니메이션
        foreach (var state in buttonStates)
        {
            state.buttonObj.SetActive(true); // 다시 활성화
            
            if (state.index == targetSwitch.CurrentIndex)
            {
                // 선택된 버튼은 애니메이션으로 복귀
                RectTransform rect = state.buttonObj.GetComponent<RectTransform>();
                StartCoroutine(AnimateRectTransform(rect, state.originalLocalPos, state.originalSize));
            }
            else
            {
                // 나머지는 페이드 인 복구
                RectTransform rect = state.buttonObj.GetComponent<RectTransform>();
                rect.sizeDelta = state.originalSize;
                rect.localPosition = state.originalLocalPos;

                CanvasGroup cg = state.buttonObj.GetComponent<CanvasGroup>();
                if (cg == null) cg = state.buttonObj.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                StartCoroutine(FadeCanvasGroup(cg, 0f, 1f, 0.3f));
            }
        }
        
        // ActiveBg 복구 애니메이션
        if (targetSwitch.ActiveBackground != null)
        {
            // 원래 크기/위치 계산 (저장된 상태가 없으므로 재계산)
            RectTransform parentRect = targetSwitch.GetComponent<RectTransform>();
            float margin = 34f;
            float containerWidth = parentRect.rect.width - margin;
            int count = targetSwitch.Options.Count;
            float btnWidth = containerWidth / count;
            
            // 현재 선택된 버튼의 저장된 originalLocalPos 찾기
            var currentBtnState = buttonStates.Find(s => s.index == targetSwitch.CurrentIndex);
            if (currentBtnState != null)
            {
                Vector2 targetSize = new Vector2(btnWidth, targetSwitch.ButtonContainer.rect.height);
                Vector2 targetPos = currentBtnState.originalLocalPos; // 대략 맞음 Z=0
                targetSwitch.AnimateBackgroundTo(targetPos, targetSize);
            }
        }
    }

    private IEnumerator AnimateRectTransform(RectTransform target, Vector3 targetPos, Vector2 targetSize)
    {
        Vector3 startPos = target.localPosition;
        Vector2 startSize = target.sizeDelta;
        float duration = 0.2f; // SwitchButton animationDuration과 맞추면 좋음
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = time / duration;
            // Easing 적용 가능 (AnimationCurve사용 권장)
            float curve = t; // 간단히 선형 또는 EaseInOut

            target.localPosition = Vector3.Lerp(startPos, targetPos, curve);
            target.sizeDelta = Vector2.Lerp(startSize, targetSize, curve);
            yield return null;
        }

        target.localPosition = targetPos;
        target.sizeDelta = targetSize;
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float start, float end, float duration, float delay = 0f)
    {
        cg.alpha = start;
        if (delay > 0f) yield return new WaitForSeconds(delay);

        float time = 0f;
        while (time < duration)
        {
            time += Time.deltaTime;
            float t = time / duration;
            cg.alpha = Mathf.Lerp(start, end, t);
            yield return null;
        }
        cg.alpha = end;
    }

    private IEnumerator FadeOutAndDeactivate(CanvasGroup cg, float duration)
    {
        float startAlpha = cg.alpha;
        float time = 0f;
        while (time < duration)
        {
            time += Time.deltaTime;
            float t = time / duration;
            cg.alpha = Mathf.Lerp(startAlpha, 0f, t);
            yield return null;
        }
        cg.alpha = 0f;
        cg.gameObject.SetActive(false);
    }

    private void CreateSecondSection(float centerX, float width, float height, GameObject prefab)
    {
        if (prefab == null) return;

        GameObject wrapper = new GameObject("Section2_Content", typeof(RectTransform));
        wrapper.transform.SetParent(targetSwitch.ButtonContainer, false);
        expandedObjects.Add(wrapper);

        RectTransform rt = wrapper.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);
        rt.localPosition = new Vector3(centerX, 0, 0);

        // 페이드 효과를 위한 CanvasGroup 추가
        CanvasGroup cg = wrapper.AddComponent<CanvasGroup>();
        cg.alpha = 0f; // 투명하게 시작
        StartCoroutine(FadeCanvasGroup(cg, 0f, 1f, 0.3f, 0f)); // 1초 대기(확장 후) 0.5초 동안 페이드인

        // 프리팹 생성
        GameObject content = Instantiate(prefab, wrapper.transform);
        RectTransform contentRt = content.GetComponent<RectTransform>();
        
        // 꽉 채우기
        contentRt.anchorMin = Vector2.zero;
        contentRt.anchorMax = Vector2.one;
        contentRt.pivot = new Vector2(0.5f, 0.5f);
        contentRt.offsetMin = Vector2.zero;
        contentRt.offsetMax = Vector2.zero;
    }

    private void CreateThirdSection(float centerX, float width, float height)
    {
        if (closeButtonPrefab == null) return;

        GameObject btnObj = Instantiate(closeButtonPrefab, targetSwitch.ButtonContainer);
        expandedObjects.Add(btnObj);

        RectTransform rt = btnObj.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);
        rt.localPosition = new Vector3(centerX, 0, 0);

        // 페이드 효과를 위한 CanvasGroup 처리
        CanvasGroup cg = btnObj.GetComponent<CanvasGroup>();
        if (cg == null) cg = btnObj.AddComponent<CanvasGroup>();
        cg.alpha = 0f; // 투명하게 시작
        StartCoroutine(FadeCanvasGroup(cg, 0f, 1f, 0.3f, 0f)); // 1초 대기(확장 후) 0.5초 동안 페이드인

        // 버튼 컴포넌트 찾기 (프리팹 또는 자식에 있을 수 있음)
        Button btn = btnObj.GetComponent<Button>();
        if (btn == null) btn = btnObj.GetComponentInChildren<Button>();
        
        if (btn != null)
        {
            btn.onClick.AddListener(() => Collapse());
        }
    }
}