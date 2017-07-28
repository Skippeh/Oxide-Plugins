using System.Collections.Generic;
using System.Linq;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins.AutoCrafterNamespace.UI
{
	public abstract class UIBase
	{
		public string Identifier => Elements.FirstOrDefault()?.Name;
		public CuiElementContainer Elements { get; protected set; } = new CuiElementContainer();

		public bool Dirty { get; private set; } = true;

		public abstract void CreateUI();

		public virtual void Destroy()
		{
		}

		public virtual void Tick(float elapsed)
		{
		}

		/// <summary>
		/// Sets the dirty flag. The ui will be sent to players on the next ui tick.
		/// </summary>
		protected void MakeDirty()
		{
			Dirty = true;
		}
		
		/// <summary>
		/// Removes the dirty flag.
		/// </summary>
		public void ResetDirty()
		{
			Dirty = false;
		}
	}
}