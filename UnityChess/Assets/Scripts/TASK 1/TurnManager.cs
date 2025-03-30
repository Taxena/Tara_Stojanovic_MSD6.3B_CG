using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityChess;
using UnityEngine;
using System;

public class TurnManager : NetworkBehaviour
{
    public static TurnManager Instance { get; private set; }

    public NetworkVariable<Side> NetworkedTurn = new NetworkVariable<Side>(
        Side.White,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> isWhiteTurn = new NetworkVariable<bool>(true);
    private NetworkVariable<ulong> currentTurnPlayerId = new NetworkVariable<ulong>(0);

    public event Action<bool> OnTurnChanged;
    public event Action<ulong> OnCurrentPlayerChanged;

    public enum PlayerColor
    {
        White,
        Black
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }


    // Start is called before the first frame update
    void Start()
    {
        if (IsServer)
        {
            GameManager.MoveExecutedEvent += OnMoveExecuted;
        }
    }

    private void OnDestroy()
    {
        if (IsServer)
        {
            GameManager.MoveExecutedEvent -= OnMoveExecuted;
        }
    }

    private void OnMoveExecuted()
    {
        if (!IsServer) return;

        Side nextTurn = GameManager.Instance.SideToMove;
        NetworkedTurn.Value = nextTurn;
    }


}