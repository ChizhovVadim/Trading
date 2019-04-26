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

		//TODO void GetAdvices (string security, BlockingCollection<Advice> outAdvices, CancellationToken token);
		BlockingCollection<Advice> GetAdvices (string security, CancellationToken token);

		void PublishCandles (BlockingCollection<Candle> candles, CancellationToken token);
	}
}
