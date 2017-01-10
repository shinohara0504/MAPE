﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;


namespace MAPE.Http {
	public class Request: Message {
		#region data

		public string Method {
			get;
			protected set;
		}

		public string Host {
			get;
			protected set;
		}

		public MessageBuffer.Span ProxyAuthorizationSpan {
			get;
			protected set;
		}

		#endregion


		#region creation and disposal

		public Request(): base() {
			// initialize members
			ResetThisClassLevelMessageProperties();

			return;
		}

		#endregion


		#region methods

		public new bool Read() {
			return base.Read();
		}

		#endregion


		#region overrides/overridables

		protected override void ResetMessageProperties() {
			// reset this class level
			ResetThisClassLevelMessageProperties();

			// reset the base class level
			base.ResetMessageProperties();
		}

		protected override void ScanStartLine(HeaderBuffer headerBuffer) {
			// argument checks
			Debug.Assert(headerBuffer != null);

			// read items
			string method = headerBuffer.ReadSpaceSeparatedItem(skipItem: false, decapitalize: false, lastItem: false);
			string target = headerBuffer.ReadSpaceSeparatedItem(skipItem: false, decapitalize: false, lastItem: false);
			string httpVersion = headerBuffer.ReadSpaceSeparatedItem(skipItem: false, decapitalize: false, lastItem: true);

			// set message properties
			this.Method = method;
			this.Version = HeaderBuffer.ParseVersion(httpVersion);
			if (string.IsNullOrEmpty(target) == false &&  target[0] != '/') {
				// adjust target
				// ToDo: is this secure?
				if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == false && target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == false) {
					target = $"http://{target}";
				}
				try {
					Uri uri = new Uri(target);
					this.Host = $"{uri.Host}:{uri.Port}";
				} catch {
					// continue
				}
			}

			return;
		}

		protected override bool IsInterestingHeaderFieldFirstChar(char decapitalizedFirstChar) {
			switch (decapitalizedFirstChar) {
				case 'h':   // possibly "host"
					return true;
				case 'p':   // possibly "proxy-authorization"
					return true;
				default:
					return base.IsInterestingHeaderFieldFirstChar(decapitalizedFirstChar);
			}
		}

		protected override void ScanHeaderFieldValue(HeaderBuffer headerBuffer, string decapitalizedFieldName, int startOffset) {
			switch (decapitalizedFieldName) {
				case "host":
					// save its value, but its span is unnecessary
					if (string.IsNullOrEmpty(this.Host)) {
						this.Host = HeaderBuffer.TrimHeaderFieldValue(headerBuffer.ReadFieldASCIIValue(false));
					} else {
						headerBuffer.SkipField();
					}
					break;
				case "proxy-authorization":
					// save its span, but its value is unnecessary
					headerBuffer.SkipField();
					this.ProxyAuthorizationSpan = new MessageBuffer.Span(startOffset, headerBuffer.CurrentOffset);
					break;
				default:
					base.ScanHeaderFieldValue(headerBuffer, decapitalizedFieldName, startOffset);
					break;
			}
		}

		#endregion


		#region privates

		private void ResetThisClassLevelMessageProperties() {
			// reset message properties of this class level
			this.Method = null;
			this.Host = null;
			this.ProxyAuthorizationSpan = MessageBuffer.Span.ZeroToZero;

			return;
		}

		#endregion
	}
}
