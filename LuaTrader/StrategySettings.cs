using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace LuaTrader
{
	static class Serializer
	{
		public static T Load<T> (string filePath)
		{
			var serializer = new XmlSerializer (typeof(T));
			using (var fileStream = new FileStream (filePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				var result = (T)serializer.Deserialize (fileStream);
				return result;
			}
		}
	}

	public class StrategySettings
	{
		public List<Client> Clients { get; set; }

		public string AdvisorUrl { get; set; }

		public string StartTime { get; set; }

		public string LogPath { get; set; }
	}

	public class Client
	{
		[XmlAttribute]
		public string Key { get; set; }

		[XmlAttribute]
		public string Path { get; set; }

		[XmlAttribute]
		public string Login { get; set; }

		[XmlAttribute]
		public string Password { get; set; }

		[XmlAttribute]
		public string Firm { get; set; }

		[XmlAttribute]
		public string Portfolio{ get; set; }

		[XmlAttribute]
		public double Amount { get; set; }

		[XmlAttribute]
		public double AmountReduction { get; set; }

		[XmlAttribute]
		public double MaxAmount { get; set; }

		[XmlAttribute]
		public double Weight { get; set; }

		[XmlAttribute]
		public bool PublishCandles { get; set; }
	}
}
