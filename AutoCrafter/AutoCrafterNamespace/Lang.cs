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
			{"crafter-inrange", "Crafter active"},
			{"join-message", "This server has the AutoCrafter mod. Type /autocrafter to read more."},
			{"chat-title", "AutoCrafter"},
			{"chat-title-craft", "Crafting"},
			{"chat-title-usage", "Usage"},
			{"chat-title-more", "More"},
			{"chat-description-craft", "How to craft and what the requirements are."},
			{"chat-description-more", "More info that is useful to know but might not be obvious."},
			{"chat-unknown-selection", "Unknown sub menu selection. Please select one of the following:"},
			{
				"chat-default-text", "AutoCrafter allows for automatic crafting, even after you log off or go out to grind or kill nakeds.\n" +
				                     "To learn more, type /autocrafter and then one of the following words:\n"
			},
			{
				"chat-usage-text", "To start crafting something, stand infront of the crafter and start crafting normally.\n" +
				                   "You will know it's working if the machine starts and there's a message at the bottom of the screen."
			},
			{"chat-usage-text-droptop", "It is possible to put items in by dropping them at the top of the machine."},
			{
				"chat-more-text", "- You can put code locks on the crafters.\n" +
				                  "- Destroying it takes 2 c4, or 6 rockets. Melee is not viable.\n" +
				                  "- If destroyed the loot will spill out on the ground.\n" +
				                  "- You can check the HP by hitting it once with a hammer. Continue hitting it if you want to repair."
			},
			{
				"chat-craft-text-top", "To craft, you must first place a research table, then hit it two times with a hammer.\n" +
				                       "The requirements are:"
			},
			{
				"chat-craft-text-bottom", "It is possible to downgrade by hitting it twice again with a hammer. You will receive a full refund.\n" +
				                          "Note that upgrading and downgrading is limited by a 10 minute window from when you first placed the research table or upgraded."
			}
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