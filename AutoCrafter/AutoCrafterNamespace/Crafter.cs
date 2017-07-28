﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Rust;
using UnityEngine;

namespace Oxide.Plugins.AutoCrafterNamespace
{
	public class Crafter
	{
		public class CraftTask
		{
			public readonly int TaskID;
			public ItemBlueprint Blueprint;
			public int Amount;
			public ulong SkinID;

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

		// Lookup table for players on each crafting task.
		private readonly Dictionary<BasePlayer, Dictionary<CraftTask, int>> taskLookup = new Dictionary<BasePlayer, Dictionary<CraftTask, int>>();
		
		private readonly DroppedItemContainer outputContainer;
		private readonly ItemContainer outputInventory;
		private readonly Timer resetDespawnTimer;
		private float nextPickup = Time.time;
		private const float pickupDelay = 0.5f;
		private float nextUiUpdate = Time.time;
		private const float uiUpdateDelay = 0.5f;

		/// <param name="recycler">The recycler entity we're "overwriting".</param>
		public Crafter(Recycler recycler)
		{
			Recycler = recycler;

			Vector3 outputPosition = Position + (recycler.transform.forward * 0.4f) + (recycler.transform.up * 0.72f) + (recycler.transform.right * -0.25f);
			Quaternion outputRotation = recycler.ServerRotation * Quaternion.Euler(90, 0, 0);

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
			var item = ItemManager.Create(ItemManager.FindItemDefinition("rock"), 1);
			item.MoveToContainer(outputInventory, outputInventory.capacity - 1);
			
			var rigidBody = outputContainer.GetComponent<Rigidbody>();
			rigidBody.isKinematic = true; // Prevent physics from moving the container.

			// Reset despawn timer on loot bag once per minute.
			resetDespawnTimer = Utility.Timer.Every(60, () =>
			{
				if (!outputContainer.IsDestroyed)
					outputContainer.ResetRemovalTime();
			});
		}

		public void Tick(float elapsed)
		{
			ProcessQueue(elapsed);
			ProcessWorldItems();
			ProcessNearbyPlayers();
			ProcessUiUpdates();
		}

		public void Destroy()
		{
			resetDespawnTimer.DestroyToPool();
			outputContainer.Kill();

			foreach (var player in NearbyPlayers)
			{
				SendClearCraftingList(player);
			}
		}

		private void ProcessQueue(float elapsed)
		{
			if (!Recycler.IsOn() || CraftingTasks.Count <= 0)
				return;

			var currentTask = CraftingTasks.FirstOrDefault();

			if (currentTask != null)
			{
				Debug.Log("Processing queue: " + currentTask.Blueprint.targetItem.displayName.english + " x" + currentTask.Amount + " (" + currentTask.Elapsed + "/" + currentTask.Blueprint.time + ")");
				currentTask.Elapsed += elapsed;

				if (currentTask.Elapsed >= currentTask.Blueprint.time)
				{
					Debug.Log("Add " + currentTask.Blueprint.amountToCreate + "x " + currentTask.Blueprint.targetItem.displayName.english + " to output container");

					var item = ItemManager.Create(currentTask.Blueprint.targetItem, currentTask.Blueprint.amountToCreate, currentTask.SkinID);

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

						if (worldItem.item.MoveToContainer(outputInventory))
						{
							nextPickup = Time.time + pickupDelay;
							FxManager.PlayFx(worldItem.ServerPosition, Constants.StackSoundFxPrefab);
						}
					}
				}
			}
		}

		private void ProcessNearbyPlayers()
		{
			List<BasePlayer> nearPlayers = new List<BasePlayer>();

			Vector3 checkPosition = Position + Recycler.transform.up * 0.75f;
			float checkRadius = 2.5f;
			Vis.Entities(checkPosition, checkRadius, nearPlayers);

			var previousNearbyPlayers = NearbyPlayers.ToList(); // Nearby players last tick

			// A function that determines whether the given player is crafting anything unrelated to this crafter. Returns true if the user is crafting.
			Func<BasePlayer, bool> crafting = (player) =>
			{
				var itemCrafter = player.inventory.crafting;

				if (itemCrafter.queue.Count <= 0)
					return false;

				// Since we know queue count is > 0 and the player does not have any task id's on this crafter, we know the craft tasks are all unrelated to this crafter.
				if (!taskLookup.ContainsKey(player))
					return true;

				// The only way to reach this point is if the player had no craft tasks to begin with, and this crafter added the craft tasks along with an entry in the task lookup.
				return false;
			};

			// Keep all players that are the following:
			// - Alive and not sleeping
			// - Not crafting anything (that isn't related to this crafter).
			// - Can see the recycler from their position, aka not behind a wall or anything
			nearPlayers = nearPlayers.Where(plr => plr.IsAlive() && !plr.IsSleeping() && !crafting(plr) && Recycler.IsVisible(plr.ServerPosition)).ToList();

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

			/*if (NearbyPlayers.Count > 0)
			{
				Debug.Log(NearbyPlayers.Count);
			}*/

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
		}

		/// <summary>
		/// Called when a player goes out of range of this crafter.
		/// </summary>
		private void OnPlayerLeave(BasePlayer player)
		{
			SendClearCraftingList(player);
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

					return;
				}
			}

			var craftTask = new CraftTask(blueprint, amount, skinId);
			CraftingTasks.Add(craftTask);

			foreach (var player in NearbyPlayers)
			{
				SendAddCraftingTask(player, craftTask);
			}

			// Turn on recycler
			if (!Recycler.IsOn())
			{
				Recycler.StartRecycling();
			}
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
			var lookup = GetTaskLookupDict(player);
			var task = lookup.FirstOrDefault(kv => kv.Value == taskid);

			if (task.Key == null)
			{
				return false;
			}

			return CancelTask(task.Key, player);
		}

		#endregion
	}
}