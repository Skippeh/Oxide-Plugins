using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins.AutoCrafterNamespace.UI
{
	/// <summary>
	/// A notifier at the bottom of the screen that shows up when a player is inside a crafters range.
	/// </summary>
	public class ActiveCrafterUI : UIBase
	{
		public override void CreateUI()
		{
			var root = new CuiPanel
			{
				RectTransform =
				{
					AnchorMin = "0.425 0",
					AnchorMax = "0.575 0.021"
				},
				Image =
				{
					Color = "0 0 0 0",
					FadeIn = 0.2f
				},
				FadeOut = 0.2f
			};

			var background = new CuiPanel
			{
				RectTransform =
				{
					AnchorMin = "0 0",
					AnchorMax = "1 1"
				},
				Image =
				{
					Color = "0 0 0 0.8"
				}
			};
			
			var label = new CuiLabel
			{
				RectTransform =
				{
					AnchorMin = "0 0",
					AnchorMax = "1 1"
				},
				Text =
				{
					Text = Lang.Translate(null, "crafter-inrange"),
					Color = "0.9 0.9 0.9 1",
					FontSize = 12,
					Align = TextAnchor.MiddleCenter
				}
			};

			string rootKey = Elements.Add(root, "Overlay");
			Elements.Add(background, rootKey);
			Elements.Add(label, rootKey);
		}
	}
}