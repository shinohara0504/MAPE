﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;


namespace MAPE.Http {
	public class HeaderBuffer: MessageBuffer {
		#region data

		private static readonly char[] WS = new char[] { (char)SP, (char)HTAB };    // SP, HTAB


		private Stream input;

		private List<byte[]> memoryBlocks;

		private int currentMemoryBlockBaseOffset;

		private StringBuilder stockStringBuf;

		#endregion


		#region properties

		public bool CanRead {
			get {
				return this.input != null;
			}
		}

		public int CurrentOffset {
			get {
				return this.currentMemoryBlockBaseOffset + this.Next;
			}
		}

		#endregion


		#region creation and disposal

		public HeaderBuffer(): base() {
			// initialize members
			this.input = null;
			this.memoryBlocks = new List<byte[]>();
			this.currentMemoryBlockBaseOffset = 0;
			this.stockStringBuf = new StringBuilder();

			return;
		}

		public override void Dispose() {
			// ensure detached
			DetachStream();

			// clear resources
			this.stockStringBuf = null;
			this.memoryBlocks = null;

			base.Dispose();
		}

		/// <summary>
		/// </summary>
		/// <param name="input"></param>
		/// <remarks>
		/// This object does not own the ownership of <paramref name="input"/>.
		/// That is, this object does not Dispose it in its Detach() call.
		/// </remarks>
		public void AttachStream(Stream input) {
			// argument checks
			// input may be null

			// state checks
			if (this.input != null) {
				throw new InvalidOperationException("This object already attached streams.");
			}

			// set input
			this.input = input;
			// the buffer state should be 'reset' state 
			Debug.Assert(this.memoryBlocks.Count == 0);
			Debug.Assert(this.currentMemoryBlockBaseOffset == 0);
			Debug.Assert(this.stockStringBuf.Length == 0);
			Debug.Assert(this.Limit == 0);
			Debug.Assert(this.Next == 0);

			return;
		}

		public void DetachStream() {
			// state checks
			if (this.input == null) {
				// nothing to do
				return;
			}

			// do not dispose the input, just clear it
			// This object does not have the ownership of it.
			this.input = null;

			// reset buffer state
			ResetBuffer();

			return;
		}

		#endregion


		#region methods - parse

		public static Version ParseVersion(string value) {
			// argument checks
			if (value == null) {
				throw new ArgumentNullException(nameof(value));
			}
			if (value.StartsWith(VersionPrefix) == false) {
				// invalid syntax
				throw CreateBadRequestException();
			}

			// parse HTTP version
			// This parsing does not check strict syntax, but enough here.
			Version version;
			if (Version.TryParse(value.Substring(VersionPrefix.Length), out version) == false) {
				throw CreateBadRequestException();
			}

			return version;
		}

		public static int ParseStatusCode(string value) {
			// argument checks
			if (value == null) {
				throw new ArgumentNullException(nameof(value));
			}

			// parse status-code
			// This parsing does not check strict syntax, but enough here.
			int statusCode;
			if (int.TryParse(value, out statusCode) == false) {
				throw CreateBadRequestException();
			}

			return statusCode;
		}

		public static string TrimHeaderFieldValue(string fieldValue) {
			// argument checks
			if (fieldValue == null) {
				throw new ArgumentNullException(nameof(fieldValue));
			}

			return fieldValue.Trim(WS);
		}

		public static long ParseHeaderFieldValueAsLong(string fieldValue) {
			// argument checks
			if (fieldValue == null) {
				throw new ArgumentNullException(nameof(fieldValue));
			}
			fieldValue = TrimHeaderFieldValue(fieldValue);

			// parse the field value as long
			// This parsing does not check strict syntax, but enough here.
			long value;
			if (long.TryParse(fieldValue, out value) == false) {
				throw CreateBadRequestException();
			}

			return value;
		}

		public static bool IsChunkedSpecified(string decapitalizedFieldValue) {
			// argument checks
			if (decapitalizedFieldValue == null) {
				throw new ArgumentNullException(nameof(decapitalizedFieldValue));
			}
			decapitalizedFieldValue = TrimHeaderFieldValue(decapitalizedFieldValue);

			// check whether 'chunked' is specified at the last of the fieldValue
			// This parsing does not check strict syntax.
			// This is enough for our use.
			if (decapitalizedFieldValue.EndsWith(ChunkedTransferCoding)) {
				int prevIndex = decapitalizedFieldValue.Length - ChunkedTransferCoding.Length - 1;
				if (prevIndex < 0) {
					return true;
				}
				switch (decapitalizedFieldValue[prevIndex]) {
					case (char)SP:
					case (char)HTAB:
					case (char)Colon:
					case ',':
						return true;
				}
			}

			return false;
		}

