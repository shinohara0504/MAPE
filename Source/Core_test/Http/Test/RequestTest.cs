﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using Xunit;


namespace MAPE.Http.Test {
	public class RequestTest: MessageTest {
		#region test - Read & Write (ReadHeader & Redirect)

		public new abstract class ReadAndWriteTestBase<TRequest>: MessageTest.ReadAndWriteTestBase<TRequest> where TRequest: Request {
			#region creation

			protected ReadAndWriteTestBase(IAdapter adapter): base(adapter) {
			}

			#endregion
		}

		public abstract class ReadAndWriteBasicTest<TRequest>: ReadAndWriteTestBase<TRequest> where TRequest : Request {
			#region creation

			protected ReadAndWriteBasicTest(IAdapter adapter): base(adapter) {
			}

			#endregion


			#region tests

			[Fact(DisplayName = "simple")]
			public void Simple() {
				// ARRANGE
				string input = CreateMessageString(
					"GET / HTTP/1.1",
					"Host: www.example.org",
					"",
					EmptyBody
				);
				string expectedOutput = input;

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					Assert.Equal("GET", request.Method);
					Assert.Equal(new Version(1, 1), request.Version);
					Assert.Equal("www.example.org:80", request.Host);
				});
			}

			[Fact(DisplayName = "method: POST")]
			public void Method_POST() {
				// ARRANGE
				string input = CreateMessageString(
					"POST / HTTP/1.1",
					"Host: www.example.org",
					"Content-Length: 3",
					"",
					"123"
				);
				string expectedOutput = input;

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					Assert.Equal("POST", request.Method);
					Assert.Equal("www.example.org:80", request.Host);
					Assert.Equal(3, request.ContentLength);
				});
			}

			[Fact(DisplayName = "method: CONNECT")]
			public void Method_CONNECT() {
				// ARRANGE
				string input = CreateMessageString(
					"CONNECT www.example.org:443 HTTP/1.1",
					"Host: www.example.org:443",
					"",
					EmptyBody
				);
				string expectedOutput = input;

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					Assert.Equal("CONNECT", request.Method);
					Assert.Equal(true, request.IsConnectMethod);
				});
			}

			[Fact(DisplayName = "HTTP-version: 1.0")]
			public void Version_10() {
				// ARRANGE
				string input = CreateMessageString(
					"GET / HTTP/1.0",
					"Host: www.example.org",
					"",
					EmptyBody
				);
				string expectedOutput = input;

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					Assert.Equal("GET", request.Method);
					Assert.Equal(new Version(1, 0), request.Version);
					Assert.Equal("www.example.org:80", request.Host);
				});
			}

			[Fact(DisplayName = "HTTP-version: invalid HTTP-name")]
			public void Version_InvalidHTTPName() {
				// ARRANGE
				string input = CreateMessageString(
					"GET / HTTPS/1.1",
					"Host: www.example.org",
					"",
					EmptyBody
				);
				string expectedOutput = input;

				// TEST
				HttpException actual = Assert.Throws<HttpException>(
					() => TestReadWrite(input, expectedOutput)
				);
				Assert.Equal(HttpStatusCode.BadRequest, actual.HttpStatusCode);
			}

			[Fact(DisplayName = "HTTP-version: lower-case HTTP-name")]
			public void Version_LowerCaseHTTPName() {
				// ARRANGE
				string input = CreateMessageString(
					"GET / http/1.1",
					"Host: www.example.org",
					"",
					EmptyBody
				);
				string expectedOutput = input;

				// TEST
				HttpException actual = Assert.Throws<HttpException>(
					() => TestReadWrite(input, expectedOutput)
				);
				Assert.Equal(HttpStatusCode.BadRequest, actual.HttpStatusCode);
			}

			[Fact(DisplayName = "HTTP-version: invalid digits")]
			public void Version_InvalidDigits() {
				// ARRANGE
				string input = CreateMessageString(
					"GET / HTTP/1.1.2",
					"Host: www.example.org",
					"",
					EmptyBody
				);
				string expectedOutput = input;

				// TEST
				HttpException actual = Assert.Throws<HttpException>(
					() => TestReadWrite(input, expectedOutput)
				);
				Assert.Equal(HttpStatusCode.BadRequest, actual.HttpStatusCode);
			}

			[Fact(DisplayName = "request-target: origin-form")]
			public void RequestTarget_OriginForm() {
				// ARRANGE
				string input = CreateMessageString(
					"GET /abc/def?ghi=kl HTTP/1.1",
					"Host: www.example.org:81",
					"",
					EmptyBody
				);
				string expectedOutput = input;

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					Assert.Equal(new DnsEndPoint("www.example.org", 81), request.HostEndPoint);
					Assert.Equal("www.example.org:81", request.Host);
					Assert.Equal(new Span(30, 56), request.HostSpan);

					// TargetUri is null for request-target of origin-form
					Assert.Equal(null, request.TargetUri);
				});
			}

			[Fact(DisplayName = "request-target: absolute-form")]
			public void RequestTarget_AbsoluteForm() {
				// ARRANGE
				string input = CreateMessageString(
					"GET http://www.example.org/abc/def?ghij=kl HTTP/1.1",
					"Host: test.example.org:123",       // dare to give different value from request-target for test
					"",
					EmptyBody
				);
				string expectedOutput = input;

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					// HostEndPoint and Host are derived from the request-target. 
					Assert.Equal(new DnsEndPoint("www.example.org", 80), request.HostEndPoint);
					Assert.Equal("www.example.org:80", request.Host);

					// HostSpan is the span of the actual Host field.
					Assert.Equal(new Span(53, 81), request.HostSpan);

					Assert.Equal(new Uri("http://www.example.org/abc/def?ghij=kl"), request.TargetUri);
				});
			}

			[Fact(DisplayName = "request-target: authority-form with port")]
			public void RequestTarget_AuthorityForm_with_port() {
				// ARRANGE
				string input = CreateMessageString(
					"CONNECT www.example.org:443 HTTP/1.1",
					"Host: www.example.org:400",    // dare to give different value from request-target for test
					"",
					EmptyBody
				);
				string expectedOutput = input;

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					// HostEndPoint and Host are derived from the request-target. 
					Assert.Equal(new DnsEndPoint("www.example.org", 443), request.HostEndPoint);
					Assert.Equal("www.example.org:443", request.Host);

					// HostSpan is the span of the actual Host field.
					Assert.Equal(new Span(38, 65), request.HostSpan);

					// TargetUri is null for request-target of authority-form
					Assert.Equal(null, request.TargetUri);
				});
			}

			[Fact(DisplayName = "request-target: authority-form without port")]
			public void RequestTarget_AuthorityForm_without_port() {
				// ARRANGE
				string input = CreateMessageString(
					"CONNECT www.example.org HTTP/1.1",
					"Host: www.example.org:400",    // dare to give different value from request-target for test
					"",
					EmptyBody
				);
				string expectedOutput = input;

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					// HostEndPoint and Host are derived from the request-target. 
					Assert.Equal(new DnsEndPoint("www.example.org", 443), request.HostEndPoint);
					Assert.Equal("www.example.org:443", request.Host);

					// HostSpan is the span of the actual Host field.
					Assert.Equal(new Span(34, 61), request.HostSpan);

					// TargetUri is null for request-target of authority-form
					Assert.Equal(null, request.TargetUri);
				});
			}

			[Fact(DisplayName = "request-target: asterisk-form")]
			public void RequestTarget_AsteriskForm() {
				// ARRANGE
				string input = CreateMessageString(
					"OPTIONS * HTTP/1.1",
					"Host: www.example.org:80",
					"",
					EmptyBody
				);
				string expectedOutput = input;

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					Assert.Equal(new DnsEndPoint("www.example.org", 80), request.HostEndPoint);
					Assert.Equal("www.example.org:80", request.Host);
					Assert.Equal(new Span(20, 46), request.HostSpan);

					// TargetUri is null for request-target of asterisk-form
					Assert.Equal(null, request.TargetUri);
				});
			}

			[Fact(DisplayName = "header: large")]
			public void Header_Large() {
				// ARRANGE
				StringBuilder buf = new StringBuilder();
				buf.Append($"GET / HTTP/1.1{CRLF}");
				buf.Append($"Host: www.example.org:80{CRLF}");
				for (int i = 0; i < 50; ++i) {
					buf.Append($"X-Test-{i}: 012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789{CRLF}");
				}
				buf.Append(CRLF);
				string input = buf.ToString();

				// This sample header to be tested must consume more than two memory blocks.
				// Note that the length of the string is equal to the count of message octets in this sample.  
				Debug.Assert(ComponentFactory.MemoryBlockCache.MemoryBlockSize < input.Length);
				string expectedOutput = input;

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					Assert.Equal("www.example.org:80", request.Host);
				});
			}

			[Fact(DisplayName = "body: tiny")]
			public void Body_Tiny() {
				// ARRANGE
				string input = CreateMessageString(
					"PUT / HTTP/1.1",
					"Host: www.example.org:80",
					"Content-Length: 5",
					"",
					"12345"
				);
				// The body length should be in range of 'tiny' length.
				// That is, the whole message must be stored in one memory block.
				// In a Request object, 'tiny' length body is stored in the rest of header's memory block.
				// Note that the length of the string is equal to the count of message octets in this sample.  
				Debug.Assert(input.Length < ComponentFactory.MemoryBlockCache.MemoryBlockSize); 
				string expectedOutput = input;

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					Assert.Equal(5, request.ContentLength);
				});
			}

			[Fact(DisplayName = "body: small")]
			public void Body_Small() {
				// ARRANGE
				const int bodyLength = ComponentFactory.MemoryBlockCache.MemoryBlockSize - 10;
				string header = CreateMessageString(
					"PUT / HTTP/1.1",
					"Host: www.example.org:80",
					$"Content-Length: {bodyLength}",
					""
				);

				// The body length should be in range of 'small' length.
				// That is, 
				// * It cannot be stored in the rest of header's memory block, but
				// * It can be stored in one memory block.
				// In a Request object, 'small' length body is stored in a memory block.
				Debug.Assert(ComponentFactory.MemoryBlockCache.MemoryBlockSize < header.Length + 2 + bodyLength);
				Debug.Assert(bodyLength <= ComponentFactory.MemoryBlockCache.MemoryBlockSize);

				// TEST
				TestReadWriteSimpleBody(header, bodyLength, (request) => {
					Assert.Equal(bodyLength, request.ContentLength);
				});
			}

			[Fact(DisplayName = "body: medium")]
			public void Body_Medium() {
				// ARRANGE
				const int bodyLength = 10 * 1024;   // 10k
				string header = CreateMessageString(
					"PUT / HTTP/1.1",
					"Host: www.example.org:80",
					$"Content-Length: {bodyLength}",
					""
				);

				// The body length should be in range of 'medium' length.
				// That is, 
				// * It must be larger than the memory block size, and
				// * It must be smaller than or equal to BodyBuffer.BodyStreamThreshold.
				// In a Request object, 'medium' length body is stored in a MemoryStream.
				Debug.Assert(ComponentFactory.MemoryBlockCache.MemoryBlockSize < bodyLength);
				Debug.Assert(bodyLength <= BodyBuffer.BodyStreamThreshold);

				// TEST
				TestReadWriteSimpleBody(header, bodyLength, (request) => {
					Assert.Equal(bodyLength, request.ContentLength);
				});
			}

			[Fact(DisplayName = "body: large")]
			public void Body_Large() {
				// ARRANGE
				const int bodyLength = BodyBuffer.BodyStreamThreshold * 2;
				string header = CreateMessageString(
					"PUT / HTTP/1.1",
					"Host: www.example.org:80",
					$"Content-Length: {bodyLength}",
					""
				);

				// The body length should be in range of 'large' length.
				// That is, 
				// * It must be larger than BodyBuffer.BodyStreamThreshold.
				// In a Request object, 'large' length body is stored in a FileStream.
				Debug.Assert(BodyBuffer.BodyStreamThreshold < bodyLength);

				// TEST
				TestReadWriteSimpleBody(header, bodyLength, (request) => {
					Assert.Equal(bodyLength, request.ContentLength);
				});
			}

			[Fact(DisplayName = "body: chunked")]
			public void Body_Chunked() {
				using (MemoryStream input = new MemoryStream()) {
					// ToDo: in VS2017, convert it to a local method
					Action<string> writeLine = (string line) => {
						WriteLinesTo(input, line);
					};
					Action<long> writeChunkData = (long size) => {
						WriteRandomBody(size, input, appendCRLF: true);
					};

					// write header
					WriteLinesTo(
						input,
						"PUT / HTTP/1.1",
						"Host: www.example.org:80",
						$"Transfer-Encoding: chunked",
						""
					);

					// write chunked body
					writeLine("2F");        // chunk-size
					writeChunkData(0x2F);   // chunk-data
					writeLine("08");        // chunk-size, in redundant format
					writeChunkData(0x08);   // chunk-data
					writeLine("000");       // last-chunk, in redundant format
					writeLine("");          // end of chunked-body

					input.Position = 0;
					Stream expectedOutput = input;  // same to the input

					// Test
					TestReadWrite(input, expectedOutput, (request) => {
						Assert.Equal(-1, request.ContentLength);
					});
				}
			}

			[Fact(DisplayName = "body: chunked with trailers")]
			public void Body_Chunked_with_Trailers() {
				using (MemoryStream input = new MemoryStream()) {
					// ToDo: in VS2017, convert it to a local method
					Action<string> writeLine = (string line) => {
						WriteLinesTo(input, line);
					};
					Action<long> writeChunkData = (long size) => {
						WriteRandomBody(size, input, appendCRLF: true);
					};

					// write header
					WriteLinesTo(
						input,
						"PUT / HTTP/1.1",
						"Host: www.example.org:80",
						$"Transfer-Encoding: chunked",
						""
					);

					// write chunked body
					writeLine("0");                 // last-chunk, empty chunk-data
					writeLine("X-Test-1: dummy");   // trailer
					writeLine("");                  // end of chunked-body

					input.Position = 0;
					Stream expectedOutput = input;  // same to the input

					// Test
					TestReadWrite(input, expectedOutput, (request) => {
						Assert.Equal(-1, request.ContentLength);
					});
				}
			}

			// ToDo: body
			//  with chunk-ext 
			//  multi transfer-coding in Transfer-Encoding

			[Fact(DisplayName = "modification: append")]
			public void Modification_append() {
				// ARRANGE
				string input = CreateMessageString(
					"GET / HTTP/1.1",
					"Host: www.example.org",
					"",
					EmptyBody
				);
				string expectedOutput = CreateMessageString(
					"GET / HTTP/1.1",
					"Host: www.example.org",
					"X-Test: dummy",
					"",
					EmptyBody
				);
				Func<Modifier, bool> handler = (modifier) => {
					modifier.WriteASCIIString("X-Test: dummy", appendCRLF: true);
					return true;
				};

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					request.AddModification(request.EndOfHeaderFields, handler);
				});
			}

			[Fact(DisplayName = "modification: append at the same point")]
			public void Modification_append_samepoint() {
				// ARRANGE
				string input = CreateMessageString(
					"GET / HTTP/1.1",
					"Host: www.example.org",
					"",
					EmptyBody
				);
				// keep the order of X-Test-1, X-Test-2 and X-Test-3.
				string expectedOutput = CreateMessageString(
					"GET / HTTP/1.1",
					"Host: www.example.org",
					"X-Test-1: dummy",
					"X-Test-2: dummy",
					"X-Test-3: dummy",
					"",
					EmptyBody
				);
				Func<Modifier, bool> handler1 = (modifier) => {
					modifier.WriteASCIIString("X-Test-1: dummy", appendCRLF: true);
					return true;
				};
				Func<Modifier, bool> handler2 = (modifier) => {
					modifier.WriteASCIIString("X-Test-2: dummy", appendCRLF: true);
					return true;
				};
				Func<Modifier, bool> handler3 = (modifier) => {
					modifier.WriteASCIIString("X-Test-3: dummy", appendCRLF: true);
					return true;
				};

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					Span span = request.EndOfHeaderFields;
					Debug.Assert(span.Length == 0);

					// add at the same point
					request.AddModification(span, handler1);
					request.AddModification(span, handler2);
					request.AddModification(span, handler3);
				});
			}

			[Fact(DisplayName = "modification: change")]
			public void Modification_change() {
				// ARRANGE
				string input = CreateMessageString(
					"GET http://www.example.org/test/index.html?abc=def HTTP/1.1",
					"Host: www.example.org",
					"",
					EmptyBody
				);
				string expectedOutput = CreateMessageString(
					"GET /test/index.html?abc=def HTTP/1.1",
					"Host: www.example.org",
					"",
					EmptyBody
				);

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					request.AddModification(
						request.RequestTargetSpan,
						(modifier) => {
							modifier.WriteASCIIString(request.TargetUri.PathAndQuery);	
							return true;
						}
					);
				});
			}

			[Fact(DisplayName = "modification: suppressModification")]
			public void Modification_suppressModification() {
				// ARRANGE
				string input = CreateMessageString(
					"GET http://www.example.org/test/index.html?abc=def HTTP/1.1",
					"Host: www.example.org",
					"",
					EmptyBody
				);
				string expectedOutput = input;	// not modified

				// TEST
				TestReadWrite(input, expectedOutput, (request) => {
					request.AddModification(
						request.RequestTargetSpan,
						(modifier) => {
							modifier.WriteASCIIString(request.TargetUri.PathAndQuery);
							return true;
						}
					);
				}, request: null, suppressModification: true);
			}

			#endregion
		}

		public class ReadAndWrite: ReadAndWriteBasicTest<Request> {
			#region types

			private class RequestAdapter: IAdapter {
				#region properties

				public static readonly RequestAdapter Instance = new RequestAdapter();

				#endregion


				#region IAdapter

				public Request Create() {
					return new Request();
				}

				public bool Read(Request message, Stream input, Request request) {
					return message.Read(input);
				}

				public void Write(Request message, Stream output, bool suppressModification) {
					message.Write(output, suppressModification);
				}

				public bool ReadHeader(Request message, Stream input, Request request) {
					return message.ReadHeader(input);
				}

				public void Redirect(Request message, Stream output, Stream input, bool suppressModification) {
					message.Redirect(output, input, suppressModification);
				}

				#endregion
			}

			#endregion


			#region creation

			public ReadAndWrite(): base(RequestAdapter.Instance) {
			}

			#endregion
		}

		#endregion
	}
}
