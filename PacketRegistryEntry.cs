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
	public sealed class PacketRegistryEntry {
		private uint m_Id;
		public uint Id { get { return m_Id; } }
		
		private Type m_Type;
		public Type Type { get { return m_Type; } }
		
		private PacketEncryptionMethod m_Encryption;
		public PacketEncryptionMethod Encryption { get { return m_Encryption; } }
		
		public PacketRegistryEntry(uint id, Type type, PacketEncryptionMethod encryption )
		{
			m_Id = id;
			m_Type = type;
			m_Encryption = encryption;
		}
		
		public override string ToString()
		{
			return String.Format("PacketRegistryEntry (Id=0x{0:X8}, Type={1}, Encryption={2})", m_Id, m_Type.FullName, m_Encryption);
		}

	}
}
