﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using MAPE.Utils;


namespace MAPE.Http {
	public class Request: Message {
		#region data

		public string Method {
			get;
			protected set;
		}

		public DnsEndPoint HostEndPoint {
			get;
			protected set;
		}

		public Uri Uri {
			get;
			protected set;
		}

		public MessageBuffer.Span ProxyAuthorizationSpan {
			get;
			protected set;
		}

		#endregion


		#region properties

		public string Host {
			get {
				DnsEndPoint endPoint = this.HostEndPoint;
				return (endPoint == null) ? string.Empty : $"{endPoint.Host}:{endPoint.Port}";
			}
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
			if (string.IsNullOrEmpty(target) == false) {
				char firstChar = target[0];
				if (firstChar != '/' && firstChar != '*') {
					// absolute-form or authority-form
					Uri uri = null;
					DnsEndPoint hostEndPoint = null;

					if (target.Contains("://")) {
						// maybe absolute-form 
						try {
							uri = new Uri(target);
							hostEndPoint = new DnsEndPoint(uri.Host, uri.Port);
						} catch {
							// continue
						}
					} else {
						// maybe authority-form 
						try {
							// assume https scheme
							uri = new Uri($"https://{target}");
							hostEndPoint = new DnsEndPoint(uri.Host, uri.Port);
							uri = null; // this.Uri is not set in case of authority-form 
						} catch {
							// continue
						}
					}
					this.HostEndPoint = hostEndPoint;
					this.Uri = uri;
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
					if (this.HostEndPoint == null) {
						string hostValue = HeaderBuffer.TrimHeaderFieldValue(headerBuffer.ReadFieldASCIIValue(false));
						this.HostEndPoint = Util.ParseEndPoint(hostValue);
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
			this.HostEndPoint = null;
			this.Uri = null;
			this.ProxyAuthorizationSpan = MessageBuffer.Span.ZeroToZero;

			return;
		}

		#endregion
	}
}
