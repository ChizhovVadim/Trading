using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Trading.Core;

namespace Trading.Advisor
{
    public class StrategyConfig
	{
		[XmlAttribute]
		public string Name { get; set; }

		[XmlAttribute]
		public string SecurityCode { get; set; }

		[XmlAttribute]
		public double Lever { get; set; }

		[XmlAttribute]
		public double Weight { get; set; }

		[XmlAttribute]
		public double StdVolatility { get; set; }

		[XmlAttribute]
		public int Direction { get; set; }

		public StrategyConfig ()
		{
			this.Lever = 1;
			this.Weight = 1;
			this.StdVolatility = 0.006;
		}
	}

	public static class AdviceExtensions
	{
		public static Advice WithPosition(this Advice advice, double position)
		{
			return new Advice () {
				SecurityCode = advice.SecurityCode,
				DateTime = advice.DateTime,
				Price = advice.Price,
				Position = position
			};
		}
	}

	public class AdvisorProvider
	{
		Dictionary<string, Func<StrategyConfig, Func<Candle, Advice>>> advisors;

		public AdvisorProvider ()
		{
			this.advisors = new Dictionary<string, Func<StrategyConfig, Func<Candle, Advice>>> (StringComparer.OrdinalIgnoreCase) {
				["Dual" ] = Dual,
			};
		}

		public Func<Candle, Advice> GetAdvisor (StrategyConfig config)
		{
			Func<StrategyConfig, Func<Candle, Advice>> advisorFactory = null;
			if (!advisors.TryGetValue (config.Name, out advisorFactory)) {
				throw new ArgumentException ($"Not found advisor '{config.Name}'.");
			}
			Func<Candle, Advice> result = advisorFactory (config);

			result = ApplyTrendControl (result);
			result = ApplyVolatilityControl (result, config);

			if (config.Direction != 0)
			{
				result = ApplyDirection(result, config.Direction);
			}
			result = ApplySlow (result);
			if (config.Lever != 1 || config.Weight != 1) {
				result = ApplyLever (result, config.Lever * config.Weight);
			}
			return result;
		}

		static Func<Candle, Advice> ApplyLever (Func<Candle, Advice> advisor, double lever)
		{
			return candle => {
				var advice = advisor (candle);
				if (advice == null) {
					return null;
				}
				return advice.WithPosition(advice.Position * lever);
			};
		}

		static Func<Candle, Advice> ApplyDirection(Func<Candle, Advice> advisor, int direction)
		{
			return candle =>
			{
				var advice = advisor(candle);
				if (advice == null) {
					return null;
				}
				var position = advice.Position;
				if (direction == 1)
				{
					position = Math.Max(0, position);
				}
				else if (direction == -1)
				{
					position = Math.Min(0, position);
				}
				return advice.WithPosition(position);
			};
		}

		static Func<Candle, Advice> ApplySlow(Func<Candle, Advice> advisor)
		{
			double maxStep = 2.0 / 4;
			var ratio = 0.0;
			return candle => {
				var advice = advisor(candle);
				if (advice == null) {
					return null;
				}
				ratio = Math.Max(ratio - maxStep, Math.Min(ratio + maxStep, advice.Position));
				return advice.WithPosition(ratio);
			};
		}

		static Func<Candle, Advice> ApplyVolatilityControl(Func<Candle, Advice> advisor, StrategyConfig config)
		{
			var vc = new VolatilityComputer (config.StdVolatility);
			var rebalanceRequiredIndicator = new TimeRebalanceIndicator ();
			double volRatio = 1.0;
			Candle lastCandle = null;
			return candle => {
				var advice = advisor(candle);
				if (advice == null) {
					return null;
				}

				vc.Add (candle);
				rebalanceRequiredIndicator.Add(candle);

				if ((lastCandle != null && DateHelper.IsNewDayStarted(lastCandle.DateTime, candle.DateTime)) ||
					rebalanceRequiredIndicator.GetValue()) {
					volRatio = vc.GetValue();
				}

				lastCandle = candle;
				return advice.WithPosition(advice.Position * volRatio);
			};
		}

		static Func<Candle, Advice> ApplyTrendControl(Func<Candle, Advice> advisor)
		{
			double trendRatio = 1.0;
			Candle lastCandle = null;
			var candles = new List<Candle> ();
			var rebalanceRequiredIndicator = new TimeRebalanceIndicator ();
			return candle => {
				var advice = advisor(candle);
				if (advice == null) {
					return null;
				}

				rebalanceRequiredIndicator.Add(candle);

				if ((lastCandle != null && DateHelper.IsNewDayStarted(lastCandle.DateTime, candle.DateTime)) ||
					rebalanceRequiredIndicator.GetValue()) {
					candles.Add(candle);
					if (candles.Count > 20*3) {
						candles.RemoveAt(0);
					}
					var range = candles.Select (x => x.ClosePrice);
					var H = range.Max ();
					var L = range.Min ();
					trendRatio = 0.34+Mathematics.InterpolateLinear(Math.Log(H / L), 0.025, 0.05, 0, 0.66);
				}

				lastCandle = candle;
				return advice.WithPosition(advice.Position * trendRatio);
			};
		}

		static Func<Candle, Advice> Dual(StrategyConfig config)
		{
			var advisors = new[]
			{
				MonthTurtle(config),
			};

			return candle =>
			{
				var advices = advisors.Select(x => x(candle)).ToList();
				if (advices.Any(x => x == null))
					return null;                
				return new Advice() {
					DateTime = candle.DateTime,
					Price = candle.ClosePrice,
					Position = advices.Average(x => x.Position)
				};
			};
		}

		static Func<Candle, Advice> MonthTurtle(StrategyConfig config)
		{
			Candle lastCandle = null;
			double ratio = 0;

			var rebalanceRequiredIndicator = new TimeRebalanceIndicator ();
			var candles = new List<Candle> ();

			return candle => {
				if (lastCandle == null) {
					lastCandle = candle;
					return null;
				}

				if (candle.DateTime <= lastCandle.DateTime) {
					return null;
				}

				if (!DateHelper.IsMainFortsSession (candle.DateTime)) {
					lastCandle = candle;
					return null;
				}

				if (DateHelper.IsNewDayStarted(lastCandle.DateTime, candle.DateTime)) {
					candles.Add (candle);
					if (candles.Count > 20) {
						candles.RemoveAt(0);
					}
				}

				rebalanceRequiredIndicator.Add(candle);

				if (rebalanceRequiredIndicator.GetValue() && candles.Count > 0) {
					var range = candles.Select (x => x.ClosePrice);
					var H = range.Max ();
					var L = range.Min ();
					var C = L + 0.5 * (H-L);

					if (candle.ClosePrice >= H) {
						ratio = 1;
					} else if (candle.ClosePrice <= L) {
						ratio = -1;
					} else if (candle.ClosePrice > C) {
						ratio = Math.Max(0, ratio);
					}  else if (candle.ClosePrice < C) {
						ratio = Math.Min(0, ratio);
					}
				}

				lastCandle = candle;
				return new Advice() {
					SecurityCode = candle.SecurityCode,
					DateTime = candle.DateTime,
					Price = candle.ClosePrice,
					Position = ratio
				};
			};
		}
	}
}