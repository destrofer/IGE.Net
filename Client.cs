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
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.IO;
using System.Reflection;
using System.Text;

using IGE.Net.SystemPackets;

namespace IGE.Net {
	public class Client : RawClient {
		public const int RSAKeySize = 2048;
		public const int RijndaelKeySize = 256;		// in bits. supported: 128, 192 or 256
		public const int RijndaelBlockSize = 128;	// in bits. supported: 128, 192 or 256
		public const PaddingMode RijndaelPadding = PaddingMode.PKCS7;
		
		private ManualResetEvent m_CanProcessPackets = new ManualResetEvent(false);
		
		private BinaryWriter m_WriterStream = null;
		private Thread m_PacketReceiverThread = null;

		protected uint m_ReceivePacketSerial = 0;
		public uint ReceivePacketSerial { get { return m_ReceivePacketSerial; } set { m_ReceivePacketSerial = value; } }
		
		protected uint m_SendPacketSerial = 0;
		public uint SendPacketSerial { get { return m_SendPacketSerial; } set { m_SendPacketSerial = value; } }

		protected ManualResetEvent m_CryptoReady = new ManualResetEvent(false);

		protected RijndaelManaged m_RijCrypto = null;
		public RijndaelManaged RijCrypto { get { return m_RijCrypto; } }
		
		protected RSACryptoServiceProvider m_RSACryptoPublic = null;
		public RSACryptoServiceProvider RSACryptoPublic { get { return m_RSACryptoPublic; } }

		protected RSACryptoServiceProvider m_RSACryptoPrivate = null;
		public RSACryptoServiceProvider RSACryptoPrivate { get { return m_RSACryptoPrivate; } }

		public event PacketReceivedEventHandler PacketReceivedEvent;
		
		private Server m_Server = null;
		public Server Server { get { return m_Server; } }

		private object SyncSend = new Object();
		
		public Client() {
		}
		
		protected override void Init(Server server) {
			m_Server = server;
			
			m_RSACryptoPrivate = new RSACryptoServiceProvider(RSAKeySize);
			m_RSACryptoPrivate.PersistKeyInCsp = false;
			
			m_SendPacketSerial = 0;
			m_ReceivePacketSerial = 0;

			IgeNetStream stream = GetStream();
			stream.CalculateChecksums = true;
			m_WriterStream = new BinaryWriter(stream);
			
			m_CanProcessPackets.Reset();
			(m_PacketReceiverThread = new Thread(PacketReceiverThread)).Start(GetStream());
			
			base.Init(server);
		}
		
		protected override void PostInit(Server server) {
			InitInternal();
			base.PostInit(server);
		}
		
		protected override void OnInitException(Server server, Exception ex) {
			InitInternal();
			base.OnInitException(server, ex);
		}

		protected override void Deinit(bool is_manual) {
			DeinitInternal();
			base.Deinit(is_manual);
		}
		
		protected override void OnDeinitException(bool is_manual, Exception ex) {
			DeinitInternal();
			base.OnDeinitException(is_manual, ex);
		}
		
		private void InitInternal() {
			m_CanProcessPackets.Set();
		}
		
		private void DeinitInternal() {
			if( m_PacketReceiverThread != null ) {
				// this might not be needed
				try { m_PacketReceiverThread.Interrupt(); } catch {}
				try { m_PacketReceiverThread.Join(); } catch {}
				m_PacketReceiverThread = null;
			}
			
			if( m_WriterStream != null ) {
				m_WriterStream.Dispose();
				m_WriterStream = null;
			}
			
			m_RSACryptoPublic = null;
			m_RSACryptoPrivate = null;
			m_RijCrypto = null;
			
			m_Server = null;
		}
		
		protected override void OnRawData(byte[] data) {
			EnqueueIncomingRawData(data); // Put received data to input buffer so that it would be available to Recv* methods.
		}
		
