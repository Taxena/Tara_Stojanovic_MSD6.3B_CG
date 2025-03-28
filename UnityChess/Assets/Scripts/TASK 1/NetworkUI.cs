using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;
using Unity.Netcode.Transports.UTP;

public class NetworkUI : MonoBehaviour
{
    [SerializeField] private Button JoinButton;
    [SerializeField] private Button RejoinButton;
    [SerializeField] private Button HostButton;
    [SerializeField] private Button LeaveButton;

    private UnityTransport transport;

    private void Awake()
    {
        
    }

    private void JoinGame()
    {

    }

    private void RejoinGame()
    {

    }

    private void HostGame()
    {

    }

    private void LeaveGame()
    {

    }

    private void Start()
    {
        transport = NetworkManager.Singleton.GetComponent<UnityTransport>();

    }
}
