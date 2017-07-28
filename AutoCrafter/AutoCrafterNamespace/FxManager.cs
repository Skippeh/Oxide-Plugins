using System.Collections.Generic;
using System.Linq;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins.AutoCrafterNamespace
{
	public static class FxManager
	{
		public class RepeatedFx
		{
			private static int idCounter = 0;

			public int Id { get; private set; }
			public string FxName { get; private set; }
			public Vector3 Position { get; set; }
			public float Interval { get; private set; }
			public Timer Timer { get; set; }

			public float NextPlay = Time.time;

			public RepeatedFx(string fxName, Vector3 position, float interval)
			{
				Id = idCounter++;
				FxName = fxName;
				Position = position;
				Interval = interval;
				Timer = timerManager.Every(interval, Play);
			}

			private void Play()
			{
				PlayFx(Position, FxName);
			}
		}

		private static readonly Dictionary<int, RepeatedFx> RepeatingFx = new Dictionary<int, RepeatedFx>();

		private static PluginTimers timerManager;

		public static void Initialize(PluginTimers timer)
		{
			timerManager = timer;
		}

		public static void Destroy()
		{
			foreach (var fx in RepeatingFx.Values.ToList())
			{
				StopFx(fx);
			}
		}

		/// <summary>
		/// Plays the specified fx at the specified position.
		/// </summary>
		/// <param name="position">The position to play at.</param>
		/// <param name="fxName">The fx to play.</param>
		public static void PlayFx(Vector3 position, string fxName)
		{
			SpawnFx(position, fxName);
		}

		/// <summary>
		/// Plays the specified fx at the specified position repeatedly with the given interval.
		/// </summary>
		/// <param name="position">The position to play at.</param>
		/// <param name="fxName">The fx to play.</param>
		/// <param name="interval">The delay between plays in seconds.</param>
		/// <param name="initialDelay">Specifies an initial delay in seconds before playing the fx for the first time.</param>
		/// <returns></returns>
		public static RepeatedFx PlayFx(Vector3 position, string fxName, float interval, bool playAtSpawn = true)
		{
			var fx = new RepeatedFx(fxName, position, interval);

			if (playAtSpawn)
			{
				PlayFx(fx.Position, fx.FxName);
			}

			RepeatingFx.Add(fx.Id, fx);
			return fx;
		}

		/// <summary>
		/// Stops playing the repeating fx.
		/// </summary>
		/// <param name="fx">The fx to stop repeating.</param>
		public static void StopFx(RepeatedFx fx)
		{
			fx.Timer.DestroyToPool();
			fx.Timer = null;
			RepeatingFx.Remove(fx.Id);
		}

		private static void SpawnFx(Vector3 position, string fxName)
		{
			Effect.server.Run(fxName, position);
		}
	}
}