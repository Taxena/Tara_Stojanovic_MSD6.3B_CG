using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class PerformanceAndLatency : NetworkBehaviour
{
    [SerializeField] private float pingInterval = 2.0f;
    
    private Dictionary<ulong, float> clientPings = new Dictionary<ulong, float>();
    private Dictionary<ulong, float> pingStartTimes = new Dictionary<ulong, float>();
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        StartCoroutine(PingRoutine());
    }
    
    private IEnumerator PingRoutine()
    {
        yield return new WaitForSeconds(1.0f);
        
        while (NetworkManager.Singleton.IsListening)
        {
            if (IsServer)
            {
                foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                {
                    pingStartTimes[clientId] = Time.realtimeSinceStartup;
                    PingClientRpc(clientId);
                }
            }
            
            yield return new WaitForSeconds(pingInterval);
            
            if (IsServer)
                LogPingMetrics();
        }
    }
    
    [ClientRpc]
    private void PingClientRpc(ulong clientId)
    {
        if (NetworkManager.Singleton.LocalClientId == clientId)
            PingServerRpc(NetworkManager.LocalClientId);
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void PingServerRpc(ulong clientId, ServerRpcParams serverRpcParams = default)
    {
        ulong senderId = serverRpcParams.Receive.SenderClientId;
        
        if (pingStartTimes.TryGetValue(senderId, out float startTime))
        {
            float pingMs = (Time.realtimeSinceStartup - startTime) * 1000f;
            clientPings[senderId] = pingMs;
            
            NotifyClientPingClientRpc(pingMs, senderId);
        }
    }
    
    [ClientRpc]
    private void NotifyClientPingClientRpc(float pingMs, ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == targetClientId && !IsServer)
            Debug.Log($"My ping: {pingMs:F2} ms");
    }
    
    private void LogPingMetrics()
    {
        if (clientPings.Count == 0)
            return;
            
        Debug.Log($"Connected Clients: {NetworkManager.Singleton.ConnectedClientsIds.Count}");
        foreach (var entry in clientPings)
        {
            Debug.Log($"Client {entry.Key} ping: {entry.Value:F2} ms");
        }
    }
}
