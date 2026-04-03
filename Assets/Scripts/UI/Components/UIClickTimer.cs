using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 버튼 클릭 또는 활성화 시 타이머를 시작하고 경과 시간을 mm:ss 형식으로 표시합니다.
/// SwitchButtonExpander와 연동하여 닫기 이벤트 시 타이머를 정지할 수 있습니다.
/// </summary>
public class UIClickTimer : MonoBehaviour
{
    #region Constants

    private const bool AUTO_START_ON_ENABLE = true;
    private const bool RESET_ON_START = true;

    #endregion

    #region Serialized Fields

    [SerializeField] private TMP_Text _timeText;
    [SerializeField] private UISwitchButtonExpander _targetExpander;
    [SerializeField] private Button _startButton;

    #endregion

    #region Private Fields

    private bool _isRunning;
    private float _elapsedTime;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        if (_timeText == null)
        {
            _timeText = GetComponent<TMP_Text>();
        }

        if (_targetExpander == null)
        {
            _targetExpander = FindObjectOfType<UISwitchButtonExpander>();
        }
    }

    private void OnEnable()
    {
        if (_targetExpander != null)
        {
            _targetExpander.OnCollapseEvent.AddListener(StopTimer);
        }

        if (_startButton != null)
        {
            _startButton.onClick.AddListener(OnButtonClick);
        }

        if (AUTO_START_ON_ENABLE)
        {
            StartTimer();
        }
    }

    private void OnDisable()
    {
        if (_targetExpander != null)
        {
            _targetExpander.OnCollapseEvent.RemoveListener(StopTimer);
        }

        if (_startButton != null)
        {
            _startButton.onClick.RemoveListener(OnButtonClick);
        }

        StopTimer();
    }

    private void Update()
    {
        if (_isRunning)
        {
            _elapsedTime += Time.deltaTime;
            UpdateDisplay();
        }
    }

    #endregion

    #region Core Functionality

    /// <summary>
    /// 타이머를 시작합니다.
    /// </summary>
    public void StartTimer()
    {
        if (RESET_ON_START)
        {
            _elapsedTime = 0f;
        }
        
        _isRunning = true;
        UpdateDisplay();
    }

    /// <summary>
    /// 타이머를 정지합니다.
    /// </summary>
    public void StopTimer()
    {
        _isRunning = false;
    }

    /// <summary>
    /// 타이머를 리셋합니다.
    /// </summary>
    public void ResetTimer()
    {
        _elapsedTime = 0f;
        _isRunning = false;
        UpdateDisplay();
    }

    #endregion

    #region Helper Methods

    private void OnButtonClick()
    {
        StartTimer();
    }

    private void UpdateDisplay()
    {
        if (_timeText != null)
        {
            int minutes = Mathf.FloorToInt(_elapsedTime / 60f);
            int seconds = Mathf.FloorToInt(_elapsedTime % 60f);
            _timeText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }
    }

    #endregion
}
