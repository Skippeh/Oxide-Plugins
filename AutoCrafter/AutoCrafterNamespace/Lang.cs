using System.Collections.Generic;
using Oxide.Core.Plugins;

namespace Oxide.Plugins.AutoCrafterNamespace
{
	public static class Lang
	{
		private static Plugin plugin;
		private static Core.Libraries.Lang lang;

		public static Dictionary<string, string> DefaultMessages { get; private set; } = new Dictionary<string, string>
		{
			{"nopermission", "You don't have permission to use this."},
			{"invalid-target", "The deployable in front of you is not a {0}."},
			{"target-notowned", "You are not the owner of this deployable."},
			{"no-target", "No deployable could be found."},
			{"target-not-crafter", "The recycler in front of you is not a crafter."}
		};

		public static void Initialize(Plugin plugin, Core.Libraries.Lang lang)
		{
			Lang.plugin = plugin;
			Lang.lang = lang;
		}

		public static string Translate(BasePlayer player, string key, params object[] format)
		{
			return string.Format(lang.GetMessage(key, plugin, player.UserIDString), format);
		}
	}
}