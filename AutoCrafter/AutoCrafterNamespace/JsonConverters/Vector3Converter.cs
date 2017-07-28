using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Oxide.Plugins.AutoCrafterNamespace.JsonConverters
{
	public class Vector3Converter : JsonConverter
	{
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var vec = (Vector3) value;
			serializer.Serialize(writer, new float[] {vec.x, vec.y, vec.z});
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			Vector3 result = new Vector3();
			JArray jVec = JArray.Load(reader);

			result.x = jVec[0].ToObject<float>();
			result.y = jVec[1].ToObject<float>();
			result.z = jVec[2].ToObject<float>();

			return result;
		}

		public override bool CanConvert(Type objectType)
		{
			return objectType == typeof(Vector3);
		}
	}
}