﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;


namespace MAPE.Http {
	public abstract class Message: IDisposable {
		#region data

		private HeaderBuffer headerBuffer;

		private BodyBuffer bodyBuffer;

		private List<MessageBuffer.Modification> modifications;

		private Stream output;

		public MessageReadingState ReadingState {
			get;
			private set;
		}

		public Version Version {
			get;
			protected set;
		}

		public long ContentLength {
			get;
			protected set;
		}

		public Span EndOfHeaderFields {
			get;
			protected set;
		}

		#endregion


		#region properties

		public bool IsStreamAttached {
			get {
				return this.headerBuffer.CanRead || this.output != null;
			}
		}

		public bool MessageRead {
			get {
				return this.ReadingState == MessageReadingState.Body;
			}
		}

		protected Stream Output {
			get {
				return this.output;
			}
		}

		protected IReadOnlyList<MessageBuffer.Modification> Modifications {
			get {
				return this.modifications;
			}
		}

		#endregion


		#region creation and disposal

		protected Message() {
			// initialize members
			this.headerBuffer = new HeaderBuffer();
			this.bodyBuffer = new BodyBuffer();
			this.modifications = new List<MessageBuffer.Modification>();
			ResetThisClassLevelMessageProperties();

			return;
		}

		public void Dispose() {
			this.modifications.Clear();
			this.modifications = null;
			this.bodyBuffer.Dispose();
			this.headerBuffer.Dispose();

			return;
		}

		#endregion


		#region methods - lifecycle

		/// <summary>
		/// 
		/// </summary>
		/// <param name="input"></param>
		/// <param name="output"></param>
		/// <remarks>
		/// This object does not own the ownership of <paramref name="input"/> and <paramref name="output"/> .
		/// That is, this object does not Dispose them in its Detach() call.
		/// </remarks>
		public void AttachStreams(Stream input, Stream output) {
			// argument checks
			Debug.Assert(input != null || output != null);

			// attach input and output
			this.headerBuffer.AttachStream(input);
			this.bodyBuffer.AttachStream(this.headerBuffer);
			this.output = output;

			Debug.Assert(this.ContentLength == 0);

			return;
		}

		public void DetachStreams() {
			// reset message properties read from input
			ResetMessageProperties();

			// detach input and output
			this.output = null;
			this.bodyBuffer.DetachStream();
			this.headerBuffer.DetachStream();

			return;
		}

		#endregion


		#region methods

		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		/// <remarks>
		/// The public version of Read() is provided by derived class, Request class and Response class,
		/// utilizing this implementation.
		/// </remarks>
		protected bool Read(Stream input) {
			// argument checks
			if (input == null) {
				throw new ArgumentNullException(nameof(input));
			}

			// read a message from the input
			ReadHeader(input);
			if (this.ReadingState == MessageReadingState.Header) {
				ReadBody(input);
			}

			return this.ReadingState == MessageReadingState.Body;
		}

		protected bool ReadHeader(Stream input) {
			// argument checks
			if (input == null) {
				throw new ArgumentNullException(nameof(input));
			}

			// read header part from the input
			HeaderBuffer headerBuffer = this.headerBuffer;
			// headerBuffer.InputStream = input;
			try {
				// state checks
				Debug.Assert(headerBuffer.CanRead);

				// clear current contents
				ResetMessageProperties();
				headerBuffer.ResetBuffer();
				bodyBuffer.ResetBuffer();
				Debug.Assert(this.ReadingState == MessageReadingState.None);

				// read start line
				ScanStartLine(headerBuffer);

				// read header fields
				bool emptyLine;
				do {
					emptyLine = ScanHeaderField(headerBuffer);
				} while (emptyLine == false);

				// update state
				int endOfHeaderOffset = headerBuffer.CurrentOffset - 2;  // subtract empty line bytes
				this.EndOfHeaderFields = new Span(endOfHeaderOffset, endOfHeaderOffset);
				this.ReadingState = MessageReadingState.Header;
			} catch (EndOfStreamException) {
				// no data from input
				// Note that incomplete data results an exception other than EndOfStreamException.
				Debug.Assert(this.ReadingState == MessageReadingState.None);
				// continue
			} catch {
				this.ReadingState = MessageReadingState.Error;
				throw;
			} finally {
//				headerBuffer.InputStream = null;
			}

			return this.ReadingState == MessageReadingState.Header;
		}

