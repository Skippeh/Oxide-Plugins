using System.Collections.Generic;
using Oxide.Core.Plugins;

namespace Oxide.Plugins.AutoCrafterNamespace
{
	public static class Lang
	{
		private static Plugin plugin;
		private static Core.Libraries.Lang lang;

		public static readonly Dictionary<string, string> DefaultMessages = new Dictionary<string, string>
		{
			{"nopermission", "You don't have permission to use this."},
			{"invalid-target", "The deployable in front of you is not a {0}."},
			{"target-notowned", "You are not the owner of this deployable."},
			{"no-target", "No deployable could be found."},
			{"target-not-crafter", "The recycler in front of you is not a crafter."},
			{"crafted-items", "Crafted items"},
			{"queue-items", "Queue items"},
			{"item-notfound-skipping-ingredient", "Could not find an item with the shortname '{0}', skipping this ingredient!"},
			{"hit-again-to-repair", "Hit again to repair"},
			{"hp-message", "HP: {0}/{1}"},
			{"ingredients-missing-youneed", "You do not have the required ingredients.\nYou need:"},
			{"hammer-confirm-upgrade", "Hit again to upgrade to a crafter..."},
			{"hammer-confirm-downgrade", "Hit again to downgrade to a research table...\n\nItems will not be lost."},
			{"crafter-inrange", "Crafter active"}
		};

		public static void Initialize(Plugin plugin, Core.Libraries.Lang lang)
		{
			Lang.plugin = plugin;
			Lang.lang = lang;
		}

		public static string Translate(BasePlayer player, string key, params object[] format)
		{
			return string.Format(lang.GetMessage(key, plugin, player?.UserIDString), format);
		}
	}
}