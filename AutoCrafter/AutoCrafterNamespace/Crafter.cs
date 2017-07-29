using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Oxide.Plugins.AutoCrafterNamespace.Extensions;
using Oxide.Plugins.AutoCrafterNamespace.UI;
using Rust;
using UnityEngine;

namespace Oxide.Plugins.AutoCrafterNamespace
{
	public class Crafter
	{
		public delegate void PlayerEnterDelegate(Crafter crafter, BasePlayer player);
		public delegate void PlayerLeaveDelegate(Crafter crafter, BasePlayer player);

		public class CraftTask
		{
			[JsonIgnore] public ItemBlueprint Blueprint;
			public int Amount;
			public ulong SkinID;

			[JsonProperty("ItemID")]
			private int _itemid => Blueprint.targetItem.itemid;

			/// <summary>
			/// Number of seconds this has been crafting for.
			/// </summary>
			public float Elapsed;

			public CraftTask(ItemBlueprint blueprint, int amount, ulong skinId)
			{
				Blueprint = blueprint;
				Amount = amount;
				SkinID = skinId;
			}
		}

		[JsonIgnore]
		public Recycler Recycler { get; private set; }

		[JsonIgnore]
		public Vector3 Position => Recycler.ServerPosition;

		/// <summary>
		/// Gets a list of players that are near the crafter, and should receive craft queue updates and be able to add/delete from queue
		/// </summary>
		[JsonIgnore]
		public List<BasePlayer> NearbyPlayers { get; private set; } = new List<BasePlayer>();

		public List<CraftTask> CraftingTasks { get; private set; } = new List<CraftTask>();

		[JsonIgnore]
		public ItemContainer OutputInventory => outputInventory;

		[JsonIgnore]
		public DroppedItemContainer Output => outputContainer;

		/// <summary>
		/// Gets the codelock on this crafter. May be null.
		/// </summary>
		[JsonIgnore]
		public CodeLock CodeLock { get; private set; }

		#region Json exclusive properties for saving/loading

		[JsonProperty("Code")]
		private string _code => CodeLock?.code;

		[JsonProperty("GuestCode")]
		private string _guestCode => CodeLock?.guestCode;

		[JsonProperty("AuthedPlayers")]
		private List<ulong> _authedPlayers => CodeLock?.whitelistPlayers;

		[JsonProperty("GuestPlayers")]
		private List<ulong> _guestPlayers => CodeLock?.guestPlayers;

		[JsonProperty("HasCodeLock")]
		private bool _hasCodelock => CodeLock != null;

		[JsonProperty("IsLocked")]
		private bool _locked => CodeLock?.IsLocked() ?? false;

		[JsonProperty("OutputItems")]
		private object _outputItems => OutputInventory.itemList.Select(item =>
		{
			if (item.info.itemid == 98228420) // Hidden item
				return null;

			return new
			{
				item.position,
				item.info.itemid,
				item.amount,
				item.skin
			};
		}).Where(obj => obj != null).ToList();

		[JsonProperty("On")]
		private bool _turnedOn => Recycler.IsOn();

		#endregion

		public event PlayerEnterDelegate PlayerEnter;
		public event PlayerLeaveDelegate PlayerLeave;

		/// <summary>
		/// Gets or sets the time this was created in UTC.
		/// </summary>
		public DateTime CreationTime { get; set; }

		// Lookup table for players on each crafting task.
		private readonly Dictionary<BasePlayer, Dictionary<CraftTask, int>> taskLookup = new Dictionary<BasePlayer, Dictionary<CraftTask, int>>();
		
		private DroppedItemContainer outputContainer;
		private ItemContainer outputInventory;
		private readonly Timer resetDespawnTimer;
		private float nextPickup = Time.time;
		private const float pickupDelay = 0.5f;
		private float nextUiUpdate = Time.time;
		private const float uiUpdateDelay = 0.5f;