		protected void ReadBody(Stream input) {
			// argument checks
			if (input == null) {
				// at this point, input is dummy
				throw new ArgumentNullException(nameof(input));
			}

			// state checks
			if (this.ReadingState != MessageReadingState.Header) {
				throw new InvalidOperationException("The current position is not the end of the header.");
			}

			// read body part from the input
			// Note that the bodyBuffer reads bytes through the headerBuffer.
			HeaderBuffer headerBuffer = this.headerBuffer;
			// headerBuffer.InputStream = input;
			try {
				// state checks
				Debug.Assert(headerBuffer.CanRead);
				Debug.Assert(bodyBuffer.CanRead);

				// read body
				ScanBody(this.bodyBuffer);
				this.ReadingState = MessageReadingState.Body;
			} catch {
				this.ReadingState = MessageReadingState.Error;
				throw;
			} finally {
				//headerBuffer.InputStream = null;
			}

			return;
		}

		public void Write(Stream output, bool suppressModification = false) {
			// argument checks
			if (output == null) {
				throw new ArgumentNullException(nameof(output));
			}
			if (output.CanWrite == false) {
				throw new ArgumentException("It is not writable", nameof(output));
			}

			// state checks
			if (this.ReadingState != MessageReadingState.Body) {
				throw new InvalidOperationException("The body part is not read.");
			}

			// write a message
			IEnumerable<MessageBuffer.Modification> modifications = (suppressModification == false && 0 < this.modifications.Count) ? this.modifications : null;
			WriteHeader(output, this.headerBuffer, modifications);
			WriteBody(output, this.bodyBuffer);

			return;
		}

		public void Redirect(Stream output, Stream input, bool suppressModification = false) {
			// argument checks
			if (output == null) {
				throw new ArgumentNullException(nameof(output));
			}
			if (output.CanWrite == false) {
				throw new ArgumentException("It is not writable", nameof(output));
			}

			// state checks
			if (this.ReadingState != MessageReadingState.Header) {
				throw new InvalidOperationException();	// ToDo: message
			}

			// write/redirect a message
			IEnumerable<MessageBuffer.Modification> modifications = (0 < this.modifications.Count) ? this.modifications : null;
			WriteHeader(output, this.headerBuffer, modifications);
			RedirectBody(output, input, this.bodyBuffer);

			// update state
			this.ReadingState = MessageReadingState.BodyRedirected;

			return;
		}

		public void Write() {
			// state checks
			Stream output = this.output;
			if (output == null) {
				throw new InvalidOperationException();
			}

			Write(output);
		}

		public void ClearModifications() {
			this.modifications.Clear();
		}

		public void AddModification(Span span, Func<Modifier, bool> handler) {
			// argument checks
			Debug.Assert(0 <= span.Start);
			Debug.Assert(span.Start <= span.End);
			// handler can be null

			// state checks
			List<MessageBuffer.Modification> modifications = this.modifications;
			Debug.Assert(modifications != null);

			// find the insertion point
			int index = modifications.Count;
			for (int i = 0; i < modifications.Count; ++i) {
				MessageBuffer.Modification modification = modifications[i];
				if (span.End < modification.Start) {
					index = i;
					break;
				} else if (span.End == modification.Start) {
					if (0 < modification.Length || 0 < span.Length) {
						index = i;
						break;
					}
					// continue
					// keep order of the inserting (0-length) modifications on the same point 
				} else if (span.Start < modification.End) {
					// overlapped
					throw new ArgumentException("It conflicts with an existing span.", nameof(span));
				}
			}

			// insert a modification
			modifications.Insert(index, new MessageBuffer.Modification(span, handler));

			return;
		}

		#endregion


		#region overridables

