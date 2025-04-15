using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityChess;
using System;
using TMPro;

public class TurnManager : NetworkBehaviour
{
    public static TurnManager Instance { get; private set; }

    [SerializeField]
    private TMP_Text playerSideText;

    private NetworkVariable<int> currentTurn = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public static event Action<Side> OnTurnChanged;
    private NetworkVariable<ulong> whitePlayerId = new NetworkVariable<ulong>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<ulong> blackPlayerId = new NetworkVariable<ulong>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private GameManager gameManager;
    private BoardManager boardManager;
    private Side localPlayerSide = Side.None;
    
    public bool IsWhitePlayer(ulong clientId) => whitePlayerId.Value == clientId;
    public bool IsBlackPlayer(ulong clientId) => blackPlayerId.Value == clientId;

    public bool IsLocalPlayerTurn => GetCurrentTurnSide() == localPlayerSide;
    public Side GetCurrentTurnSide() => currentTurn.Value % 2 == 0 ? Side.White : Side.Black;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else if (Instance != this)
            Destroy(gameObject);
    }

    private void Start()
    {
        gameManager = GameManager.Instance;
        boardManager = BoardManager.Instance;
        
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        currentTurn.OnValueChanged += OnTurnValueChanged;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }
    
    private void OnDestroy()
    {
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        
        if (Instance == this)
            Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            whitePlayerId.Value = NetworkManager.ServerClientId;
            localPlayerSide = Side.White;
            if (playerSideText != null)
                playerSideText.text = "Playing as White";
            
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
            {
                if (clientId != NetworkManager.ServerClientId)
                {
                    blackPlayerId.Value = clientId;
                    break;
                }
            }
        }
        else
        {
            localPlayerSide = Side.Black;
            if (playerSideText != null)
                playerSideText.text = "Playing as Black";
            RequestBlackPlayerAssignmentServerRpc(NetworkManager.Singleton.LocalClientId);
        }

        OnTurnChanged?.Invoke(GetCurrentTurnSide());
        UpdatePieceInteractivity();
    }

    private void OnTurnValueChanged(int previous, int current)
    {
        Side currentSide = GetCurrentTurnSide();
        OnTurnChanged?.Invoke(currentSide);
        UpdatePieceInteractivity();
    }

    private void UpdatePieceInteractivity()
    {
        VisualPiece[] allPieces = FindObjectsOfType<VisualPiece>();
        
        foreach (VisualPiece piece in allPieces)
            piece.enabled = IsLocalPlayerTurn && piece.PieceColor == localPlayerSide;
    }

    private void OnMoveExecuted()
    {
        if (IsServer)
            StartCoroutine(DelayedTurnChange());
    }
    
    private IEnumerator DelayedTurnChange()
    {
        yield return new WaitForSeconds(0.1f);
        currentTurn.Value += 1;
        UpdatePieceInteractivity();
    }
    
    public void NotifyOfMove(Square startSquare, Square endSquare, Side pieceSide)
    {
        if (!IsServer)
        {
            string startSquareStr = startSquare.ToString();
            string endSquareStr = endSquare.ToString();
            MoveServerRpc(startSquareStr, endSquareStr);
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void MoveServerRpc(string startSquareStr, string endSquareStr, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        
        Side currentSide = GetCurrentTurnSide();
        bool isValidPlayer = (currentSide == Side.White && clientId == whitePlayerId.Value) ||
                           (currentSide == Side.Black && clientId == blackPlayerId.Value);
        
        BroadcastMoveClientRpc(startSquareStr, endSquareStr);
    }
    
    [ClientRpc]
    private void BroadcastMoveClientRpc(string startSquareStr, string endSquareStr)
    {
        Square startSquare = SquareUtil.StringToSquare(startSquareStr);
        Square endSquare = SquareUtil.StringToSquare(endSquareStr);
        
        if (!gameManager.TryGetLegalMoveAndExecute(startSquare, endSquare))
            SimulateMove(startSquare, endSquare);
    }
    
    private void SimulateMove(Square startSquare, Square endSquare)
    {
        GameObject pieceGO = boardManager.GetPieceGOAtPosition(startSquare);
        if (pieceGO == null) return;
        
        GameObject targetSquareGO = boardManager.GetSquareGOByPosition(endSquare);
        if (targetSquareGO == null) return;
        
        boardManager.TryDestroyVisualPiece(endSquare);
        
        Transform pieceTransform = pieceGO.transform;
        
        PieceController networkController = pieceGO.GetComponent<PieceController>();
        if (networkController != null)
        {
            MonoBehaviour[] behaviours = pieceGO.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
                if (behaviour != networkController)
                    behaviour.enabled = false;
        }
        
        pieceTransform.SetParent(targetSquareGO.transform, false);
        pieceTransform.position = targetSquareGO.transform.position;
        
        if (networkController != null)
        {
            MonoBehaviour[] behaviours = pieceGO.GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
                behaviour.enabled = true;
        }
    }
    
    public void OnClientConnected(ulong clientId)
    {
        if (IsServer && clientId != NetworkManager.ServerClientId)
        {
            if (blackPlayerId.Value == 0)
            {
                blackPlayerId.Value = clientId;
                SendGameStateToClientClientRpc(clientId);
            }
        }
    }
    
    [ClientRpc]
    private void SendGameStateToClientClientRpc(ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == targetClientId)
        {
            localPlayerSide = Side.Black;
            if (playerSideText != null)
                playerSideText.text = "Playing as Black";
            OnTurnChanged?.Invoke(GetCurrentTurnSide());
            UpdatePieceInteractivity();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestBlackPlayerAssignmentServerRpc(ulong clientId)
    {
        if (blackPlayerId.Value == 0)
            blackPlayerId.Value = clientId;
    }
}