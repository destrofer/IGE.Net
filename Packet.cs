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
using System.Threading;
using System.IO;
using System.Reflection;
using System.Text;
using System.Globalization;

namespace IGE.Net {
	public abstract class Packet {
		private static Dictionary<uint, PacketRegistryEntry> PacketIdRegistry = new Dictionary<uint, PacketRegistryEntry>();
		private static Dictionary<Type, PacketRegistryEntry> PacketTypeRegistry = new Dictionary<Type, PacketRegistryEntry>();
	
		static Packet() {
			// First time the packet class is used, it scans the assembly for
			// classes derived from IGE.Net.Packet class and having
			// [Packet(0x####)] attribute assigned to them
			// TODO: try to rescan assemblies if packet with an unknown id is received
			foreach( Assembly asy in AppDomain.CurrentDomain.GetAssemblies() )
				ScanAssemblyForPackets(asy);
		}
		
		private static void ScanAssemblyForPackets(Assembly asy) {
			Type packetType = typeof(Packet);
			Type attribType = typeof(PacketAttribute);
			PacketRegistryEntry entry;
			uint packetId;
			
			foreach( Type type in asy.GetTypes() ) {
				if( !type.IsSubclassOf(packetType) )
					continue;
				foreach( PacketAttribute attr in type.GetCustomAttributes(attribType, false) ) {
					packetId = attr.PacketId;
					if( PacketIdRegistry.ContainsKey(packetId) )
						throw new Exception(String.Format("Duplicate packet id used: {0}. Classes using it: {1} and {2}", ((PacketRegistryEntry)PacketIdRegistry[packetId]).Type.FullName, type.FullName));
					entry = new PacketRegistryEntry(packetId, type, attr.Encryption);
					PacketIdRegistry.Add(packetId, entry);
					PacketTypeRegistry.Add(type, entry);
				}
			}
		}

		/*public static Dictionary<uint, PacketRegistryEntry> GetPacketRegist() {
			return PacketIdRegistry;
		}*/
		
		public static Packet CreateInstance(uint packetId) {
			if( !PacketIdRegistry.ContainsKey(packetId) )
				throw new Exception(String.Format("Unrecognized packet id: 0x{0:X8}", packetId));
			return (Packet)Activator.CreateInstance(((PacketRegistryEntry)PacketIdRegistry[packetId]).Type, BindingFlags.CreateInstance, null, null, CultureInfo.CurrentCulture);
		}

		public static PacketRegistryEntry FindPacketInfo(Packet packet) {
			return FindPacketInfo(packet.GetType());
		}
		
		public static PacketRegistryEntry FindPacketInfo(Type packetType) {
			if( !PacketTypeRegistry.ContainsKey(packetType) )
				throw new Exception(String.Format("Cannot send a packet of class {0} since it does not have an id assigned. Use [Packet(ushort packet_id)] attribute on the packet class to assign an id.", packetType.FullName));
			return PacketTypeRegistry[packetType];
		}
		
		public static PacketRegistryEntry FindPacketInfo(uint packetId) {
			if( !PacketIdRegistry.ContainsKey(packetId) )
				throw new Exception(String.Format("Unrecognized packet id: 0x{0:X8}", packetId));
			return (PacketRegistryEntry)PacketIdRegistry[packetId];
		}
		
		public Packet() {
		}
		
		public uint Id { get { return FindPacketInfo(this.GetType()).Id; } }
		public PacketEncryptionMethod Encryption { get { return FindPacketInfo(this.GetType()).Encryption; } }
		
		public virtual void Serialize(BinaryWriter writer) {
		}
		
		public virtual void Deserialize(BinaryReader reader) {
		}
		
		public override string ToString() {
			return "";
		}
	}
}
