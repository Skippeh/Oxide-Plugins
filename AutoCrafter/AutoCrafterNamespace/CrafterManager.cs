﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Apex;
using Newtonsoft.Json.Linq;
using Oxide.Plugins.AutoCrafterNamespace.Extensions;
using UnityEngine;

namespace Oxide.Plugins.AutoCrafterNamespace
{
	public static class CrafterManager
	{
		public static Dictionary<Vector3, Crafter> Crafters { get; private set; } = new Dictionary<Vector3, Crafter>();
		private static readonly Dictionary<Recycler, Crafter> crafterLookup = new Dictionary<Recycler, Crafter>();

		private static PluginTimers timerManager;

		private static float lastTick;
		private static Timer tickTimer;

		#region Initialization, destruction and save/loading

		public static void Initialize(PluginTimers timerManager)
		{
			CrafterManager.timerManager = timerManager;

			lastTick = Time.time;
			tickTimer = timerManager.Every(0.5f, Tick); // Tick every 500ms
		}

		public static void Destroy()
		{
			tickTimer.DestroyToPool();

			foreach (var crafter in Crafters.Values)
			{
				crafter.Destroy();
			}
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
				string strPosition = kv.Key;
				Vector3 position = Utility.ParseXYZ(strPosition);

				List<BaseEntity> entities = new List<BaseEntity>();
				Vis.Entities(position, 0.1f, entities); // Find all entities within 0.1 game units of the saved position.

				// Compare entity positions and take the first recycler that is within 0.001 units of the saved position.
				float maxDistanceSqr = 0.001f * 0.001f;
				var recycler = entities.FirstOrDefault(ent => ent is Recycler && (position - ent.ServerPosition).sqrMagnitude <= maxDistanceSqr) as Recycler;

				if (recycler == null)
				{
					Debug.LogWarning("Unable to load crafter; recycler at saved position was not found. (" + position.ToString("0.########") + ")");
					continue;
				}

				CreateCrafter(recycler);
				++loadedCount;
			}

			Debug.Log("Loaded " + loadedCount + " crafter(s).");
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

			var gears = ItemManager.Create(ItemManager.FindItemDefinition("gears"), Constants.RecyclerNumInputSlots);
			var pipes = ItemManager.Create(ItemManager.FindItemDefinition("metalpipe"), Constants.RecyclerNumInputSlots);

			for (int i = 0; i < Constants.RecyclerNumInputSlots; ++i)
			{
				var split = gears.SplitItem(1) ?? gears;
				var split2 = pipes.SplitItem(1) ?? pipes;

				split.MoveToContainer(recycler.inventory, i, false);
				split2.MoveToContainer(recycler.inventory, i + Constants.RecyclerNumInputSlots, false);
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
		/// <param name="replaceWithResearchTable">If true, then the recycler will be replaced with a research table.</param>
		public static void DestroyCrafter(Crafter crafter, bool replaceWithResearchTable)
		{
			if (!Crafters.Remove(crafter.Position))
			{
				Debug.LogWarning("Vector3 comparison most likely inaccurate, testing search...");

				var kv = Crafters.First(kv2 => kv2.Value == crafter);

				if (!Crafters.Remove(kv.Key))
				{
					throw new NotImplementedException("Need to change dict key from vector3 to something more reliable.");
				}

				Debug.Log("Found.");
			}

			crafterLookup.Remove(crafter.Recycler);

			if (replaceWithResearchTable)
			{
				var researchTableEntity = GameManager.server.CreateEntity(Constants.DeployedResearchTablePrefab, crafter.Recycler.ServerPosition, crafter.Recycler.ServerRotation);
				var researchTable = researchTableEntity.GetComponent<ResearchTable>();
				researchTable.OwnerID = crafter.Recycler.OwnerID; // Copy ownership to research table.
				researchTable.Spawn();
			}

			// Remove all items in input slots
			var itemArray = crafter.Recycler.inventory.itemList.ToArray();
			for (int i = 0; i < Constants.RecyclerNumInputSlots; ++i)
			{
				if (i >= itemArray.Length)
					break;

				itemArray[i].Remove();
			}

			// Drop all items in recycler onto the ground
			if (crafter.Recycler.inventory.AnyItems())
				crafter.Recycler.inventory.Drop(Constants.ItemDropPrefab, crafter.Recycler.ServerPosition + (replaceWithResearchTable ? new Vector3(0, 1.5f, 0) : Vector3.zero), crafter.Recycler.ServerRotation);

			crafter.Recycler.Kill();
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
		/// Returns the crafter that's within range of the given player. If there's multiple then the closest one will be returned.
		/// </summary>
		public static Crafter FindByPlayer(BasePlayer player)
		{
			// Sort crafters by distance from player and search starting from the closest one.
			var crafters = Crafters.OrderBy(kv => (player.ServerPosition - kv.Key).sqrMagnitude);
			return crafters.FirstOrDefault(kv => kv.Value.NearbyPlayers.Contains(player)).Value;
		}

		#endregion

		private static void Tick()
		{
			float elapsed = Time.time - lastTick; // Elapsed time in seconds since last tick.
			lastTick = Time.time;

			Crafters.Values.ToList().ForEach(crafter =>
			{
				crafter.Tick(elapsed);
			});
		}
	}
}