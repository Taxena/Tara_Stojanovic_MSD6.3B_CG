using UnityEngine;
using Unity.Netcode;

public class NetworkTest : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        Debug.Log($"Network spawn on {(IsHost ? "HOST" : "CLIENT")}, ID: {NetworkManager.Singleton.LocalClientId}");
    }
}