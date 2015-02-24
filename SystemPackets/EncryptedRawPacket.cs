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

namespace IGE.Net.SystemPackets {
	[Packet(0xFFFFFFFB, PacketEncryptionMethod.Rijndael)]
	public class EncryptedRawPacket : Packet {
		public byte[] Data = null;
		
		public EncryptedRawPacket(byte[] data) {
			Data = data;
		}
		
		public EncryptedRawPacket() {
		}
		
		public override void Serialize(BinaryWriter writer) {
			base.Serialize(writer);
			if( Data == null )
				writer.Write((int)-1);
			else {
				writer.Write(Data.Length);
				if( Data.Length > 0 )
					writer.Write(Data);
			}
		}
		
		public override void Deserialize(BinaryReader reader) {
			base.Deserialize(reader);
			int size = reader.ReadInt32();
			if( size < 0 )
				Data = null;
			else
				Data = (size == 0) ? new byte[0] : reader.ReadBytes(size);
		}
	}
}
