using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Oxide.Plugins.AutoCrafterNamespace;
using Oxide.Plugins.AutoCrafterNamespace.Extensions;
using Rust;
using Network = UnityEngine.Network;

namespace Oxide.Plugins
{
	[Info("AutoCrafter", "Skipcast", "1.0.0")]
	[Description("A machine that automatically crafts items so the player can do fun stuff instead.")]
	public class AutoCrafter : RustPlugin
	{
		private readonly List<ItemAmount> UpgradeCost = new List<ItemAmount>();

		/// <summary>
		/// Used for keeping track of when research tables were placed so we know if enough time has passed that upgrading is impossible.
		/// </summary>
		private readonly List<BaseEntity> upgradeableEntities = new List<BaseEntity>();

		private bool serverInitialized = false;

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
			recycler.inventory.Clear();
			recycler.inventory.itemList.Clear();
		}

		void OnEntitySpawned(BaseNetworkable networkable)
		{
			if (!serverInitialized) // Check if server is initialized. This hook tends to call on startup before OnServerInitialized has been called.
				return;

			var entity = networkable as BaseEntity;

			if (entity == null)
				return;

			if (entity.OwnerID == 0)
				return;

			var researchTable = entity as ResearchTable;

			if (researchTable == null)
				return;
			
			upgradeableEntities.Add(researchTable);
			timer.Once(Constants.TimeToUpgrade, () => upgradeableEntities.Remove(researchTable));
		}

		void OnEntityKill(BaseNetworkable entity)
		{
			if (!serverInitialized) // Check if server is initialized. This hook tends to call on startup before OnServerInitialized has been called.
				return;

			var researchTable = entity as ResearchTable;

			if (researchTable != null)
			{
				upgradeableEntities.Remove(researchTable);
			}

			var recycler = entity as Recycler;

			if (recycler == null)
				return;

			var crafter = CrafterManager.GetCrafter(recycler);

			if (crafter == null)
				return;

			CrafterManager.DestroyCrafter(crafter, false, false);
		}

		void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
		{
			float newHealth = entity.Health() - info.damageTypes.Total();

			// Empty recycler inventory if it's about to be killed to avoid dropping hidden items.
			if (newHealth <= 0)
			{
				var recycler = entity as Recycler;

				if (!(entity is Recycler))
					return;

				var crafter = CrafterManager.GetCrafter(recycler);

				if (crafter == null)
					return;

				recycler.inventory.Clear();
				recycler.inventory.itemList.Clear();
			}
		}

		void OnPlayerInput(BasePlayer player, InputState input)
		{
			if (input.WasJustPressed(BUTTON.FIRE_PRIMARY))
			{
				var activeItem = player.GetActiveItem();

				if (activeItem?.info.itemid != -975723312) // Codelock
					return;

				var ray = player.eyes.HeadRay();
				RaycastHit hit;

				if (!Physics.Raycast(ray, out hit, 3, 1 << (int) Layer.Deployed))
					return;

				var recycler = hit.transform.GetComponentInParent<Recycler>();

				if (recycler == null)
					return;

				if (player.IsBuildingBlocked(recycler.ServerPosition, recycler.ServerRotation, recycler.bounds))
					return;

				var crafter = CrafterManager.GetCrafter(recycler);

				if (crafter == null)
					return;

				if (crafter.AddCodelock())
				{
					activeItem.UseItem();
				}
			}
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
			var entity = info.HitEntity as BaseCombatEntity;
			var recycler = entity as Recycler;
			var researchTable = entity as ResearchTable;
			
			if (entity == null || (recycler == null && researchTable == null))
				return null;

			Func<string> hpMessage = () =>
			{
				return "HP: " + entity.Health().ToString("0") + "/" + entity.MaxHealth();
			};

			// Don't allow upgrading/downgrading/repairing if there's less than 8 seconds since the entity was attacked.
			if (entity.SecondsSinceAttacked < 8)
			{
				if (recycler != null && CrafterManager.ContainsRecycler(recycler))
				{
					// Show hp info if repairing is blocked.
					player.ShowScreenMessage(hpMessage(), 2);
				}
				return null;
			}

			// Make sure entity is full health, otherwise repair.
			if (entity.Health() < entity.MaxHealth())
			{
				if (recycler == null)
					return null;

				if (!CrafterManager.ContainsRecycler(recycler))
					return null;

				entity.DoRepair(player);
				player.ShowScreenMessage(hpMessage(), 2);
				return true;
			}

			// Only allow upgrading/downgrading if we have building permission.
			if (player.IsBuildingBlocked(entity.ServerPosition, entity.ServerRotation, entity.bounds))
			{
				if (recycler != null && CrafterManager.ContainsRecycler(recycler)) // Only show hp info if this is a crafter
				{
					// Show hp info if building blocked.
					player.ShowScreenMessage(hpMessage(), 2);
				}

				return null;
			}

			// Check permission and if the entity owner is the current player.
			if (!permission.UserHasPermission(player.UserIDString, Constants.UsePermission) || entity.OwnerID != player.userID)
			{
				player.ShowScreenMessage(hpMessage(), 2);
				return null;
			}
			
			if (researchTable != null) // Upgrade to crafter (if less than 10 minutes since placement)
			{
				if (!upgradeableEntities.Contains(researchTable))
					return null;

				return HandleUpgradeRequest(player, researchTable);
			}

			var crafter = CrafterManager.GetCrafter(recycler);

			if (crafter == null)
				return null;

			if (DateTime.UtcNow - crafter.CreationTime > TimeSpan.FromSeconds(Constants.TimeToUpgrade))
			{
				player.ShowScreenMessage(hpMessage(), 2);
				return null;
			}

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

			serverInitialized = true;
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