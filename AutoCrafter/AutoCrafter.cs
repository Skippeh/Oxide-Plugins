using System;
using System.Collections.Generic;
using System.Linq;
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

		/// <summary>
		/// List of players that have received the first join message.
		/// </summary>
		private List<ulong> introducedPlayers = new List<ulong>(); 

		private bool serverInitialized = false;

		#region Rust hooks

		private object OnItemCraft(ItemCraftTask task)
		{
			var player = task.owner;
			var crafter = CrafterManager.FindByPlayer(player);

			if (crafter != null && crafter.PlayerCanAccess(player))
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
				if (!crafter.PlayerCanAccess(player))
				{
					crafter.PlayLockedSound();
					player.CloseInventory();
					return;
				}

				player.inventory.loot.Clear();
				player.inventory.loot.StartLootingEntity(crafter.Output);
				player.inventory.loot.AddContainer(crafter.OutputInventory);
				player.inventory.loot.SendImmediate();
				player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", crafter.Output.lootPanelName);

				if (crafter.IsLocked())
					crafter.PlayAccessSound();
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

				if (!Physics.Raycast(ray, out hit, 2.2f, 1 << (int) Layer.Deployed))
					return;

				var recycler = hit.transform.GetComponentInParent<Recycler>();

				if (recycler == null)
					return;

				if (player.IsBuildingBlocked(recycler.ServerPosition, recycler.ServerRotation, recycler.bounds))
					return;

				var crafter = CrafterManager.GetCrafter(recycler);

				if (crafter == null)
					return;

				if (crafter.AddCodeLock())
				{
					activeItem.UseItem();
					FxManager.PlayFx(crafter.CodeLock.ServerPosition, Constants.CodelockPlaceSoundPrefab);
				}
			}
		}

		// Show message if enabled
		void OnPlayerSpawn(BasePlayer player)
		{
			if (!serverInitialized) // Check if server is initialized. This hook tends to call on startup before OnServerInitialized has been called.
				return;

			if (!Utility.Config.ShowPlayerInstructionsOnFirstJoin)
				return;

			ShowJoinMessage(player);
		}

		// Make sure nothing is clipping into recycler. Pretty hacky method, but the recycler doesn't block things like other deployables.
		object CanBuild(Planner plan, Construction prefab, Vector3 position)
		{
			BasePlayer player = plan.GetOwnerPlayer();
			
			List<Recycler> recyclers = new List<Recycler>();
			Vis.Entities(position, prefab.bounds.size.magnitude / 3f, recyclers, 1 << (int) Layer.Deployed);
			
			if (recyclers.Count <= 0)
			{
				return null;
			}
			
			return true;
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
				return Lang.Translate(player, "hp-message", entity.Health(), entity.MaxHealth());
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

			if (!lastHammerHit.ContainsKey(player))
				lastHammerHit[player] = 0;

			((DecayEntity) entity).DecayTouch(); // Reset decay

			// Make sure entity is full health, otherwise repair.
			if (entity.Health() < entity.MaxHealth())
			{
				if (recycler == null)
					return null;

				if (!CrafterManager.ContainsRecycler(recycler))
					return null;

				if (Time.time - lastHammerHit[player] > Constants.HammerConfirmTime)
				{
					player.ShowScreenMessage(hpMessage() + "\n\n" + Lang.Translate(player, "hit-again-to-repair"), Constants.HammerConfirmTime);
					lastHammerHit[player] = Time.time;
					return true;
				}

				lastHammerHit[player] = Time.time;
				player.HideScreenMessage();
				entity.DoRepair(player);
				player.ShowScreenMessage(hpMessage(), 2);

				// Reset last hammer hit so that the player won't accidentally downgrade/upgrade with the next hammer hit.
				if (entity.Health() >= entity.MaxHealth())
				{
					lastHammerHit[player] = 0;
				}

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

			// Check if player has authed on potential codelock.
			if (!crafter.PlayerCanAccess(player))
			{
				crafter.PlayLockedSound();
				return true;
			}

			return HandleDowngradeRequest(player, crafter);
		}

		protected override void LoadDefaultConfig()
		{
			Utility.Config = new PluginConfig();
			Utility.Config.UpgradeCost.AddRange(new List<PluginConfig.ItemAmount>
			{
				new PluginConfig.ItemAmount("metal.refined", 25),
				new PluginConfig.ItemAmount("metal.fragments", 500),
				new PluginConfig.ItemAmount("techparts", 3),
				new PluginConfig.ItemAmount("gears", 3)
			});
		}

		private void Loaded()
		{
			if (Utility.Config == null)
			{
				Utility.Config = Config.ReadObject<PluginConfig>();
				Config.WriteObject(Utility.Config); // Save any new or removed properties.
			}
			else
			{
				Config.WriteObject(Utility.Config);
			}
		}
		
		private void OnServerInitialized()
		{
			Utility.Timer = timer;

			Config.Settings.AddConverters();
			permission.RegisterPermission(Constants.UsePermission, this);
			lang.RegisterMessages(Lang.DefaultMessages, this, "en");

			UiManager.Initialize();
			Lang.Initialize(this, lang);
			FxManager.Initialize();

			foreach (var itemAmount in Utility.Config.UpgradeCost)
			{
				var itemDef = ItemManager.FindItemDefinition(itemAmount.Shortname);

				if (itemDef == null)
				{
					PrintError(Lang.Translate(null, "item-notfound-skipping-ingredient", itemAmount.Shortname));
					continue;
				}

				UpgradeCost.Add(new ItemAmount(itemDef, itemAmount.Amount));
			}
			
			CrafterManager.Initialize();
			CrafterManager.Load();

			if (Utility.Config.ShowPlayerInstructionsOnFirstJoin)
			{
				// Load previously introduced players
				introducedPlayers = Core.Interface.Oxide.DataFileSystem.ReadObject<List<ulong>>("AutoCrafter/IntroducedPlayers");

				foreach (var player in BasePlayer.activePlayerList)
				{
					ShowJoinMessage(player);
				}
			}

			serverInitialized = true;
		}

		private void OnServerSave()
		{
			CrafterManager.Save();

			if (Utility.Config.ShowPlayerInstructionsOnFirstJoin)
			{
				Core.Interface.Oxide.DataFileSystem.WriteObject("AutoCrafter/IntroducedPlayers", introducedPlayers);
			}
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

		object OnRecyclerToggle(Recycler recycler, BasePlayer player)
		{
			var crafter = CrafterManager.GetCrafter(recycler);

			if (crafter == null)
				return null;

			if (!crafter.PlayerCanAccess(player))
			{
				crafter.PlayLockedSound();
				return true;
			}

			return null;
		}

		#endregion

		#region Chat commands

		[ChatCommand("autocrafter")]
		private void ChatCmd_Autocrafter(BasePlayer player, string command, string[] args)
		{
			string submenu = args.FirstOrDefault();
			StringBuilder message = new StringBuilder();
			string title = null;

			Action appendMenus = () =>
			{
				message.AppendLine("- craft : " + Lang.Translate(player, "chat-description-craft"));
				message.Append("- more : " + Lang.Translate(player, "chat-description-more"));
			};

			switch (submenu)
			{
				default:
				{
					message.Append(Lang.Translate(player, "chat-unknown-selection") + "\n");
					appendMenus();
					break;
				}
				case null:
				{
					message.AppendLine(Lang.Translate(player, "chat-default-text"));
					appendMenus();
					break;
				}
				case "craft":
				{
					title = Lang.Translate(player, "chat-title-craft");
					message.AppendLine(Lang.Translate(player, "chat-craft-text-top"));

					foreach (var itemAmount in UpgradeCost)
					{
						message.AppendLine("- " + itemAmount.amount + "x " + itemAmount.itemDef.displayName.english);
					}

					message.AppendLine();
					message.AppendLine(Lang.Translate(player, "chat-craft-text-bottom"));
					
					break;
				}
				case "usage":
				{
					title = Lang.Translate(player, "chat-title-usage");
					message.AppendLine(Lang.Translate(player, "chat-usage-text"));

					if (Utility.Config.ScanForWorldItems)
					{
						message.AppendLine(Lang.Translate(player, "chat-usage-text-droptop"));
					}
					break;
				}
				case "more":
				{
					title = Lang.Translate(player, "chat-title-more");
					message.AppendLine(Lang.Translate(player, "chat-more-text"));
					break;
				}
			}

			message.Insert(0, "<size=20>" + Lang.Translate(player, "chat-title") + (title != null ? (" - " + title) : "") + "</size>\n");

			player.ChatMessage(message.ToString());
		}

		#endregion

		// For keeping track of how long ago they requested with the previous hammer hit. Used for confirming by hitting twice with hammer to upgrade, downgrade, or repair.
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

					player.ShowScreenMessage(Lang.Translate(player, "ingredients-missing-youneed") + "\n" + ingredientsStr, 10, TextAnchor.MiddleLeft);
					return true;
				}
			}

			float lastHit = lastHammerHit[player];
			
			if (Time.time - lastHit > Constants.HammerConfirmTime) // Confirm the upgrade
			{
				lastHammerHit[player] = Time.time;
				player.ShowScreenMessage(Lang.Translate(player, "hammer-confirm-upgrade"), Constants.HammerConfirmTime);
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
			float lastRequest = lastHammerHit[player];

			if (Time.time - lastRequest > Constants.HammerConfirmTime) // Confirm the downgrade
			{
				string message = Lang.Translate(player, "hp-message", crafter.Recycler.Health(), crafter.Recycler.MaxHealth());
				message += "\n\n" + Lang.Translate(player, "hammer-confirm-downgrade");

				lastHammerHit[player] = Time.time;
				player.ShowScreenMessage(message, Constants.HammerConfirmTime);
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

			// Refund codelock if one is attached
			if (crafter.CodeLock != null)
			{
				var item = ItemManager.Create(ItemManager.FindItemDefinition("lock.code"));
				player.GiveItem(item);
			}

			return true;
		}

		private void ShowJoinMessage(BasePlayer player)
		{
			if (introducedPlayers.Contains(player.userID))
				return;

			string message = Lang.Translate(player, "join-message");

			if (Utility.Config.ShowInstructionsAsGameTip)
				player.ShowGameTip(message, 10f);
			else
				player.ChatMessage(message);

			introducedPlayers.Add(player.userID);
		}
	}
}