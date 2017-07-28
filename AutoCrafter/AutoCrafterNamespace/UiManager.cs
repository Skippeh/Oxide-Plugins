using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Game.Rust.Cui;
using Oxide.Plugins.AutoCrafterNamespace.UI;
using UnityEngine;

namespace Oxide.Plugins.AutoCrafterNamespace
{
	public static class UiManager
	{
		/// <summary>
		/// Lookup map of active uis and the players that have it active.
		/// </summary>
		private static readonly Dictionary<string, List<BasePlayer>> activeUis = new Dictionary<string, List<BasePlayer>>();

		/// <summary>
		/// Lookup table for object based uis.
		/// </summary>
		private static readonly Dictionary<string, UIBase> uiLookup = new Dictionary<string, UIBase>();

		private static Timer tickTimer;
		private static float lastTick = Time.time;

		public static void Initialize()
		{
			tickTimer = Utility.Timer.Every(0.5f, Tick); // Update ui every 500ms.
		}

		public static void Destroy()
		{
			tickTimer.DestroyToPool();
			ClearAllUI();
			DestroyAllUI();
		}

		private static void Tick()
		{
			float elapsed = Time.time - lastTick;
			lastTick = Time.time;

			foreach (var ui in uiLookup.Values)
			{
				var playerList = GetPlayerList(ui);

				ui.Tick(elapsed);

				if (ui.Dirty)
				{
					SendUI(ui, playerList);
					ui.ResetDirty();
				}
			}
		}

		/// <summary>
		/// Sends the given ui to the specified players.
		/// </summary>
		/// <param name="ui">The ui to send.</param>
		/// <param name="players">The players to send to.</param>
		private static void SendUI(UIBase ui, IEnumerable<BasePlayer> players)
		{
			foreach (var player in players)
			{
				SendUI(ui, player);
			}
		}

		/// <summary>
		/// Sends the given ui the the specified player.
		/// </summary>
		/// <param name="ui">The ui to send,</param>
		/// <param name="player">The player to send to.</param>
		private static void SendUI(UIBase ui, BasePlayer player)
		{
			CuiHelper.DestroyUi(player, ui.Identifier);
			CuiHelper.AddUi(player, ui.Elements);
		}

		#region Public api methods

		public static T CreateUI<T>() where T : UIBase
		{
			var instance = Activator.CreateInstance<T>();
			instance.CreateUI();

			if (instance.Identifier == null)
				throw new InvalidOperationException("Instantiated UI does not have an identifier set after ui creation.");

			if (uiLookup.ContainsKey(instance.Identifier))
				throw new InvalidOperationException("Instantiated UI does not have a unique identifier set. (conflict found)");

			activeUis.Add(instance.Identifier, new List<BasePlayer>());
			uiLookup.Add(instance.Identifier, instance);

			return instance;
		}

		/// <summary>
		/// Adds the given player to the specified ui. The player will receied the ui and subsequent updates until removed.
		/// </summary>
		/// <param name="ui">The ui to send to the player.</param>
		/// <param name="player">The player to add.</param>
		public static void AddPlayerUI(UIBase ui, BasePlayer player)
		{
			var players = GetPlayerList(ui);
			players.Add(player);

			SendUI(ui, player);
		}

		/// <summary>
		/// Removes all ui instances for all players.
		/// </summary>
		public static void ClearAllUI()
		{
			foreach (string uiKey in activeUis.Keys.ToList())
			{
				ClearUI(uiLookup[uiKey]);
			}
		}

		/// <summary>
		/// Removes the ui for all the given players.
		/// </summary>
		/// <param name="ui">The ui to remove.</param>
		public static void RemoveUI(UIBase ui, IEnumerable<BasePlayer> players)
		{
			foreach (var player in players)
			{
				RemoveUI(ui, player);
			}
		}

		/// <summary>
		/// Removes the ui for the given player.
		/// </summary>
		/// <param name="ui">The ui to remove.</param>
		public static void RemoveUI(UIBase ui, BasePlayer player)
		{
			if (!activeUis.ContainsKey(ui.Identifier))
				throw new ArgumentException("There is no active ui with the specified key.");

			CuiHelper.DestroyUi(player, ui.Identifier);
			activeUis[ui.Identifier].Remove(player);
		}

		/// <summary>
		/// Removes the ui for all players.
		/// </summary>
		/// <param name="ui">The ui to remove.</param>
		public static void ClearUI(UIBase ui)
		{
			if (!activeUis.ContainsKey(ui.Identifier))
				throw new ArgumentException("There is no active ui with the specified key.");

			foreach (var player in activeUis[ui.Identifier].ToList())
			{
				RemoveUI(ui, player);
			}
		}

		public static void DestroyAllUI()
		{
			foreach (var ui in uiLookup.Values.ToList())
			{
				DestroyUI(ui);
			}
		}

		public static void DestroyUI(UIBase ui)
		{
			ClearUI(ui);
			ui.Destroy();

			activeUis.Remove(ui.Identifier);
			uiLookup.Remove(ui.Identifier);
		}

		#endregion

		private static List<BasePlayer> GetPlayerList(UIBase ui)
		{
			if (activeUis.ContainsKey(ui.Identifier))
				return activeUis[ui.Identifier];

			var list = new List<BasePlayer>();
			activeUis[ui.Identifier] = list;
			return list;
		} 
	}
}