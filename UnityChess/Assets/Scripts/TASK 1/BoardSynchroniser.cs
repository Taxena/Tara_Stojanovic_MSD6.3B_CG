using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityChess;
using Unity.Netcode;

public class BoardSynchroniser : NetworkBehaviour
{
    private NetworkVariable<NetworkString> boardState = new NetworkVariable<NetworkString>(
        new NetworkString(""),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    
    private GameManager gameManager;
    private BoardManager boardManager;
    
    private void Start()
    {
        gameManager = GameManager.Instance;
        boardManager = BoardManager.Instance;
        
        if (gameManager == null || boardManager == null)
        {
            enabled = false;
            return;
        }
        
        GameManager.MoveExecutedEvent += OnMoveExecuted;
        boardState.OnValueChanged += OnBoardStateChanged;
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsServer)
            SyncBoardState();
    }
    
    private void OnDestroy()
    {
        GameManager.MoveExecutedEvent -= OnMoveExecuted;
        boardState.OnValueChanged -= OnBoardStateChanged;
    }
    
    // Syncs board after a move
    private void OnMoveExecuted()
    {
        if (IsServer)
            SyncBoardState();
    }
    
    // Starts coroutine to update board
    private void SyncBoardState()
    {
        if (!IsServer) return;
        StartCoroutine(DelayedSync());
    }
    
    private IEnumerator DelayedSync()
    {
        yield return new WaitForSeconds(0.1f);
        boardState.Value = new NetworkString(gameManager.SerializeGame());
    }
    
    // Loads board and updates pieces
    private void OnBoardStateChanged(NetworkString previousValue, NetworkString newValue)
    {
        if (string.IsNullOrEmpty(newValue.Value)) return;
        
        Board currentBoard = gameManager.CurrentBoard;
        gameManager.LoadGame(newValue.Value);
        UpdateVisualPieces(currentBoard, gameManager.CurrentBoard);
    }
    
    // Updates visual pieces on the board
    private void UpdateVisualPieces(Board oldBoard, Board newBoard)
    {
        for (int file = 1; file <= 8; file++)
        {
            for (int rank = 1; rank <= 8; rank++)
            {
                Square square = new Square(file, rank);
                Piece newPiece = newBoard[square];
                Piece oldPiece = oldBoard[square];
                
                if (newPiece != oldPiece)
                {
                    boardManager.TryDestroyVisualPiece(square);
                    
                    if (newPiece != null)
                        boardManager.CreateAndPlacePieceGO(newPiece, square);
                }
            }
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void ExecuteMoveServerRpc(string startSquareStr, string endSquareStr, ServerRpcParams rpcParams = default)
    {
        Square startSquare = SquareUtil.StringToSquare(startSquareStr);
        Square endSquare = SquareUtil.StringToSquare(endSquareStr);
        
        if (gameManager.TryGetLegalMoveAndExecute(startSquare, endSquare))
            NotifyClientsAboutMoveClientRpc(startSquareStr, endSquareStr);
    }
    
    // Updates piece movement on clients
    [ClientRpc]
    private void NotifyClientsAboutMoveClientRpc(string startSquareStr, string endSquareStr)
    {
        if (IsServer) return;
        
        Square startSquare = SquareUtil.StringToSquare(startSquareStr);
        Square endSquare = SquareUtil.StringToSquare(endSquareStr);
        
        GameObject pieceGO = boardManager.GetPieceGOAtPosition(startSquare);
        if (pieceGO == null) return;
        
        GameObject targetSquareGO = boardManager.GetSquareGOByPosition(endSquare);
        if (targetSquareGO == null) return;
        
        boardManager.TryDestroyVisualPiece(endSquare);
        
        Transform pieceTransform = pieceGO.transform;
        pieceTransform.SetParent(targetSquareGO.transform);
        pieceTransform.position = targetSquareGO.transform.position;
    }
    
    public struct NetworkString : INetworkSerializable
    {
        private string value;
        public string Value => value;
        
        public NetworkString(string value) => this.value = value;
        
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref value);
        }
    }
    
    // Syncs loaded game
    public void SyncLoadedGameState(string fen)
    {
        if (!IsServer) return;

        boardState.Value = new NetworkString(fen);
    }
    
    // Saves game state to Firestore
    [ServerRpc(RequireOwnership = false)]
    public void SaveGameToFirestoreServerRpc()
    {
        FirebaseGameLogger firebase = FindObjectOfType<FirebaseGameLogger>();
        if (firebase == null) return;

        string fen = GameManager.Instance.SerializeGame();
        if (!string.IsNullOrEmpty(fen))
        {
            firebase.SaveGameState(fen);
            Debug.Log("Game state saved to Firestore");
        }
    }
    
    // Loads game state from Firestore
    [ServerRpc(RequireOwnership = false)]
    public void LoadGameFromFirestoreServerRpc()
    {
        StartCoroutine(LoadGameFromFirestoreRoutine());
    }
    
    // Coroutine to load game from Firestore
    private IEnumerator LoadGameFromFirestoreRoutine()
    {
        FirebaseGameLogger firebase = FindObjectOfType<FirebaseGameLogger>();
        if (firebase == null) yield break;

        var task = firebase.LoadGameState();
        while (!task.IsCompleted) yield return null;

        if (task.IsFaulted || task.Result == null) yield break;

        string fen = task.Result;
        GameManager.Instance.LoadGame(fen);
        SyncLoadedGameState(fen);
    }
}
