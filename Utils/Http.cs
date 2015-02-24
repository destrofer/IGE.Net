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
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Drawing;

using System.IO;

using SystemBitmap = System.Drawing.Bitmap;

namespace IGE.Net.Utils {
	public class Http {
		public Dictionary<string, string> Headers = null;
		
		public string this[string key] {
			get { return Headers[key]; }
			set {
				if( value == null )
					Headers.Remove(key);
				else
					Headers[key] = value;
			}
		}
		
		public Http() : this(new Dictionary<string, string>()) {
		}
		
		public Http(Dictionary<string, string> headers) {
			Headers = headers;
		}
		
		protected virtual void ApplyHeaders(HttpWebRequest request) {
			foreach( KeyValuePair<string, string> pair in Headers ) {
				switch(pair.Key.ToLower()) {
					case "method": request.Method = pair.Value; break;
					case "referer": request.Referer = pair.Value; break;
					case "user-agent": request.UserAgent = pair.Value; break;
					case "content-type": request.ContentType = pair.Value; break;
					case "content-length": request.ContentLength = pair.Value.ToInt64(0); break;
					default: request.Headers[pair.Key] = pair.Value; break;
				}
			}
		}

		protected virtual void ApplyHeaders(WebClient request) {
			foreach( KeyValuePair<string, string> pair in Headers ) {
				request.Headers[pair.Key] = pair.Value;
			}
		}
		
		public virtual HttpWebResponse CreateRequest(string url) {
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(new Uri(url, UriKind.Absolute));
			request.KeepAlive = true;
			request.Method = "GET";
			ApplyHeaders(request);
			return (HttpWebResponse)request.GetResponse();
		}
		
		public virtual Encoding ParseEncoding(string charset) {
			switch(charset) {
				// case "ISO-8859-1": return Encoding.ASCII;
				case "UTF-8": return Encoding.UTF8;
			}
			return Encoding.ASCII; // Encoding.UTF8;
		}

		public virtual SystemBitmap GetAsBitmap(string url) {
			HttpWebResponse response = CreateRequest(url);
			SystemBitmap bitmap = null;
			using(Stream stream = response.GetResponseStream()) {
				bitmap = new SystemBitmap(stream);
				stream.Close();
			}
			response.Close();
			return bitmap;
		}
		
		public virtual string DownloadAsString(string url) {
			HttpWebResponse response = CreateRequest(url);
			StreamReader reader = new StreamReader(response.GetResponseStream(), ParseEncoding(response.CharacterSet));
			string responseText = reader.ReadToEnd();
			reader.Close();
			response.Close();
			return responseText;
			
			/*
			// this ignores the charset!
			using( WebClient client = new WebClient() ) {
				ApplyHeaders(client);
				return client.DownloadString(url);
			}
			*/
		}
		
		public virtual void DownloadToFile(string url, string targetPath) {
			using( WebClient client = new WebClient() ) {
				ApplyHeaders(client);
				client.DownloadFile(url, targetPath);
			}
		}
	}
}
