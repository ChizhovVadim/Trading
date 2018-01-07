using System;
namespace Trading.Advisor
{
	public class HistoryCandle
	{
		public DateTime DateTime;

		public double OpenPrice;

		public double HighPrice;

		public double LowPrice;

		public double ClosePrice;

		public double Volume;

		public override string ToString()
		{
			return String.Format("{0:g} {1}", DateTime, ClosePrice);
		}
	}
}