		[MTAThread]
		private void PacketReceiverThread(object param) {
			IgeNetStream stream = (IgeNetStream)param;
			Log(LogLevel.Debug, "PacketReceiverThread: start");
			try {
				using( BinaryReader reader = new BinaryReader(stream) ) {
					Packet packet;
					uint packetId, checksum, checksum2;
					((IgeNetStream)reader.BaseStream).CalculateChecksums = true;
					while(true) {
						// Log(LogLevel.Debug, "PacketReceiverThread: resetting read checksum");
						// stream.ResetReadChecksum();
						
						//Thread.MemoryBarrier();
						
						Log(LogLevel.Debug, "PacketReceiverThread: reading packet id");
						packetId = reader.ReadUInt32();
						
						Log(LogLevel.Debug, "PacketReceiverThread: creating packet 0x{0:X8} instance", packetId);
						packet = Packet.CreateInstance(packetId);
						
						switch( packet.Encryption ) {
							case PacketEncryptionMethod.RSA: {
								int cryptoSize = reader.ReadInt32();
								Log(LogLevel.Debug, "PacketReceiverThread: deserializing packet 0x{0:X8} from an RSA encrypted block ({1} bytes)", packetId, cryptoSize);
								using(BinaryReader cryptoReader = new BinaryReader(new MemoryStream(m_RSACryptoPrivate.Decrypt(reader.ReadBytes(cryptoSize), false)), Encoding.Unicode)) {
									packet.Deserialize(cryptoReader);
								}
								break;
							}
							
							case PacketEncryptionMethod.Rijndael: {
								int cryptoSize = reader.ReadInt32();
								Log(LogLevel.Debug, "PacketReceiverThread: deserializing packet 0x{0:X8} from a Rijndael crypto stream ({1} bytes)", packetId, cryptoSize);
								using(MemoryStream mem = new MemoryStream(reader.ReadBytes(cryptoSize))) {
									using(BinaryReader cryptoReader = new BinaryReader(new CryptoStream(mem, m_RijCrypto.CreateDecryptor(), CryptoStreamMode.Read), Encoding.Unicode)) {
										packet.Deserialize(cryptoReader);
									}
								}
								break;
							}
							
							default: {
								Log(LogLevel.Debug, "PacketReceiverThread: deserializing packet  0x{0:X8} from a read stream", packetId);
								packet.Deserialize(reader);
								break;
							}
						}
						
						if( GameDebugger.MinNetLogLevel <= LogLevel.VerboseDebug )
							Log(LogLevel.VerboseDebug, "PacketReceiverThread: received packet '{0}'. Packet contents: {1}", packet.GetType().FullName, packet.ToString());
						else
							Log(LogLevel.Debug, "PacketReceiverThread: received packet '{0}'", packet.GetType().FullName);
						
						//Thread.MemoryBarrier();
						
						// packet.Deserialize(reader);
						checksum = stream.ReadChecksum ^ unchecked(m_ReceivePacketSerial++);
						
						//Thread.MemoryBarrier();
						
						checksum2 = reader.ReadUInt32();
						
						//Thread.MemoryBarrier();
						
						Log(LogLevel.Debug, "PacketReceiverThread: comparing checksums (calculated=0x{0:X8}; received=0x{1:X8})", checksum, checksum2);
						if( checksum != checksum2 ) {
							Disconnect();
							throw new UserFriendlyException("The protocol might be broken or some packet serializer and deserializer don't match each other.", "Connection error");
						}
						
						MethodInfo method;
						if( packet is RSAServerPublicKeyPacket || packet is RSAClientPublicKeyPacket || packet is RijndaelEncryptionKeyPacket ) {
							Log(LogLevel.Debug, "PacketReceiverThread: searching for handler method in IGE.Net.Client");
							method = typeof(Client).GetMethod("OnPacketReceived", BindingFlags.ExactBinding | BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { packet.GetType() }, null);
						}
						else {
							m_CanProcessPackets.WaitOne(); // block any non encryption key exchange packets
							Log(LogLevel.Debug, "PacketReceiverThread: searching for handler method in derived class");
							method = GetType().GetMethod("OnPacketReceived", BindingFlags.ExactBinding | BindingFlags.InvokeMethod | BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { packet.GetType() }, null);
						}
						
						if( method != null ) {
							Log(LogLevel.Debug, "PacketReceiverThread: invoking custom packet received event");
							method.Invoke(this, new object[] { packet });
						}
						else {
							Log(LogLevel.Debug, "PacketReceiverThread: invoking default packet received event");
							OnPacketReceived(packet);
						}

						if( PacketReceivedEvent != null )
							PacketReceivedEvent.Invoke(this, new PacketReceivedEventArgs(packet));
					}
				}
			}
			catch(Exception ex) {
				Log(LogLevel.Debug, "PacketReceiverThread exception: {0}", ex.ToString());
			}
			finally {
				stream.Dispose();
			}
			Log(LogLevel.Debug, "PacketReceiverThread: end");
		}
		
