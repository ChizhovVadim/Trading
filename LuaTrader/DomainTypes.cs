using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace LuaTrader
{
	[DataContract]
	public class Candle
	{
		[DataMember]
		public string SecurityCode{ get; set; }

		[DataMember]
		public DateTime DateTime{ get; set; }

		[DataMember]
		public double ClosePrice{ get; set; }

		[DataMember]
		public double Volume{ get; set; }
	}

	public interface ICandleService
	{
		BlockingCollection<Candle> GetCandles (string security, CancellationToken token);
	}

	[DataContract]
	public class Advice
	{
		[DataMember]
		public string SecurityCode{ get; set; }

		[DataMember]
		public DateTime DateTime{ get; set; }

		[DataMember]
		public double Price{ get; set; }

		[DataMember]
		public double Position{ get; set; }
	}

	public interface IAdvisorService
	{
		List<string> GetSecurities ();

		BlockingCollection<Advice> GetAdvices (string security, CancellationToken token);

		void PublishCandles (BlockingCollection<Candle> candles, CancellationToken token);
	}

	public interface ITraderService
	{
		void Terminal ();

		double GetAmount (string portfolio);

		int GetPosition (string portfolio, string security);

		void RegisterOrder (string portfolio, string security, int volume, double price, CancellationToken token);
	}
}
