using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using Unity.Netcode.Transports.UTP;
using System;
using TMPro;

public class NetworkUI : MonoBehaviour
{
    [SerializeField] private Button JoinButton;
    [SerializeField] private Button RejoinButton;
    [SerializeField] private Button HostButton;
    [SerializeField] private Button LeaveButton;
    [SerializeField] private TMP_InputField sessionCodeInput;
    [SerializeField] private TMP_Text connectionStatusText;
    [SerializeField] private TMP_Text errorMessageText;
    [SerializeField] private float errorMessageDisplayTime = 3f;
    [SerializeField] private Button openStoreButton;

    private string currentSessionCode = "";
    private ConnectionState currentConnectionState = ConnectionState.Disconnected;
    private Coroutine errorMessageCoroutine;
    private UnityTransport transport;

    public enum ConnectionState { Disconnected, Connecting, Connected, Failed }

    private void Awake()
    {
        JoinButton.onClick.AddListener(JoinGame);
        RejoinButton.onClick.AddListener(RejoinGame);
        HostButton.onClick.AddListener(HostGame);
        LeaveButton.onClick.AddListener(LeaveGame);
    }

    private void Start()
    {
        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        InvokeRepeating(nameof(CheckTransportState), 1f, 2f);
        SetConnectionState(ConnectionState.Disconnected);
        
        if (openStoreButton != null)
        {
            openStoreButton.onClick.AddListener(OnOpenStoreClicked);
        }
    }

    // Attempts to join a session
    private void JoinGame()
    {
        if (currentConnectionState != ConnectionState.Disconnected)
        {
            ShowErrorMessage("Already connected or connecting");
            return;
        }

        string sessionCode = sessionCodeInput.text.Trim().ToUpper();
        if (string.IsNullOrEmpty(sessionCode) || sessionCode.Length != 6)
        {
            ShowErrorMessage("Invalid session code. Please enter a 6-character code.");
            return;
        }

        currentSessionCode = sessionCode;
        SetConnectionState(ConnectionState.Connecting);

        byte[] connectionData = System.Text.Encoding.ASCII.GetBytes(sessionCode);
        NetworkManager.Singleton.NetworkConfig.ConnectionData = connectionData;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        if (!NetworkManager.Singleton.StartClient())
        {
            SetConnectionState(ConnectionState.Failed);
            ShowErrorMessage("Failed to connect to server");
        }
    }

    // Rejoins a previous session
    private void RejoinGame()
    {
        if (currentConnectionState != ConnectionState.Disconnected)
        {
            ShowErrorMessage("Already connected or connecting");
            return;
        }

        if (string.IsNullOrEmpty(currentSessionCode))
        {
            ShowErrorMessage("No previous session to rejoin");
            return;
        }

        SetConnectionState(ConnectionState.Connecting);

        byte[] connectionData = System.Text.Encoding.ASCII.GetBytes(currentSessionCode);
        NetworkManager.Singleton.NetworkConfig.ConnectionData = connectionData;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        if (!NetworkManager.Singleton.StartClient())
        {
            SetConnectionState(ConnectionState.Failed);
            ShowErrorMessage("Failed to rejoin session");
        }
    }

    // Hosts a new game session
    private void HostGame()
    {
        if (currentConnectionState != ConnectionState.Disconnected)
        {
            ShowErrorMessage("Already connected or hosting");
            return;
        }

        currentSessionCode = GenerateSessionCode();
        sessionCodeInput.text = currentSessionCode;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

        if (NetworkManager.Singleton.StartHost())
            SetConnectionState(ConnectionState.Connected);
        else
        {
            SetConnectionState(ConnectionState.Failed);
            ShowErrorMessage("Failed to host game");
        }
    }

    // Generates a random session code
    private string GenerateSessionCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        System.Random random = new System.Random();

        char[] codeArray = new char[6];
        for (int i = 0; i < 6; i++)
            codeArray[i] = chars[random.Next(chars.Length)];

        return new string(codeArray);
    }

    // Leaves the current game session
    private void LeaveGame()
    {
        if (currentConnectionState == ConnectionState.Disconnected)
        {
            ShowErrorMessage("Not connected to any game");
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.Shutdown();
        
        SetConnectionState(ConnectionState.Disconnected);
    }

    // Checks for unexpected disconnect
    private void CheckTransportState()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsConnectedClient && currentConnectionState == ConnectionState.Connected)
        {
            SetConnectionState(ConnectionState.Disconnected);
            ShowErrorMessage("Lost connection to server.");
        }
    }

    // Handles client connection and disconnection events
    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId || NetworkManager.Singleton.IsHost)
            SetConnectionState(ConnectionState.Connected);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            SetConnectionState(ConnectionState.Disconnected);
            ShowErrorMessage("Disconnected from the game");
        }
    }

    // Updates state and UI
    private void SetConnectionState(ConnectionState state)
    {
        currentConnectionState = state;
        UpdateUIBasedOnConnectionState();
    }

    // Enables/disables buttons based on state
    private void UpdateUIBasedOnConnectionState()
    {
        switch (currentConnectionState)
        {
            case ConnectionState.Disconnected:
                connectionStatusText.text = "Disconnected";
                connectionStatusText.color = Color.red;
                HostButton.interactable = true;
                JoinButton.interactable = true;
                RejoinButton.interactable = !string.IsNullOrEmpty(currentSessionCode);
                LeaveButton.interactable = false;
                break;
            case ConnectionState.Connecting:
                connectionStatusText.text = "Connecting...";
                connectionStatusText.color = Color.yellow;
                HostButton.interactable = false;
                JoinButton.interactable = false;
                RejoinButton.interactable = false;
                LeaveButton.interactable = true;
                break;
            case ConnectionState.Connected:
                connectionStatusText.text = "Connected";
                connectionStatusText.color = Color.green;
                HostButton.interactable = false;
                JoinButton.interactable = false;
                RejoinButton.interactable = false;
                LeaveButton.interactable = true;
                break;
            case ConnectionState.Failed:
                connectionStatusText.text = "Connection Failed";
                connectionStatusText.color = Color.red;
                HostButton.interactable = true;
                JoinButton.interactable = true;
                RejoinButton.interactable = !string.IsNullOrEmpty(currentSessionCode);
                LeaveButton.interactable = false;
                break;
        }
    }

    // Displays error messages
    private void ShowErrorMessage(string message)
    {
        if (errorMessageCoroutine != null)
            StopCoroutine(errorMessageCoroutine);

        errorMessageCoroutine = StartCoroutine(ShowErrorMessageCoroutine(message));
    }

    // Shows and hides error after timeout
    private System.Collections.IEnumerator ShowErrorMessageCoroutine(string message)
    {
        errorMessageText.text = message;
        errorMessageText.gameObject.SetActive(true);
        yield return new WaitForSeconds(errorMessageDisplayTime);
        errorMessageText.gameObject.SetActive(false);
        errorMessageCoroutine = null;
    }
    
    // Opens the DLC store
    private void OnOpenStoreClicked()
    {
        if (DLCStoreManager.Instance != null)
        {
            DLCStoreManager.Instance.ShowStore();
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        
        if (openStoreButton != null)
        {
            openStoreButton.onClick.RemoveListener(OnOpenStoreClicked);
        }
        CancelInvoke(nameof(CheckTransportState));
    }
}
