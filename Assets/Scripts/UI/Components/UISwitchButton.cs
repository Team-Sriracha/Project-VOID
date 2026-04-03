using DG.Tweening;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>
/// 여러 옵션 중 하나를 선택할 수 있는 스위치 버튼 컴포넌트입니다.
/// DOTween을 사용하여 부드러운 애니메이션 효과를 제공합니다.
/// </summary>
public class UISwitchButton : MonoBehaviour
{
    #region Nested Classes

    [System.Serializable]
    public class SwitchOption
    {
        public string text;
        public Sprite icon;
    }

    #endregion

    #region Constants

    private const float DEFAULT_ANIMATION_DURATION = 0.2f;
    private const float MARGIN = 34f;

    #endregion

    #region Serialized Fields

    [Header("설정")]
    [SerializeField] private List<SwitchOption> options = new List<SwitchOption>();
    [SerializeField] private int defaultIndex = 0;
    [SerializeField] private float animationDuration = DEFAULT_ANIMATION_DURATION;
    [SerializeField] private Ease easeType = Ease.OutQuad;

    [Header("참조")]
    [Tooltip("선택된 항목을 강조하며 이동하는 배경 이미지입니다.")]
    [SerializeField] private RectTransform activeBackground;
    [Tooltip("버튼이 생성될 부모 컨테이너입니다.")]
    [SerializeField] private RectTransform buttonContainer;
    [Tooltip("버튼 내부 내용물 프리팹입니다. Image와 TMP_Text 컴포넌트가 포함되어야 합니다.")]
    [SerializeField] private GameObject contentPrefab;

    [Header("시각적 요소")]
    [Tooltip("선택된 상태의 텍스트/아이콘 색상입니다.")]
    [SerializeField] private Color activeContentColor = Color.black;
    [Tooltip("비활성 상태의 텍스트/아이콘 색상입니다.")]
    [SerializeField] private Color inactiveContentColor = Color.gray;

    [Header("이벤트")]
    public UnityEvent<int> onValueChanged;

    #endregion

    #region Private Fields

    private List<GameObject> _spawnedButtons = new List<GameObject>();
    private int _currentIndex = -1;
    private int _previousIndex = -1;
    private Tween _moveTween;
    private Sequence _colorSequence;

    #endregion

    #region Properties

