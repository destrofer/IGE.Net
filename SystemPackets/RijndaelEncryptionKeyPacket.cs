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
using System.Security.Cryptography;

namespace IGE.Net.SystemPackets {
	[Packet(0xFFFFFFFD, PacketEncryptionMethod.RSA)]
	public sealed class RijndaelEncryptionKeyPacket : Packet {
		public RijndaelManaged Rijndael = null;
		
		public RijndaelEncryptionKeyPacket() {
		}
		
		public RijndaelEncryptionKeyPacket(RijndaelManaged rijndael) {
			Rijndael = rijndael;
		}
		
		public override void Serialize(BinaryWriter writer) {
			base.Serialize(writer);
			if( Rijndael == null || Rijndael.KeySize == 0 || Rijndael.BlockSize == 0 ) {
				writer.Write(0);
			}
			else {
				byte[] key = Rijndael.Key;
				byte[] iv = Rijndael.IV;
				writer.Write(Rijndael.KeySize);
				writer.Write(Rijndael.BlockSize);
				writer.Write(key.Length);
				writer.Write(key);
				writer.Write(iv.Length);
				writer.Write(iv);
			}
		}
		
		public override void Deserialize(BinaryReader reader) {
			base.Deserialize(reader);
			int keySize = reader.ReadInt32();
			if( keySize == 0 ) {
				Rijndael = null;
			}
			else {
				Rijndael = new RijndaelManaged();
				Rijndael.KeySize = keySize;
				Rijndael.BlockSize = reader.ReadInt32();
				Rijndael.Key = reader.ReadBytes(reader.ReadInt32());
				Rijndael.IV = reader.ReadBytes(reader.ReadInt32());
			}
		}
	}
}
