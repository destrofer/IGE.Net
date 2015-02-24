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
using System.Net.Sockets;

namespace IGE.Net {
	public delegate void ListenerCloseEventHandler(Listener listener, ListenerCloseEventArgs args);
	public delegate void NewConnectionEventHandler(Listener listener, NewConnectionEventArgs args);
	public delegate void DisconnectEventHandler(RawClient client, DisconnectEventArgs args);
	public delegate void ConnectEventHandler(RawClient client, ConnectEventArgs args);
	public delegate void PacketReceivedEventHandler(RawClient client, PacketReceivedEventArgs args);
	public delegate void StreamDataEventHandler(RawClient client, StreamDataEventArgs args);

	public class ListenerCloseEventArgs : EventArgs {
		public ListenerCloseEventArgs() : base() {
		}
	}
	
	public class NewConnectionEventArgs : EventArgs {
		protected Socket m_Socket;
		public Socket Socket { get { return m_Socket; } }
		
		public NewConnectionEventArgs(Socket socket) : base() {
			m_Socket = socket;
		}
	}
	
	public class DisconnectEventArgs : EventArgs {
		protected bool m_IsManual;
		public bool IsManual { get { return m_IsManual; } }
		
		public DisconnectEventArgs(bool is_manual) : base() {
			m_IsManual = is_manual;
		}
	}
	
	public class ConnectEventArgs : EventArgs {
		protected Server m_Server;
		public Server Server { get { return m_Server; } }
		
		public ConnectEventArgs(Server server) : base() {
			m_Server = server;
		}
	}
	
	public class PacketReceivedEventArgs : EventArgs {
		protected Packet m_Packet;
		public Packet Packet { get { return m_Packet; } }

		public PacketReceivedEventArgs(Packet packet) : base() {
			m_Packet = packet;
		}
	}
	
	public class StreamDataEventArgs : EventArgs {
		protected byte[] m_Data;
		public byte[] Data { get { return m_Data; } }

		public StreamDataEventArgs(byte[] data) : base() {
			m_Data = data;
		}
	}
}
