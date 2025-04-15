// This is a refactored version of the original DLCStoreManager to support per-player coins and avatar ownership.
// All changes ensure data is stored per-client and synced correctly across the network.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using Firebase.Storage;
using System.Xml;

public class DLCStoreManager : NetworkBehaviour
{
    public static DLCStoreManager Instance { get; private set; }

    [SerializeField] private GameObject storePanel;
    [SerializeField] private Transform avatarContainer;
    [SerializeField] private Button closeStoreButton;
    [SerializeField] private TMP_Text playerCoinsText;
    [SerializeField] private GameObject avatarItemPrefab;
    [SerializeField] private Image whitePlayerAvatarDisplay;
    [SerializeField] private Image blackPlayerAvatarDisplay;
    [SerializeField] private int startingCoins = 1000;
    [SerializeField] private FirebaseGameLogger firebaseLogger;

    private Dictionary<ulong, int> playerCoinsDict = new();
    private Dictionary<ulong, HashSet<string>> ownedAvatarsByClient = new();
    private Dictionary<string, Texture2D> avatarTextures = new();
    private Dictionary<string, List<StoreItem>> pendingAvatarItems = new();
    private Dictionary<string, List<ulong>> pendingPlayerAvatars = new();
    private Dictionary<ulong, string> playerAvatars = new();

