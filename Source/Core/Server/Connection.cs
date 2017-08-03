﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MAPE.ComponentBase;
using MAPE.Http;
using MAPE.Utils;

namespace MAPE.Server {
    public class Connection: TaskingComponent, ICommunicationOwner {
		#region constants

		public const string ComponentNameBase = "Connection";

		#endregion


		#region data

		private ConnectionCollection owner = null;

		#endregion


		#region data - synchronized by locking this

		private int retryCount;

		private TcpClient client = null;

		private ReconnectableTcpClient server = null;

		private bool usingProxy = false;

		private Proxy.BasicCredential proxyCredential = null;

		#endregion


		#region properties

		public Proxy Proxy {
			get {
				return this.owner.Owner;
			}
		}

		public IServerComponentFactory ComponentFactory {
			get {
				return this.owner.ComponentFactory;
			}
		}

		#endregion


		#region creation and disposal

		public Connection(): base(allocateComponentId: false) {
			// initialize members
			this.ComponentName = ComponentNameBase;

			return;
		}

		public override void Dispose() {
			// stop communicating
			StopCommunication();
		}


		public void ActivateInstance(ConnectionCollection owner) {
			// argument checks
			Debug.Assert(owner != null);

			lock (this) {
				// state checks
				if (this.owner != null) {
					throw new InvalidOperationException("The instance is in use.");
				}

				// initialize members
				this.ParentComponentId = owner.Owner.ComponentId;
				this.ComponentId = Logger.AllocComponentId();
				this.owner = owner;
				this.retryCount = owner.Owner.RetryCount;
				Debug.Assert(this.client == null);
				Debug.Assert(this.server == null);
				Debug.Assert(this.proxyCredential == null);
			}

			return;
		}

		public void DeactivateInstance() {
			lock (this) {
				// state checks
				if (this.owner == null) {
					// already deactivated
					return;
				}
				if (this.client != null) {
					throw new InvalidOperationException("The instance is still working.");
				}

				// uninitialize members
				Debug.Assert(this.proxyCredential == null);
				Debug.Assert(this.server == null);
				Debug.Assert(this.client == null);
				this.owner = null;
				this.Task = null;
			}

			return;
		}

		#endregion


		#region methods

		public void StartCommunication(TcpClient client) {
			// argument checks
			if (client == null) {
				throw new ArgumentNullException(nameof(client));
			}

			try {
				lock (this) {
					// log
					bool verbose = ShouldLog(TraceEventType.Verbose);
					if (verbose) {
						LogVerbose($"Starting for {client.Client.RemoteEndPoint.ToString()} ...");
					}

					// state checks
					if (this.owner == null) {
						throw new ObjectDisposedException(this.ComponentName);
					}

					Task communicatingTask = this.Task;
					if (communicatingTask != null) {
						throw new InvalidOperationException("It already started communication.");
					}
					communicatingTask = new Task(Communicate);
					communicatingTask.ContinueWith(
						(t) => {
							LogVerbose("Stopped.");
							this.ComponentName = ComponentNameBase;
							this.owner.OnConnectionCompleted(this);
						}
					);
					this.Task = communicatingTask;

					this.ComponentName = $"{ComponentNameBase} <{this.ComponentId}>";
					Debug.Assert(this.client == null);
					this.client = client;

					// start communicating task
					communicatingTask.Start();

					// log
					if (verbose) {
						LogVerbose("Started.");
					}
				}
			} catch (Exception exception) {
				LogError($"Fail to start: {exception.Message}");
				throw;
			}

			return;
		}