		#endregion


		#region methods - read

		public string ReadSpaceSeparatedItem(bool skipItem, bool decapitalize, bool lastItem) {
			string item;

			// read or skip the next item
			bool endOfLine;
			if (skipItem) {
				// skip the next item
				if (lastItem) {
					SkipToCRLF();
					endOfLine = true;
				} else {
					endOfLine = SkipTo(SP);
				}
				item = null;
			} else {
				// read the next item
				StringBuilder stringBuf = this.stockStringBuf;
				Debug.Assert(stringBuf.Length == 0);
				try {
					if (lastItem) {
						ReadASCIIToCRLF(stringBuf, decapitalize);
						endOfLine = true;
					} else {
						endOfLine = ReadASCIITo(SP, stringBuf, decapitalize);
					}
					item = stringBuf.ToString();
				} finally {
					stringBuf.Clear();
				}
			}

			// check status of the line
			if (endOfLine && lastItem == false) {
				// this item should not be the last one in the start line 
				throw CreateBadRequestException();
			}

			return item;
		}

		public byte ReadFieldNameFirstByte() {
			return ReadNextByte();
		}

		public string ReadFieldName(byte firstByte) {
			StringBuilder stringBuf = this.stockStringBuf;
			Debug.Assert(stringBuf.Length == 0);
			try {
				bool endOfLine = ReadASCIITo(Colon, stringBuf, decapitalize: true, firstByte: firstByte);
				if (endOfLine) {
					// no colon is found
					throw CreateBadRequestException();
				}
				return stringBuf.ToString();
			} finally {
				stringBuf.Clear();
			}
		}

		public string ReadFieldASCIIValue(bool decapitalize) {
			StringBuilder stringBuf = this.stockStringBuf;
			Debug.Assert(stringBuf.Length == 0);
			try {
				ReadASCIIToCRLF(stringBuf, decapitalize);
				return stringBuf.ToString();
			} finally {
				stringBuf.Clear();
			}
		}

		public bool SkipField(byte firstByte) {
			return SkipToCRLF(firstByte);
		}

		public bool SkipField() {
			return SkipToCRLF();
		}

		#endregion


		#region methods - write

		public void WriteHeader(Stream output) {
			// argument checks
			if (output == null) {
				throw new ArgumentNullException(nameof(output));
			}
			if (output.CanWrite == false) {
				throw new ArgumentException("It is not writable", nameof(output));
			}

			// write header simply
			byte[] currentMemoryBlock = this.MemoryBlock;
			Debug.Assert(currentMemoryBlock != null);
			foreach (byte[] memoryBlock in this.memoryBlocks) {
				int count;
				if (memoryBlock != currentMemoryBlock) {
					// not the last memory block
					count = memoryBlock.Length;
				} else {
					// the last memory block
					// Note the range [this.Next, this.Limit) is actually a part of the body
					count = this.Next;
				}
				output.Write(memoryBlock, 0, count);
			}
			output.Flush();

			return;
		}

