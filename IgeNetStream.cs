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

namespace IGE.Net {
	/// <summary>
	/// This is a combination of network and memory stream.
	/// While auto flushing is enabled, all data is sent to client.
	/// When auto flushing is disabled, all data is stored to the buffer.
	/// Buffer does not get flushed to the client by Flush() method unless auto flushing is enabled.
	/// Such weird functionality was used to make CryptoStream work correctly with sent data.
	/// </summary>
	public class IgeNetStream : Stream {
		private const uint ChecksumBase = 0x80FC1FE7;
		private RawClient m_Client = null;
		private bool m_CalculateChecksums = false;
		private uint m_ReadChecksum = ChecksumBase;
		private uint m_WriteChecksum = ChecksumBase;
		private int m_ReadChecksumShift = 0;
		private int m_WriteChecksumShift = 0;
		
		public bool CalculateChecksums { get { return m_CalculateChecksums; } set { m_CalculateChecksums = value; } }
		public uint ReadChecksum { get { return m_ReadChecksum; } }
		public uint WriteChecksum { get { return m_WriteChecksum; } }
		
		public override bool CanRead {
			get {
				if( m_Client == null )
					throw new ObjectDisposedException("IgeNetStream");
				return m_Client.IsSynchronous;
			}
		}
		public override bool CanWrite {
			get {
				return true;
			}
		}
		public override bool CanTimeout {
			get {
				return false;
			}
		}
		public override bool CanSeek {
			get {
				return false;
			}
		}
		
		public override long Position {
			get {
				throw new NotSupportedException();
			}
			set {
				throw new NotSupportedException();
			}
		}
		public override long Length {
			get {
				throw new NotSupportedException();
			}
		}
		public virtual bool EndOfStream {
			get {
				if( m_Client == null )
					throw new ObjectDisposedException("IgeNetStream");
				return !m_Client.Connected;
			}
		}
		public virtual bool Disposed { get { return m_Client == null; } }
		
		internal IgeNetStream(RawClient client) {
			m_Client = client;
		}
		
		protected override void Dispose(bool disposing) {
			m_Client = null;
			base.Dispose(disposing);
		}
		
		public void ResetWriteChecksum() {
			m_WriteChecksum = ChecksumBase;
			m_WriteChecksumShift = 0;
		}

		public void ResetReadChecksum() {
			m_ReadChecksum = ChecksumBase;
			m_ReadChecksumShift = 0;
		}
		
		public override void Write(byte[] buffer, int offset, int length) {
			if( m_Client == null )
				throw new ObjectDisposedException("IgeNetStream");
			GameDebugger.NetLog(LogLevel.Debug, "IgeNetStream Write({0})", length);
			int sent = m_Client.Send(buffer, offset, length);
			if( sent != length )
				throw new IOException("Client disconnected");
			if( m_CalculateChecksums )
				for( int pos = offset, len = sent; len > 0; len--, pos++, m_WriteChecksumShift = (m_WriteChecksumShift + 8) & 0x1F )
					m_WriteChecksum ^= (uint)buffer[pos] << m_WriteChecksumShift;
		}
		
		public override int Read(byte[] buffer, int offset, int length) {
			if( m_Client == null )
				throw new ObjectDisposedException("IgeNetStream");
			GameDebugger.NetLog(LogLevel.Debug, "IgeNetStream Read({0})", length);
			int received = m_Client.RecvPartial(buffer, offset, length, true);
			if( received < 0 )
				throw new IOException("Client disconnected");
			if( m_CalculateChecksums )
				for( int pos = offset, len = received; len > 0; len--, pos++, m_ReadChecksumShift = (m_ReadChecksumShift + 8) & 0x1F )
					m_ReadChecksum ^= (uint)buffer[pos] << m_ReadChecksumShift;

			return received;
		}
		
		public virtual byte[] ReadToEnd() {
			if( m_Client == null )
				throw new ObjectDisposedException("IgeNetStream");
			GameDebugger.NetLog(LogLevel.Debug, "IgeNetStream ReadToEnd()");
			return m_Client.RecvToEnd();
		}
		
		public override void SetLength(long length) {
			throw new NotSupportedException();
		}
		
		public override long Seek(long offset, SeekOrigin origin) {
			throw new NotSupportedException();
		}
		
		/// <summary>
		/// </summary>
		public override void Flush() {
		}

		public override void Close() {
			// This stream must not dispose when closed. Otherwise cryptostream will kill the connection.
		}
	}
}
