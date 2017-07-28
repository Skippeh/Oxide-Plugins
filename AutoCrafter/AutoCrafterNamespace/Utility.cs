using System.Globalization;
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
	}
}