		public virtual void Send(Packet packet) {
			while(Connected && !m_CryptoReady.WaitOne(1000));; // block until encryption key exchange is complete
			SendNonBlocking(packet);
		}
		
		private void SendNonBlocking(Packet packet) {
			PacketEncryptionMethod encryption = packet.Encryption; 
			
			lock(SyncSend) {
				if( m_WriterStream == null || !Connected )
					throw new IOException("Client is not connected");

				IgeNetStream stream = (IgeNetStream)m_WriterStream.BaseStream;

				if( GameDebugger.MinNetLogLevel <= LogLevel.VerboseDebug )
					Log(LogLevel.VerboseDebug, "Sending packet: '{0}'. Packet contents: {1}", packet.GetType().FullName, packet.ToString());
				else
					Log(LogLevel.Debug, "Sending packet: '{0}'", packet.GetType().FullName);
				
				// Log(LogLevel.Debug, "Resetting send checksum");
				// stream.ResetWriteChecksum();

				//Thread.MemoryBarrier();

				Log(LogLevel.Debug, "Sending packet id: 0x{0:X8}", packet.Id);
				m_WriterStream.Write(packet.Id);

				switch(encryption) {
					case PacketEncryptionMethod.RSA: {
						Log(LogLevel.Debug, "Serializing packet to a RSA encrypted block");
						using(BinaryWriter cryptoWriter = new BinaryWriter(new MemoryStream(), Encoding.Unicode)) {
							packet.Serialize(cryptoWriter);
							byte[] cryptoData = m_RSACryptoPublic.Encrypt(((MemoryStream)cryptoWriter.BaseStream).ToArray(), false);
							Log(LogLevel.Debug, "Encrypted serialized block size: {0}", cryptoData.Length);
							m_WriterStream.Write(cryptoData.Length);
							m_WriterStream.Write(cryptoData);
						}
						break;
					}

					case PacketEncryptionMethod.Rijndael: {
						Log(LogLevel.Debug, "Serializing packet to a Rijndael crypto stream");
						using(MemoryStream mem = new MemoryStream()) {
							using( BinaryWriter cryptoWriter = new BinaryWriter(new CryptoStream(mem, m_RijCrypto.CreateEncryptor(), CryptoStreamMode.Write), Encoding.Unicode) ) {
								packet.Serialize(cryptoWriter);
							}
							byte[] cryptoData = mem.ToArray();
							Log(LogLevel.Debug, "Encrypted serialized block size: {0}", cryptoData.Length);
							m_WriterStream.Write(cryptoData.Length);
							m_WriterStream.Write(cryptoData);
						}
						break;
					}
					
					default: {
						Log(LogLevel.Debug, "Serializing packet to a write stream");
						packet.Serialize(m_WriterStream);
						break;
					}
				}

				uint checksum = stream.WriteChecksum ^ unchecked(m_SendPacketSerial++);
				
				//Thread.MemoryBarrier();
	
				Log(LogLevel.Debug, "Sending checksum: 0x{0:X8}", checksum);
				m_WriterStream.Write(checksum);
	
				//Thread.MemoryBarrier();
	
				Log(LogLevel.Debug, "Packet sending done");
			}
		}
		
