using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Oxide.Plugins.AutoCrafterNamespace.JsonConverters;

namespace Oxide.Plugins.AutoCrafterNamespace.Extensions
{
	public static class JsonExtensions
	{
		private static readonly List<JsonConverter> converters = new List<JsonConverter>
		{
			new Vector2Converter(),
			new Vector3Converter()
		};

		/// <summary>
		/// Adds additional json converters to this settings instance. It will not add duplicate converters so it's safe to call multiple times.
		/// </summary>
		/// <param name="settings"></param>
		public static void AddConverters(this JsonSerializerSettings settings)
		{
			foreach (var converter in converters)
			{
				// Make sure the converter isn't already added.
				if (settings.Converters.Any(conv => conv.GetType() == converter.GetType()))
					continue;

				settings.Converters.Add(converter);
			}
		}
	}
}