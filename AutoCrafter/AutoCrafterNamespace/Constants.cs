namespace Oxide.Plugins.AutoCrafterNamespace
{
	public static class Constants
	{
		public const string ItemDropPrefab = "assets/prefabs/misc/item drop/item_drop.prefab";
		public const string StaticRecyclerPrefab = "assets/bundled/prefabs/static/recycler_static.prefab";
		public const string DeployedResearchTablePrefab = "assets/prefabs/deployable/research table/researchtable_deployed.prefab";
		public const string StackSoundFxPrefab = "assets/bundled/prefabs/fx/notice/stack.world.fx.prefab";
		public const string UpgradeTopTierFxPrefab = "assets/bundled/prefabs/fx/build/promote_toptier.prefab";
		public const string UpgradeMetalFxPrefab = "assets/bundled/prefabs/fx/build/promote_metal.prefab";

		public const int RecyclerNumInputSlots = 6;
		public const float CrafterNearRadius = 2.5f;
		public const float HammerConfirmTime = 2f;
		public const float TimeToUpgrade = 600f;

		public const string UsePermission = "autocrafter.use";
	}
}