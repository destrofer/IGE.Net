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
	/// Currently WebSocketClient class is implemented as a server-side client connection handler only
	/// </summary>
	public class WebSocketClient : RawClient {
		protected StringBuilder m_HeaderString = new StringBuilder();
		protected StringBuilder m_Data = new StringBuilder();
		protected string m_Method = null;
		protected string m_Path = null;
		protected string m_Protocol = null;
		protected Dictionary<string, string> m_Header = null;
	
		public ulong MaxFrameSize = 65536;
	
		private Thread m_PacketReceiverThread = null;
		
		public WebSocketClient() : base() {
		}

		protected override void Init(Server server) {
			(m_PacketReceiverThread = new Thread(PacketReceiverThread)).Start(GetStream());
			base.Init(server);
		}

		protected override void Deinit(bool is_manual) {
			DeinitInternal();
			base.Deinit(is_manual);
		}
		
		protected override void OnDeinitException(bool is_manual, Exception ex) {
			Log(LogLevel.Debug, "WebSocket/OnDeinitException: {0}", ex);
			DeinitInternal();
			base.OnDeinitException(is_manual, ex);
		}

		private void DeinitInternal() {
			if( m_PacketReceiverThread != null ) {
				// this might not be needed
				Log(LogLevel.Debug, "WebSocket/DeinitInternal: interrupting packet receiver thread");
				try { m_PacketReceiverThread.Interrupt(); } catch {}
				Log(LogLevel.Debug, "WebSocket/DeinitInternal: joining packet receiver thread");
				try { m_PacketReceiverThread.Join(); } catch {}
				Log(LogLevel.Debug, "WebSocket/DeinitInternal: packet receiver joined");
				m_PacketReceiverThread = null;
			}
		}
		
		protected override void OnRawData(byte[] buffer) {
			if( m_Header == null ) {
				m_HeaderString.Append(Encoding.ASCII.GetString(buffer));
				int len = m_HeaderString.Length;
				if( len > 4 ) {
					string headerString = m_HeaderString.ToString();
					string newLines = null;
					if( headerString.Substring(len - 2).Equals("\n\n") )
						newLines = "\n";
					else if( headerString.Substring(len - 4).Equals("\r\n\r\n") )
						newLines = "\r\n";
					if( newLines != null ) {
						m_Header = new Dictionary<string, string>();
						string[] header = headerString.Split(new string[] {newLines}, 256, StringSplitOptions.RemoveEmptyEntries);
						int index = 0;
						char[] sep = new char[] {':'};
						string[] kv = null;
						foreach(string head in header) {
							if( index++ == 0 ) {
								kv = head.Split(new char[] {' '}, 3, StringSplitOptions.None);
								if( kv.Length != 3 ) {
									Disconnect();
									return;
								}
								m_Method = kv[0];
								m_Path = kv[1];
								m_Protocol = kv[2];
							}
							else {
								kv = head.Split(sep, 2, StringSplitOptions.None);
								if( kv.Length > 2 ){
									Disconnect();
									return;
								}
								if( kv.Length != 2 ) // some unknown string, not a header
									continue;
								m_Header.Add(kv[0].Trim(), kv[1].Trim());
								// Console.WriteLine("> {0} : {1}", kv[0].Trim(), kv[1].Trim());
							}
						}
						
						StringBuilder response = new StringBuilder();
						
						response.Append("HTTP/1.1 101 Switching Protocols");
						response.Append(newLines);
						
						response.Append(String.Format("Upgrade: {0}", m_Header["Upgrade"]));
						response.Append(newLines);
						
						response.Append("Connection: Upgrade");
						response.Append(newLines);
						
						response.Append(String.Format("Sec-WebSocket-Accept: {0}", Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.ASCII.GetBytes(String.Format("{0}258EAFA5-E914-47DA-95CA-C5AB0DC85B11", m_Header["Sec-WebSocket-Key"]))))));
						response.Append(newLines);
						
						response.Append("Sec-WebSocket-Protocol: igews");
						response.Append(newLines);
						response.Append(newLines);

						if( GameDebugger.MinNetLogLevel <= LogLevel.VerboseDebug )
							Log(LogLevel.VeryVerboseDebug, "WebSocket/OnRawData:\n{0}", response.ToString());
						else
							Log(LogLevel.Debug, "WebSocket/OnRawData: sending 101 Switching Protocols");
						
						Send(Encoding.ASCII.GetBytes(response.ToString()));
						
						// Console.WriteLine("Responding with:\n{0}", response);
						Log(LogLevel.Debug, "WebSocket: OnHandshakeDone");
						OnHandshakeDone();
					}
				}
				return;
			}
			
			EnqueueIncomingRawData(buffer);
		}

		[MTAThread]
		private void PacketReceiverThread(object param) {
			IgeNetStream stream = (IgeNetStream)param;
			Log(LogLevel.Debug, "WebSocket/PacketReceiverThread: start");
			try {
				using( BinaryReader reader = new BinaryReader(stream) ) {
					byte len;
					byte FrameInfo = 0;
					byte[] FrameMask = new byte[4];
					ulong FrameSize = 0;
					byte[] data;
					bool hasMask;
					
					while(true) {
						FrameInfo = reader.ReadByte();
						len = reader.ReadByte();
						hasMask = ((len & 0x80) != 0);
						len = (byte)(len & 0x7F);
						
						// FIX: this might actually crash, because byte queue might not contain enough data to get length of a bigger frame
						if( len == 127 ) {
							FrameSize =
								(ulong)reader.ReadByte() << 56
								| (ulong)reader.ReadByte() << 48
								| (ulong)reader.ReadByte() << 40
								| (ulong)reader.ReadByte() << 32
								| (ulong)reader.ReadByte() << 24
								| (ulong)reader.ReadByte() << 16
								| (ulong)reader.ReadByte() << 8
								| (ulong)reader.ReadByte();
						}
						else if( len == 126 ) {
							FrameSize =
								(ulong)reader.ReadByte() << 8
								| (ulong)reader.ReadByte();
						}
						else {
							FrameSize = (ulong)len;
						}
						
						if( FrameSize > MaxFrameSize ) {
							Disconnect();
							break;
						}
						
						if( hasMask )
							FrameMask = reader.ReadBytes(4);
						
						data = (FrameSize > 0) ? reader.ReadBytes((int)FrameSize) : null;
						if( data != null && hasMask )
							for(int i = (int)FrameSize - 1; i >= 0; i-- )
								data[i] = (byte)(data[i] ^ FrameMask[i & 3]);
						
						OnFrameData(FrameInfo, data);
						data = null;
					}
				}
			}
			catch(Exception ex) {
				Log(LogLevel.Debug, "WebSocket/PacketReceiverThread exception: {0}", ex.ToString());
			}
			finally {
				stream.Dispose();
			}
			Log(LogLevel.Debug, "WebSocket/PacketReceiverThread: end");
		}
		
		protected override void OnConnect(Server server) {
			// Overriding is necessary to prevent default encryption key packet
			// exchange routine.
			if( server == null )
				throw new NotImplementedException("WebSocketClient class is implemented as a server-side client connection handler only");
		}
		
		protected virtual void OnHandshakeDone() {
		}
		
		private void OnFrameData(byte frameInfo, byte[] data) {
			// flags (FrameInfo & n):
			// 0x80 - final frame
			// 0x40 - RSV1
			// 0x20 - RSV2
			// 0x10 - RSV3
			
			// opcode (FrameInfo & 0x0F):
			// 0x00 - non-control: continuation frame
			// 0x01 - non-control: text
			// 0x02 - non-control: binary
			// 0x08 - control: disconnect
			// 0x09 - control: ping
			// 0x0A - control: pong
			
			switch( frameInfo & 0x0F ) {
				case 0x08:
					Disconnect();
					break;
					
				case 0x09:
					SendRawMessage(0x8A, null); // respond with "pong"
					break;
					
				case 0x01:
					string message = (data == null) ? "" : Encoding.UTF8.GetString(data);
					OnTextMessage(message);
					break;
					
				case 0x02:
					OnBinaryMessage(data);
					break;
			}
		}
		
		private void SendRawMessage(byte op, byte[] data) {
			byte len;
			int rlen, offs;
			byte[] packetData;
			rlen = (data == null) ? 0 : data.Length;
			
			if( rlen > 65536 ) {
				packetData = new byte[10 + rlen];
				packetData[2] = packetData[3] = packetData[4] = packetData[5] = 0;
				packetData[6] = (byte)((rlen >> 24) & 0xFF);
				packetData[7] = (byte)((rlen >> 16) & 0xFF);
				packetData[8] = (byte)((rlen >> 8) & 0xFF);
				packetData[9] = (byte)(rlen & 0xFF);
				len = 127;
				offs = 10;
			}
			else if( rlen > 125 ) {
				packetData = new byte[4 + rlen];
				packetData[2] = (byte)((rlen >> 8) & 0xFF);
				packetData[3] = (byte)(rlen & 0xFF);
				len = 126;
				offs = 4;
			}
			else {
				packetData = new byte[2 + rlen];
				len = (byte)rlen;
				offs = 2;
			}
			
			packetData[0] = op;
			packetData[1] = len;
			if( rlen > 0 )
				Array.Copy(data, 0, packetData, offs, rlen);
			
			Send(packetData);
		}
		
		public void SendTextMessage(string message) {
			SendRawMessage(0x81, (message == null || message.Length == 0) ? null : Encoding.UTF8.GetBytes(message));
		}
		
		public void SendBinaryMessage(byte[] message) {
			SendRawMessage(0x82, message);
		}
		
		protected virtual void OnTextMessage(string message) {
		}
		
		protected virtual void OnBinaryMessage(byte[] message) {
		}
	}

}