    private List<AvatarData> availableAvatars = new();
    private FirebaseStorage storage;
    private bool isStoreInitialized = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (closeStoreButton != null) closeStoreButton.onClick.AddListener(HideStore);
        storage = FirebaseStorage.DefaultInstance;
        if (storePanel != null) storePanel.SetActive(false);
        ParseAvatarXml();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    public override void OnNetworkDespawn()
    {
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    // Loads avatar data from an XML file into the store
    private void ParseAvatarXml()
    {
        availableAvatars.Clear();
        TextAsset xmlFile = Resources.Load<TextAsset>("Avatars");
        if (xmlFile == null) return;

        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.LoadXml(xmlFile.text);
        XmlNodeList avatarNodes = xmlDoc.SelectNodes("//avatar");

        foreach (XmlNode avatarNode in avatarNodes)
        {
            string id = avatarNode.Attributes["id"].Value;
            string name = avatarNode.SelectSingleNode("name")?.InnerText;
            int price = int.Parse(avatarNode.SelectSingleNode("price")?.InnerText);
            string imageUrl = avatarNode.SelectSingleNode("imageUrl")?.InnerText;

            if (!string.IsNullOrEmpty(id))
                availableAvatars.Add(new AvatarData { Id = id, Name = name, Price = price, ImageFileName = imageUrl });
        }
    }

    // Displays the DLC store panel and populates it
    public void ShowStore() {
        storePanel.SetActive(true);
        PopulateStoreUI();
    }

    // Hides the DLC store panel
    public void HideStore() => storePanel.SetActive(false);

    // Creates UI elements for all avatars
    private void PopulateStoreUI()
    {
        foreach (Transform child in avatarContainer)
            Destroy(child.gameObject);

        ulong clientId = NetworkManager.Singleton.LocalClientId;
        int coins = playerCoinsDict.ContainsKey(clientId) ? playerCoinsDict[clientId] : 0;
        playerCoinsText.text = $"Coins: {coins}";

        foreach (AvatarData avatar in availableAvatars)
        {
            GameObject itemGO = Instantiate(avatarItemPrefab, avatarContainer);
            StoreItem item = itemGO.GetComponent<StoreItem>();

            bool isOwned = ownedAvatarsByClient.ContainsKey(clientId) && ownedAvatarsByClient[clientId].Contains(avatar.Id);
            item.Initialize(avatar, isOwned, OnAvatarPurchaseRequest, OnAvatarSelected);

            if (avatarTextures.TryGetValue(avatar.Id, out Texture2D tex))
                item.SetImage(tex);
            else
                StartCoroutine(DownloadAvatarImage(avatar.Id, avatar.ImageFileName, item));
        }
    }

    // Called when a player tries to buy an avatar
    private bool OnAvatarPurchaseRequest(string avatarId)
    {
        RequestAvatarPurchaseServerRpc(NetworkManager.Singleton.LocalClientId, avatarId);
        return false;
    }

    // Checks purchase, deducts coins, adds avatar to owned list, and syncs
    [ServerRpc(RequireOwnership = false)]
    private void RequestAvatarPurchaseServerRpc(ulong clientId, string avatarId)
    {
        AvatarData avatar = availableAvatars.Find(a => a.Id == avatarId);
        if (avatar == null || !playerCoinsDict.ContainsKey(clientId)) return;

        if (!ownedAvatarsByClient.ContainsKey(clientId))
            ownedAvatarsByClient[clientId] = new HashSet<string>();

        if (ownedAvatarsByClient[clientId].Contains(avatarId)) return;
        if (playerCoinsDict[clientId] < avatar.Price) return;

        playerCoinsDict[clientId] -= avatar.Price;
        ownedAvatarsByClient[clientId].Add(avatarId);
        firebaseLogger?.LogDLCPurchase(avatarId);

        UpdateCoinsClientRpc(clientId, playerCoinsDict[clientId]);
        SyncOwnershipClientRpc(clientId, avatarId);
    }

    // Updates the coins for the player
    [ClientRpc]
    private void UpdateCoinsClientRpc(ulong clientId, int coins)
    {
        if (NetworkManager.Singleton.LocalClientId != clientId) return;
        if (!playerCoinsDict.ContainsKey(clientId))
            playerCoinsDict[clientId] = coins;
        else
            playerCoinsDict[clientId] = coins;

        playerCoinsText.text = $"Coins: {coins}";
        PopulateStoreUI();
    }

    // Updates the client's owned avatar list and refreshes UI
    [ClientRpc]
    private void SyncOwnershipClientRpc(ulong clientId, string avatarId)
    {
        if (!ownedAvatarsByClient.ContainsKey(clientId))
            ownedAvatarsByClient[clientId] = new HashSet<string>();
        ownedAvatarsByClient[clientId].Add(avatarId);
        PopulateStoreUI();
    }

    // Called when a player selects an avatar
    private void OnAvatarSelected(string avatarId)
    {
        ulong clientId = NetworkManager.Singleton.LocalClientId;

        if (!ownedAvatarsByClient.ContainsKey(clientId) || !ownedAvatarsByClient[clientId].Contains(avatarId))
            return;

        RequestAvatarChangeServerRpc(clientId, avatarId);
    }

    // Sets the selected avatar as active and syncs it to all clients
    [ServerRpc(RequireOwnership = false)]
    private void RequestAvatarChangeServerRpc(ulong clientId, string avatarId)
    {
        if (!ownedAvatarsByClient.ContainsKey(clientId) || !ownedAvatarsByClient[clientId].Contains(avatarId)) return;

        playerAvatars[clientId] = avatarId;
        NotifyAvatarChangeClientRpc(clientId, avatarId);
    }

    // Updates avatar display if the avatar value changes
    private void OnAvatarChanged(NetworkString previous, NetworkString current)
    {
        if (string.IsNullOrEmpty(current.Value)) return;
        UpdateAvatarDisplay(current.Value);
    }

    // Applies avatar texture to the white or black player slot
    private void UpdateAvatarDisplay(string avatarId)
    {
        if (!avatarTextures.TryGetValue(avatarId, out Texture2D texture)) return;
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        bool isWhitePlayer = TurnManager.Instance.IsWhitePlayer(NetworkManager.Singleton.LocalClientId);

        if (isWhitePlayer) whitePlayerAvatarDisplay.sprite = sprite;
        else blackPlayerAvatarDisplay.sprite = sprite;
    }

    // Downloads avatar image from Firebase and assigns it to UI
    private IEnumerator DownloadAvatarImage(string avatarId, string imageName, StoreItem item = null)
    {
        if (string.IsNullOrEmpty(imageName)) yield break;

        if (!pendingAvatarItems.ContainsKey(avatarId))
        {
            pendingAvatarItems[avatarId] = new List<StoreItem>();
            StorageReference imageRef = storage.GetReference($"Avatars/{imageName}");
            var downloadTask = imageRef.GetBytesAsync(1024 * 1024);

            if (item != null) pendingAvatarItems[avatarId].Add(item);
            while (!downloadTask.IsCompleted) yield return null;

            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(downloadTask.Result);
            avatarTextures[avatarId] = texture;

            foreach (var pending in pendingAvatarItems[avatarId])
                pending?.SetImage(texture);

            pendingAvatarItems.Remove(avatarId);
        }
        else if (item != null)
        {
            pendingAvatarItems[avatarId].Add(item);
        }
    }

    // Initializes coins and avatar ownership for newly connected clients
    private void OnClientConnected(ulong clientId)
    {
        if (!playerCoinsDict.ContainsKey(clientId))
        {
            playerCoinsDict[clientId] = startingCoins;
            ownedAvatarsByClient[clientId] = new HashSet<string>();
        }

        NotifyAvatarChangeClientRpc(clientId, availableAvatars[0].Id);
    }

    // Updates avatar display for all clients based on selected avatar
    [ClientRpc]
    private void NotifyAvatarChangeClientRpc(ulong clientId, string avatarId)
    {
        playerAvatars[clientId] = avatarId;

        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            UpdateAvatarDisplay(avatarId);
        }
        else
        {
            bool isWhite = TurnManager.Instance.IsWhitePlayer(clientId);
            if (avatarTextures.TryGetValue(avatarId, out Texture2D tex))
            {
                Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                if (isWhite && whitePlayerAvatarDisplay != null)
                    whitePlayerAvatarDisplay.sprite = sprite;
                else if (!isWhite && blackPlayerAvatarDisplay != null)
                    blackPlayerAvatarDisplay.sprite = sprite;
            }
        }
    }

    // Returns the active avatar ID for the given client
    public string GetPlayerAvatarId(ulong clientId) =>
        playerAvatars.ContainsKey(clientId) ? playerAvatars[clientId] : "";

    // Returns the texture of the avatar for the given ID
    public Texture2D GetAvatarTexture(string avatarId) =>
        avatarTextures.ContainsKey(avatarId) ? avatarTextures[avatarId] : null;

    // Checks if the client is the white player (host)
    public bool IsWhitePlayer(ulong clientId) => clientId == NetworkManager.ServerClientId;
}

// Represents data for a single avatar in the DLC store
[System.Serializable]
public class AvatarData
{
    public string Id;
    public string Name;
    public int Price;
    public string ImageFileName;
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