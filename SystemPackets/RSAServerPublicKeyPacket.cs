﻿/*
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
	// must not be encrypted, or connection will lock down
	[Packet(0xFFFFFFFF)]
	public class RSAServerPublicKeyPacket : Packet {
		public RSACryptoServiceProvider RSA = null;
		public byte[] Signature = null;
		
		public RSAServerPublicKeyPacket() {
		}
		
		public RSAServerPublicKeyPacket(RSACryptoServiceProvider rsa, byte[] signature) {
			RSA = rsa;
			Signature = signature;
		}
		
		public override void Serialize(BinaryWriter writer) {
			base.Serialize(writer);
			if( RSA == null ) {
				writer.Write((int)0);
			}
			else {
				byte[] key = RSA.ExportCspBlob(false);
				writer.Write(RSA.KeySize);
				writer.Write(key.Length);
				writer.Write(key);
				writer.Write(Signature.Length);
				writer.Write(Signature);
			}
		}
		
		public override void Deserialize(BinaryReader reader) {
			base.Deserialize(reader);
			int keySize = reader.ReadInt32();
			if( keySize == 0 ) {
				RSA = null;
			}
			else {
				RSA = new RSACryptoServiceProvider(keySize);
				RSA.PersistKeyInCsp = false;
				RSA.ImportCspBlob(reader.ReadBytes(reader.ReadInt32()));
				Signature = reader.ReadBytes(reader.ReadInt32());
			}
		}
	}
}
