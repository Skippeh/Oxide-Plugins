using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins.AutoCrafterNamespace.UI
{
	public class ScreenMessageUI : UIBase
	{
		public string Text
		{
			get { return contentLabel.Text.Text; }
			set
			{
				contentLabel.Text.Text = value;
				MakeDirty();
			}
		}

		public TextAnchor TextAnchor
		{
			get
			{
				return contentLabel.Text.Align;
			}
			set
			{
				contentLabel.Text.Align = value;
				MakeDirty();
			}
		}

		private CuiLabel contentLabel;

		public override void CreateUI()
		{
			string rootKey = Elements.Add(new CuiPanel
			{
				RectTransform =
				{
					AnchorMin = "0.4 0.15",
					AnchorMax = "0.6 0.3"
				},
				Image =
				{
					Color = "0 0 0 0.7"
				}
			});

			contentLabel = new CuiLabel
			{
				RectTransform =
				{
					AnchorMin = "0.02 0.01",
					AnchorMax = "0.98 0.99"
				},
				Text =
				{
					Text = "",
					Color = "0.9 0.9 0.9 1",
					FontSize = 14,
					Align = TextAnchor.MiddleCenter
				}
			};
			
			Elements.Add(contentLabel, rootKey);
		}
	}
}