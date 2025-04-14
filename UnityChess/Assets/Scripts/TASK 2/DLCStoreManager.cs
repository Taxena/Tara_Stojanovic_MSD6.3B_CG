using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Firebase.Storage;
using System;
using TMPro;
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
    
    private NetworkVariable<int> playerCoins = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<NetworkString> currentAvatarId = new NetworkVariable<NetworkString>(new NetworkString(""), 
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    
    private List<AvatarData> availableAvatars = new List<AvatarData>();
    private Dictionary<string, bool> ownedAvatars = new Dictionary<string, bool>();
    private Dictionary<string, Texture2D> avatarTextures = new Dictionary<string, Texture2D>();
    private Dictionary<ulong, string> playerAvatars = new Dictionary<ulong, string>();
    
    private FirebaseStorage storage;
    private bool isStoreInitialized = false;
    private Dictionary<string, List<StoreItem>> pendingAvatarItems = new Dictionary<string, List<StoreItem>>();
    private Dictionary<string, List<ulong>> pendingPlayerAvatars = new Dictionary<string, List<ulong>>();
    
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }
    
    private void Start()
    {
        if (closeStoreButton != null) closeStoreButton.onClick.AddListener(HideStore);
        storage = FirebaseStorage.DefaultInstance;
        if (storePanel != null) storePanel.SetActive(false);
        LoadOwnedAvatars();
        /*ResetCoinsToDefault();
        ResetAllPlayerData();*/
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsOwner)
        {
            playerCoins.Value = PlayerPrefs.GetInt("PlayerCoins", startingCoins);
            UpdateCoinsDisplay();
            InitializeAvatarStore();
            string savedAvatarId = PlayerPrefs.GetString("CurrentAvatarId", "");
            if (!string.IsNullOrEmpty(savedAvatarId))
                currentAvatarId.Value = new NetworkString(savedAvatarId);
        }
        
        currentAvatarId.OnValueChanged += OnAvatarChanged;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        currentAvatarId.OnValueChanged -= OnAvatarChanged;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }
    
    private void InitializeAvatarStore()
    {
        if (isStoreInitialized) return;
        ParseAvatarXml();
        isStoreInitialized = true;
    }
    
    public void ShowStore()
    {
        if (!isStoreInitialized) InitializeAvatarStore();
        storePanel.SetActive(true);
        PopulateStoreUI();
    }
    
    public void HideStore() => storePanel.SetActive(false);
    
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
            string id = avatarNode.Attributes["id"]?.Value;
            string name = avatarNode.SelectSingleNode("name")?.InnerText;
            int price = int.Parse(avatarNode.SelectSingleNode("price")?.InnerText ?? "0");
            string imageUrl = avatarNode.SelectSingleNode("imageUrl")?.InnerText;
            
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(imageUrl))
            {
                availableAvatars.Add(new AvatarData
                {
                    Id = id,
                    Name = name,
                    Price = price,
                    ImageFileName = imageUrl
                });
            }
        }
    }
    
    private void PopulateStoreUI()
    {
        foreach (Transform child in avatarContainer)
            Destroy(child.gameObject);
        
        UpdateCoinsDisplay();
        
        foreach (AvatarData avatar in availableAvatars)
        {
            if (avatarContainer == null || avatarItemPrefab == null) continue;
            
            GameObject itemGO = Instantiate(avatarItemPrefab, avatarContainer);
            StoreItem item = itemGO.GetComponent<StoreItem>();
            
            if (item != null)
            {
                bool isOwned = ownedAvatars.ContainsKey(avatar.Id) && ownedAvatars[avatar.Id];
                item.Initialize(avatar, isOwned, OnAvatarPurchased, OnAvatarSelected);
                
                if (avatarTextures.ContainsKey(avatar.Id))
                    item.SetImage(avatarTextures[avatar.Id]);
                else
                    StartCoroutine(DownloadAvatarImage(avatar.Id, avatar.ImageFileName, item));
            }
        }
    }
    
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
            
            if (downloadTask.IsFaulted || downloadTask.IsCanceled)
            {
                pendingAvatarItems.Remove(avatarId);
                yield break;
            }
            
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(downloadTask.Result);
            avatarTextures[avatarId] = texture;
            
            foreach (var pendingItem in pendingAvatarItems[avatarId])
                if (pendingItem != null) pendingItem.SetImage(texture);
            
            if (pendingPlayerAvatars.ContainsKey(avatarId))
            {
                foreach (var clientId in pendingPlayerAvatars[avatarId])
                    UpdatePlayerAvatarDisplay(clientId, avatarId);
                pendingPlayerAvatars.Remove(avatarId);
            }
            
            if (currentAvatarId.Value.Value == avatarId)
                UpdateAvatarDisplay(avatarId);
            
            pendingAvatarItems.Remove(avatarId);
        }
        else if (item != null)
            pendingAvatarItems[avatarId].Add(item);
    }
    
    private bool OnAvatarPurchased(string avatarId)
    {
        if (!IsOwner) return false;
            
        AvatarData avatar = availableAvatars.Find(a => a.Id == avatarId);
        if (avatar == null) return false;
        if (ownedAvatars.ContainsKey(avatarId) && ownedAvatars[avatarId]) return true;
        if (playerCoins.Value < avatar.Price) return false;
            
        playerCoins.Value -= avatar.Price;
        ownedAvatars[avatarId] = true;
        
        SaveOwnedAvatars();
        PlayerPrefs.SetInt("PlayerCoins", playerCoins.Value);
        PlayerPrefs.Save();
        
        UpdateCoinsDisplay();
        return true;
    }
    
    private void OnAvatarSelected(string avatarId)
    {
        if (!IsOwner) return;
        if (!ownedAvatars.ContainsKey(avatarId) || !ownedAvatars[avatarId]) return;
            
        currentAvatarId.Value = new NetworkString(avatarId);
        PlayerPrefs.SetString("CurrentAvatarId", avatarId);
        PlayerPrefs.Save();
    }
    
    private void OnAvatarChanged(NetworkString previous, NetworkString current)
    {
        if (string.IsNullOrEmpty(current.Value)) return;
            
        ulong clientId = NetworkManager.Singleton.LocalClientId;
        playerAvatars[clientId] = current.Value;
        
        UpdateAvatarDisplay(current.Value);
        NotifyAvatarChangeServerRpc(clientId, current.Value);
    }
    
    private void UpdateAvatarDisplay(string avatarId)
    {
        if (string.IsNullOrEmpty(avatarId)) return;
            
        if (!avatarTextures.TryGetValue(avatarId, out Texture2D texture) || texture == null)
        {
            AvatarData avatar = availableAvatars.Find(a => a.Id == avatarId);
            if (avatar != null) StartCoroutine(DownloadAvatarImage(avatarId, avatar.ImageFileName));
            return;
        }
            
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        bool isWhitePlayer = IsWhitePlayer(NetworkManager.Singleton.LocalClientId);
        
        if (isWhitePlayer && whitePlayerAvatarDisplay != null)
            whitePlayerAvatarDisplay.sprite = sprite;
        else if (!isWhitePlayer && blackPlayerAvatarDisplay != null)
            blackPlayerAvatarDisplay.sprite = sprite;
    }
    
    private void UpdateCoinsDisplay()
    {
        if (playerCoinsText != null) playerCoinsText.text = $"Coins: {playerCoins.Value}";
    }
    
    private void LoadOwnedAvatars()
    {
        ownedAvatars.Clear();
        string ownedAvatarsString = PlayerPrefs.GetString("OwnedAvatars", "");
        if (!string.IsNullOrEmpty(ownedAvatarsString))
        {
            foreach (string id in ownedAvatarsString.Split(','))
                if (!string.IsNullOrEmpty(id)) ownedAvatars[id] = true;
        }
    }
    
    private void SaveOwnedAvatars()
    {
        string ownedAvatarsString = "";
        foreach (var kvp in ownedAvatars)
            if (kvp.Value) ownedAvatarsString += kvp.Key + ",";
        
        PlayerPrefs.SetString("OwnedAvatars", ownedAvatarsString);
        PlayerPrefs.Save();
    }
    
    public Texture2D GetAvatarTexture(string avatarId) => 
        avatarTextures.ContainsKey(avatarId) ? avatarTextures[avatarId] : null;
    
    public string GetPlayerAvatarId(ulong clientId) => 
        playerAvatars.ContainsKey(clientId) ? playerAvatars[clientId] : "";
    
    private bool IsWhitePlayer(ulong clientId) => clientId == NetworkManager.ServerClientId;
    
    private void OnClientConnected(ulong clientId)
    {
        if (IsServer)
            foreach (var playerAvatar in playerAvatars)
                NotifyAvatarChangeClientRpc(playerAvatar.Key, playerAvatar.Value);
    }

    [ServerRpc(RequireOwnership = false)]
    private void NotifyAvatarChangeServerRpc(ulong clientId, string avatarId)
    {
        if (IsServer)
        {
            playerAvatars[clientId] = avatarId;
            NotifyAvatarChangeClientRpc(clientId, avatarId);
        }
    }
    
    [ClientRpc]
    private void NotifyAvatarChangeClientRpc(ulong clientId, string avatarId)
    {
        if (!IsServer)
        {
            playerAvatars[clientId] = avatarId;
            if (clientId != NetworkManager.Singleton.LocalClientId)
                UpdatePlayerAvatarDisplay(clientId, avatarId);
        }
    }
    
    private void UpdatePlayerAvatarDisplay(ulong clientId, string avatarId)
    {
        bool isWhitePlayer = IsWhitePlayer(clientId);
        
        if (avatarTextures.TryGetValue(avatarId, out Texture2D texture) && texture != null)
        {
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            
            if (isWhitePlayer && whitePlayerAvatarDisplay != null)
                whitePlayerAvatarDisplay.sprite = sprite;
            else if (!isWhitePlayer && blackPlayerAvatarDisplay != null)
                blackPlayerAvatarDisplay.sprite = sprite;
        }
        else
        {
            AvatarData avatar = availableAvatars.Find(a => a.Id == avatarId);
            if (avatar != null)
            {
                if (!pendingPlayerAvatars.ContainsKey(avatarId))
                    pendingPlayerAvatars[avatarId] = new List<ulong>();
                
                pendingPlayerAvatars[avatarId].Add(clientId);
                StartCoroutine(DownloadAvatarImage(avatarId, avatar.ImageFileName));
            }
        }
    }
    
    public void ResetCoinsToDefault()
    {
        if (IsOwner)
        {
            playerCoins.Value = startingCoins;
        }
    
        PlayerPrefs.SetInt("PlayerCoins", startingCoins);
        PlayerPrefs.Save();
    
        UpdateCoinsDisplay();
    }
    
    
    public void ResetAllPlayerData()
    {
        if (IsOwner)
        {
            playerCoins.Value = startingCoins;
        }
    
        PlayerPrefs.SetInt("PlayerCoins", startingCoins);
    
        ownedAvatars.Clear();
    
        if (availableAvatars.Count > 0)
        {
            string defaultAvatarId = availableAvatars[0].Id;
            ownedAvatars[defaultAvatarId] = true;
        }
    
        SaveOwnedAvatars();
    
        if (IsOwner && availableAvatars.Count > 0)
        {
            string defaultAvatarId = availableAvatars[0].Id;
            currentAvatarId.Value = new NetworkString(defaultAvatarId);
            PlayerPrefs.SetString("CurrentAvatarId", defaultAvatarId);
        }
        else
        {
            PlayerPrefs.SetString("CurrentAvatarId", "");
        }
    
        PlayerPrefs.Save();
    
        UpdateCoinsDisplay();
    
        if (storePanel.activeSelf)
        {
            PopulateStoreUI();
        }
    }
}

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