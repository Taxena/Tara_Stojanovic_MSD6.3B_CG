using UnityChess;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.UI;

public class GameEndManager : NetworkBehaviour
{
    [SerializeField] private Button resignButton;
    
    private GameManager gameManager;
    private UIManager uiManager;
    private TurnManager turnManager;
    private BoardManager boardManager;
    
    private NetworkVariable<GameEndState> gameEndState = new NetworkVariable<GameEndState>(
        GameEndState.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public enum GameEndState
    {
        None,
        WhiteWinsByCheckmate,
        BlackWinsByCheckmate,
        DrawByStalemate,
        WhiteResigned,
        BlackResigned,
        PlayerDisconnected
    }

    private void Start()
    {
        gameManager = GameManager.Instance;
        uiManager = UIManager.Instance;
        turnManager = TurnManager.Instance;
        boardManager = BoardManager.Instance;
        
        if (resignButton != null) resignButton.onClick.AddListener(ResignGame);
        GameManager.MoveExecutedEvent += CheckForGameEnd;
        GameManager.GameResetToHalfMoveEvent += ResetGameEndState;
        GameManager.NewGameStartedEvent += OnNewGameStarted;
        gameEndState.OnValueChanged += OnGameEndStateChanged;
        
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnPlayerDisconnected;
        }
        
        ResetUIElements();
    }

    private void OnDisable()
    {
        GameManager.MoveExecutedEvent -= CheckForGameEnd;
        GameManager.GameResetToHalfMoveEvent -= ResetGameEndState;
        GameManager.NewGameStartedEvent -= OnNewGameStarted;
        if (resignButton != null) resignButton.onClick.RemoveListener(ResignGame);
        gameEndState.OnValueChanged -= OnGameEndStateChanged;
        
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnPlayerDisconnected;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void CheckForGameEnd()
    {
        if (!IsServer || gameEndState.Value != GameEndState.None) return;

        if (gameManager.HalfMoveTimeline.TryGetCurrent(out HalfMove move))
        {
            if (move.CausedCheckmate)
            {
                gameEndState.Value = move.Piece.Owner == Side.White 
                    ? GameEndState.WhiteWinsByCheckmate 
                    : GameEndState.BlackWinsByCheckmate;
            }
            else if (move.CausedStalemate)
            {
                gameEndState.Value = GameEndState.DrawByStalemate;
            }
        }
    }

    private void OnGameEndStateChanged(GameEndState previous, GameEndState current)
    {
        if (current == GameEndState.None) return;

        string message = GetEndStateMessage(current);
        Debug.Log($"Game ended: {message}");
        
        if (uiManager != null && !string.IsNullOrEmpty(message))
        {
            uiManager.resultText.text = message;
            uiManager.resultText.gameObject.SetActive(true);
        }
        
        if (boardManager != null) boardManager.SetActiveAllPieces(false);
        if (resignButton != null) resignButton.interactable = false;
    }

    private string GetEndStateMessage(GameEndState state)
    {
        return state switch
        {
            GameEndState.WhiteWinsByCheckmate => "White wins by checkmate!",
            GameEndState.BlackWinsByCheckmate => "Black wins by checkmate!",
            GameEndState.DrawByStalemate => "Game ends in a draw by stalemate.",
            GameEndState.WhiteResigned => "White resigned. Black wins!",
            GameEndState.BlackResigned => "Black resigned. White wins!",
            GameEndState.PlayerDisconnected => "Game ended - player disconnected.",
            _ => ""
        };
    }

    public void ResignGame()
    {
        if (!IsServer)
        {
            ResignGameServerRpc();
            return;
        }

        SetResignState(NetworkManager.Singleton.LocalClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ResignGameServerRpc(ServerRpcParams rpcParams = default)
    {
        SetResignState(rpcParams.Receive.SenderClientId);
    }
    
    private void SetResignState(ulong clientId)
    {
        if (turnManager.IsWhitePlayer(clientId))
            gameEndState.Value = GameEndState.WhiteResigned;
        else if (turnManager.IsBlackPlayer(clientId))
            gameEndState.Value = GameEndState.BlackResigned;
    }
    
    private void OnNewGameStarted() => ResetGameEndState();
    
    private void ResetGameEndState()
    {
        if (IsServer) gameEndState.Value = GameEndState.None;
        ResetUIElements();
    }
    
    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            ResetUIElements();
            if (IsServer && gameEndState.Value != GameEndState.None)
                gameEndState.Value = GameEndState.None;
        }
    
        if (IsServer && clientId != NetworkManager.Singleton.LocalClientId)
            NotifyClientAboutGameStateClientRpc(clientId);
    }

    [ClientRpc]
    private void NotifyClientAboutGameStateClientRpc(ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == targetClientId)
            ResetUIElements();
    }
    
    public void ForceCheckmate(Side winningSide)
    {
        if (!IsServer)
        {
            ForceCheckmateServerRpc(winningSide == Side.White);
            return;
        }
        
        if (gameEndState.Value == GameEndState.None)
        {
            gameEndState.Value = winningSide == Side.White 
                ? GameEndState.WhiteWinsByCheckmate 
                : GameEndState.BlackWinsByCheckmate;
        }
    }
    
    public void ForceStalemate()
    {
        if (!IsServer)
        {
            ForceStalemateServerRpc();
            return;
        }
        
        if (gameEndState.Value == GameEndState.None)
            gameEndState.Value = GameEndState.DrawByStalemate;
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void ForceCheckmateServerRpc(bool isWhiteWinner)
    {
        if (gameEndState.Value == GameEndState.None)
        {
            gameEndState.Value = isWhiteWinner 
                ? GameEndState.WhiteWinsByCheckmate 
                : GameEndState.BlackWinsByCheckmate;
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void ForceStalemateServerRpc()
    {
        if (gameEndState.Value == GameEndState.None)
            gameEndState.Value = GameEndState.DrawByStalemate;
    }
    
    private void OnPlayerDisconnected(ulong clientId)
    {
        if (!IsServer) return;
        
        bool isGamePlayer = turnManager.IsWhitePlayer(clientId) || turnManager.IsBlackPlayer(clientId);
        
        if (isGamePlayer && gameEndState.Value == GameEndState.None)
        {
            gameEndState.Value = GameEndState.PlayerDisconnected;
            if (boardManager != null)
            {
                boardManager.ClearBoard();
                gameManager.StartNewGame();
            }
        }
    }
    
    private void ResetUIElements()
    {
        if (resignButton != null) resignButton.interactable = true;
        
        if (uiManager != null && uiManager.resultText != null)
            uiManager.resultText.gameObject.SetActive(false);
    }
}