		public void WriteHeader(Stream output, IEnumerable<Modification> modifications) {
			// argument checks
			if (modifications == null) {
				// call the simpler implementation
				WriteHeader(output);
				return;
			}
			if (output == null) {
				throw new ArgumentNullException(nameof(output));
			}
			if (output.CanWrite == false) {
				throw new ArgumentException("It is not writable", nameof(output));
			}

			// Modifications must be sorted and their spans must not be overlapped.
			// This assertion is to detect error in early point.
			Debug.Assert(IsValidModifications(modifications));

			// write header bytes with specified modifications
			using (IEnumerator<byte[]> i = this.memoryBlocks.GetEnumerator()) {
				// preparations
				Modifier modifier = new Modifier(output);
				int currentOffset = 0;
				byte[] memoryBlock = null;
				int next = 0;
				int limit = 0;

				Func<bool> getNextMemoryBlock = () => {
					if (i.MoveNext()) {
						memoryBlock = i.Current;
						next = 0;
						limit = (memoryBlock == this.MemoryBlock)? this.Next: memoryBlock.Length;
						return true;
					} else {
						memoryBlock = null;
						next = 0;
						limit = 0;
						return false;
					}
				};

				Action<int> write = (count) => {
					Debug.Assert(memoryBlock != null);
					Debug.Assert(0 <= next && next < limit);
					Debug.Assert(next <= limit - count);
					output.Write(memoryBlock, next, count);
				};

				Action<int> skip = (count) => {
					Debug.Assert(memoryBlock != null);
					Debug.Assert(0 <= next && next < limit);
					Debug.Assert(next <= limit - count);
				};

				Func<int, Action<int>, bool> handleTo = (end, handler) => {
					int backlog = end - currentOffset;
					while (0 < backlog) {
						// calculate the count to be handled bytes in the memory block
						int count = limit - next;
						if (count == 0) {
							// no more bytes to be handled in the memory block
							// get the next memory block
							if (getNextMemoryBlock() == false) {
								// end of header data
								currentOffset = end - backlog;
								return false;	// no more data
							}
							count = limit - next;
						}
						if (backlog < count) {
							count = backlog;
						}

						// handle the bytes
						handler(count);
						next += count;
						backlog -= count;
					}
					currentOffset = end;

					return true;	// has more data
				};

				Func<int, bool> writeTo = (end) => {
					return handleTo(end, write);
				};

				Func<int, bool> skipTo = (end) => {
					return handleTo(end, skip);
				};


				// handle each span to be modified
				int prevEnd = 0;
				foreach (Modification modification in modifications) {
					// modification checks
					if (modification.Start < prevEnd) {
						throw new ArgumentException("It must be sorted and their spans must not be overlapped", nameof(modifications));
					}
					Debug.Assert(modification.Start <= modification.End);

					// modify output
					if (modification.Handler != null) {
						if (writeTo(modification.Start) == false) {
							// no more data
							break;
						}
						if (modification.Handler(modifier)) {
							// modified
							skipTo(modification.End);
						} else {
							// won't modified
							// write this span in the next iteration
						}
					}

					// prepare the next iteration
					prevEnd = modification.End;
				}
				writeTo(int.MaxValue);
			}
			output.Flush();

			return;
		}

		#endregion


		#region overrides/overridables

		public override void ResetBuffer() {
			// reset this class level
			this.stockStringBuf.Clear();
			this.currentMemoryBlockBaseOffset = 0;
			this.memoryBlocks.ForEach(
				(memoryBlock) => {
					try {
						ComponentFactory.FreeMemoryBlock(memoryBlock);
					} catch {
							// continue
						}
				}
			);
			this.memoryBlocks.Clear();

			// reset the base class level
			base.ResetBuffer();
		}

		protected override byte[] UpdateMemoryBlock(byte[] currentMemoryBlock) {
			// calculate the base offset of the next memory block
			int newBaseOffset = this.currentMemoryBlockBaseOffset;
			if (currentMemoryBlock == null) {
				Debug.Assert(newBaseOffset == 0);
			} else {
				int increment = currentMemoryBlock.Length;
				if (int.MaxValue - newBaseOffset < increment) {
					// header size exceeds 2G
					throw new HttpException(HttpStatusCode.InternalServerError);
				}
				newBaseOffset += currentMemoryBlock.Length;
			}

			// allocate a new memory block
			byte[] newMemoryBlock = ComponentFactory.AllocMemoryBlock();
			try {
				this.memoryBlocks.Add(newMemoryBlock);
			} catch {
				ComponentFactory.FreeMemoryBlock(newMemoryBlock);
				throw;
			}

			// update its state
			this.currentMemoryBlockBaseOffset = newBaseOffset;

			return newMemoryBlock;
		}

		protected override void ReleaseMemoryBlockOnResetBuffer(byte[] memoryBlock) {
			// nothing to do
			// Memory blocks are maintained in this.memoryBlocks,
			// and managed by this class level.
		}

		protected override int ReadBytes(byte[] buffer, int offset, int count) {
			// argument checks
			Debug.Assert(buffer != null);
			Debug.Assert(0 <= offset && offset <= buffer.Length);
			Debug.Assert(0 <= count && count <= buffer.Length - offset);

			// state checks
			if (this.input == null) {
				throw new InvalidOperationException("No input stream is attached to this object.");
			}

			// read bytes from the input
			int readCount = this.input.Read(buffer, offset, count);
			if (readCount <= 0) {
				// end of stream is invalid except at the beginning of a HttpMessage
				if (this.currentMemoryBlockBaseOffset == 0 && this.Limit == 0) {
					// the beginning of a header
					// This is a normal end of stram, i.e. no more HttpMessage.
					throw new EndOfStreamException();
				} else {
					// unexpected end of stream
					throw CreateBadRequestException();
				}
			}

			return readCount;
		}

		#endregion
	}
}
