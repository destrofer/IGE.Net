/*
 * Author: Viacheslav Soroka
 * 
 * This file is part of IGE <https://github.com/destrofer/IGE>.
 * 
 * IGE is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * IGE is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with IGE.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Threading;

using IGE;
using IGE.Net;
using IGE.Net.SystemPackets;

namespace IGE.Net.Clients {
	/// <summary>
	/// Currently HTTPClient is implemented as a server-side client connection handler only
	/// </summary>
	public class HTTPClient : RawClient {
		private char[] FirstLineSplitter = new char[] {' '};
		private char[] HeaderLineSplitter = new char[] {':'};

		protected Thread m_RequestHandlerThread = null;
		
		protected bool m_HeaderDone = false;
		protected HTTPRequest m_Request = new HTTPRequest();
		protected HTTPResponse m_Response = new HTTPResponse();
		protected ByteQueue m_Line = new ByteQueue();
		protected long m_BytesReceived = 0;

		public long BytesReceived { get { return m_BytesReceived; } }
		public HTTPRequest Request { get { return m_Request; } }
		public HTTPResponse Response { get { return m_Response; } }

		public HTTPClient() : base() {
		}

		protected override void Init(Server server) {
			// (m_PacketReceiverThread = new Thread(PacketReceiverThread)).Start(GetStream());
			base.Init(server);
		}

		protected override void Deinit(bool is_manual) {
			DeinitInternal();
			base.Deinit(is_manual);
		}
		
		protected override void OnDeinitException(bool is_manual, Exception ex) {
			Log(LogLevel.Debug, "HTTP/OnDeinitException: {0}", ex);
			DeinitInternal();
			base.OnDeinitException(is_manual, ex);
		}

		private void DeinitInternal() {
			if( m_RequestHandlerThread != null ) {
				// this might not be needed
				if( Thread.CurrentThread != m_RequestHandlerThread ) {
					Log(LogLevel.Debug, "HTTP/DeinitInternal: interrupting request handler thread");
					try { m_RequestHandlerThread.Interrupt(); } catch {}
					Log(LogLevel.Debug, "HTTP/DeinitInternal: joining request handler thread");
					try { m_RequestHandlerThread.Join(); } catch {}
					Log(LogLevel.Debug, "HTTP/DeinitInternal: request handler joined");
				}
				else {
					Log(LogLevel.Debug, "HTTP/DeinitInternal: disconnect was called from within request handler thread");
				}
				m_RequestHandlerThread = null;
			}
		}

		protected override void OnRawData(byte[] buffer) {
			int pos = 0;
			byte b;
			
			if( !m_HeaderDone ) {
				while( pos < buffer.Length ) {
					b = buffer[pos++];
					m_BytesReceived++;
					if( b == '\n' ) {
						string line = Encoding.UTF8.GetString(m_Line.ToArray()).Trim('\r');
						if( line.Length != 0 ) {
							if( m_Request.Method == null ) {
								string[] firstLine = line.Split(FirstLineSplitter, 3);
								if( firstLine.Length < 3 ) {
									Log(LogLevel.Debug, "HTTP/OnRawData: Bad first request line");
									SendStatus(HTTPStatus.BadRequest, "Bad first request line");
									Disconnect();
									return;
								}
								m_Request.Method = firstLine[0];
								m_Request.URI = firstLine[1];
								m_Request.Protocol = firstLine[2];
								Log(LogLevel.Debug, "HTTP/OnRawData: received header first line:\n  Method={0}\n  URI={1}\n  Protocol={2}", m_Request.Method, m_Request.URI, m_Request.Protocol);
							}
							else {
								string[] headerLine = line.Split(HeaderLineSplitter, 2);
								if( headerLine.Length < 2 ) {
									Log(LogLevel.Debug, "HTTP/OnRawData: Header consists only of name");
									SendStatus(HTTPStatus.BadRequest, "Header consists only of name");
									Disconnect();
									return;
								}
								m_Request.Header[headerLine[0].Trim()] = headerLine[1].Trim();
								if( GameDebugger.MinNetLogLevel <= LogLevel.VerboseDebug )
									Log(LogLevel.VerboseDebug, "HTTP/OnRawData: {0}: {1}", headerLine[0].Trim(), headerLine[1].Trim());
							}
						}
						else {
							m_HeaderDone = true;
							m_Request.Parse();
							
							Log(LogLevel.Debug, "HTTP/OnRawData: starting request handler thread");
							m_RequestHandlerThread = new Thread(HandleRequest);
							m_RequestHandlerThread.Start();

							if( m_Request.ContentLength < 0 ) {
								Log(LogLevel.Debug, "HTTP/OnRawData: Negative content length");
								Disconnect();
								return;
							}
							
							break;
						}
					}
					else {
						m_Line.Enqueue(b);
						if( m_Line.Length > 2048 ) {
							Log(LogLevel.Debug, "HTTP/OnRawData: Too long line in a header");
							SendStatus(HTTPStatus.BadRequest, "Too long line in a header");
							Disconnect();
							return;
						}
						if( m_BytesReceived > 32768 ) {
							Log(LogLevel.Debug, "HTTP/OnRawData: Too long header");
							SendStatus(HTTPStatus.BadRequest, "Too long header");
							Disconnect();
							return;
						}
					}
				}
			}

			if( !m_HeaderDone || pos >= buffer.Length )
				return; // wait for another portion of the data
			
			m_Request.ContentBytesReceived += buffer.Length - pos;
			EnqueueIncomingRawData(buffer, pos, buffer.Length - pos);
		}
		
		protected virtual void HandleRequest() {
			SendStatus(HTTPStatus.NotImplemented, "Request handling is not implemented yet.");
			Disconnect();
		}
		
		protected virtual void SendStatus(HTTPStatus statusCode, string errorString) {
			Log(LogLevel.Debug, "HTTP: Sending status {0}", statusCode);
			m_Response.Status = statusCode;
			AddStandardHeaders();
			m_Response.Body.Append(errorString);
			Send(m_Response);
		}
		
		protected virtual void AddStandardHeaders() {
			m_Response.Header["Server"] = "IGE/1.0";
			m_Response.Header["Content-Type"] = "text/html; charset=utf-8";
			m_Response.Header["Connection"] = "close";
		}
		
		protected virtual void StreamFile(string fileName) {
			int read;
			long len;
			byte[] buffer = new byte[65536];
			Log(LogLevel.Debug, "HTTP/StreamFile({0})", fileName);
			
			using(BinaryReader r = new BinaryReader(new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))) {
				Log(LogLevel.Debug, "HTTP/StreamFile({0}): Content-Length={1}", fileName, r.BaseStream.Length);
				m_Response.Header["Content-Length"] = r.BaseStream.Length.ToString();
				Send(m_Response);
				
				while( Connected ) {
					len = r.BaseStream.Length - r.BaseStream.Position;
					if( len <= 0 ) break;
					if( len > 65536 ) len = 65536;
					read = r.Read(buffer, 0, (int)len);
					if( read > 0 ) {
						if( Send(buffer, 0, read) != read ) {
							Log(LogLevel.Debug, "HTTP/StreamFile({0}): Connection closed while streaming file", fileName);
							throw new ConnectionLostException("Connection closed while streaming file");
						}
					}
					
					if( read < (int)len ) { // some error occured
						Log(LogLevel.Debug, "HTTP/StreamFile({0}): read {1} bytes when expecting {2}", fileName, read, len);
						break;
					}
				}

				Log(LogLevel.Debug, "HTTP/StreamFile({0}): file sent", fileName);
			}
		}
		
		public virtual bool Send(HTTPData data) {
			byte[] dataBytes = data.GetBytes();
			return Send(dataBytes) == dataBytes.Length;
		}
	}
	
	public class HTTPData {
		public Encoding Encoding = Encoding.UTF8;
		public Dictionary<string, string> Header = new Dictionary<string, string>();
		
		public HTTPData() {
		}
		
		public virtual void AppendToResponse(StringBuilder headerString) {
			foreach(KeyValuePair<string, string> pair in Header) {
				headerString.Append(pair.Key);
				headerString.Append(": ");
				headerString.Append(pair.Value);
				headerString.Append("\r\n");
			}
			headerString.Append("\r\n");
		}
		
		public virtual byte[] GetBytes() {
			StringBuilder str = new StringBuilder();
			AppendToResponse(str);
			return Encoding.GetBytes(str.ToString());
		}
		
		public override string ToString() {
			StringBuilder str = new StringBuilder();
			AppendToResponse(str);
			return str.ToString();
		}
	}

	public class HTTPRequest : HTTPData {
		public string Method = null;
		public string URI = null;
		public string Protocol = null;
		public long ContentLength = 0;
		public long ContentBytesReceived = 0;
		
		public override void AppendToResponse(StringBuilder headerString) {
			headerString.Append(Method);
			headerString.Append(' ');
			headerString.Append(URI);
			headerString.Append(' ');
			headerString.Append(Protocol);
			headerString.Append("\r\n");
			
			base.AppendToResponse(headerString);
		}
		
		public virtual bool Parse() {
			bool result = true;
			if( Header.ContainsKey("Content-Length") )
				result = result && long.TryParse(Header["Content-Length"], out ContentLength);
			return result;
		}
	}
	
	public class HTTPResponse : HTTPData {
		public HTTPStatus Status = HTTPStatus.OK;
		public StringBuilder Body = new StringBuilder();
		
		public override void AppendToResponse(StringBuilder headerString) {
			headerString.Append("HTTP/1.1 ");
			headerString.Append((int)Status);
			headerString.Append(' ');
			headerString.Append(GetStatusMessage(Status));
			headerString.Append("\r\n");
			
			base.AppendToResponse(headerString);
			
			headerString.Append(Body);
		}
		
		public virtual string GetStatusMessage(HTTPStatus statusCode) {
			switch( statusCode ) {
				case HTTPStatus.Continue: return "Continue";
				case HTTPStatus.SwitchingProtocols: return "Switching Protocols";
				
				case HTTPStatus.OK: return "OK";
				case HTTPStatus.Created: return "Created";
				case HTTPStatus.Accepted: return "Accepted";
				case HTTPStatus.NonAuthoritativeInformation: return "Non-Authoritative Information";
				case HTTPStatus.NoContent: return "No Content";
				case HTTPStatus.ResetContent: return "Reset Content";
				case HTTPStatus.PartialContent: return "Partial Content";

				case HTTPStatus.MultipleChoices: return "Multiple Choices";
				case HTTPStatus.MovedPermanently: return "Moved Permanently";
				case HTTPStatus.Found: return "Found";
				case HTTPStatus.SeeOther: return "See Other";
				case HTTPStatus.NotModified: return "Not Modified";
				case HTTPStatus.UseProxy: return "Use Proxy";
				case HTTPStatus.TemporaryRedirect: return "Temporary Redirect";
				
				case HTTPStatus.BadRequest: return "Bad Request";
				case HTTPStatus.Unauthorized: return "Unauthorized";
				case HTTPStatus.PaymentRequired: return "Payment Required";
				case HTTPStatus.Forbidden: return "Forbidden";
				case HTTPStatus.NotFound: return "Not Found";
				case HTTPStatus.MethodNotAllowed: return "Method Not Allowed";
				case HTTPStatus.NotAcceptable: return "Not Acceptable";
				case HTTPStatus.ProxyAuthenticationRequired: return "Proxy Authentication Required";
				case HTTPStatus.RequestTimeout: return "Request Timeout";
				case HTTPStatus.Conflict: return "Conflict";
				case HTTPStatus.Gone: return "Gone";
				case HTTPStatus.LengthRequired: return "Length Required";
				case HTTPStatus.PreconditionFailed: return "Precondition Failed";
				case HTTPStatus.RequestEntityTooLarge: return "Request Entity Too Large";
				case HTTPStatus.RequestURITooLong: return "Request-URI Too Long";
				case HTTPStatus.UnsupportedMediaType: return "Unsupported Media Type";
				case HTTPStatus.RequestedRangeNotSatisfiable: return "Requested Range Not Satisfiable";
				case HTTPStatus.ExpectationFailed: return "Expectation Failed";

				case HTTPStatus.InternalServerError: return "Internal Server Error";
				case HTTPStatus.NotImplemented: return "Not Implemented";
				case HTTPStatus.BadGateway: return "Bad Gateway";
				case HTTPStatus.ServiceUnavailable: return "Service Unavailable";
				case HTTPStatus.GatewayTimeout: return "Gateway Timeout";
				case HTTPStatus.HTTPVersionNotSupported: return "HTTP Version Not Supported";
			}
			return "Unknown";
		}
	}
	
	// http://www.w3.org/Protocols/rfc2616/rfc2616-sec10.html
	public enum HTTPStatus {
		Continue = 100,
		SwitchingProtocols = 101,
		
		OK = 200,
		Created = 201,
		Accepted = 202,
		NonAuthoritativeInformation = 203,
		NoContent = 204,
		ResetContent = 205,
		PartialContent = 206,
		
		MultipleChoices = 300,
		MovedPermanently = 301,
		Found = 302,
		SeeOther = 303,
		NotModified = 304,
		UseProxy = 305,
		TemporaryRedirect = 307,
		
		BadRequest = 400,
		Unauthorized = 401,
		PaymentRequired = 402,
		Forbidden = 403,
		NotFound = 404,
		MethodNotAllowed = 405,
		NotAcceptable = 406,
		ProxyAuthenticationRequired = 407,
		RequestTimeout = 408,
		Conflict = 409,
		Gone = 410,
		LengthRequired = 411,
		PreconditionFailed = 412,
		RequestEntityTooLarge = 413,
		RequestURITooLong = 414,
		UnsupportedMediaType = 415,
		RequestedRangeNotSatisfiable = 416,
		ExpectationFailed = 417,
		
		InternalServerError = 500,
		NotImplemented = 501,
		BadGateway = 502,
		ServiceUnavailable = 503,
		GatewayTimeout = 504,
		HTTPVersionNotSupported = 505,		
	}
}