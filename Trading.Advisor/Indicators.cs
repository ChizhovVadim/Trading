using System;
using System.Collections.Generic;
using System.Linq;
using Trading.Core;

namespace Trading.Advisor
{
    interface Indicator<TIn, TOut>
	{
		void Add (TIn item);
		TOut GetValue ();
	}

	class VolatilityComputer : Indicator<Candle, double>
	{
		const int Period = 100;

		double stdVolatility;
		List<double> buffer;
		double k;
		double ratio;
		Candle lastCandle;

		public VolatilityComputer (double stdVolatility)
		{
			this.stdVolatility = stdVolatility;
			this.buffer = new List<double> (2 * Period);
			this.k = Math.Sqrt (Period);
			this.ratio = 1;
		}

		public void Add (Candle candle)
		{
			if (lastCandle != null) {
				if (DateHelper.IsMainFortsSession (candle.DateTime) &&
					!DateHelper.IsNewDayStarted (lastCandle.DateTime, candle.DateTime)) {
					buffer.Add (Math.Log (candle.ClosePrice / lastCandle.ClosePrice));
					if (buffer.Count >= Period * 2) {
						buffer.RemoveRange (0, buffer.Count - Period);
					}
				}
			}
			lastCandle = candle;
		}

		public double GetValue ()
		{
			if (buffer.Count >= Period) {
				double stDev = k * buffer.Skip (buffer.Count - Period).StDev ();
				ratio = Math.Min (1, stdVolatility / stDev);
			}
			return ratio;
		}
	}

	class TimeRebalanceIndicator : Indicator<Candle, bool>
	{
		static TimeSpan[] rebalanceTimes = new []
		{
			new TimeSpan(12, 30, 00),
			new TimeSpan(16, 30, 00)
		};

		Candle lastCandle;
		bool isRebalanceRequired;

		public void Add (Candle candle)
		{
			isRebalanceRequired = lastCandle != null &&
				rebalanceTimes.Any (x => lastCandle.DateTime.TimeOfDay < x && x <= candle.DateTime.TimeOfDay);
			lastCandle = candle;
		}

		public bool GetValue()
		{
			return isRebalanceRequired;
		}
	}
}