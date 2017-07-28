using System.Linq;

namespace Oxide.Plugins.AutoCrafterNamespace.Extensions
{
	public static class ContainerExtensions
	{
		/// <summary>
		/// Returns true if there are items in this container that aren't about to be removed.
		/// </summary>
		/// <param name="container"></param>
		/// <returns></returns>
		public static bool AnyItems(this ItemContainer container)
		{
			if (container.itemList == null || container.itemList.Count <= 0)
				return false;

			return container.itemList.Any(item => item.removeTime <= 0f);
		}
	}
}