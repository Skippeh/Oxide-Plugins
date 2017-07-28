using System.Collections.Generic;

namespace Oxide.Plugins.AutoCrafterNamespace.Extensions
{
	public static class BasePlayerExtensions
	{
		private static readonly Dictionary<BasePlayer, Timer> gameTipTimers = new Dictionary<BasePlayer, Timer>(); 

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
	}
}