		/// <param name="recycler">The recycler entity we're "overwriting".</param>
		public Crafter(Recycler recycler)
		{
			CreationTime = DateTime.UtcNow;

			Recycler = recycler;

			CreateOutputContainer();

			// Reset despawn timer on loot bag once per minute.
			resetDespawnTimer = Utility.Timer.Every(60, () =>
			{
				if (!outputContainer.IsDestroyed)
					outputContainer.ResetRemovalTime();
			});

			recycler.gameObject.AddComponent<GroundWatch>();
			recycler.gameObject.AddComponent<DestroyOnGroundMissing>();

			recycler.repair.enabled = true;
			recycler.repair.itemTarget = ItemManager.FindItemDefinition("wall.frame.shopfront.metal");
			
			// Set health to 1000
			Recycler._maxHealth = 1000;
			Recycler.health = recycler.MaxHealth();

			// Set up damage protection
			Recycler.baseProtection.density = 4;
			
			for (int i = 0; i < Recycler.baseProtection.amounts.Length; ++i)
			{
				Recycler.baseProtection.amounts[i] = Utility.Config.CrafterProtectionProperties[i];
			}
		}

		private void CreateOutputContainer()
		{
			Vector3 outputPosition = Position + (Recycler.transform.forward * 0f) + (Recycler.transform.up * 0.72f) + (Recycler.transform.right * -0.25f);
			Quaternion outputRotation = Recycler.ServerRotation * Quaternion.Euler(90, 0, 0);

			outputContainer = (DroppedItemContainer) GameManager.server.CreateEntity(Constants.ItemDropPrefab, outputPosition, outputRotation);
			outputContainer.playerName = "Crafted items";
			outputContainer.enableSaving = false;
			outputContainer.ShowHealthInfo = true;
			outputContainer.Spawn();

			outputContainer.TakeFrom(new ItemContainer());
			outputInventory = (ItemContainer) typeof (DroppedItemContainer).GetField("inventory", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(outputContainer);

			if (outputInventory == null)
			{
				throw new NotImplementedException("Could not find private 'inventory' field in type DroppedItemContainer.");
			}

			// Add a hidden inventory slot in output container to prevent it from despawning when closing empty loot.
			outputInventory.capacity = 37;
			var item = ItemManager.Create(ItemManager.FindItemDefinition("gears"), 1);
			item.MoveToContainer(outputInventory, outputInventory.capacity - 1);

			var rigidBody = outputContainer.GetComponent<Rigidbody>();
			rigidBody.isKinematic = true; // Prevent physics from moving the container.
		}

		public void Tick(float elapsed)
		{
			if (outputContainer.IsDestroyed)
			{
				CrafterManager.DestroyCrafter(this, false, destroyOutputContainer: false); // Don't destroy output container because it's already destroyed.
			}

			ProcessQueue(elapsed);
			ProcessWorldItems();
			ProcessNearbyPlayers();
			ProcessUiUpdates();
		}
		
		/// <param name="unloading">Specify true if the plugin is unloading.</param>
		public void Destroy(bool destroyOutputContainer, bool unloading = false)
		{
			resetDespawnTimer.DestroyToPool();

			foreach (var player in NearbyPlayers)
			{
				OnPlayerLeave(player);
			}

			if (!unloading)
			{
				// Drop queue items
				if (CraftingTasks.Count > 0)
				{
					var container = new ItemContainer();
					container.ServerInitialize(null, 36);

					foreach (var task in CraftingTasks)
					{
						foreach (var ingredient in task.Blueprint.ingredients)
						{
							var item = ItemManager.CreateByItemID(ingredient.itemid, (int) ingredient.amount * task.Amount);

							if (!item.MoveToContainer(container))
								item.Drop(Position + Recycler.transform.up * 1.25f, Recycler.GetDropVelocity(), Recycler.ServerRotation);
						}
					}

					var droppedContainer = container.Drop(Constants.ItemDropPrefab, Position + Recycler.transform.up * 1.25f, Recycler.ServerRotation);
					droppedContainer.playerName = "Queue items";
				}
			}

			Recycler.Kill();
			CodeLock?.Kill();

			if (!outputContainer.IsDestroyed)
			{
				// Remove rock from output container that keeps it from despawning when emptied
				OutputInventory.GetSlot(OutputInventory.capacity - 1).Remove();

				// Force kill output bag if there's nothing in it.
				if (!destroyOutputContainer && OutputInventory.AnyItems())
				{
					// Enable physics on output container
					Output.GetComponent<Rigidbody>().isKinematic = false;
				}
				else
				{
					outputContainer.Kill();
				}
			}
		}

		private void ProcessQueue(float elapsed)
		{
			if (!Recycler.IsOn() || CraftingTasks.Count <= 0)
				return;

			var currentTask = CraftingTasks.FirstOrDefault();

			if (currentTask != null)
			{
				currentTask.Elapsed += elapsed;

				if (currentTask.Elapsed >= currentTask.Blueprint.time)
				{
					var item = ItemManager.CreateByItemID(currentTask.Blueprint.targetItem.itemid, currentTask.Blueprint.amountToCreate, currentTask.SkinID);

					if (!GiveItem(item))
					{
						item.Drop(Recycler.GetDropPosition(), Recycler.GetDropVelocity());
						Recycler.StopRecycling();
					}

					currentTask.Amount -= 1;
					currentTask.Elapsed -= currentTask.Blueprint.time;

					if (currentTask.Amount <= 0)
					{
						// Remove from ui
						foreach (var player in NearbyPlayers)
						{
							SendRemoveCraftingTask(player, currentTask);
						}

						CraftingTasks.RemoveAt(0);

						// Stop recycler if there's nothing more to craft.
						if (CraftingTasks.Count <= 0)
						{
							Recycler.StopRecycling();
						}
					}
				}
			}
		}

		private void ProcessWorldItems()
		{
			if (Utility.Config.ScanForWorldItems && Recycler.IsOn())
			{
				List<BaseEntity> entities = new List<BaseEntity>();

				Vector3 position = Position + (Recycler.transform.up * 1.5f) + (Recycler.transform.forward * 0.1f) + (Recycler.transform.right * -0.25f);
				float radius = 0.3f;

				Vis.Entities(position, radius, entities);
				entities = entities.Where(ent => ent.GetComponent<WorldItem>() != null).ToList();

				if (nextPickup <= Time.time)
				{
					foreach (var entity in entities)
					{
						if (nextPickup > Time.time)
							break;

						var worldItem = (WorldItem) entity;

						/*if (worldItem.item.MoveToContainer(outputInventory))
						{
							nextPickup = Time.time + pickupDelay;
							FxManager.PlayFx(worldItem.ServerPosition, Constants.StackSoundFxPrefab);
						}*/

						bool partiallyInserted = false;

						for (int i = 0; i < outputInventory.capacity - 1; ++i)
						{
							var slot = outputInventory.GetSlot(i);
							if (slot == null)
							{
								worldItem.item.MoveToContainer(outputInventory, i);
								partiallyInserted = true;
								break;
							}

							if (slot.info == worldItem.item.info && slot.skin == worldItem.item.skin && slot.amount < slot.info.stackable)
							{
								int available = slot.info.stackable - slot.amount;
								int toMove = Math.Min(available, worldItem.item.amount);
								worldItem.item.amount -= toMove;
								slot.amount += toMove;

								slot.MarkDirty();

								partiallyInserted = true;

								if (worldItem.item.amount <= 0)
								{
									worldItem.item.Remove();
									worldItem.Kill();
									break;
								}
							}
						}

						if (partiallyInserted)
						{
							FxManager.PlayFx(worldItem.ServerPosition, Constants.StackSoundFxPrefab);
						}
					}
				}
			}
		}

		private void ProcessNearbyPlayers()
		{
			List<BasePlayer> nearPlayers = new List<BasePlayer>();

			Vector3 checkPosition = Position + Recycler.transform.up * 0.75f + Recycler.transform.forward * 1f + Recycler.transform.right * -0.2f;
			float checkRadius = Constants.CrafterNearRadius;
			Vis.Entities(checkPosition, checkRadius, nearPlayers);

			var previousNearbyPlayers = NearbyPlayers.ToList(); // Nearby players last tick
			
			// Keep all players that are the following:
			// - Alive and not sleeping
			// - Can see the recycler from their position, aka not behind a wall or anything
			nearPlayers = nearPlayers.Where(plr => plr.IsAlive() && !plr.IsSleeping() && PlayerCanAccess(plr) && Recycler.IsVisible(plr.ServerPosition)).ToList();

			var playersLeaving = previousNearbyPlayers.Where(plr => !nearPlayers.Contains(plr)).ToList();
			var playersEntering = nearPlayers.Where(plr => !previousNearbyPlayers.Contains(plr)).ToList();

			foreach (var player in playersLeaving)
			{
				NearbyPlayers.Remove(player);
				OnPlayerLeave(player);
			}

			foreach (var player in playersEntering)
			{
				NearbyPlayers.Add(player);
				OnPlayerEnter(player);
			}
			
			/*foreach (var player in BasePlayer.activePlayerList)
			{
				player.SendConsoleCommand("ddraw.sphere", 0.5f, Color.red, checkPosition, checkRadius);
			}*/
		}

		private void ProcessUiUpdates()
		{
			if (!(Time.time > nextUiUpdate))
				return;

			nextUiUpdate = Time.time + uiUpdateDelay;

			foreach (var player in NearbyPlayers)
			{
				SendCraftingListUpdate(player);
			}
		}

		/// <summary>
		/// Called when a player comes into range of this crafter.
		/// </summary>
		private void OnPlayerEnter(BasePlayer player)
		{
			if (CraftingTasks.Count > 0)
			{
				SendCraftingList(player);
			}

			PlayerEnter?.Invoke(this, player);
		}

		/// <summary>
		/// Called when a player goes out of range of this crafter.
		/// </summary>
		private void OnPlayerLeave(BasePlayer player)
		{
			SendClearCraftingList(player);
			PlayerLeave?.Invoke(this, player);
		}

		private void SendCraftingList(BasePlayer player)
		{
			foreach (var task in CraftingTasks)
			{
				SendAddCraftingTask(player, task);
			}
		}

		private void SendCraftingListUpdate(BasePlayer player)
		{
			foreach (var task in CraftingTasks)
			{
				SendUpdateCraftingTask(player, task);
			}
		}

		private void SendAddCraftingTask(BasePlayer player, CraftTask task)
		{
			var crafting = player.inventory.crafting;
			crafting.taskUID++;
			player.Command("note.craft_add", crafting.taskUID, task.Blueprint.targetItem.itemid);

			var dict = GetTaskLookupDict(player);
			dict.Add(task, crafting.taskUID);
		}

		private void SendUpdateCraftingTask(BasePlayer player, CraftTask task)
		{
			var lookup = GetTaskLookupDict(player);
			int taskUID = lookup[task];
			player.Command("note.craft_start", taskUID, task.Blueprint.time - task.Elapsed, task.Amount);
		}

		private void SendClearCraftingList(BasePlayer player)
		{
			var lookup = GetTaskLookupDict(player);
			foreach (var kv in lookup.ToDictionary(kv => kv.Key, kv => kv.Value))
			{
				SendRemoveCraftingTask(player, kv.Key);
			}
		}

		private void SendRemoveCraftingTask(BasePlayer player, CraftTask task)
		{
			var lookup = GetTaskLookupDict(player);
			int taskUID = lookup[task];
			player.Command("note.craft_done", taskUID, 0);
			lookup.Remove(task);
		}

		private Dictionary<CraftTask, int> GetTaskLookupDict(BasePlayer player)
		{
			if (taskLookup.ContainsKey(player))
				return taskLookup[player];

			var dictionary = new Dictionary<CraftTask, int>();
			taskLookup.Add(player, dictionary);
			return dictionary;
		}

		#region Public api methods

		public void AddCraftTask(ItemBlueprint blueprint, int amount, ulong skinId = 0)
		{
			// Merge with current craft queue if the current crafting item is the same blueprint and skin id.
			if (CraftingTasks.Count > 0)
			{
				var currentTask = CraftingTasks.First();

				if (currentTask.Blueprint == blueprint && currentTask.SkinID == skinId)
				{
					currentTask.Amount += amount;

					// Send new amount to all players
					foreach (var player in NearbyPlayers)
					{
						SendUpdateCraftingTask(player, currentTask);
					}

					return currentTask;
				}
			}

			var craftTask = new CraftTask(blueprint, amount, skinId);
			CraftingTasks.Add(craftTask);

			foreach (var player in NearbyPlayers)
			{
				SendAddCraftingTask(player, craftTask);
			}

			// Turn on recycler
			if (startRecycler && !Recycler.IsOn())
			{
				Recycler.StartRecycling();
			}

			return craftTask;
		}

		public void AddCraftTask(ItemCraftTask task)
		{
			AddCraftTask(task.blueprint, task.amount, (ulong) task.skinID);
		}

		/// <summary>
		/// Puts the given item in the output container.
		/// </summary>
		public bool GiveItem(Item item)
		{
			return item.MoveToContainer(outputInventory);
		}

		/// <summary>
		/// Cancels the given craft task. Returns true if the task was found and cancelled.
		/// </summary>
		/// <param name="refundTo">The refunded items will be added to this players inventory.</param>
		public bool CancelTask(CraftTask task, BasePlayer refundTo)
		{
			CraftingTasks.Remove(task);

			foreach (var player in NearbyPlayers)
			{
				SendRemoveCraftingTask(player, task);
			}

			var refundItems = task.Blueprint.ingredients;

			foreach (var itemAmount in refundItems)
			{
				float amount = itemAmount.amount * task.Amount; // Use float to be forward compatible (itemAmount.amount is float).

				var item = ItemManager.CreateByItemID(itemAmount.itemid, (int) amount);
				refundTo.GiveItem(item);
			}

			// Stop recycler if crafting queue is empty.
			if (CraftingTasks.Count <= 0)
			{
				Recycler.StopRecycling();
			}

			return true;
		}

		/// <summary>
		/// Cancels the craft task that is associated with the given taskid.
		/// </summary>
		/// <param name="player">The player that the taskid belongs to.</param>
		/// <param name="taskid">The craft taskid.</param>
		public bool CancelByTaskId(BasePlayer player, int taskid)
		{
			if (!PlayerCanAccess(player))
				return false;

			var lookup = GetTaskLookupDict(player);
			var task = lookup.FirstOrDefault(kv => kv.Value == taskid);

			if (task.Key == null)
			{
				return false;
			}

			return CancelTask(task.Key, player);
		}

		/// <summary>
		/// Replaces the recycler with a research table and then destroys the crafter. Default behaviour will drop the output loot onto the ground.
		/// </summary>
		public void Downgrade(bool destroyOutputContainer = false, bool unloading = false)
		{
			var researchTableEntity = GameManager.server.CreateEntity(Constants.DeployedResearchTablePrefab, Recycler.ServerPosition, Recycler.ServerRotation);
			var researchTable = researchTableEntity.GetComponent<ResearchTable>();
			researchTable.OwnerID = Recycler.OwnerID; // Copy ownership to research table.
			researchTable.Spawn();

			Destroy(destroyOutputContainer, unloading);
		}

		/// <summary>
		/// Adds a codelock to this crafter.
		/// </summary>
		public bool AddCodeLock()
		{
			if (CodeLock != null)
				return false;

			var instance = (CodeLock) GameManager.server.CreateEntity(Constants.CodelockPrefab, Position + (Recycler.transform.forward * 0.41f) + (Recycler.transform.up * 0.747f) + (Recycler.transform.right * 0.273f), Recycler.ServerRotation * Quaternion.Euler(0, -90, 0));
			instance.enableSaving = false;
			instance.Spawn();
			CodeLock = instance;
			
			return true;
		}

		/// <summary>
		/// Returns true if the player has authed on codelock if there is one and it's locked.
		/// </summary>
		public bool PlayerCanAccess(BasePlayer player)
		{
			if (!IsLocked())
				return true;
			
			return CodeLock.whitelistPlayers.Contains(player.userID) || CodeLock.guestPlayers.Contains(player.userID);
		}

		public void PlayLockedSound()
		{
			FxManager.PlayFx(CodeLock?.ServerPosition ?? Position, "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab");
		}

		public void PlayAccessSound()
		{
			FxManager.PlayFx(CodeLock?.ServerPosition ?? Position, "assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab");
		}

		public bool IsLocked()
		{
			return CodeLock != null && CodeLock.IsLocked();
		}

		#endregion
	}
}