		public bool StopCommunication(int millisecondsTimeout = 0) {
			bool stopConfirmed = false;
			try {
				Task communicatingTask;
				lock (this) {
					// state checks
					if (this.owner == null) {
						throw new ObjectDisposedException(this.ComponentName);
					}

					communicatingTask = this.Task;
					if (communicatingTask == null) {
						// already stopped
						return true;
					}
					LogVerbose("Stopping...");

					// force the connections to close
					// It will cause exceptions on I/O in communicating thread.
					CloseTcpConnections();
				}

				// wait for the completion of the listening task
				// Note that -1 timeout means 'Infinite'.
				if (millisecondsTimeout != 0) {
					stopConfirmed = communicatingTask.Wait(millisecondsTimeout);
				}

				// log
				// "Stopped." will be logged in the continuation of the communicating task. See StartCommunication().
			} catch (Exception exception) {
				LogError($"Fail to stop: {exception.Message}");
				throw;
			}

			return stopConfirmed;
		}

		#endregion


		#region overridables

		protected virtual IEnumerable<MessageBuffer.Modification> GetModifications(Request request, Response response) {
			// argument checks
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}
			// response may be null

			// Currently only basic authorization for the proxy is handled.

			// ToDo: thread protection if pipe line mode is supported.
			IReadOnlyCollection<byte> overridingProxyAuthorization;
			if (response == null) {
				// first request
				if (request.ProxyAuthorizationSpan.IsZeroToZero == false) {
					// the client specified Proxy-Authorization
					overridingProxyAuthorization = null;
				} else {
					Proxy.BasicCredential proxyCredential = this.proxyCredential;
					if (proxyCredential == null) {
						string endPoint = this.server.EndPoint;
						string realm = null;
						proxyCredential = this.Proxy.GetServerBasicCredentials(endPoint, realm, firstRequest: true, oldBasicCredentials: null);
						this.proxyCredential = proxyCredential;	// may be null
					}

					overridingProxyAuthorization = proxyCredential?.Bytes;
				}
			} else {
				// re-sending request
				if (response.StatusCode == 407) {
					// 407: Proxy Authentication Required
					// the current credential seems to be invalid (or null)
					string endPoint = this.server.EndPoint;
					string realm = "Proxy";	// ToDo: extract realm from the field
					Proxy.BasicCredential proxyCredential = this.Proxy.GetServerBasicCredentials(endPoint, realm, firstRequest: false, oldBasicCredentials: this.proxyCredential);
					this.proxyCredential = proxyCredential;
					overridingProxyAuthorization = proxyCredential?.Bytes;
				} else {
					// no need to resending
					overridingProxyAuthorization = null;
				}
			}

			MessageBuffer.Modification[] modifications;
			if (overridingProxyAuthorization == null) {
				modifications = null;
			} else {
				modifications = new MessageBuffer.Modification[] {
					new MessageBuffer.Modification(
						request.ProxyAuthorizationSpan.IsZeroToZero? request.EndOfHeaderFields: request.ProxyAuthorizationSpan,
						(mb) => { mb.Write(overridingProxyAuthorization); return true; }	
					)
				};
			}

			return modifications;
		}

		#endregion


		#region ICommunicationOwner - for Communication class only

		IHttpComponentFactory ICommunicationOwner.ComponentFactory {
			get {
				return this.ComponentFactory.HttpComponentFactory;
			}
		}

		IComponentLogger ICommunicationOwner.Logger {
			get {
				return this;
			}
		}

		bool ICommunicationOwner.UsingProxy {
			get {
				return this.usingProxy;
			}
		}

