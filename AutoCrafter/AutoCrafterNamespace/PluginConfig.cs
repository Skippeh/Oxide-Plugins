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

		public bool ShowPlayerInstructionsOnFirstJoin { get; set; } = true;
		public bool ShowInstructionsAsGameTip { get; set; } = true;

		public List<ItemAmount> UpgradeCost { get; set; } = new List<ItemAmount>();
		
		public float[] CrafterProtectionProperties { get; set; } =
		{
			0.98f, // Generic
			0, // Hunger
			0, // Thirst
			0, // Cold
			0, // Drowned
			1, // Heat
			0, // Bleeding
			0, // Poison
			0, // Suicide
			0.999f, // Bullet
			0.99f, // Slash
			0.99f, // Blunt
			0, // Fall
			1, // Radiation
			0.99f, // Bite
			0.98f, // Stab
			0.3f, // Explosion
			0, // RadiationExposure
			0, // ColdExposure
			0, // Decay
			0, // ElectricShock
			1 // Arrow
		};
	}
}