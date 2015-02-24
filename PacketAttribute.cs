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

namespace IGE.Net {
	[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
	public class PacketAttribute : Attribute {
		protected uint m_PacketId;
		public uint PacketId { get { return m_PacketId; } }
		
		protected PacketEncryptionMethod m_Encryption;
		public PacketEncryptionMethod Encryption { get { return m_Encryption; } }
		
		public PacketAttribute(uint packet_id) : this(packet_id, PacketEncryptionMethod.None) {
		}
		
		public PacketAttribute(uint packet_id, PacketEncryptionMethod encryption ) {
			m_PacketId = packet_id;
			m_Encryption = encryption;
		}
	}
	
	public enum PacketEncryptionMethod : byte {
		None, // no security
		Rijndael, // good security, good speed
		RSA // max security, low speed, has a limitation: packet size must be smaller than RSA key size
	}
}
