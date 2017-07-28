using System.Linq;
using UnityEngine;
using Oxide.Plugins.AutoCrafterNamespace;
using Oxide.Plugins.AutoCrafterNamespace.Extensions;
using Oxide.Plugins.AutoCrafterNamespace.JsonConverters;
using Rust;

namespace Oxide.Plugins
{
	[Info("AutoCrafter", "Skipcast", "1.0.0")]
	[Description("A machine that automatically crafts items so the player can do fun stuff instead.")]
	public class AutoCrafter : RustPlugin
	{
		const string UsePermission = "autocrafter.use";
		const float MaxDistance = 10f;

		#region Rust hooks

		private object OnItemCraft(ItemCraftTask task)
		{
			var player = task.owner;
			var crafter = CrafterManager.FindByPlayer(player);

			if (crafter != null)
			{
				crafter.AddCraftTask(task);
				return true;
			}

			return null;
		}

		private void OnLootEntity(BasePlayer player, BaseEntity entity)
		{
			var recycler = entity as Recycler;

			if (recycler == null)
				return;

			var crafter = CrafterManager.GetCrafter(recycler);

			if (crafter == null)
				return;

			// Open the output container instead of the recycler ui.
			NextFrame(() =>
			{
				player.inventory.loot.Clear();
				player.inventory.loot.StartLootingEntity(crafter.Output);
				player.inventory.loot.AddContainer(crafter.OutputInventory);
				player.inventory.loot.SendImmediate();
				player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", crafter.Output.lootPanelName);
			});
		}

		private object OnServerCommand(ConsoleSystem.Arg arg)
		{
			if (arg.Connection == null)
				return null;

			var player = (BasePlayer) arg.Connection.player;

			if (arg.cmd?.FullName == "craft.canceltask")
			{
				int taskid = arg.GetInt(0, -1);

				if (taskid == -1)
					return null;

				var crafter = CrafterManager.FindByPlayer(player);

				if (crafter == null)
					return null;

				if (!crafter.CancelByTaskId(player, taskid))
				{
					arg.ReplyWith("Could not cancel the specified task.");
				}

				return true;
			}

			return null;
		}

		private void Init()
		{
			Utility.Timer = timer;
			Utility.Config = new PluginConfig(); // Todo: load from disk

			Config.Settings.AddConverters();
			permission.RegisterPermission(UsePermission, this);
			lang.RegisterMessages(Lang.DefaultMessages, this, "en");
		}
		
		private void OnServerInitialized()
		{
			Lang.Initialize(this, lang);
			FxManager.Initialize(timer);
			CrafterManager.Initialize(timer);
			CrafterManager.Load();
		}

		private void OnServerSave()
		{
			CrafterManager.Save();
		}

		private void Unload()
		{
			FxManager.Destroy();
			CrafterManager.Destroy();
		}

		private object OnRecycleItem(Recycler recycler, Item item)
		{
			if (CrafterManager.ContainsRecycler(recycler))
			{
				// Prevent recycling
				return true;
			}

			return null;
		}

		protected override void LoadDefaultConfig()
		{

		}

		#endregion

		#region Chat commands

		[ChatCommand("autocrafter")]
		private void ChatCmd_AutoCrafter(BasePlayer player)
		{
			if (!permission.UserHasPermission(player.UserIDString, UsePermission))
			{
				player.TranslatedChatMessage("nopermission");
				return;
			}

			ResearchTable entity;
			if (!FindOwnedEntity(player, out entity))
				return;

			CrafterManager.CreateCrafter(entity);
		}

		private static bool FindOwnedEntity<T>(BasePlayer player, out T entity) where T : BaseEntity
		{
			RaycastHit hit;
			if (!Physics.Raycast(player.eyes.HeadRay(), out hit, MaxDistance, 1 << (int) Layer.Deployed))
			{
				player.TranslatedChatMessage("no-target");
				entity = null;
				return false;
			}

			entity = hit.transform.GetComponentInParent<T>();

			if (entity == null)
			{
				player.TranslatedChatMessage("invalid-target", typeof (T).Name);
				return false;
			}

			// Check that the entity is owned by the player, or if he's admin and the entity was not spawned by the game.
			if (entity.OwnerID != player.userID && (!player.IsAdmin || entity.OwnerID == 0))
			{
				player.TranslatedChatMessage("target-notowned");
				Debug.Log(entity.OwnerID);
				return false;
			}

			return true;
		}

		[ChatCommand("downgradecrafter")]
		private void Chatcmd_DowngradeCrafter(BasePlayer player)
		{
			if (!permission.UserHasPermission(player.UserIDString, UsePermission))
			{
				player.TranslatedChatMessage("nopermission");
				return;
			}

			Recycler recycler;
			if (!FindOwnedEntity(player, out recycler))
			{
				return;
			}

			var crafter = CrafterManager.GetCrafter(recycler);

			if (crafter == null)
			{
				player.TranslatedChatMessage("target-not-crafter");
				return;
			}

			CrafterManager.DestroyCrafter(crafter, true);
		}

		[ChatCommand("iscrafter")]
		private void ChatCmd_IsCrafter(BasePlayer player)
		{
			if (!player.IsAdmin)
				return;

			Recycler recycler;

			if (!FindOwnedEntity(player, out recycler))
				return;

			player.ChatMessage(CrafterManager.ContainsRecycler(recycler).ToString());
		}

		#endregion
	}
}