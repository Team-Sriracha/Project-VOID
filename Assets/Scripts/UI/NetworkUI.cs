using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 네트워크 연결 UI를 관리합니다.
/// Server/Client 시작 버튼을 제공합니다.
/// </summary>
public class NetworkUI : MonoBehaviour
{
    #region Serialized Fields

    [Header("UI 요소")]
    [SerializeField] private Button _serverButton;
    [SerializeField] private Button _clientButton;
    [SerializeField] private GameObject _menuPanel;

    [Header("네트워크")]
    [SerializeField] private NetworkManager _networkManager;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        if (_serverButton != null)
        {
            _serverButton.onClick.AddListener(OnServerButtonClicked);
        }

        if (_clientButton != null)
        {
            _clientButton.onClick.AddListener(OnClientButtonClicked);
        }
    }

    private void OnDestroy()
    {
        if (_serverButton != null)
        {
            _serverButton.onClick.RemoveListener(OnServerButtonClicked);
        }

        if (_clientButton != null)
        {
            _clientButton.onClick.RemoveListener(OnClientButtonClicked);
        }
    }

    #endregion

    #region Button Callbacks

    private void OnServerButtonClicked()
    {
        if (_networkManager != null)
        {
            _networkManager.StartServer();
            HideMenu();
        }
        else
        {
            Debug.LogError("[NetworkUI] NetworkManager가 할당되지 않았습니다!");
        }
    }

    private void OnClientButtonClicked()
    {
        if (_networkManager != null)
        {
            _networkManager.StartClient();
            HideMenu();
        }
        else
        {
            Debug.LogError("[NetworkUI] NetworkManager가 할당되지 않았습니다!");
        }
    }

    #endregion

    #region Helper Methods

    private void HideMenu()
    {
        if (_menuPanel != null)
        {
            _menuPanel.SetActive(false);
        }
    }

    #endregion
}
