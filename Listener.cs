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
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;

namespace IGE.Net {
	public class Listener {
		public event NewConnectionEventHandler NewConnectionEvent;
		public event ListenerCloseEventHandler CloseEvent;
		
		protected IPEndPoint m_Binding;
		protected Socket m_Socket;
		protected Thread m_Thread;
		
		public IPEndPoint Binding {
			get { return m_Binding; }
			set { SetBinding(value); }
		}
		
		public string BoundIp {
			get { return m_Binding.Address.ToString(); }
			set { SetBinding(new IPEndPoint(IPAddress.Parse(value), m_Binding.Port)); }
		}
		
		public int BoundPort {
			get { return m_Binding.Port; }
			set { SetBinding(new IPEndPoint(m_Binding.Address, value)); }
		}
		
		public Listener() {
			m_Socket = null;
			m_Binding = new IPEndPoint(RawClient.GetMyIp(), RawClient.DefaultPort);
			m_Thread = null;
		}
		
		public Listener(string ip) : this() {
			m_Binding = new IPEndPoint(IPAddress.Parse(ip), RawClient.DefaultPort);
		}

		public Listener(IPAddress ip) : this() {
			m_Binding = new IPEndPoint(ip, RawClient.DefaultPort);
		}
		
		public Listener(int port) : this() {
			m_Binding = new IPEndPoint(RawClient.GetMyIp(), port);
		}
		
		public Listener(string ip, int port) : this() {
			m_Binding = new IPEndPoint(IPAddress.Parse(ip), port);
		}
		
		public Listener(IPAddress ip, int port) : this() {
			m_Binding = new IPEndPoint(ip, port);
		}
		
		public Listener(IPEndPoint endpoint) : this() {
			m_Binding = endpoint;
		}
		
		public bool SetBinding(IPEndPoint endpoint) {
			return false;
		}
		
		public bool Start() {
			if( m_Socket != null )
				return false;
			m_Thread = new Thread(ListenerThread);
			m_Thread.Start();
			return true;
		}
		
		public void Stop() {
			if( m_Socket == null )
				return;
			m_Socket.Close();
		}
		
		[MTAThread]
		private void ListenerThread() {
			m_Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			m_Socket.Bind(m_Binding);
			m_Socket.Listen(5);
			Socket new_connection = null;
			
			do {
				new_connection = null;
				try {
					new_connection = m_Socket.Accept();
				}
				catch( SocketException ) {
				}
				if( new_connection == null )
					break;
				if( NewConnectionEvent != null )
					NewConnectionEvent.Invoke(this, new NewConnectionEventArgs(new_connection));
				else
					new_connection.Close();
			} while(true);
			m_Socket = null;
			
			if( CloseEvent != null )
				CloseEvent.Invoke(this, new ListenerCloseEventArgs());
		}
	}
}