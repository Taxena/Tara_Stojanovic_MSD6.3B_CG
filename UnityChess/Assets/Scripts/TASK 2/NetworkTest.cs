using UnityEngine;
using Unity.Netcode;

public class NetworkTest : NetworkBehaviour
{
    // Just a debugging script
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Debug.Log($"Network spawn on {(IsHost ? "HOST" : "CLIENT")}, ID: {NetworkManager.Singleton.LocalClientId}");
    }
}