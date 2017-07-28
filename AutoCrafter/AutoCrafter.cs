using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Oxide.Plugins.AutoCrafterNamespace;
using Oxide.Plugins.AutoCrafterNamespace.Extensions;

namespace Oxide.Plugins
{
	[Info("AutoCrafter", "Skipcast", "1.0.0")]
	[Description("A machine that automatically crafts items so the player can do fun stuff instead.")]
	public class AutoCrafter : RustPlugin
	{
		private readonly List<ItemAmount> UpgradeCost = new List<ItemAmount>();

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

		void OnEntityGroundMissing(BaseEntity entity)
		{
			var recycler = entity as Recycler;

			if (recycler == null)
				return;

			var crafter = CrafterManager.GetCrafter(recycler);

			if (crafter == null)
				return;

			// Empty recycler, otherwise the hidden items inside it will drop into the world.
			foreach (var item in recycler.inventory.itemList)
			{
				item.Remove();
			}

			recycler.inventory.itemList.Clear();
		}

		void OnEntityKill(BaseNetworkable entity)
		{
			var recycler = entity as Recycler;

			if (recycler == null)
				return;

			var crafter = CrafterManager.GetCrafter(recycler);

			if (crafter == null)
				return;

			CrafterManager.DestroyCrafter(crafter, false, false);
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

				var crafters = CrafterManager.FindAllByPlayer(player);

				foreach (var crafter in crafters)
				{
					if (crafter.CancelByTaskId(player, taskid))
						return true;
				}

				return null;
			}

			return null;
		}

		private object OnHammerHit(BasePlayer player, HitInfo info)
		{
			BaseEntity entity = info.HitEntity;
			var recycler = entity as Recycler;
			var researchTable = entity as ResearchTable;

			if ((recycler == null && researchTable == null) || !permission.UserHasPermission(player.UserIDString, Constants.UsePermission) || player.IsBuildingBlocked(entity.ServerPosition, entity.ServerRotation, entity.bounds))
				return null;

			if (entity.OwnerID != player.userID)
				return null;

			if (researchTable != null) // Upgrade to crafter (if less than 10 minutes since placement)
			{
				return HandleUpgradeRequest(player, researchTable);
			}

			var crafter = CrafterManager.GetCrafter(recycler);

			if (crafter == null)
				return null;

			return HandleDowngradeRequest(player, crafter);
		}

		private void Init()
		{
			Utility.Timer = timer;
			Utility.Config = new PluginConfig(); // Todo: load from disk

			foreach (var itemAmount in Utility.Config.UpgradeCost)
			{
				var itemDef = ItemManager.FindItemDefinition(itemAmount.Shortname);

				if (itemDef == null)
				{
					PrintError("Could not find item with the shortname: '" + itemAmount.Shortname + "', skipping this ingredient!");
					continue;
				}

				UpgradeCost.Add(new ItemAmount(itemDef, itemAmount.Amount));
			}

			Config.Settings.AddConverters();
			permission.RegisterPermission(Constants.UsePermission, this);
			lang.RegisterMessages(Lang.DefaultMessages, this, "en");
		}
		
		private void OnServerInitialized()
		{
			UiManager.Initialize();
			Lang.Initialize(this, lang);
			FxManager.Initialize();
			CrafterManager.Initialize();
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
			UiManager.Destroy();
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
		
		// For keeping track of how long ago they requested with the previous hammer hit. Used for confirming by hitting twice with hammer to upgrade or downgrade.
		private readonly Dictionary<BasePlayer, float> lastHammerHit = new Dictionary<BasePlayer, float>();

		// Return value:
		// - null = continue with default behaviour of hammer hit
		// - anything else: prevent default behaviour.
		private object HandleUpgradeRequest(BasePlayer player, ResearchTable researchTable)
		{
			// Make sure research table is full health, or allow it if repairing is disabled.
			if (researchTable.Health() < researchTable.MaxHealth() && researchTable.repair.enabled)
				return null;

			// Don't allow upgrading if there's less than 8 seconds since the research table was attacked.
			if (researchTable.SecondsSinceAttacked < 8)
				return null;

			if (UpgradeCost.Count > 0)
			{
				if (!player.CanCraft(UpgradeCost))
				{
					StringBuilder builder = new StringBuilder();

					foreach (var ingredient in UpgradeCost)
					{
						builder.AppendLine("- x" + ingredient.amount.ToString("0") + " " + ingredient.itemDef.displayName.english);
					}

					string ingredientsStr = builder.ToString();

					player.ShowScreenMessage("You do not have the required ingredients.\nYou need:\n" + ingredientsStr, 10, TextAnchor.MiddleLeft);
					return true;
				}
			}

			float lastHit;
			lastHammerHit.TryGetValue(player, out lastHit);
			
			if (Time.time - lastHit > Constants.HammerConfirmTime) // Confirm the upgrade
			{
				lastHammerHit[player] = Time.time;
				player.ShowScreenMessage("Hit again to upgrade to a crafter...", Constants.HammerConfirmTime);
				return true;
			}
			
			lastHammerHit[player] = 0; // Reset time

			foreach (var ingredient in UpgradeCost)
			{
				List<Item> takenItems = new List<Item>();
				player.inventory.Take(takenItems, ingredient.itemid, (int)ingredient.amount);
			}

			CrafterManager.CreateCrafter(researchTable);
			FxManager.PlayFx(researchTable.ServerPosition, Constants.UpgradeTopTierFxPrefab);
			player.HideScreenMessage();
			return true;
		}

		// Return value:
		// - null = continue with default behaviour of hammer hit
		// - anything else: prevent default behaviour.
		private object HandleDowngradeRequest(BasePlayer player, Crafter crafter)
		{
			float lastRequest;
			lastHammerHit.TryGetValue(player, out lastRequest);

			if (Time.time - lastRequest > Constants.HammerConfirmTime) // Confirm the downgrade
			{
				lastHammerHit[player] = Time.time;
				player.ShowScreenMessage("Hit again to downgrade to a research table...\n\nItems will not be lost.", Constants.HammerConfirmTime);
				return true;
			}
			
			lastHammerHit[player] = 0; // Reset time
			
			CrafterManager.DestroyCrafter(crafter, true, false);
			FxManager.PlayFx(crafter.Position, Constants.UpgradeMetalFxPrefab);
			player.HideScreenMessage();

			foreach (var itemAmount in UpgradeCost)
			{
				player.GiveItem(ItemManager.CreateByItemID(itemAmount.itemid, (int) itemAmount.amount));
			}

			return true;
		}
	}
}