    public RectTransform ButtonContainer => buttonContainer;
    public RectTransform ActiveBackground => activeBackground;
    public GameObject ContentPrefab => contentPrefab;
    public List<SwitchOption> Options => options;
    public List<GameObject> SpawnedButtons => _spawnedButtons;
    public int CurrentIndex => _currentIndex;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        InitializeButtons();
    }

    private void OnDestroy()
    {
        KillAllTweens();
    }

    #endregion

    #region Initialization

    private void InitializeButtons()
    {
        // 기존 버튼 초기화
        foreach (Transform child in buttonContainer)
        {
            Destroy(child.gameObject);
        }
        _spawnedButtons.Clear();

        RectTransform parentRect = GetComponent<RectTransform>();
        
        float containerWidth = parentRect.rect.width - MARGIN;
        float containerHeight = parentRect.rect.height - MARGIN;
        
        int count = options.Count;

        // 1. 버튼 컨테이너 설정
        if (buttonContainer != null)
        {
            buttonContainer.anchorMin = new Vector2(0.5f, 0.5f);
            buttonContainer.anchorMax = new Vector2(0.5f, 0.5f);
            buttonContainer.pivot = new Vector2(0.5f, 0.5f);
            
            buttonContainer.sizeDelta = new Vector2(containerWidth, containerHeight);
            buttonContainer.anchoredPosition = Vector2.zero;
        }

        if (count == 0) return;

        float btnWidth = containerWidth / count;
        float btnHeight = containerHeight;

        // 2. 활성 배경(ActiveBg) 크기 설정
        if (activeBackground != null)
        {
            activeBackground.sizeDelta = new Vector2(btnWidth, btnHeight);
        }

        // 3. 버튼 생성
        for (int i = 0; i < count; i++)
        {
            int index = i;
            
            // 버튼 객체 생성
            GameObject btnObj = new GameObject($"Button_{i}", typeof(RectTransform), typeof(Button), typeof(Image));
            btnObj.transform.SetParent(buttonContainer, false);
            
            RectTransform btnRect = btnObj.GetComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(btnWidth, btnHeight);
            
            // 버튼 투명 처리 (터치 영역 확보용)
            Image btnImage = btnObj.GetComponent<Image>();
            btnImage.color = Color.clear;

            _spawnedButtons.Add(btnObj);

            // 내용물 프리팹 생성
            if (contentPrefab != null)
            {
                GameObject contentObj = Instantiate(contentPrefab, btnObj.transform);
                RectTransform contentRect = contentObj.GetComponent<RectTransform>();
                
                // 내용물 중앙 정렬
                contentRect.anchorMin = new Vector2(0.5f, 0.5f);
                contentRect.anchorMax = new Vector2(0.5f, 0.5f);
                contentRect.pivot = new Vector2(0.5f, 0.5f);
                contentRect.anchoredPosition = Vector2.zero;
                contentRect.sizeDelta = new Vector2(btnWidth, btnHeight);

                // 텍스트 및 아이콘 설정
                var option = options[i];
                
                var textComp = contentObj.GetComponentInChildren<TMP_Text>();
                if (textComp != null) 
                {
                    textComp.text = option.text;
                }

                // 아이콘 이미지 탐색 및 설정
                Image[] images = contentObj.GetComponentsInChildren<Image>();
                foreach(var img in images)
                {
                    if (img.gameObject != contentObj) 
                    {
                        img.sprite = option.icon;
                        img.gameObject.SetActive(option.icon != null);
                    }
                    else if (images.Length == 1) 
                    {
                        img.sprite = option.icon;
                        img.gameObject.SetActive(option.icon != null);
                    }
                }
            }

            // 클릭 이벤트 연결
            Button btnFn = btnObj.GetComponent<Button>();
            if (btnFn != null)
            {
                btnFn.onClick.AddListener(() => OnOptionClicked(index));
            }
        }

        // 레이아웃 갱신
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(buttonContainer);

        // 초기값 선택
        if (options.Count > 0)
        {
            defaultIndex = Mathf.Clamp(defaultIndex, 0, options.Count - 1);
            SelectOption(defaultIndex, true);
        }
    }

    #endregion

    #region Button Selection

    /// <summary>
    /// 옵션 클릭 이벤트 핸들러입니다.
    /// </summary>
    public void OnOptionClicked(int index)
    {
        if (_currentIndex == index) return;
        SelectOption(index, false);
    }

    /// <summary>
    /// 특정 옵션을 선택합니다.
    /// </summary>
    public void SelectOption(int index, bool immediate)
    {
        if (index < 0 || index >= options.Count) return;

        if (_currentIndex != index)
        {
            _previousIndex = _currentIndex;
        }

        _currentIndex = index;
        onValueChanged?.Invoke(_currentIndex);

        UpdateVisuals(immediate);
        MoveBackground(immediate);
    }

    #endregion

    #region Animation Methods

    /// <summary>
    /// 배경을 선택된 버튼 위치로 이동시킵니다.
    /// </summary>
    private void MoveBackground(bool immediate)
    {
        _moveTween?.Kill();

        if (_currentIndex < 0 || _currentIndex >= _spawnedButtons.Count) return;

        RectTransform targetBtn = _spawnedButtons[_currentIndex].GetComponent<RectTransform>();
        Vector3 targetWorldPos = targetBtn.position;
        Vector3 targetLocalPos = activeBackground.parent.InverseTransformPoint(targetWorldPos);
        targetLocalPos.z = 0;

        if (immediate)
        {
            activeBackground.localPosition = targetLocalPos;
        }
        else
        {
            _moveTween = activeBackground.DOLocalMove(targetLocalPos, animationDuration).SetEase(easeType);
        }
    }

    /// <summary>
    /// 외부에서 ActiveBg를 직접 애니메이션으로 제어하기 위한 API입니다.
    /// </summary>
    public void AnimateBackgroundTo(Vector2 targetLocalPos, Vector2 targetSize)
    {
        _moveTween?.Kill();

        Sequence sequence = DOTween.Sequence();
        sequence.Append(activeBackground.DOLocalMove(targetLocalPos, animationDuration));
        sequence.Join(activeBackground.DOSizeDelta(targetSize, animationDuration));
        sequence.SetEase(easeType);
        
        _moveTween = sequence;
    }

    /// <summary>
    /// 버튼 색상을 업데이트합니다.
    /// </summary>
    private void UpdateVisuals(bool immediate)
    {
        _colorSequence?.Kill();

        if (immediate)
        {
            for (int i = 0; i < _spawnedButtons.Count; i++)
            {
                bool isSelected = (i == _currentIndex);
                SetButtonContentColor(_spawnedButtons[i], isSelected ? activeContentColor : inactiveContentColor);
            }
        }
        else
        {
            AnimateColors();
        }
    }

    /// <summary>
    /// 색상 전환 애니메이션을 실행합니다.
    /// </summary>
    private void AnimateColors()
    {
        _colorSequence = DOTween.Sequence();

        for (int i = 0; i < _spawnedButtons.Count; i++)
        {
            Color targetColor;

            if (i == _currentIndex)
            {
                targetColor = activeContentColor;
            }
            else
            {
                targetColor = inactiveContentColor;
            }

            GameObject btnObj = _spawnedButtons[i];

            // Text 색상 전환
            var texts = btnObj.GetComponentsInChildren<TMP_Text>();
            foreach (var txt in texts)
            {
                _colorSequence.Join(txt.DOColor(targetColor, animationDuration).SetEase(easeType));
            }

            // Image 색상 전환 (버튼 배경 제외)
            var images = btnObj.GetComponentsInChildren<Image>();
            foreach (var img in images)
            {
                if (img.gameObject != btnObj)
                {
                    _colorSequence.Join(img.DOColor(targetColor, animationDuration).SetEase(easeType));
                }
            }
        }
    }

    /// <summary>
    /// 버튼 내부 컨텐츠의 색상을 설정합니다.
    /// </summary>
    private void SetButtonContentColor(GameObject btnObj, Color color)
    {
        var texts = btnObj.GetComponentsInChildren<TMP_Text>();
        foreach (var txt in texts) txt.color = color;

        var images = btnObj.GetComponentsInChildren<Image>();
        foreach (var img in images)
        {
            if (img.gameObject != btnObj)
            {
                img.color = color;
            }
        }
    }

    /// <summary>
    /// 모든 Tween을 중지합니다.
    /// </summary>
    private void KillAllTweens()
    {
        _moveTween?.Kill();
        _colorSequence?.Kill();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 2개 옵션 스위치의 텍스트를 갱신하고 버튼을 재생성합니다.
    /// </summary>
    /// <param name="firstText">첫 번째 옵션 텍스트</param>
    /// <param name="secondText">두 번째 옵션 텍스트</param>
    /// <param name="selectedIndex">선택할 인덱스</param>
    public void SetOptionTexts(string firstText, string secondText, int selectedIndex = 0)
    {
        Sprite firstIcon = options != null && options.Count > 0 ? options[0].icon : null;
        Sprite secondIcon = options != null && options.Count > 1 ? options[1].icon : null;
        SetOptionContents(firstText, firstIcon, secondText, secondIcon, selectedIndex);
    }

    /// <summary>
    /// 2개 옵션 스위치의 텍스트/아이콘을 갱신하고 버튼을 재생성합니다.
    /// </summary>
    /// <param name="firstText">첫 번째 옵션 텍스트</param>
    /// <param name="firstIcon">첫 번째 옵션 아이콘</param>
    /// <param name="secondText">두 번째 옵션 텍스트</param>
    /// <param name="secondIcon">두 번째 옵션 아이콘</param>
    /// <param name="selectedIndex">선택할 인덱스</param>
    public void SetOptionContents(string firstText, Sprite firstIcon, string secondText, Sprite secondIcon, int selectedIndex = 0)
    {
        if (options == null)
        {
            options = new List<SwitchOption>();
        }

        while (options.Count < 2)
        {
            options.Add(new SwitchOption());
        }

        options[0].text = firstText ?? string.Empty;
        options[0].icon = firstIcon;
        options[1].text = secondText ?? string.Empty;
        options[1].icon = secondIcon;

        if (options.Count > 2)
        {
            options.RemoveRange(2, options.Count - 2);
        }

        defaultIndex = Mathf.Clamp(selectedIndex, 0, 1);
        InitializeButtons();
    }

    /// <summary>
    /// 단일 옵션 스위치의 텍스트를 갱신하고 버튼을 재생성합니다.
    /// </summary>
    /// <param name="optionText">단일 옵션 텍스트</param>
    public void SetSingleOptionText(string optionText)
    {
        Sprite optionIcon = options != null && options.Count > 0 ? options[0].icon : null;
        SetSingleOption(optionText, optionIcon);
    }

    /// <summary>
    /// 단일 옵션 스위치의 텍스트/아이콘을 갱신하고 버튼을 재생성합니다.
    /// </summary>
    /// <param name="optionText">단일 옵션 텍스트</param>
    /// <param name="optionIcon">단일 옵션 아이콘</param>
    public void SetSingleOption(string optionText, Sprite optionIcon)
    {
        if (options == null)
        {
            options = new List<SwitchOption>();
        }

        if (options.Count == 0)
        {
            options.Add(new SwitchOption());
        }

        options[0].text = optionText ?? string.Empty;
        options[0].icon = optionIcon;

        if (options.Count > 1)
        {
            options.RemoveRange(1, options.Count - 1);
        }

        defaultIndex = 0;
        InitializeButtons();
    }

    /// <summary>
    /// 버튼들의 레이아웃 및 배경 크기를 초기 상태로 재설정합니다.
    /// 외부 스크립트에서 크기를 조작한 후 복구할 때 사용합니다.
    /// </summary>
    public void RestoreLayout()
    {
        RectTransform parentRect = GetComponent<RectTransform>();
        float containerWidth = parentRect.rect.width - MARGIN;
        float containerHeight = parentRect.rect.height - MARGIN;
        int count = options.Count;

        if (count == 0) return;

        float bgWidth = containerWidth / count;
        float btnHeight = containerHeight;

        if (activeBackground != null)
        {
            activeBackground.sizeDelta = new Vector2(bgWidth, btnHeight);
        }

        SelectOption(_currentIndex, true);
    }

    #endregion
}
