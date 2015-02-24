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
	public class RawClient : IDisposable {
		public const int DefaultPort = 30163;
		
		public const int MaxSendBufferSize = 65536;
		public const int MaxRecvBufferSize = 65536;
		
		private static ulong NextConnectionIndex = 0;
		private ulong m_ConnectionIndex;		
		public ulong ConnectionIndex { get { return m_ConnectionIndex; } }
		
		private Socket m_Socket = null;
		private IPEndPoint m_LocalEndpoint = new IPEndPoint(IPAddress.None, 0);
		private IPEndPoint m_RemoteEndpoint = new IPEndPoint(IPAddress.None, 0);
		
		public IPEndPoint LocalEndpoint { get { return m_LocalEndpoint; } }
		public IPEndPoint RemoteEndpoint { get { return m_RemoteEndpoint; } }
		
		public bool Connected { get { lock(SyncRoot) { return (AlreadyDisconnecting || m_Socket == null) ? false : m_Socket.Connected; } } }
		
		private ByteQueue m_Output = new ByteQueue(65536); // 64 KB per chunk
		private ManualResetEvent m_OutputReady = new ManualResetEvent(false);
		private ManualResetEvent m_OutputEmpty = new ManualResetEvent(true);
		private ManualResetEvent m_OutputAccepts = new ManualResetEvent(true);

		private ByteQueue m_Input = new ByteQueue(65536); // 64 KB per chunk
		private ManualResetEvent m_InputReady = new ManualResetEvent(false);
		private ManualResetEvent m_InputEmpty = new ManualResetEvent(true);
		private ManualResetEvent m_InputAccepts = new ManualResetEvent(true);
		
		private object SyncRoot = new Object();
		private object SyncRecv = new Object();
		private object SyncSend = new Object();
		private object SyncDisconnect = new Object();
		
		private volatile bool KeepRunning = true;
		private volatile bool AlreadyDisconnecting = false;
		
		public event DisconnectEventHandler DisconnectEvent;
		public event ConnectEventHandler ConnectEvent;
		public event StreamDataEventHandler StreamDataEvent;

		private Thread m_SenderThread = null;
		private Thread m_ReceiverThread = null;
		
		private bool m_RawDataHandlerOverriden = false;

		public bool SendingThreadIdle { get { return (m_SenderThread == null || m_SenderThread.ThreadState == ThreadState.WaitSleepJoin); } }
		public bool ReceiverThreadIdle { get { return (m_ReceiverThread == null || m_ReceiverThread.ThreadState == ThreadState.WaitSleepJoin); } }
		
		public int SendBytesQueued { get { lock( m_Output ) { return m_Output.Length; } } }
		public int RecvBytesQueued { get { lock( m_Input ) { return m_Input.Length; } } }
		public bool IsSynchronous { get { return StreamDataEvent == null; } }
		
		public bool HasDataToRecv { get { return m_InputReady.WaitOne(0); } }
		public bool HasDataToSend { get { return m_OutputReady.WaitOne(0); } }
		
		public RawClient() {
			unchecked {
				m_ConnectionIndex = NextConnectionIndex++;
			}
			Type type = GetType();
			MethodInfo method = type.GetMethod("OnRawData", BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(byte[]) }, null);
			m_RawDataHandlerOverriden = !method.DeclaringType.Equals(typeof(RawClient));
			Log(LogLevel.Debug, "Constructing '{0}'. OnRawData() method is implemented in '{0}'", GetType().FullName, method.DeclaringType.FullName);
		}
		
		~RawClient() {
			Dispose();
		}
		
		public void Dispose() {
			Disconnect();
		}

		protected void Log(LogLevel level, string format, params object[] par) {
			if( GameDebugger.MinNetLogLevel <= level )
				GameDebugger.NetLog(level, String.Format("[{0}/{1:8X}] {2}", ConnectionIndex, Thread.CurrentThread.GetHashCode(), String.Format(format, par)));
		}
		
		public static IPAddress GetMyIp() {
			return GetMyIp(false);
		}
		
		public static IPAddress GetMyIp(bool v6_allowed) {
			IPAddress myaddr = IPAddress.Parse("127.0.0.1");
			foreach( IPAddress addr in Dns.GetHostEntry(Dns.GetHostName()).AddressList ) {
				if( v6_allowed || !addr.ToString().Contains(":") ) {
					myaddr = addr;
					break;
				}
			}
			return myaddr;
		}
		
		[MTAThread]
		private void SenderThread(object param) {
			Socket socket = (Socket)param;
			IAsyncResult async;
			byte[] buffer;
			int sent, offset, length;
			bool keepRunning = true;
			Log(LogLevel.Debug, "SenderThread: started");
			try {
				while( socket.Connected ) {
					keepRunning = keepRunning && KeepRunning;
					try {
						if( keepRunning ) {
							Log(LogLevel.Debug, "SenderThread: waiting for data");
							m_OutputReady.WaitOne();
						}
					}
					catch(ThreadInterruptedException) {
						Log(LogLevel.Debug, "SenderThread: interrupted while waiting for data");
						keepRunning = false;
					}
					
					if( !socket.Connected ) {
						Log(LogLevel.Debug, "SenderThread: socket was closed while waiting for data");
						break;
					}
					
					lock(m_Output) {
						if( m_Output.Length == 0 ) {
							Log(LogLevel.Debug, "SenderThread: seems i was interrupted");
							if( keepRunning )
								continue;
							Log(LogLevel.Debug, "SenderThread: got signal to stop so quitting");
							break;
						}
						buffer = m_Output.Dequeue(m_Output.Length);
						m_OutputReady.Reset();
						m_OutputEmpty.Set();
						m_OutputAccepts.Set();
						Log(LogLevel.Debug, "SenderThread: read {0} bytes from the queue", buffer.Length);
					}
					
					length = buffer.Length;
					offset = 0;
					while( offset < length && socket.Connected ) {
						Log(LogLevel.Debug, "SenderThread: beginning to send");
						async = socket.BeginSend(buffer, offset, length - offset, SocketFlags.None, null, null);
						try {
							async.AsyncWaitHandle.WaitOne(1000);
						}
						catch(ThreadInterruptedException) {
							Log(LogLevel.Debug, "SenderThread: interrupted while sending");
							keepRunning = false;
						}
						sent = socket.EndSend(async);
						Log(LogLevel.Debug, "SenderThread: sent {0} bytes", sent);
						//sent = socket.Send(buffer, offset, length - offset, SocketFlags.None);
						if( sent > 0 )
							offset += sent;
					}
					buffer = null;
				}
				Log(LogLevel.Debug, "SenderThread: end loop");
			}
			catch(Exception ex) {
				Log(LogLevel.Debug, "SenderThread exception: {0}", ex);
			}
			if( KeepRunning ) {
				Log(LogLevel.Debug, "SenderThread: doing internal disconnect");
				DisconnectInternal(m_SenderThread);
			}
			Log(LogLevel.Debug, "SenderThread: ended");
		}
		
		[MTAThread]
		private void ReceiverThread(object param) {
			Socket socket = (Socket)param;
			byte[] buffer = new byte[65536], data;
			int recv;
			IAsyncResult async;
			Log(LogLevel.Debug, "ReceiverThread: started");
			try {
				while( KeepRunning && socket.Connected ) {
					Log(LogLevel.Debug, "ReceiverThread: beginning to receive");
					async = socket.BeginReceive(buffer, 0, buffer.Length, SocketFlags.None, null, null);
					try {
						async.AsyncWaitHandle.WaitOne(1000);
					}
					catch(ThreadInterruptedException) {
						Log(LogLevel.Debug, "ReceiverThread: interrupted while receiving");
						break;
					}
					recv = socket.EndReceive(async);
					Log(LogLevel.Debug, "ReceiverThread: received {0} bytes", recv);
					if( recv > 0 ) {
						// m_TempSocket.Shutdown(SocketShutdown.Send);
						data = new byte[recv];
						Array.Copy(buffer, data, recv);
						if( StreamDataEvent != null )
							StreamDataEvent.Invoke(this, new StreamDataEventArgs(data));
						else
							OnRawData(data);
					}
					else
						break;
				}
				
				Log(LogLevel.Debug, "ReceiverThread: end loop");
			}
			catch(Exception ex) {
				Log(LogLevel.Debug, "ReceiverThread exception: {0}", ex);
			}
			if( KeepRunning ) {
				Log(LogLevel.Debug, "ReceiverThread: doing internal disconnect");
				DisconnectInternal(m_ReceiverThread);
			}
			Log(LogLevel.Debug, "ReceiverThread: ended");
		}
		
		public void WaitSendEmpty() {
			while( !WaitSendEmpty(1000) ) {
				if( !Connected )
					throw new ConnectionLostException("RawClient lost connection while waiting for send buffer to become empty.");
			}
		}
		
		public void WaitSendReady() {
			while( !WaitSendReady(1000) ) {
				if( !Connected )
					throw new ConnectionLostException("RawClient lost connection while waiting for send buffer to get at least one byte.");
			}
		}
		
		public void WaitSendAccepts() {
			while( !WaitSendAccepts(1000) ) {
				if( !Connected )
					throw new ConnectionLostException("RawClient lost connection while waiting for send buffer to accept data.");
			}
		}
		
		public void WaitRecvEmpty() {
			while( !WaitRecvEmpty(1000) ) {
				if( !Connected )
					throw new ConnectionLostException("RawClient lost connection while waiting for recv buffer to become empty.");
			}
		}

		public void WaitRecvReady() {
			while( !WaitRecvReady(1000) ) {
				if( !Connected )
					throw new ConnectionLostException("RawClient lost connection while waiting for recv buffer to get at least one byte.");
			}
		}

		public void WaitRecvAccepts() {
			while( !WaitRecvAccepts(1000) ) {
				if( !Connected )
					throw new ConnectionLostException("RawClient lost connection while waiting for recv buffer to accept data.");
			}
		}
		
		public bool WaitSendEmpty(int timeoutMilliseconds) {
			return m_OutputEmpty.WaitOne(timeoutMilliseconds);
		}
		
		public bool WaitSendReady(int timeoutMilliseconds) {
			return m_OutputReady.WaitOne(timeoutMilliseconds);
		}
		
		public bool WaitSendAccepts(int timeoutMilliseconds) {
			return m_OutputAccepts.WaitOne(timeoutMilliseconds);
		}
		
		public bool WaitRecvEmpty(int timeoutMilliseconds) {
			return m_InputEmpty.WaitOne(timeoutMilliseconds);
		}
		
		public bool WaitRecvReady(int timeoutMilliseconds) {
			return m_InputReady.WaitOne(timeoutMilliseconds);
		}
		
		public bool WaitRecvAccepts(int timeoutMilliseconds) {
			return m_InputAccepts.WaitOne(timeoutMilliseconds);
		}
		
		private void CreateThreads(Server server) {
			Log(LogLevel.Debug, "Creating sender and receiver threads");
			Init(server);
			
			m_SenderThread = new Thread(SenderThread);
			m_SenderThread.IsBackground = true;
			m_SenderThread.Start(m_Socket);
			
			m_ReceiverThread = new Thread(ReceiverThread);
			m_ReceiverThread.IsBackground = true;
			m_ReceiverThread.Start(m_Socket);
		}
		
		public bool Connect() {
			return Connect(GetMyIp(), DefaultPort);
		}
		
		public bool Connect(int port) {
			return Connect(GetMyIp(), port);
		}
		
		public bool Connect(IPAddress addr) {
			return Connect(addr, DefaultPort);
		}
		
		public bool Connect(IPAddress addr, int port) {
			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			try {
				socket.NoDelay = true;
				socket.Connect(new IPEndPoint(addr, port));
				
				AssignSocket(socket, null);
			}
			// catch( SocketException ex ) {
			catch( Exception ex ) {
				Log(LogLevel.Debug, "Connect exception: {0}", ex);
				return false;
			}
			return true;
		}
		
		/// <summary>
		/// Called when connection is established, but before sender and receiver threads are created.
		/// Be aware that this mehod is called within sync root.
		/// NO MATTER WHAT, DO NOT USE THESE METHODS AND PROPERTIES IN INIT() METHOD:
		/// 	Connected property
		/// 	Connect()
		/// 	Disconnect()
		/// 	AssignSocket()
		/// </summary>
		/// <param name="server"></param>
		protected virtual void Init(Server server) {
		}
		
		/// <summary>
		/// Called when disconnecting, but before stopping sender and receiver threads.
		/// Be aware that this mehod is called within sync root.
		/// NO MATTER WHAT, DO NOT USE THESE METHODS AND PROPERTIES IN INIT() METHOD:
		/// 	Connected property
		/// 	Connect()
		/// 	Disconnect()
		/// 	AssignSocket()
		/// </summary>
		/// <param name="is_manual"></param>
		protected virtual void Deinit(bool is_manual) {
		}

		/// <summary>
		/// Called after all OnConnect event handlers invoked
		/// </summary>
		/// <param name="server"></param>
		protected virtual void PostInit(Server server) {
		}

		/// <summary>
		/// Called after all OnDisconnect event handlers invoked.
		/// </summary>
		/// <param name="is_manual"></param>
		protected virtual void PostDeinit(bool is_manual) {
		}

		/// <summary>
		/// Called if any OnConnect event handler throws an error.
		/// </summary>
		/// <param name="server"></param>
		/// <param name="ex"></param>
		/// <returns>TRUE to throw exception further or FALSE to ignore errors</returns>
		protected virtual void OnInitException(Server server, Exception ex) {
			if( !(ex is ThreadInterruptedException ) )
				throw ex;
		}

		/// <summary>
		/// Called after all OnDisconnect event handlers invoked.
		/// </summary>
		/// <param name="is_manual"></param>
		/// <param name="ex"></param>
		/// <returns>TRUE to throw exception further or FALSE to ignore errors</returns>
		protected virtual void OnDeinitException(bool is_manual, Exception ex) {
			if( !(ex is ThreadInterruptedException ) )
				throw ex;
		}
		
		public bool AssignSocket(Socket socket, Server server) {
			lock(SyncRoot) {
				if( socket == m_Socket )
					return false;
				if( m_Socket != null )
					DisconnectInternal(null);
				m_Socket = socket;
				if( m_Socket == null )
					return false;
				m_Socket.NoDelay = true;
				m_Socket.ReceiveTimeout = 1000;
				m_Socket.SendTimeout = 1000;
				CreateThreads(server);
				
				m_LocalEndpoint = (IPEndPoint)socket.LocalEndPoint;
				m_RemoteEndpoint = (IPEndPoint)socket.RemoteEndPoint;
				
				Log(LogLevel.Info, "Connected: Local={0}; Remote={1}", m_LocalEndpoint.ToString(), m_RemoteEndpoint.ToString());
			}
			
			try {
				OnConnect(server);
				if( ConnectEvent != null )
					ConnectEvent.Invoke(this, new ConnectEventArgs(server));
				PostInit(server);
			}
			catch(Exception ex) {
				GameDebugger.NetLog(LogLevel.Error, "Exception while initializing socket: {0}", ex);
				OnInitException(server, ex);
			}
			
			return true;
		}
		
		private void DisconnectInternal(Thread callerThread) {
			bool invokeEvent = false;
			
			// prevent deadlock in case when multiple threads try to disconnect at the same time
			lock(SyncDisconnect) {
				if( AlreadyDisconnecting ) {
					Log(LogLevel.Debug, "Already disconnecting. Disconnect call stack: {0}", Environment.StackTrace);
					return;
				}
				AlreadyDisconnecting = true;
			}
			
			Log(LogLevel.Debug, "Thread is clear to disconnect");
			
			lock(SyncRoot) {
				if( m_Socket != null ) {
					Log(LogLevel.Debug, "Beginning to disconnect: IsManual={0}", callerThread == null);
					KeepRunning = false;
					
					Deinit(callerThread == null);
					
					// if we disconnect that means we no longer care what data comes in
					// so shutting down receiver first is logical, right?
					
					Log(LogLevel.Debug, "Interrupting receiver thread");
					try { m_ReceiverThread.Interrupt(); } catch {}

					Log(LogLevel.Debug, "Interrupting sender thread");
					try { m_SenderThread.Interrupt(); } catch {}

					Log(LogLevel.Debug, "Interruption signals sent");
					
					if( callerThread != m_SenderThread )
						try { m_SenderThread.Join(); } catch {}
				
					if( callerThread != m_ReceiverThread )
						try { m_ReceiverThread.Join(); } catch {}

					Log(LogLevel.Debug, "Threads joined");
					
					try { m_Socket.Shutdown(SocketShutdown.Send); } catch {}

					Log(LogLevel.Debug, "Socket is shut down");

					try { m_Socket.Close(); } catch {}

					Log(LogLevel.Debug, "Socket is closed");
					
					m_SenderThread = null;
					m_ReceiverThread = null;
					m_Socket = null;
					KeepRunning = true;
					invokeEvent = true;
					
					Log(LogLevel.Debug, "Clearing send queue");
					
					ClearBuffers();
					
					Log(LogLevel.Info, "Disconnected: Local={0}; Remote={1}", m_LocalEndpoint.ToString(), m_RemoteEndpoint.ToString());
				}
			}
			
			if( invokeEvent ) {
				try {
					OnDisconnect(callerThread == null);
					if( DisconnectEvent != null )
						DisconnectEvent(this, new DisconnectEventArgs(callerThread == null));
					PostDeinit(callerThread == null);
				}
				catch(Exception ex) {
					GameDebugger.NetLog(LogLevel.Error, "Exception while disconnecting socket: {0}", ex);
					OnDeinitException(callerThread == null, ex);
				}
			}
			
			AlreadyDisconnecting = false;
		}
		
		public void Disconnect() {
			DisconnectInternal(null);
		}
		
		public void ClearBuffers() {
			lock(m_Output) {
				m_Output.Clear();
				m_OutputReady.Reset();
				m_OutputEmpty.Set();
				m_OutputAccepts.Set();
			}
			lock(m_Input) {
				m_Input.Clear();
				m_InputReady.Reset();
				m_InputEmpty.Set();
				m_InputAccepts.Set();
			}
		}

		public int Send(byte[] data) {
			return Send(data, 0, data.Length);
		}
		
		public int Send(byte[] data, int offset, int length) {
			// if( Thread.CurrentThread == m_SenderThread ) // this should never happen
				// throw new UserFriendlyException("Tried to use Send() within sender thread. This would lock the thread.", "Connection error");
			if( !Connected )
				return 0;
			int sent = 0, len;
			lock(SyncSend) {
				while(sent < length) {
					Log(LogLevel.Debug, "Waiting for send buffer to get some free bytes");
					try {
						while( !WaitSendAccepts(1000) ) {
							Log(LogLevel.Debug, "One more second");
							if( !Connected ) {
								Log(LogLevel.Debug, "Send interrupted at {0} bytes (not connected)", sent);
								break;
							}
						}
					}
					catch {
						Log(LogLevel.Debug, "Send interrupted at {0} bytes (thread interrupted)", sent);
						break;
					}
					
					Log(LogLevel.Debug, "Trying to queue {0} bytes into send buffer", length - sent);
					lock(m_Output) {
						len = Math.Min(MaxSendBufferSize - m_Output.Length, length - sent);
						if( len > 0 ) {
							m_Output.Enqueue(data, offset + sent, len);
							sent += len;
							m_OutputReady.Set();
							m_OutputEmpty.Reset();
							if( m_Output.Length >= MaxSendBufferSize )
								m_OutputAccepts.Reset();
							Log(LogLevel.Debug, "Send enqueued {0} bytes (total buffered {1} bytes)", len, m_Output.Length);
						}
					}
				}
			}
			return sent;
		}
		
		public byte[] Recv(int length) {
			byte[] buffer = new byte[length];
			int received = Recv(buffer, 0, length);
			return (received < length) ? null : buffer;
		}

		public int Recv(byte[] buffer, int length) {
			return Recv(buffer, 0, length);
		}

		public int Recv(byte[] buffer) {
			return Recv(buffer, 0, buffer.Length);
		}
		
		public int Recv(byte[] buffer, int offset, int length) {
			int received = 0, len;
			while( received < length ) {
				len = RecvPartial(buffer, offset + received, length - received, true);
				if( len <= 0 )
					break;
				received += len;
			}
			return received;
		}

		public int RecvPartial(byte[] buffer) {
			return RecvPartial(buffer, 0, buffer.Length, true);
		}

		public int RecvPartial(byte[] buffer, int length) {
			return RecvPartial(buffer, 0, length, true);
		}
		
		public int RecvPartial(byte[] buffer, int offset, int length) {
			return RecvPartial(buffer, offset, length, true);
		}
		
		public int RecvPartial(byte[] buffer, int offset, int length, bool waitForSomeData) {
			if( !Connected )
				return -1;
			if( StreamDataEvent != null )
				throw new UserFriendlyException("Tried to RecvPartial() data from IGE.Net.RawClient while it has a StreamDataEvent attached. You cannot use both blocking and asynchronous receiving at the same time.", "Connection error");
			if( Thread.CurrentThread == m_ReceiverThread )
				throw new UserFriendlyException("Tried to use RecvPartial() within receiver thread. This would lock the thread.", "Connection error");
			int received = 0;
			lock(SyncRecv) {
				while(length > 0) {
					if( waitForSomeData ) {
						try { WaitRecvReady(); }
						catch {
							Log(LogLevel.Debug, "RecvPartial interrupted");
							return -1;
						}
					}
					
					lock(m_Input) {
						received = Math.Min(length, m_Input.Length);
						if( received > 0 ) {
							m_Input.Dequeue(buffer, offset, received);
							if( m_Input.Length == 0 ) {
								m_InputReady.Reset();
								m_InputEmpty.Set();
								m_InputAccepts.Set();
							}
							else if( m_Input.Length < MaxRecvBufferSize )
								m_InputAccepts.Set();
							Log(LogLevel.Debug, "RecvPartial dequeued {0} bytes (total buffered {1} bytes)", received, m_Input.Length);
							break;
						}
						if( !waitForSomeData )
							break;
					}
				}
			}
			return received;
		}

		public byte[] RecvAll() {
			return RecvAll(true);
		}
		
		public byte[] RecvAll(bool waitForSomeData) {
			if( !Connected )
				return null;
			if( StreamDataEvent != null )
				throw new UserFriendlyException("Tried to RecvAll() data from IGE.Net.RawClient while it has a StreamDataEvent attached. You cannot use both blocking and asynchronous receiving at the same time.", "Connection error");
			if( Thread.CurrentThread == m_ReceiverThread )
				throw new UserFriendlyException("Tried to use RecvAll() within receiver thread. This would lock the thread.", "Connection error");
			int length;
			lock(SyncRecv) {
				while(true) {
					if( waitForSomeData ) {
						try { WaitRecvReady(); }
						catch {
							Log(LogLevel.Debug, "RecvAll interrupted");
							return null;
						}
					}
					
					lock(m_Input) {
						length = m_Input.Length;
						if( length > 0 ) {
							byte[] data = m_Input.Dequeue(length);
							m_InputReady.Reset();
							m_InputEmpty.Set();
							m_InputAccepts.Set();
							Log(LogLevel.Debug, "RecvAll dequeued {0} bytes", length);
							return data;
						}
						if( !waitForSomeData )
							return new byte[0];
					}
				}
			}
		}
		
		public byte[] RecvToEnd() {
			if( !Connected )
				return null;
			if( StreamDataEvent != null )
				throw new UserFriendlyException("Tried to RecvToEnd() data from IGE.Net.RawClient while it has a StreamDataEvent attached. You cannot use both blocking and asynchronous receiving at the same time.", "Connection error");
			if( Thread.CurrentThread == m_ReceiverThread )
				throw new UserFriendlyException("Tried to use RecvToEnd() within receiver thread. This would lock the thread.", "Connection error");
			ByteQueue data = new ByteQueue(65536);
			int length;
			lock(SyncRecv) {
				while(Connected) {
					try { WaitRecvReady(); }
					catch {
						Log(LogLevel.Debug, "RecvToEnd interrupted");
						break;
					}
					
					lock(m_Input) {
						length = m_Input.Length;
						if( length > 0 ) {
							data.Enqueue(m_Input.Dequeue(length));
							m_InputReady.Reset();
							m_InputEmpty.Set();
							m_InputAccepts.Set();
							Log(LogLevel.Debug, "RecvToEnd dequeued {0} bytes", length);
						}
					}
				}
			}
			return data.ToArray();
		}

		protected void EnqueueIncomingRawData(byte[] data, int offset, int length) {
			int stored = offset, len;
			length += offset;
			while(stored < length) {
				try { WaitRecvAccepts(); }
				catch {
					Log(LogLevel.Debug, "EnqueueIncomingRawData interrupted at {0} bytes", stored - offset);
					break;
				}
				
				lock(m_Input) {
					len = Math.Min(MaxRecvBufferSize - m_Input.Length, length - stored);
					if( len > 0 ) {
						m_Input.Enqueue(data, stored, len);
						stored += len;
						m_InputReady.Set();
						m_InputEmpty.Reset();
						if( m_Input.Length >= MaxRecvBufferSize )
							m_InputAccepts.Reset();
						Log(LogLevel.Debug, "EnqueueIncomingRawData enqueued {0} bytes (total buffered {1} bytes)", len, m_Input.Length);
					}
				}
			}
		}
		
		protected void EnqueueIncomingRawData(byte[] data) {
			EnqueueIncomingRawData(data, 0, data.Length);
		}
		
		protected virtual void OnConnect(Server server) {
		}
		
		protected virtual void OnDisconnect(bool is_manual) {
		}
		
		protected virtual void OnRawData(byte[] data) {
			if( m_RawDataHandlerOverriden )
				throw new UserFriendlyException(String.Format("Class {0} must not call IGE.Net.RawClient.OnRawData() method from it's overriden version. Exception is thrown to protect from accidental connection lockdown. If you are going to use RawNetStream or any RawClient read/recv methods on deriving class then use RawClient.EnqueueIncomingRawData() method to store the raw data into the input buffer.", GetType().FullName), "Connection error");
			EnqueueIncomingRawData(data);
		}
		
		public IgeNetStream GetStream() {
			return new IgeNetStream(this);
		}
	}
	
	public class ConnectionLostException : Exception {
		public ConnectionLostException(string message) : base(message) {
		}
		
		public ConnectionLostException() : this("Connection lost") {
		}
	}
}
