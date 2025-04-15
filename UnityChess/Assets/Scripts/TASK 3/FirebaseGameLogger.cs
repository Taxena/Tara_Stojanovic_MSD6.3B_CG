using UnityEngine;
using Firebase;
using Firebase.Analytics;
using Firebase.Firestore;
using System;
using System.Threading.Tasks;

public class FirebaseGameLogger : MonoBehaviour
{
    private FirebaseFirestore db;
    private string userId;

    /*private void Awake()
    {
        /*FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
        {
            if (task.Result == DependencyStatus.Available)
            {
                db = FirebaseFirestore.DefaultInstance;
                userId = SystemInfo.deviceUniqueIdentifier;
                Debug.Log("Firebase initialized for user: " + userId);
            }
        });#1#
    }*/
    
    private async void Awake()
    {
#if UNITY_EDITOR
        if (ParrelSync.ClonesManager.IsClone())
        {
            Debug.Log("[Firebase] Clone detected, skipping Firebase init.");
            Destroy(gameObject);
            return;
        }
#endif

        Debug.Log("[FirebaseGameLogger] Starting initialization...");

        var dependencyStatus = await FirebaseApp.CheckAndFixDependenciesAsync();
        if (dependencyStatus == DependencyStatus.Available)
        {
            db = FirebaseFirestore.DefaultInstance;
            userId = SystemInfo.deviceUniqueIdentifier;
            Debug.Log("Firebase initialized.");
        }
        else
        {
            Debug.LogError($"Firebase failed to initialize: {dependencyStatus}");
        }
    }


    public void LogMatchStart()
    {
        if (db == null) return;
        
        FirebaseAnalytics.LogEvent("match_start", new Parameter("timestamp", DateTime.UtcNow.ToString()));
        db.Collection("matches").AddAsync(new
        {
            user = userId,
            event_type = "start",
            timestamp = Timestamp.GetCurrentTimestamp()
        });
    }

    public void LogMatchEnd(string result)
    {
        if (db == null) return;
        
        FirebaseAnalytics.LogEvent("match_end", new Parameter("result", result));
        db.Collection("matches").AddAsync(new
        {
            user = userId,
            event_type = "end",
            result,
            timestamp = Timestamp.GetCurrentTimestamp()
        });
    }

    public void LogDLCPurchase(string avatarId)
    {
        if (db == null) return;

        FirebaseAnalytics.LogEvent("dlc_purchase", new Parameter("avatar_id", avatarId));

        db.Collection("purchases").AddAsync(new
        {
            user = userId,
            avatar_id = avatarId,
            timestamp = Timestamp.GetCurrentTimestamp()
        });

        Debug.Log($"DLC purchase logged: {avatarId}");
    }

    public async Task SaveGameState(string fen)
    {
        if (db == null) return;
        
        DocumentReference doc = db.Collection("saved_games").Document(userId);
        await doc.SetAsync(new { fen });
        Debug.Log("Game state saved to Firestore.");
    }

    public async Task<string> LoadGameState()
    {
        if (db == null) return null;

        DocumentReference doc = db.Collection("saved_games").Document(userId);
        DocumentSnapshot snapshot = await doc.GetSnapshotAsync();

        if (snapshot.Exists && snapshot.TryGetValue("fen", out string fen))
        {
            Debug.Log("Loaded saved game state from Firestore.");
            return fen;
        }

        Debug.LogWarning("No saved game found.");
        return null;
    }
}