		IEnumerable<MessageBuffer.Modification> ICommunicationOwner.OnCommunicate(int repeatCount, Request request, Response response) {
			// argument checks
			if (request == null) {
				throw new ArgumentNullException(nameof(request));
			}
			if (request.HostEndPoint == null) {
				throw new HttpException(HttpStatusCode.BadRequest);
			}
			if (response == null && repeatCount != 0) {
				throw new ArgumentNullException(nameof(response));
			}

			// preparations
			ReconnectableTcpClient server = this.server;
			if (server == null) {
				throw new InvalidOperationException("The server connection has been closed.");
			}
			Action<string, int, Exception> onConnectionError = (h, p, e) => {
				LogError($"Cannot connect to the actual proxy '{h}:{p}': {e.Message}");
				throw new HttpException(HttpStatusCode.InternalServerError, "Not Connected to Actual Proxy");
			};
			bool logVerbose = ShouldLog(TraceEventType.Verbose);

			// start connection
			if (response == null) {
				// the start of a request
				// connect to the server
				IActualProxy actualProxy = this.Proxy.ActualProxy;
				IReadOnlyCollection<DnsEndPoint> remoteEndPoints = null;
				if (actualProxy != null) {
					if (request.Uri != null) {
						remoteEndPoints = actualProxy.GetProxyEndPoints(request.Uri);
					} else {
						remoteEndPoints = actualProxy.GetProxyEndPoints(request.HostEndPoint);
					}
				}
				if (remoteEndPoints != null) {
					this.usingProxy = true;
				} else {
					remoteEndPoints = new DnsEndPoint[] {
						request.HostEndPoint
					};
					this.usingProxy = false;
				}

				try {
					server.EnsureConnect(remoteEndPoints);
				} catch (Exception exception) {
					// the case that server connection is not available
					this.usingProxy = false;
					onConnectionError(null, 0, exception);
				}
			}

			// retry checks and get modifications
			IEnumerable<MessageBuffer.Modification> modifications = null;
			if (repeatCount <= this.retryCount) {
				// get actual modifications
				modifications = GetModifications(request, response);
			} else {
				LogWarning("Overruns the retry count. Responding the current response.");
			}

			// log and Keep-Alive check
			if (response != null) {
				// log the round trip result
				LogRoundTripResult(request, response, modifications != null);

				// manage the connection for non-Keep-Alive mode
				if (response.KeepAliveEnabled == false && !(response.StatusCode == 200 && request.Method == "CONNECT")) {
					// disconnect the server connection
					server.Disconnect();

					// re-connect the server connection if the request is going to be re-sent
					if (modifications != null) {
						try {
							server.EnsureConnect();
						} catch (Exception exception) {
							// the case that server connection is not available
							onConnectionError(server.Host, server.Port, exception);
						}
					}
				}
			}

			return modifications;
		}

		HttpException ICommunicationOwner.OnError(Request request, Exception exception) {
			// argument checks
			// request can be null
			// exception can be null

			HttpException httpException = null;
			try {
				// interpret the exception to HttpException
				// Null httpError means no need to send any error message to the client.
				if (exception is EndOfStreamException) {
					// an EndOfStreamException means disconnection at an appropriate timing.
					LogVerbose($"The communication ends normally.");
				} else {
					if (exception != null && request != null && request.MessageRead) {
						httpException = exception as HttpException;
						if (httpException == null) {
							httpException = new HttpException(exception);
							Debug.Assert(httpException.HttpStatusCode == HttpStatusCode.InternalServerError);
						}
					}

					// log the state
					if (exception != null) {
						// report the original exception message (not httpException's)
						LogError($"Error: {exception.Message}");
					}
					if (httpException != null) {
						string method = request?.Method;
						if (string.IsNullOrEmpty(method)) {
							method = "(undetected method)";
						}
						LogError($"Trying to respond an error response: {method} -> {httpException.StatusCode}, {request?.Host}");
					}
				}
			} catch {
				// continue
				// this method should not throw any exception
			}

			return httpException;
		}

		void ICommunicationOwner.OnTunnelingStarted(CommunicationSubType communicationSubType) {
			// log
			switch (communicationSubType) {
				case CommunicationSubType.Session:
					Debug.Assert(this.server != null);
					this.server.Reconnectable = false;	// no need to reconnect in tunneling mode
					LogVerbose("Started tunneling mode.");
					break;
				case CommunicationSubType.UpStream:
				case CommunicationSubType.DownStream:
					LogVerbose($"Started {communicationSubType.ToString()} tunneling.");
					break;
			}

			return;
		}