		protected virtual void OnPacketReceived(Packet packet) {
			Log(LogLevel.Warning, "Client received a packet that was not handled");
		}
		
		protected void OnPacketReceived(RSAServerPublicKeyPacket packet) {
			if( m_RSACryptoPublic != null || m_Server != null ) {
				Log(LogLevel.Warning, "Received RSA public key packet from server while should not. Did someone try to inject one?");
				Disconnect();
				return;
			}
			
			m_RSACryptoPublic = packet.RSA;
			Log(LogLevel.Debug, "Received RSA public key from the server: KeySize={0}; Signature={1}", m_RSACryptoPublic.KeySize, packet.Signature.Length);
			
			m_RijCrypto = new RijndaelManaged();
			m_RijCrypto.KeySize = RijndaelKeySize;
			m_RijCrypto.BlockSize = RijndaelBlockSize;
			m_RijCrypto.Padding = RijndaelPadding;
			m_RijCrypto.GenerateKey();
			m_RijCrypto.GenerateIV();

			Log(LogLevel.Debug, "Sending Rijndael encryption key packet to the server: KeySize={0}", m_RijCrypto.KeySize);
			SendNonBlocking(new RijndaelEncryptionKeyPacket(m_RijCrypto));
			
			// WARNING: this must be done AFTER sendig Rijndael key, because client's RSA public key
			// is encrypted using Rijndael algorithm and server must already have the key to decrypt it.
			Log(LogLevel.Debug, "Sending RSA encryption key packet to the server: KeySize={0}", m_RSACryptoPrivate.KeySize);
			SendNonBlocking(new RSAClientPublicKeyPacket(m_RSACryptoPrivate));

			m_CryptoReady.Set(); // rijndael key is generated and sent to the server. it is safe to send rijndael encrypted packets now.
		}

		protected void OnPacketReceived(RSAClientPublicKeyPacket packet) {
			if( m_RSACryptoPublic != null || m_Server == null ) {
				Log(LogLevel.Warning, "Received RSA public key packet from client while should not. Did someone try to inject one?");
				Disconnect();
				return;
			}
			
			m_RSACryptoPublic = packet.RSA;
			Log(LogLevel.Debug, "Received RSA public key from the client: KeySize={0}", m_RSACryptoPublic.KeySize);
			
			m_CryptoReady.Set(); // we have received client's Rijndael key and RSA public key. Now we can send packets 
		}

		protected void OnPacketReceived(RijndaelEncryptionKeyPacket packet) {
			if( m_RijCrypto != null || m_Server == null ) {
				Log(LogLevel.Warning, "Received Rijndael encryption key packet while should not. Did someone try to inject one?");
				Disconnect();
				return;
			}
			
			m_RijCrypto = packet.Rijndael;
			Log(LogLevel.Debug, "Received Rijndael encryption key packet from the client: KeySize={0}", m_RijCrypto.KeySize);
		}

		protected override void OnConnect(Server server) {
			if( server != null ) {
				Log(LogLevel.Debug, "Sending RSA public key to the client: KeySize={0}", m_RSACryptoPrivate.KeySize);
				SendNonBlocking(new RSAServerPublicKeyPacket(m_RSACryptoPrivate, new byte[0]));
			}
			base.OnConnect(server);
		}
		
		protected override void OnDisconnect(bool is_manual) {
			base.OnDisconnect(is_manual);
			m_CryptoReady.Reset();
			m_RSACryptoPrivate = null;
			m_RSACryptoPublic = null;
			m_RijCrypto = null;
		}
	}
}
