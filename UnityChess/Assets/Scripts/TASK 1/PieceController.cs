using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityChess;
using Unity.Netcode;

public class PieceController : MonoBehaviour
{
    private VisualPiece visualPiece;
    
    private void Awake()
    {
        visualPiece = GetComponent<VisualPiece>();
    }
    
    private void Start()
    {
        TurnManager.OnTurnChanged += OnTurnChanged;
        StartCoroutine(MonitorPieceMovement());
    }
    
    private void OnDestroy()
    {
        TurnManager.OnTurnChanged -= OnTurnChanged;
        StopAllCoroutines();
    }
    
    private IEnumerator MonitorPieceMovement()
    {
        VisualPiece piece = visualPiece;
        if (piece == null) yield break;
        
        Transform lastParent = transform.parent;
        Square lastSquare = piece.CurrentSquare;
        
        while (true)
        {
            yield return null;
            
            if (transform.parent != lastParent)
            {
                Square currentSquare = piece.CurrentSquare;
                
                if (currentSquare.File != lastSquare.File || currentSquare.Rank != lastSquare.Rank)
                    NotifyNetworkOfMove(lastSquare, currentSquare);
                
                lastParent = transform.parent;
                lastSquare = currentSquare;
            }
        }
    }
    
    private void OnTurnChanged(Side currentTurnSide)
    {
        if (TurnManager.Instance != null && visualPiece != null)
        {
            visualPiece.enabled = TurnManager.Instance.IsLocalPlayerTurn && 
                                 visualPiece.PieceColor == currentTurnSide;
        }
    }
    
    private void NotifyNetworkOfMove(Square startSquare, Square endSquare)
    {
        if (TurnManager.Instance != null && TurnManager.Instance.IsLocalPlayerTurn)
        {
            TurnManager.Instance.NotifyOfMove(startSquare, endSquare, visualPiece.PieceColor);
        }
        else
        {
            Transform originalParent = BoardManager.Instance.GetSquareGOByPosition(startSquare).transform;
            transform.parent = originalParent;
            transform.position = originalParent.position;
        }
    }
}
