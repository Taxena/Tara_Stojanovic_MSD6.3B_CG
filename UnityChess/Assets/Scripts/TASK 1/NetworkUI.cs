using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using Unity.Netcode.Transports.UTP;
using System;

public class NetworkUI : MonoBehaviour
{
    [SerializeField] private Button JoinButton;
    [SerializeField] private Button RejoinButton;
    [SerializeField] private Button HostButton;
    [SerializeField] private Button LeaveButton;

    private UnityTransport transport;

    private void Awake()
    {
        JoinButton.onClick.AddListener(JoinGame);
        RejoinButton.onClick.AddListener(RejoinGame);
        HostButton.onClick.AddListener(HostGame);
        LeaveButton.onClick.AddListener(LeaveGame);
    }

    private void JoinGame()
    {
        Debug.Log("CLIENT JOINED THE GAME");
        NetworkManager.Singleton.StartClient();
    }

    private void RejoinGame()
    {
        throw new NotImplementedException();
    }

    private void HostGame()
    {
        NetworkManager.Singleton.StartHost();
        Debug.Log("GAME IS HOSTED");
        //Debug.Log($"Server started listening on {transport.ConnectionData.ServerListenAddress} and port {transport.ConnectionData.Port}");
        CheckIfRunningLocally();
    }

    private void LeaveGame()
    {
        throw new NotImplementedException();
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
    }
}
