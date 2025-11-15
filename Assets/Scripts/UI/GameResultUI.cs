using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 게임 결과 UI (승리/패배)
/// </summary>
public class GameResultUI : MonoBehaviour
{
    #region Serialized Fields

    [Header("UI 패널")]
    [SerializeField] private GameObject _resultPanel;

    [Header("텍스트")]
    [SerializeField] private TextMeshProUGUI _resultText;
    [SerializeField] private TextMeshProUGUI _statsText;

    [Header("버튼")]
    [SerializeField] private Button _returnToLobbyButton;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        if (_returnToLobbyButton != null)
        {
            _returnToLobbyButton.onClick.AddListener(OnReturnToLobby);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 승리 UI를 표시합니다.
    /// </summary>
    /// <param name="killCount">킬 수</param>
    /// <param name="survivalTime">생존 시간 (초)</param>
    public void ShowVictory(int killCount, float survivalTime)
    {
        if (_resultPanel != null)
            _resultPanel.SetActive(true);

        if (_resultText != null)
            _resultText.text = "VICTORY!";

        UpdateStats(killCount, survivalTime);

        Debug.Log("[GameResultUI] 승리 UI 표시");
    }

    /// <summary>
    /// 패배 UI를 표시합니다.
    /// </summary>
    /// <param name="killCount">킬 수</param>
    /// <param name="survivalTime">생존 시간 (초)</param>
    /// <param name="rank">순위 (1 = 우승, 2 = 2등...)</param>
    public void ShowDefeat(int killCount, float survivalTime, int rank)
    {
        if (_resultPanel != null)
            _resultPanel.SetActive(true);

        if (_resultText != null)
            _resultText.text = $"DEFEATED\nRank: #{rank}";

        UpdateStats(killCount, survivalTime);

        Debug.Log($"[GameResultUI] 패배 UI 표시 (순위: {rank})");
    }

    /// <summary>
    /// 패널을 숨깁니다.
    /// </summary>
    public void Hide()
    {
        if (_resultPanel != null)
            _resultPanel.SetActive(false);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 통계 텍스트를 업데이트합니다.
    /// </summary>
    private void UpdateStats(int killCount, float survivalTime)
    {
        if (_statsText == null) return;

        _statsText.text = $"Kills: {killCount}";
    }

    #endregion

    #region Button Handlers

    private void OnReturnToLobby()
    {
        Debug.Log("[GameResultUI] 로비로 돌아가기");
        // TODO: 로비 씬 로드 또는 세션 종료
    }

    #endregion
}
