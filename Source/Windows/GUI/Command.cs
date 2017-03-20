﻿using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using MAPE.Utils;
using MAPE.Command;
using MAPE.Command.Settings;
using MAPE.Windows.GUI.Settings;


namespace MAPE.Windows.GUI {
	internal class Command: GUICommandBase {
		#region data

		private App app = null;

		#endregion


		#region properties

		public new CommandForWindowsGUISettings Settings {
			get {
				return (CommandForWindowsGUISettings)base.Settings;
			}
			set {
				base.Settings = value;
			}
		}

		public GUIForWindowsGUISettings GUISettings {
			get {
				return this.Settings.GUI;
			}
		}

		#endregion


		#region creation and disposal

		public Command(): base(new ComponentFactoryForWindowsGUI()) {
			// initialize members
			this.ComponentName = "MAPE GUI";

			return;
		}

		#endregion


		#region methods

		public void SetSettings(CommandForWindowsGUISettings newSettings, bool save) {
			this.Settings = newSettings;
			if (save) {
				string settingsFilePath = this.SettingsFilePath;
				if (string.IsNullOrEmpty(settingsFilePath) == false) {
					Action saveTask = () => {
						try {
							SaveSettingsToFile(newSettings, settingsFilePath);
						} catch (Exception exception) {
							LogError($"Fail to save settings: {exception.Message}");
						}
					};

					// launch save task
					Task.Run(saveTask);
				}
			}
		}

		public void SaveMainWindowSettings(MainWindowSettings mainWindowSettings) {
			string settingsFilePath = this.SettingsFilePath;
			if (string.IsNullOrEmpty(settingsFilePath) == false) {
				MainWindowSettings mainWindowSettingsClone = MAPE.Utils.Settings.Clone(mainWindowSettings);
				Action saveTask = () => {
					try {
						UpdateSettingsFile(s => { ((GUIForWindowsGUISettings)s.GUI).MainWindow = mainWindowSettingsClone; });
					} catch (Exception exception) {
						LogError($"Fail to save MainWindow settings: {exception.Message}");
					}
				};

				// launch save task
				Task.Run(saveTask);
			} else {
				LogError($"Fail to save MainWindow settings: no settings file is used.");
			}
		}

		#endregion


		#region overrides/overridables - execution

		protected override void ShowUsage(CommandSettings settings) {
			// show the Usage page in the browser
			Process.Start(GetUsagePagePath());
		}

		protected override void RunProxy(CommandSettings settings) {
			// state checks
			if (this.app != null) {
				throw new InvalidOperationException();
			}

			SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
			try {
				// run WPF application
				App app = new App(this);
				app.InitializeComponent();
				this.app = app;
				try {
					// start application
					app.Run();

					// ToDo: move to appropriate timing
					// report errors in early stages
					//				while (0 < this.ErrorMessages.Count) {
					//					string message = this.ErrorMessages.Dequeue();
					//					ShowErrorMessage(message);
					//				}
				} finally {
					this.app = null;
				}
			} finally {
				SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
			}

			return;
		}

		protected override CredentialSettings UpdateCredential(string endPoint, string realm, CredentialSettings oldCredential) {
			// argument checks
			Debug.Assert(endPoint != null);
			Debug.Assert(realm != null);    // may be empty
			// oldCredential can be null

			// state checks
			Debug.Assert(this.app != null);

			// ask user's credential
			Func<CredentialSettings> callback = () => {
				// setup a credential dialog
				CredentialDialog dialog = new CredentialDialog();
				dialog.Title = realm;
				dialog.EndPoint = endPoint;
				dialog.Credential = oldCredential;

				// show the credential dialog and get user input
				return (dialog.ShowDialog() ?? false) ? dialog.Credential : null;
			};

			return this.app.Dispatcher.Invoke<CredentialSettings>(callback);
		}

		#endregion


		#region overrides/overridables - misc

		protected override void ShowErrorMessage(string message) {
			App app = this.app;
			if (app == null) {
				// queue the error message to display it after GUI starts
				base.ShowErrorMessage(message);
			} else {
				// show error message
				app.Dispatcher.Invoke(
					() => {
						MessageBox.Show(message, this.ComponentName, MessageBoxButton.OK, MessageBoxImage.Error);
					}
				);
			}

			return;
		}

		#endregion


		#region privates

		private static string GetUsagePagePath() {
			// detect the folder path where the usage page is located
			// (that is the application folder)
			string folderPath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);

			// find the usage page for the current locale
			CultureInfo culture = CultureInfo.CurrentUICulture;
			while (string.IsNullOrEmpty(culture.Name) == false) {
				string filePath = Path.Combine(folderPath, $"Usage.{culture.Name}.html");
				if (string.Compare(culture.Name, "ja", StringComparison.OrdinalIgnoreCase) == 0) {
					return "https://github.com/ipponshimeji/MAPE/blob/master/Documentation/ja/Usage.md";
				}

				culture = culture.Parent;
			}

			// ToDo: English Pages
			return "https://github.com/ipponshimeji/MAPE";
		}

		#endregion


		#region event handler

		private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e) {
			try {
				switch (e.Mode) {
					case PowerModes.Suspend:
						SuspendProxy();
						break;
					case PowerModes.Resume:
						ResumeProxy();
						break;
				}
			} catch (Exception exception) {
				LogError($"Fail to {e.Mode}: {exception.Message}");
				// continue
			}
		}

		#endregion


		#region entry point

		[STAThread]
		static void Main(string[] args) {
			using (Command command = new Command()) {
				command.Run(args);
			}

			return;
		}

		#endregion
	}
}
