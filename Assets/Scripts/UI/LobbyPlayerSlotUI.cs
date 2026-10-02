using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Woodsmen.Networking;

namespace Woodsmen.UI
{
    /// <summary>
    /// Displays a single connected player inside the Room Lobby list.
    /// Shows player name, host indicator, ready status, and their selected class (Lumberjack or Warrior).
    /// </summary>
    public class LobbyPlayerSlotUI : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private TMP_Text classBadgeText;
        [SerializeField] private Image classIconImage;
        [SerializeField] private GameObject hostBadge;
        [SerializeField] private GameObject readyBadge;
        [SerializeField] private Image backgroundImage;

        [Header("Colors")]
        [SerializeField] private Color localPlayerColor = new Color(0.2f, 0.45f, 0.3f, 0.9f);
        [SerializeField] private Color otherPlayerColor = new Color(0.12f, 0.15f, 0.18f, 0.85f);
        [SerializeField] private Color lumberjackColor = new Color(0.85f, 0.55f, 0.2f, 1f);
        [SerializeField] private Color warriorColor = new Color(0.85f, 0.25f, 0.25f, 1f);

        public void Clear()
        {
            if (playerNameText != null) playerNameText.text = "";
            if (hostBadge != null) hostBadge.SetActive(false);
            if (readyBadge != null) readyBadge.SetActive(false);
            if (classBadgeText != null) classBadgeText.text = "";
            gameObject.SetActive(false);
        }

        public void Bind(WoodsmenLobbyPlayer player, bool isLocal)
        {
            if (player == null)
            {
                Clear();
                return;
            }
            BindDirect(player.PlayerName, player.SelectedClass, player.IsHost, player.IsReady, isLocal);
        }

        public void BindDirect(string name, CharacterClass chosenClass, bool isHost, bool isReady, bool isLocal)
        {
            if (playerNameText != null)
            {
                playerNameText.text = isLocal ? $"{name} (You)" : name;
            }

            if (hostBadge != null)
            {
                hostBadge.SetActive(isHost);
            }

            if (readyBadge != null)
            {
                readyBadge.SetActive(isReady);
            }

            if (classBadgeText != null)
            {
                classBadgeText.text = chosenClass == CharacterClass.Warrior ? "WARRIOR" : "LUMBERJACK";
                classBadgeText.color = chosenClass == CharacterClass.Warrior ? warriorColor : lumberjackColor;
            }

            if (backgroundImage != null)
            {
                backgroundImage.color = isLocal ? localPlayerColor : otherPlayerColor;
            }
        }
    }
}
