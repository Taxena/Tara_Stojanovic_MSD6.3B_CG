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

    private string currentSessionCode = "";
    private ConnectionState currentConnectionState = ConnectionState.Disconnected;
    private Coroutine errorMessageCoroutine;

    private UnityTransport transport;

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Failed
    }

    private void Awake()
    {
        JoinButton.onClick.AddListener(JoinGame);
        RejoinButton.onClick.AddListener(RejoinGame);
        HostButton.onClick.AddListener(HostGame);
        LeaveButton.onClick.AddListener(LeaveGame);

    }

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

        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("Connecting to session: " + sessionCode);
        }
        else
        {
            Debug.Log("Failed to start client");
            SetConnectionState(ConnectionState.Failed);
            ShowErrorMessage("Failed to connect to server");
        }
    }

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

        if (NetworkManager.Singleton.StartClient())
        {
            Debug.Log("Attempting to rejoin session: " + currentSessionCode);
        }
        else
        {
            Debug.Log("Failed to start client for rejoin");
            SetConnectionState(ConnectionState.Failed);
            ShowErrorMessage("Failed to rejoin session");
        }
    }

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
        {
            Debug.Log("Hosting game with session code: " + currentSessionCode);
            SetConnectionState(ConnectionState.Connected);
        }
        else
        {
            Debug.Log("Failed to start host");
            SetConnectionState(ConnectionState.Failed);
            ShowErrorMessage("Failed to host game");
        }
    }

    private string GenerateSessionCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        System.Random random = new System.Random();

        char[] codeArray = new char[6];
        for (int i = 0; i < 6; i++)
        {
            codeArray[i] = chars[random.Next(chars.Length)];
        }

        return new string(codeArray);
    }


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

        Debug.Log("Left the game session");
        SetConnectionState(ConnectionState.Disconnected);
    }

    private void CheckIfRunningLocally()
    {
        if (transport.ConnectionData.ServerListenAddress == "127.0.0.1")
        {
            Debug.LogWarning("Server is listening locally (127.0.0.1) ONLY!");
        }
    }

    private void Start()
    {
        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

        if (NetworkManager.Singleton != null)
        {
            Debug.Log($"UTP working with IP:{transport.ConnectionData.Address} and Port:{transport.ConnectionData.Port}");
            

        }

        InvokeRepeating(nameof(CheckTransportState), 1f, 2f);
        SetConnectionState(ConnectionState.Disconnected);
    }


    private void CheckTransportState()
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsConnectedClient)
        {
            Debug.Log("Lost connection to the server!");
            SetConnectionState(ConnectionState.Disconnected);
            ShowErrorMessage("Lost connection to server.");
        }
    }


    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Connected to server with client ID: {clientId}");

        if (clientId == NetworkManager.Singleton.LocalClientId || NetworkManager.Singleton.IsHost)
        {
            SetConnectionState(ConnectionState.Connected);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("Disconnected from server");
            SetConnectionState(ConnectionState.Disconnected);
            ShowErrorMessage("Disconnected from the game");
        }
        else if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
        {
            Debug.Log($"Client {clientId} disconnected");
        }
    }






    private void SetConnectionState(ConnectionState state)
    {
        currentConnectionState = state;
        UpdateUIBasedOnConnectionState();
    }

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

    private void ShowErrorMessage(string message)
    {
        if (errorMessageCoroutine != null)
        {
            StopCoroutine(errorMessageCoroutine);
        }

        errorMessageCoroutine = StartCoroutine(ShowErrorMessageCoroutine(message));
    }

    private System.Collections.IEnumerator ShowErrorMessageCoroutine(string message)
    {
        errorMessageText.text = message;
        errorMessageText.gameObject.SetActive(true);

        yield return new WaitForSeconds(errorMessageDisplayTime);

        errorMessageText.gameObject.SetActive(false);
        errorMessageCoroutine = null;
    }




    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;

        }

        CancelInvoke(nameof(CheckTransportState));
    }


}
