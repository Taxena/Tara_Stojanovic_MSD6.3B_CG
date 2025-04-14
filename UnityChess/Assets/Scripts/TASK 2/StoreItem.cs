using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StoreItem : MonoBehaviour
{
    [SerializeField] private Image avatarImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text priceText;
    [SerializeField] private Button purchaseButton;
    [SerializeField] private Button selectButton;
    [SerializeField] private GameObject ownedIndicator;
    
    private AvatarData avatarData;
    private bool isOwned;
    private Func<string, bool> onPurchaseCallback;
    private Action<string> onSelectCallback;
    
    public void Initialize(AvatarData data, bool owned, Func<string, bool> purchaseCallback, Action<string> selectCallback)
    {
        avatarData = data;
        isOwned = owned;
        onPurchaseCallback = purchaseCallback;
        onSelectCallback = selectCallback;
        
        nameText.text = data.Name;
        priceText.text = $"{data.Price} Coins";
        
        purchaseButton.onClick.RemoveAllListeners();
        selectButton.onClick.RemoveAllListeners();
        
        purchaseButton.onClick.AddListener(OnPurchaseClicked);
        selectButton.onClick.AddListener(OnSelectClicked);
        
        UpdateButtonStates();
    }
    
    public void SetImage(Texture2D texture)
    {
        if (texture != null && avatarImage != null)
            avatarImage.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    }
    
    private void OnPurchaseClicked()
    {
        if (avatarData == null || onPurchaseCallback == null) return;
            
        if (onPurchaseCallback.Invoke(avatarData.Id))
        {
            isOwned = true;
            UpdateButtonStates();
        }
    }
    
    private void OnSelectClicked()
    {
        if (avatarData == null || onSelectCallback == null || !isOwned) return;
        onSelectCallback.Invoke(avatarData.Id);
    }
    
    private void UpdateButtonStates()
    {
        purchaseButton.gameObject.SetActive(!isOwned);
        selectButton.gameObject.SetActive(isOwned);
        ownedIndicator.SetActive(isOwned);
    }
}