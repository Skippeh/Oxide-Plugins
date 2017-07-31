using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Apex;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Plugins.AutoCrafterNamespace.Extensions;
using Oxide.Plugins.AutoCrafterNamespace.UI;
using UnityEngine;

namespace Oxide.Plugins.AutoCrafterNamespace
{
	public static class CrafterManager
	{
		public static Dictionary<Vector3, Crafter> Crafters { get; private set; }
		private static Dictionary<Recycler, Crafter> crafterLookup;
		
		private static float lastTick;
		private static Timer tickTimer;

		private static ActiveCrafterUI activeCrafterUi;

		/// <summary>
		/// Keeps track of how many crafters a player is in range of.
		/// </summary>
		private static Dictionary<BasePlayer, int> numActiveCrafters;

		#region Initialization, destruction and save/loading

		public static void Initialize()
		{
			Crafters = new Dictionary<Vector3, Crafter>();
			crafterLookup = new Dictionary<Recycler, Crafter>();
			numActiveCrafters = new Dictionary<BasePlayer, int>();

			lastTick = Time.time;
			tickTimer = Utility.Timer.Every(0.2f, Tick); // Tick every 200ms

			activeCrafterUi = UiManager.CreateUI<ActiveCrafterUI>();
		}

		public static void Destroy()
		{
			tickTimer.DestroyToPool();

			foreach (var crafter in Crafters.Values)
			{
				crafter.Downgrade(true, true);
			}
			
			Crafters.Clear();
			crafterLookup.Clear();
			UiManager.DestroyUI(activeCrafterUi);
		}

		public static void Save()
		{
			var dataFile = Core.Interface.Oxide.DataFileSystem.GetFile("AutoCrafter/Crafters");
			dataFile.Settings.AddConverters();
			
			dataFile.WriteObject(Crafters.ToDictionary(kv => kv.Key.ToXYZString(), kv => kv.Value));
		}