		void ICommunicationOwner.OnTunnelingClosing(CommunicationSubType communicationSubType, Exception exception) {
			switch (communicationSubType) {
				case CommunicationSubType.Session:
					// log
					if (exception != null) {
						LogError($"Error: {exception.Message}");
					}
					LogVerbose("Closing tunneling mode.");
					break;
				case CommunicationSubType.UpStream:
				case CommunicationSubType.DownStream:
					string direction = communicationSubType.ToString();
					if (exception != null) {
						StopCommunication();

						TraceEventType eventType = TraceEventType.Error;
						if (exception is IOException) {
							SocketException socketException = exception.InnerException as SocketException;
							if (socketException != null) {
								switch (socketException.SocketErrorCode) {
									case SocketError.ConnectionReset:
									case SocketError.Interrupted:
									case SocketError.TimedOut:
										// may be terminated
										eventType = TraceEventType.Warning;
										break;
								}
							}
						}
						Log(eventType, $"Error in {direction} tunneling: {exception.Message}");
					} else {
						// shutdown the communication
						bool downStream = (communicationSubType == CommunicationSubType.DownStream);
						lock (this) {
							if (this.server != null) {
								Socket socket = this.server.Client;
								socket.Shutdown(downStream ? SocketShutdown.Receive : SocketShutdown.Send);
							}
							if (this.client != null) {
								Socket socket = this.client.Client;
								socket.Shutdown(downStream ? SocketShutdown.Send : SocketShutdown.Receive);
							}
						}
					}
					LogVerbose($"Closing {communicationSubType.ToString()} tunneling.");
					break;
			}
		}

		#endregion


		#region privates

		/// <summary>
		/// 
		/// </summary>
		/// <remarks>
		/// Note that this method is not thread safe.
		/// You must call this method inside a lock(this) scope.
		/// </remarks>
		private void CloseTcpConnections() {
			TcpClient client;
			ReconnectableTcpClient server;

			// close server connection
			server = this.server;
			this.server = null;
			client = this.client;
			this.client = null;

			if (server != null) {
				try {
					server.Dispose();
				} catch (Exception exception) {
					LogVerbose($"Exception on closing server connection: {exception.Message}");
					// continue
				}
			}
			if (client != null) {
				try {
					client.Close();
				} catch (Exception exception) {
					LogVerbose($"Exception on closing client connection: {exception.Message}");
					// continue
				}
			}

			return;
		}

		private void Communicate() {
			// preparations
			ConnectionCollection owner;
			Proxy proxy;
			TcpClient client;
			ReconnectableTcpClient server = new ReconnectableTcpClient();
			lock (this) {
				owner = this.owner;
				proxy = this.Proxy;
				client = this.client;
				this.server = server;
			}

			// communicate
			try {
				using (NetworkStream clientStream = this.client.GetStream()) {
					using (Stream serverStream = server.GetStream()) {
						Communication.Communicate(this, clientStream, serverStream);
					}
				}
			} catch (Exception exception) {
				LogError($"Error: {exception.Message}");
				// continue
			} finally {
				LogVerbose("Communication completed.");
				lock (this) {
					this.proxyCredential = null;
					CloseTcpConnections();
				}
			}

			return;
		}

		private void LogRoundTripResult(Request request, Response response, bool retrying) {
			// argument checks
			Debug.Assert(request != null);
			Debug.Assert(response != null);

			// log the result of one round trip 
			try {
				int statusCode = response.StatusCode;
				string heading = retrying ? "Retrying" : "Respond";
				string message = $"{heading}: {request.Method} -> {statusCode}, {request.Host}";

				if (statusCode < 400) {
					LogInformation(message);
				} else if (statusCode == 407) {
					LogWarning(message);
				} else {
					LogError(message);
				}
			} catch {
				// continue
				// this method should not throw any exception
			}

			return;
		}

		#endregion
	}
}
