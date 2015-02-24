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
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;

namespace IGE.Net {
	public class Server	{
		protected HashSet<Listener> m_Listeners;
		protected HashSet<RawClient> m_Clients;
		protected Type m_ClientType;
		
		public event ListenerCloseEventHandler ListenerCloseEvent;
		public event DisconnectEventHandler ClientDisconnectEvent;
		public event ConnectEventHandler ClientConnectEvent;
		
		public Server(Type client_type) {
			if( !client_type.Equals(typeof(RawClient)) && !client_type.IsSubclassOf(typeof(RawClient)) )
				throw new Exception(String.Format("Client type MUST derive from {0} or be that exact class in order to run IGE server.", typeof(RawClient).FullName));
			m_Listeners = new HashSet<Listener>();
			m_Clients = new HashSet<RawClient>();
			m_ClientType = client_type;
		}

		public bool Start() {
			return Start(RawClient.GetMyIp(), RawClient.DefaultPort);
		}

		public bool Start(int port) {
			return Start(RawClient.GetMyIp(), port);
		}

		public bool Start(IPAddress addr) {
			return Start(addr, RawClient.DefaultPort);
		}
		
		public bool Start(IPAddress addr, int port) {
			Listener listener = new Listener(addr, port);
			listener.CloseEvent += OnListenerCloseInternal;
			listener.NewConnectionEvent += OnNewConnectionInternal;
			if( !listener.Start() ) {
				listener.CloseEvent -= OnListenerCloseInternal;
				listener.NewConnectionEvent -= OnNewConnectionInternal;
				return false;
			}
			lock(m_Listeners) {
				m_Listeners.Add(listener);
			}
			return true;
		}
		
		public virtual void Stop() {
			foreach (Listener listener in GetListeners()) {
				listener.Stop();
			}
		}
		
		public virtual void DisconnectAll() {
			RawClient[] clients = GetClients();
			GameDebugger.NetLog(LogLevel.Debug, "{0}/DisconnectAll: {1} clients", GetType().Name, clients.Length);
			foreach (RawClient client in clients) {
				GameDebugger.NetLog(LogLevel.Debug, "{0}/DisconnectAll: {1}", GetType().Name, client);
				client.Disconnect();
			}
		}
		
		public virtual Listener[] GetListeners() {
			Listener[] listeners;
			lock(m_Listeners) {
				listeners = new Listener[m_Listeners.Count];
				m_Listeners.CopyTo(listeners);
			}
			return listeners;
		}
		
		public int ClientCount { get { return m_Clients.Count; } }
		
		public virtual RawClient[] GetClients() {
			RawClient[] clients;
			lock(m_Clients) {
				clients = new RawClient[m_Clients.Count];
				m_Clients.CopyTo(clients);
			}
			return clients;
		}
		
		public virtual T[] GetClients<T>() where T : RawClient {
			return (T[])GetClients();
		}
		
		protected virtual void OnListenerClose(Listener listener, ListenerCloseEventArgs args) {
		}
		
		protected virtual void OnClientDisconnect(RawClient client, DisconnectEventArgs args) {
		}
		
		protected virtual void OnClientConnect(RawClient client, ConnectEventArgs args) {		
		}

		private void OnListenerCloseInternal(Listener listener, ListenerCloseEventArgs args) {
			bool ok = false;
			lock( m_Listeners ) {
				if( m_Listeners.Contains(listener) ) {
					listener.CloseEvent -= OnListenerCloseInternal;
					listener.NewConnectionEvent -= OnNewConnectionInternal;
					m_Listeners.Remove(listener);
					ok = true;
				}
			}
			if( ok ) {
				OnListenerClose(listener, args);
				if( ListenerCloseEvent != null )
					ListenerCloseEvent.Invoke(listener, args);
			}
		}

		private void OnClientDisconnectInternal(RawClient client, DisconnectEventArgs args) {
			bool ok = false;
			lock( m_Clients ) {
				if( m_Clients.Contains(client) ) {
					client.DisconnectEvent -= OnClientDisconnectInternal;
					client.ConnectEvent -= OnClientConnectInternal;
					m_Clients.Remove(client);
					ok = true;
				}
			}
			if( ok ) {
				OnClientDisconnect(client, args);
				if( ClientDisconnectEvent != null )
					ClientDisconnectEvent.Invoke(client, args);
			}
		}
		
		private void OnClientConnectInternal(RawClient client, ConnectEventArgs args) {
			bool ok = false;
			lock( m_Clients ) {
				if( !m_Clients.Contains(client) ) {
					m_Clients.Add(client);
					ok = true;
				}
			}
			if( ok ) {
				OnClientConnect(client, args);
				if( ClientConnectEvent != null )
					ClientConnectEvent.Invoke(client, args);
			}
		}

		private void OnNewConnectionInternal(Listener listener, NewConnectionEventArgs args) {
			RawClient client = (RawClient)Activator.CreateInstance(m_ClientType);
			client.DisconnectEvent += OnClientDisconnectInternal;
			client.ConnectEvent += OnClientConnectInternal;
			client.AssignSocket(args.Socket, this);
		}
	}
}