		public static void Load()
		{
			var jCrafters = Core.Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, JObject>>("AutoCrafter/Crafters");
			var loadedCount = 0;

			foreach (var kv in jCrafters)
			{
				var jCrafter = kv.Value;
				string strPosition = kv.Key;
				Vector3 position = Utility.ParseXYZ(strPosition);

				List<BaseEntity> entities = new List<BaseEntity>();
				Vis.Entities(position, 0.1f, entities); // Find all entities within 0.1 game units of the saved position.

				// Compare entity positions and take the first research table or recycler that is within 0.001 units of the saved position.
				float maxDistanceSqr = 0.001f * 0.001f;
				var baseEntity = entities.FirstOrDefault(ent => (ent is ResearchTable || ent is Recycler) && (position - ent.ServerPosition).sqrMagnitude <= maxDistanceSqr);

				if (baseEntity == null)
				{
					Utility.LogWarning("Unable to load crafter; research table or recycler at saved position was not found. (" + position.ToString("0.########") + ")");
					continue;
				}

				var crafter = baseEntity is Recycler ? CreateCrafter((Recycler) baseEntity) : CreateCrafter((ResearchTable) baseEntity);
				crafter.CreationTime = jCrafter["CreationTime"].ToObject<DateTime>();

				// Load codelock
				bool hasCodeLock = jCrafter["HasCodeLock"].ToObject<bool>();

				if (hasCodeLock)
				{
					crafter.AddCodeLock();
					var codeLock = crafter.CodeLock;

					string code = jCrafter["Code"].ToObject<string>();
					string guestCode = jCrafter["GuestCode"].ToObject<string>();
					ulong[] authedPlayers = jCrafter["AuthedPlayers"].ToObject<ulong[]>();
					ulong[] guestPlayers = jCrafter["GuestPlayers"].ToObject<ulong[]>();
					bool isLocked = jCrafter["IsLocked"].ToObject<bool>();

					codeLock.code = code;
					codeLock.guestCode = guestCode;
					codeLock.whitelistPlayers.AddRange(authedPlayers);
					codeLock.guestPlayers.AddRange(guestPlayers);

					if (isLocked)
						codeLock.SetFlag(BaseEntity.Flags.Locked, true);
				}

				// Restore crafting queue
				foreach (var jTask in jCrafter["CraftingTasks"].Value<JArray>())
				{
					var blueprint = ItemManager.FindBlueprint(ItemManager.FindItemDefinition(jTask["ItemID"].ToObject<int>()));
					int amount = jTask["Amount"].ToObject<int>();
					int skin = jTask["SkinID"].ToObject<int>();
					
					var task = crafter.AddCraftTask(blueprint, amount, skin, false);
					task.Elapsed = jTask["Elapsed"].ToObject<float>();

					// Restore taken items
					var jTakenItems = jTask["TakenItems"].Value<JArray>();

					foreach (var jItem in jTakenItems)
					{
						int itemID = jItem["itemid"].ToObject<int>();
						int amount2 = jItem["amount"].ToObject<int>();
						ulong skin2 = jItem["skin"].ToObject<ulong>();

						var item = ItemManager.CreateByItemID(itemID, amount2, skin2);
						task.TakenItems.Add(item);
					}
				}

				// Restore output container
				foreach (var jItem in jCrafter["OutputItems"].Value<JArray>())
				{
					int itemId = jItem["itemid"].ToObject<int>();
					int amount = jItem["amount"].ToObject<int>();
					ulong skinId = jItem["skin"].ToObject<ulong>();
					int index = jItem["position"].ToObject<int>();

					var item = ItemManager.CreateByItemID(itemId, amount, skinId);
					item.MoveToContainer(crafter.OutputInventory, index);
				}

				// Restore on/off state
				if (jCrafter["On"].ToObject<bool>())
					crafter.Recycler.StartRecycling();

				// Restore hp and decay
				crafter.Recycler.health = Mathf.Clamp(jCrafter["Health"].ToObject<float>(), 0, crafter.Recycler.MaxHealth());
				typeof (DecayEntity).GetField("decayTimer", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(crafter.Recycler, jCrafter["DecayTimer"].ToObject<float>());

				++loadedCount;
			}

			Utility.Log("Loaded " + loadedCount + " crafter(s).");
		}
		
		#endregion

		#region Public api methods

		/// <summary>
		/// Creates a crafter from the given research table.
		/// </summary>
		/// <param name="researchTable">The research table to replace.</param>
		/// <returns></returns>
		public static Crafter CreateCrafter(ResearchTable researchTable)
		{
			var recyclerEntity = GameManager.server.CreateEntity(Constants.StaticRecyclerPrefab, researchTable.ServerPosition, researchTable.ServerRotation);
			var recycler = recyclerEntity.GetComponent<Recycler>();
			recyclerEntity.OwnerID = researchTable.OwnerID; // Copy ownership to recycler.
			recyclerEntity.Spawn();

			// Drop all items in research table onto the ground
			if (researchTable.inventory.AnyItems())
				researchTable.inventory.Drop(Constants.ItemDropPrefab, researchTable.ServerPosition + new Vector3(0, 1.5f, 0), researchTable.ServerRotation);

			// Remove original research table.
			researchTable.Kill();
			
			var crafter = CreateCrafter(recycler);
			return crafter;
		}
		
		/// <summary>
		/// Creates a crafter from the given recycler.
		/// </summary>
		/// <param name="recycler"></param>
		/// <returns></returns>
		public static Crafter CreateCrafter(Recycler recycler)
		{
			var crafter = new Crafter(recycler);
			crafter.PlayerEnter += OnPlayerEnterCrafter;
			crafter.PlayerLeave += OnPlayerLeaveCrafter;

			var gears = ItemManager.Create(ItemManager.FindItemDefinition("gears"), Constants.RecyclerNumInputSlots);

			for (int i = 0; i < Constants.RecyclerNumInputSlots; ++i)
			{
				var split = gears.SplitItem(1) ?? gears;
				split.MoveToContainer(recycler.inventory, i, false);
			}
			
			recycler.inventory.SetLocked(true);
			recycler.SendNetworkUpdateImmediate();

			Crafters.Add(recycler.ServerPosition, crafter);
			crafterLookup.Add(recycler, crafter);

			return crafter;
		}

		/// <summary>
		/// Destroys the given crafter and optionally spawns a research table in its place.
		/// </summary>
		/// <param name="crafter">The crafter to destroy.</param>
		/// <param name="downgrade">If true, then the recycler will be replaced with a research table.</param>
		public static void DestroyCrafter(Crafter crafter, bool downgrade, bool destroyOutputContainer, bool unloading = false)
		{
			Crafters.Remove(crafter.Position);
			crafterLookup.Remove(crafter.Recycler);
			
			if (downgrade)
			{
				crafter.Downgrade(destroyOutputContainer);
			}
			else
			{
				crafter.Destroy(destroyOutputContainer, unloading);
			}

			crafter.PlayerEnter -= OnPlayerEnterCrafter;
			crafter.PlayerLeave -= OnPlayerLeaveCrafter;
		}

		/// <summary>
		/// Returns true if the given recycler is a crafter.
		/// </summary>
		public static bool ContainsRecycler(Recycler recycler)
		{
			return crafterLookup.ContainsKey(recycler);
		}

		/// <summary>
		/// Retrieves the crafter of the given recycler. Returns null if none is found.
		/// </summary>
		public static Crafter GetCrafter(Recycler recycler)
		{
			if (!crafterLookup.ContainsKey(recycler))
				return null;

			return crafterLookup[recycler];
		}

		/// <summary>
		/// Returns the crafter that's within range and visible by the given player. If there's multiple then the closest one will be returned.
		/// </summary>
		public static Crafter FindByPlayer(BasePlayer player)
		{
			// Sort crafters by distance from player and search starting from the closest one.
			var crafters = Crafters.OrderBy(kv => (player.ServerPosition - kv.Key).sqrMagnitude);
			return crafters.FirstOrDefault(kv => kv.Value.NearbyPlayers.Contains(player)).Value;
		}

		/// <summary>
		/// Returns all crafters that are within range and visible by the given player. They will be sorted by ascending range.
		/// </summary>
		public static IEnumerable<Crafter> FindAllByPlayer(BasePlayer player)
		{
			var crafters = Crafters.OrderBy(kv => (player.ServerPosition - kv.Key).sqrMagnitude);

			foreach (var kv in crafters)
			{
				if (kv.Value.NearbyPlayers.Contains(player))
					yield return kv.Value;
			}
		} 

		#endregion

		private static void Tick()
		{
			float elapsed = Time.time - lastTick; // Elapsed time in seconds since last tick.
			lastTick = Time.time;

			foreach (var crafter in Crafters.Values.ToList())
			{
				if (crafter.Recycler.IsDestroyed)
					continue;

				crafter.Tick(elapsed);
			}
		}

		private static void OnPlayerEnterCrafter(Crafter crafter, BasePlayer player)
		{
			if (!numActiveCrafters.ContainsKey(player))
				numActiveCrafters[player] = 0;

			numActiveCrafters[player]++;

			// Only add ui for the first crafter, otherwise we'll add the player multiple times.
			if (numActiveCrafters[player] == 1)
			{
				UiManager.AddPlayerUI(activeCrafterUi, player);
			}
		}

		private static void OnPlayerLeaveCrafter(Crafter crafter, BasePlayer player)
		{
			numActiveCrafters[player]--;

			if (numActiveCrafters[player] <= 0)
			{
				numActiveCrafters.Remove(player);
				UiManager.RemoveUI(activeCrafterUi, player);
			}
		}
	}
}