		protected virtual void ResetMessageProperties() {
			// reset message properties
			ResetThisClassLevelMessageProperties();

			return;
		}

		protected abstract void ScanStartLine(HeaderBuffer headerBuffer);

		protected virtual bool ScanHeaderField(HeaderBuffer headerBuffer) {
			// argument checks
			Debug.Assert(headerBuffer != null);

			// read the first byte
			bool emptyLine;
			Func<byte, bool> hasInterest = (b) => {
				char c = Char.ToLower((char)b);
				return IsInterestingHeaderFieldFirstChar(c);
			};

			byte firstByte = headerBuffer.ReadFieldNameFirstByte();
			if (firstByte == MessageBuffer.CR || hasInterest(firstByte) == false) {
				// no interest, just skip this line
				emptyLine = headerBuffer.SkipField(firstByte);
			} else {
				// scan this field
				int startOffset = headerBuffer.CurrentOffset - 1;	// Note we have already read one byte
				string decapitalizedFieldName = headerBuffer.ReadFieldName(firstByte);
				ScanHeaderFieldValue(headerBuffer, decapitalizedFieldName, startOffset);
				emptyLine = false;
			}

			return emptyLine;
		}

		protected virtual bool IsInterestingHeaderFieldFirstChar(char decapitalizedFirstChar) {
			switch (decapitalizedFirstChar) {
				case 'c':   // possibly "content-length"
				case 't':   // possibly "transfer-encoding"
					return true;
				default:
					return false;
			}
		}

		protected virtual void ScanHeaderFieldValue(HeaderBuffer headerBuffer, string decapitalizedFieldName, int startOffset) {
			// argument checks
			Debug.Assert(headerBuffer != null);

			string value;
			switch (decapitalizedFieldName) {
				case "content-length":
					value = headerBuffer.ReadFieldASCIIValue(decapitalize: false);
					this.ContentLength = HeaderBuffer.ParseHeaderFieldValueAsLong(value);
					break;
				case "transfer-encoding":
					value = headerBuffer.ReadFieldASCIIValue(decapitalize: true);
					if (HeaderBuffer.IsChunkedSpecified(value) == false) {
						throw MessageBuffer.CreateBadRequestException();
					}
					this.ContentLength = -1;	// -1 means 'chunked'
					break;
				default:
					// just skip
					headerBuffer.SkipField();
					break;
			}
		}

		protected virtual void ScanBody(BodyBuffer bodyBuffer) {
			// argument checks
			Debug.Assert(bodyBuffer != null);

			// scan body
			switch (this.ContentLength) {
				case -1:
					// chunked body
					bodyBuffer.SkipChunkedBody();
					break;
				case 0:
					// no body
					break;
				default:
					// simple body
					bodyBuffer.SkipBody(this.ContentLength);
					break;
			}

			return;
		}

		protected virtual void WriteHeader(Stream output, HeaderBuffer headerBuffer, IEnumerable<MessageBuffer.Modification> modifications) {
			// argument checks
			Debug.Assert(output != null);
			Debug.Assert(headerBuffer != null);
			// modifications can be null

			// write message header
			headerBuffer.WriteHeader(output, modifications);
		}

		protected virtual void WriteBody(Stream output, BodyBuffer bodyBuffer) {
			// argument checks
			Debug.Assert(output != null);
			Debug.Assert(bodyBuffer != null);

			// write message body
			bodyBuffer.WriteBody(output);
		}

		protected virtual void RedirectBody(Stream output, Stream input, BodyBuffer bodyBuffer) {
			// argument checks
			Debug.Assert(output != null);
			Debug.Assert(input != null);
			Debug.Assert(bodyBuffer != null);

			// write message body
			bodyBuffer.RedirectBody(output, input, this.ContentLength);
		}

		#endregion


		#region privates

		private void ResetThisClassLevelMessageProperties() {
			// reset message properties of this class level
			this.modifications.Clear();
			this.ReadingState = MessageReadingState.None;
			this.Version = null;
			this.ContentLength = 0;
			this.EndOfHeaderFields = Span.ZeroToZero;

			return;
		}

		#endregion
	}
}
