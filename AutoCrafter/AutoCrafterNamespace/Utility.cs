using System.Globalization;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins.AutoCrafterNamespace
{
	public static class Utility
	{
		public static PluginTimers Timer { get; set; }
		public static PluginConfig Config { get; set; }

		/// <summary>
		/// Converts the Vector3 into a string in the format of "x,y,z".
		/// </summary>
		public static string ToXYZString(this Vector3 vec)
		{
			return vec.x.ToString(CultureInfo.InvariantCulture) + "," +
			       vec.y.ToString(CultureInfo.InvariantCulture) + "," +
			       vec.z.ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Parses a Vector3 from a string with the format "x,y,z".
		/// </summary>
		public static Vector3 ParseXYZ(string str)
		{
			string[] xyz = str.Split(',');
			float x = float.Parse(xyz[0], CultureInfo.InvariantCulture);
			float y = float.Parse(xyz[1], CultureInfo.InvariantCulture);
			float z = float.Parse(xyz[2], CultureInfo.InvariantCulture);
			return new Vector3(x, y, z);
		}

		public static void LogComponents(GameObject gameObject)
		{
			var components = gameObject.GetComponents<MonoBehaviour>();
			var builder = new StringBuilder();

			for (int i = 0; i < components.Length; i++)
			{
				var component = components[i];
				builder.Append(component.GetType().Name);

				if (i < components.Length - 1)
					builder.Append(", ");
			}

			Log(builder.ToString());
		}

		public static void LogComponents(MonoBehaviour behaviour)
		{
			LogComponents(behaviour.gameObject);
		}

		public static void Log(string str)
		{
			Debug.Log("[AutoCrafter] " + str);
		}

		public static void LogWarning(string str)
		{
			Debug.LogWarning("[AutoCrafter] " + str);
		}
	}
}