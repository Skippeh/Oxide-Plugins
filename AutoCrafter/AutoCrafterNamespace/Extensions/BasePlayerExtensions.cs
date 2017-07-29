using System.Collections.Generic;
using Oxide.Plugins.AutoCrafterNamespace.UI;
using ProtoBuf;
using UnityEngine;

namespace Oxide.Plugins.AutoCrafterNamespace.Extensions
{
	public static class BasePlayerExtensions
	{
		private static readonly Dictionary<BasePlayer, Timer> gameTipTimers = new Dictionary<BasePlayer, Timer>();
		private static readonly Dictionary<BasePlayer, ScreenMessageUI> screenMessages = new Dictionary<BasePlayer, ScreenMessageUI>(); 

		public static void GiveItems(this BasePlayer player, IEnumerable<Item> items, BaseEntity.GiveItemReason reason = BaseEntity.GiveItemReason.Generic)
		{
			foreach (var item in items)
			{
				player.GiveItem(item, reason);
			}
		}

		public static void TranslatedChatMessage(this BasePlayer player, string key, params object[] format)
		{
			player.ChatMessage(Lang.Translate(player, key, format));
		}

		/// <summary>
		/// Shows a game tip for the player. Optionally hide it after the specified time in seconds.
		/// </summary>
		/// <param name="message">The message to show.</param>
		/// <param name="time">The time in seconds before it dissapears. If 0 or below, it will stay forever. Use HideGameTip to hide it manually.</param>
		public static void ShowGameTip(this BasePlayer player, string message, float time = 0)
		{
			if (gameTipTimers.ContainsKey(player))
			{
				gameTipTimers[player].DestroyToPool();
				gameTipTimers.Remove(player);
			}

			player.SendConsoleCommand("gametip.showgametip", message);

			if (time > 0)
				gameTipTimers.Add(player, Utility.Timer.Once(time, player.HideGameTip));
		}

		/// <summary>
		/// Hides the game tip that the player is currently seeing.
		/// </summary>
		/// <param name="player"></param>
		public static void HideGameTip(this BasePlayer player)
		{
			if (gameTipTimers.ContainsKey(player))
			{
				gameTipTimers[player].DestroyToPool();
				gameTipTimers.Remove(player);
			}

			player.SendConsoleCommand("gametip.hidegametip");
		}

		/// <summary>
		/// Returns true if the player has the specified ingredients.
		/// </summary>
		/// <param name="ingredients">The ingredients to check.</param>
		public static bool CanCraft(this BasePlayer player, IEnumerable<ItemAmount> ingredients)
		{
			foreach (var itemAmount in ingredients)
			{
				int amount = player.inventory.GetAmount(itemAmount.itemid);

				if (amount < itemAmount.amount)
					return false;
			}

			return true;
		}

		/// <summary>
		/// Shows a screen message to the player. Optionally hide it after the specified time in seconds.
		/// </summary>
		/// <param name="message">The message to show.</param>
		/// <param name="time">The time in seconds before it dissapears. If 0 or below, it will stay forever. Use HideScreenMessage to hide it manually.</param>
		public static void ShowScreenMessage(this BasePlayer player, string message, float time, TextAnchor textAnchor = TextAnchor.MiddleCenter)
		{
			message = message.Replace("\r", ""); // Remove \r in new lines from stringbuilder etc.

			if (gameTipTimers.ContainsKey(player))
			{
				HideGameTip(player);
			}

			var screenMessage = GetOrCreateScreenMessage(player);

			screenMessage.Text = message;
			screenMessage.TextAnchor = textAnchor;
			UiManager.AddPlayerUI(screenMessage, player);

			if (time > 0)
			{
				gameTipTimers.Add(player, Utility.Timer.Once(time, () =>
				{
					HideScreenMessage(player);
				}));
			}
		}

		public static void HideScreenMessage(this BasePlayer player)
		{
			if (!screenMessages.ContainsKey(player))
				return;

			UiManager.RemoveUI(screenMessages[player], player);
		}

		private static ScreenMessageUI GetOrCreateScreenMessage(BasePlayer player)
		{
			if (!screenMessages.ContainsKey(player))
				screenMessages.Add(player, UiManager.CreateUI<ScreenMessageUI>());

			return screenMessages[player];
		}

		public static void CloseInventory(this BasePlayer player)
		{
			player.ClientRPC(null, "OnRespawnInformation", new RespawnInformation {spawnOptions = new List<RespawnInformation.SpawnOptions>()}.ToProtoBytes());
		}
	}
}