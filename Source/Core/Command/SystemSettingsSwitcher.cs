﻿using System;
using System.Diagnostics;
using System.Net;
using MAPE.Utils;
using MAPE.Server;
using SettingNames = MAPE.Command.SystemSettingsSwitcher.SettingNames;


namespace MAPE.Command {
	public static class SystemSettingsSwitcherSettingsExtensions {
		#region methods

		public static WebProxy GetWebProxyValue(this Settings settings, string settingName, WebProxy defaultValue) {
			Settings.Value value = settings.GetValue(settingName);
			if (value.IsNull == false) {
				return value.GetObjectValue().CreateWebProxy();
			} else {
				return defaultValue;
			}
		}

		public static WebProxy CreateWebProxy(this Settings settings) {
			Settings.Value host = settings.GetValue(SettingNames.Host);
			Settings.Value port = settings.GetValue(SettingNames.Port);
			if (host.IsNull || port.IsNull) {
				throw new FormatException($"Both '{SettingNames.Host}' and '{SettingNames.Port}' settings are indispensable.");
			}

			return new WebProxy(host.GetStringValue(), port.GetInt32Value());
		}

		#endregion
	}

	public class SystemSettingsSwitcher {
		#region types

		public static class SettingNames {
			#region constants

			public const string EnableSystemSettingsSwitch = "EnableSystemSettingsSwitch";

			public const string ActualProxy = "ActualProxy";

			public const string Host = "Host";

			public const string Port = "Port";

			#endregion
		}

		#endregion


		#region data

		public readonly CommandBase Owner;

		public bool Enabled { get; protected set; } = true;

		public IWebProxy ActualProxy { get; protected set; } = null;

		#endregion


		#region creation and disposal

		/// <summary>
		/// 
		/// </summary>
		/// <param name="owner"></param>
		/// <param name="settings"></param>
		/// <param name="proxy">null means initialization for backup.</param>
		public SystemSettingsSwitcher(CommandBase owner, Settings settings, Proxy proxy) {
			// argument checks
			if (owner == null) {
				throw new ArgumentNullException(nameof(owner));
			}
			if (settings.IsNull == false && proxy == null) {
				// proxy is indispensable if settings has contents
				throw new ArgumentNullException(nameof(proxy));
			}
			// settings can contain null

			// initialize members
			this.Owner = owner;

			bool enabled;
			WebProxy actualProxy;
			if (proxy == null) {
				// simple initialization for backup
				Debug.Assert(settings.IsNull);
				enabled = true;
				actualProxy = null;
			} else {
				// usual initialization
				enabled = settings.GetBooleanValue(SettingNames.EnableSystemSettingsSwitch, defaultValue: true);
				actualProxy = settings.GetWebProxyValue(SettingNames.ActualProxy, defaultValue: null);
				if (actualProxy == null) {
					actualProxy = DetectSystemProxy();
					if (actualProxy == null) {
						throw new Exception(Properties.Resources.SystemSettingsSwitcher_NoActualProxy);
					}
				}

				// log
				if (owner.ShouldLog(TraceEventType.Verbose)) {
					Uri address = actualProxy.Address;
					owner.LogVerbose($"ActualProxy is {address.Host}:{address.Port}");
					string label = enabled ? "enabled" : "disabled";
					owner.LogVerbose($"SystemSettingsSwitch: {label}");
				}
			}

			this.Enabled = enabled;
			this.ActualProxy = actualProxy;

			return;
		}

		#endregion


		#region methods

		public SystemSettingsSwitcher Switch(bool makeBackup) {
			// get back if necessary
			SystemSettingsSwitcher backup = null;
			if (this.Enabled == false) {
				return null;
			}
			if (makeBackup) {
				backup = GetCurrentSettings();
			}

			// switch the system setting
			try {
				if (Switch(backup) == false) {
					// actually, not switched
					// backup is no use
					backup = null;
				}
			} catch {
				if (backup != null) {
					try {
						backup.Switch(null);
					} catch {
						// continue
					}
				}
				throw;
			}

			// notify the system setting change
			try {
				NotifySwitched();
			} catch (Exception exception) {
				this.Owner.LogVerbose($"Error on notifying system setting switch: {exception.Message}");
				// not fatal, continue
			}

			return backup;
		}

		protected SystemSettingsSwitcher GetCurrentSettings() {
			// create a new SystemSettingsSwitcher instance
			SystemSettingsSwitcher switcher = this.Owner.ComponentFactory.CreateSystemSettingsSwitcher(this.Owner, Settings.NullSettings, null);

			// load the current system settings into the instance
			switcher.LoadCurrentSettings();

			return switcher;			
		}

		#endregion


		#region overridables

		protected virtual void LoadCurrentSettings() {
			return;
		}

		protected virtual bool Switch(SystemSettingsSwitcher backup) {
			// argument checks
			// backup can be null

			return false;	// not switched, by default
		}

		protected virtual void NotifySwitched() {
			return;
		}

		#endregion


		#region privates

		private WebProxy DetectSystemProxy() {
			// detect the system web proxy by try to give external urls
			// ToDo: return IWebProxy which can emulate the *.pac file currently effective.
			// Note this implementation simply detect a possible typical proxy.
			// Actual system logic to select proxy may be complicated,
			// for example, it may be scripted by an auto configuration script (*.pac).
			// If WebRequest.GetSystemWebProxy() returns IWebProxy of fixed logic at this point,
			// it can be returned simply here.
			// But this IWebProxy instance will reflect upcoming system proxy switch.
			// So this implementation returns fixed address IWebProxy.
			IWebProxy proxy = WebRequest.GetSystemWebProxy();
			Func<string, WebProxy> detect = (sampleExternalUrl) => {
				Uri sampleUri = new Uri(sampleExternalUrl);
				WebProxy value = null;
				if (proxy.IsBypassed(sampleUri) == false) {
					Uri uri = proxy.GetProxy(sampleUri);
					if (uri != sampleUri) {
						// uri seems to be a proxy
						value = new WebProxy(uri.Host, uri.Port);
					}
				}
				return value;
			};

			// try with google's URL
			WebProxy systemProxy = detect("http://www.google.com/");
			if (systemProxy == null) {
				// try with Microsoft's URL
				systemProxy = detect("http://www.microsoft.com/");
			}

			return systemProxy;	// may be null
		}

		#endregion
	}
}
