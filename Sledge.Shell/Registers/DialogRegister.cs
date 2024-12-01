﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using LogicAndTrick.Oy;
using Sledge.Common.Logging;
using Sledge.Common.Shell.Components;
using Sledge.Common.Shell.Context;
using Sledge.Common.Shell.Hooks;
using Sledge.Common.Shell.Settings;

namespace Sledge.Shell.Registers
{
	/// <summary>
	/// The dialog register controls dialogs
	/// </summary>
	[Export(typeof(IStartupHook))]
	[Export(typeof(ISettingsContainer))]
	public class DialogRegister : IStartupHook, ISettingsContainer
	{
		[Import] private Forms.Shell _shell;
		[ImportMany] private IEnumerable<Lazy<IDialog>> _dialogs;
		private static DialogRegister _instance;
		private bool _useDarkMode = false;
		public async Task OnStartup()
		{
			// Register the exported dialogs
			foreach (var export in _dialogs)
			{
				Log.Debug(nameof(DialogRegister), "Loaded: " + export.Value.GetType().FullName);
				_components.Add(export.Value);

				//ColorControlsRecursively((ContainerControl)export.Value);

			}


			//ColorControlsRecursively(_shell);

			// Subscribe to context changes
			Oy.Subscribe<IContext>("Context:Changed", ContextChanged);
		}
		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
		private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
		private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

		private static void DarkMode(IntPtr handle, bool dark)
		{
			var attribute = DWMWA_USE_IMMERSIVE_DARK_MODE;

			int useImmersiveDarkMode = dark ? 1 : 0;


			DwmSetWindowAttribute(handle, (int)attribute, ref useImmersiveDarkMode, sizeof(int));

		}
		private void UseDarkMode()
		{
			if (_shell.InvokeRequired)
				_shell.Invoke(new Action(() =>
					ColorControlsRecursively(_shell, _useDarkMode)
				));
			else
				ColorControlsRecursively(_shell, _useDarkMode);

			foreach (var component in _components)
			{
				ColorControlsRecursively((ContainerControl)component, _useDarkMode);
			}
			_instance._shell.UseDarkTheme(_useDarkMode);

		}

		public static void ColorControlsRecursively(Control control, bool darkMode)
		{
			foreach (Control childControl in control.Controls)
			{


				ColorControlsRecursively(childControl, darkMode);

				if (childControl is Button button)
				{
					childControl.BackColor = darkMode ? Color.DimGray : SystemColors.Control;
					childControl.ForeColor = darkMode ? SystemColors.Control : Color.Black;

				}
				else
				{
					childControl.BackColor = darkMode ? Color.DimGray : SystemColors.Control;
					childControl.ForeColor = darkMode ? SystemColors.Control : Color.Black;
				}

			}
			if (control is Form form)
			{
				if (form.InvokeRequired)
				{
					form.InvokeLater(new Action(() =>
					{
						DarkMode(form.Handle, darkMode);
						if (control is IDialog)
							((IDialog)control).UseDarkTheme(darkMode);
					}));
				}
				else
				{
					DarkMode(form.Handle, darkMode);
					if (control is IDialog)
						((IDialog)control).UseDarkTheme(darkMode);
					if (form.MainMenuStrip == null) return;
					foreach (ToolStripItem item in form.MainMenuStrip.Items)
					{
						item.BackColor = darkMode ? Color.DimGray : SystemColors.Control;
						item.ForeColor = darkMode ? SystemColors.Control : Color.Black;
					}

				}
			}

			control.BackColor = darkMode ? Color.DimGray : SystemColors.Control;
			control.ForeColor = darkMode ? SystemColors.Control : Color.Black;
		}

		private readonly List<IDialog> _components;

		public string Name => "DialogRegister";

		public bool ValuesLoaded { get; private set; } = false;

		public DialogRegister()
		{
			_instance = this;
			_components = new List<IDialog>();
		}
		public static bool IsAnyDialogOpened() => _instance._components.Where(x => x.Visible).Any();
		public static bool IsAnyDialogFocused() => _instance._components.Where(x => x.Visible).OfType<Form>().Where(f => f.ContainsFocus).Any();

		private Task ContextChanged(IContext context)
		{
			_shell.InvokeLater(() =>
			{
				foreach (var c in _components)
				{
					var vis = c.IsInContext(context);
					if (vis != c.Visible) c.SetVisible(context, vis);
				}
			});
			return Task.CompletedTask;
		}

		public IEnumerable<SettingKey> GetKeys()
		{
			yield return new SettingKey("Interface", "UseDarkMode", typeof(Boolean));
		}

		public void LoadValues(ISettingsStore store)
		{

			_useDarkMode = store.Get<Boolean>("UseDarkMode");
			UseDarkMode();
			ValuesLoaded = true;

		}

		public void StoreValues(ISettingsStore store)
		{
			store.Set("UseDarkMode", _useDarkMode);
		}
	}
}
