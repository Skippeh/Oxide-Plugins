using System.Collections.Generic;

namespace Oxide.Plugins.AutoCrafterNamespace
{
	public class PluginConfig
	{
		public class ItemAmount
		{
			public string Shortname;
			public int Amount;

			public ItemAmount(string shortname, int amount)
			{
				Shortname = shortname;
				Amount = amount;
			}
		}

		public bool ScanForWorldItems { get; set; } = true;

		public List<ItemAmount> UpgradeCost { get; set; } = new List<ItemAmount>
		{
			new ItemAmount("metal.refined", 25),
			new ItemAmount("metal.fragments", 500),
			new ItemAmount("techparts", 3),
			new ItemAmount("gears", 3)
		};
	}
}