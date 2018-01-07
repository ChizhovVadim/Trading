using System;
using System.Collections.Generic;
using System.Linq;
using Trading.Core;

namespace Trading.Advisor
{
	class HprStatistcs
	{
		public double MonthHpr;
		public double StDev;
		public double AVaR;
		public List<DateSum> DayHprs;
		public List<DateSum> MonthHprs;
		public List<DateSum> YearGeomHprs;
		public List<DateSum> YearArifHprs;
		public DrawdownInfo DrawdownInfo;
	}

	sealed class DateSum
	{
		public DateTime Date { get; }
		public double Sum { get; }

		public DateSum(DateTime date, double sum)
		{
			Date = date;
			Sum = sum;
		}

		public override string ToString()
		{
			return String.Format("{0:d} {1:p1}", Date, Sum - 1);
		}
	}

	class DrawdownInfo
	{
		public DateTime HighEquityDate;
		public int LongestDrawdown;
		public int CurrentDrawdownDays;
		public double MaxDrawdown;
		public double CurrentDrawdown;
	}

    static class HistoryTestHelper
    {
		public static DrawdownInfo ComputeDrawdownInfo(List<DateSum> hprs)
		{
			double currentSum = 0;
			double maxSum = 0;
			int longestDrawdown = 0;
			int currentDrawdownDays = 0;
			double maxDrawdown = 0;
			DateTime highEquityDate = hprs [0].Date;

			foreach (var hpr in hprs) {
				currentSum += Math.Log(hpr.Sum);
				if (currentSum > maxSum) {
					maxSum = currentSum;
					highEquityDate = hpr.Date;
				}
				maxDrawdown = Math.Min (maxDrawdown, currentSum - maxSum);
				currentDrawdownDays = (int)(hpr.Date - highEquityDate).TotalDays;
				longestDrawdown = Math.Max (longestDrawdown, currentDrawdownDays);
			}

			return new DrawdownInfo {
				HighEquityDate = highEquityDate,
				LongestDrawdown = longestDrawdown,
				CurrentDrawdownDays = currentDrawdownDays,
				MaxDrawdown = Math.Exp(maxDrawdown),
				CurrentDrawdown = Math.Exp(currentSum-maxSum),
			};
		}

		public static IEnumerable<Candle> ToCandles(this IEnumerable<HistoryCandle> source, string securityCode)
        {
			return source.Select(x => new Candle(){
				SecurityCode = securityCode,
				DateTime = x.DateTime,
				ClosePrice = x.ClosePrice,
				Volume = x.Volume,
			});
        }

        public static Func<List<DateSum>, bool> LimitStDev(double stDev)
        {
            return items => StDev(items) <= stDev;
        }

        public static Func<List<DateSum>, bool> LimitCVaR(double cVaR)
        {
            return items => CVaR(items) >= cVaR;
        }

        public static double StDev(this List<DateSum> hprs)
        {
            return hprs
                .Select(x => Math.Log(x.Sum))
                .StDev();
        }

        public static double CVaR(this List<DateSum> hprs)
        {
            if ((hprs.Count-1) < 20) {
                return double.NaN;
            }
            return hprs
                .Select(x => x.Sum)
                .OrderBy(x => x)
                .Take((int)((hprs.Count - 1) * 0.05))
                .Average();
        }

        public static List<DateSum> WithLever(this List<DateSum> hprs, double lever)
        {
            return hprs
                .Select(x => new DateSum(x.Date, 1 + lever * (x.Sum - 1)))
                .ToList();
        }

        public static double TotalHpr(this IEnumerable<DateSum> hprs)
        {
            return hprs.Aggregate(1.0, (acc, item) => acc * item.Sum);
        }

        public static List<DateSum> ByPeriod(this IEnumerable<DateSum> hprs, Func<DateTime, DateTime> periodSelector)
        {
            return hprs
                .GroupBy(x => periodSelector(x.Date))
                .Select(gr => new DateSum(gr.Key, TotalHpr(gr)))
                .OrderBy(x => x.Date)
                .ToList();
        }

        public static double OptimalLever(this List<DateSum> hprs, Func<List<DateSum>, bool> riskSpecification)
        {
            double maxLever = 1.0 / (1.0 - hprs.Min(x => x.Sum));

            double bestHpr = 1;
            double bestLever = 0;

            const double step = 0.001;
            for (double ratio = step; ratio <= 1; ratio += step)
            {
                double lever = maxLever * ratio;
                var leverHprs = hprs.WithLever(lever);
                if (riskSpecification != null
                    && !riskSpecification(leverHprs))
                    break;
                double hpr = leverHprs.TotalHpr();
                if (hpr < bestHpr)
                    break;
                bestHpr = hpr;
                bestLever = lever;
            }

            return bestLever;
        }

        public static IEnumerable<T> Verify<T>(this IEnumerable<T> source, Func<T, DateTime> dateSelector)
        {
            var last = DateTime.MinValue;
            foreach (var item in source)
            {
                var date = dateSelector(item);
                if (last < date)
                {
                    yield return item;
                    last = date;
                }
            }
        }

        public static IEnumerable<Advice> RemoveHolidays(this IEnumerable<Advice> advices, Func<DateTime, DateTime, bool> holidayFunc)
        {
            Advice lastAdvice = null;
            foreach (var advice in advices)
            {
                if (lastAdvice != null)
                {
                    bool holiday = holidayFunc(lastAdvice.DateTime, advice.DateTime);
                    if (holiday)
                    {
						yield return lastAdvice.WithPosition(0);
                    }
                    else
                    {
                        yield return lastAdvice;
                    }
                }
                lastAdvice = advice;
            }
            if (lastAdvice != null)
            {
                yield return lastAdvice;
            }
        }

        public static IEnumerable<DateSum> ToHprs(this IEnumerable<Advice> advices, double slippage)
        {
            return advices
                .Pairwise((l, r) => new DateSum(r.DateTime, (r.Price / l.Price - 1) * l.Position - slippage * Math.Abs(r.Position - l.Position) + 1))
                .Split((l, r) => DateHelper.IsNewFortsDateStarted(l.Date, r.Date))
                .Select(x => new DateSum(x[x.Count - 1].Date.Date, x.TotalHpr()));
        }

        public static IEnumerable<string> EnumerateSecurities(string code)
        {
            int startYear = 2009;
            int finishYear = DateTime.Today.AddMonths(1).Year;
            for (int year = startYear; year <= finishYear; year++)
            {
                for (int month = 3; month <= 12; month += 3)
                {
                    DateTime d = new DateTime(year, month, 1);
                    yield return $"{code}-{d:M.yy}";
                }
            }
        }

		public static List<DateSum> CombineAdvisorHprs(List<List<DateSum>> hprs, List<double> weights)
        {
			var start = hprs.Select (x => x [0].Date).Max ();
			var finish = hprs.Select (x => x [x.Count - 1].Date).Min();
			var maps = hprs.Select(x => x.ToDictionary(y => y.Date));
            var result = new List<DateSum>();
            for (DateTime d = start; d <= finish; d = d.AddDays(1))
            {
				var dayHoprs = maps.Select (x => x.ContainsKey (d) ? x [d] : null).ToList ();
				if (dayHoprs.All (x => x == null)) {
					continue;
				}
				var total = dayHoprs.Zip (weights, (h, w) => h == null ? 0 : (h.Sum - 1) * w).Sum ();
				result.Add(new DateSum(d, total + 1));
            }
            return result;
        }

		public static HprStatistcs ComputeHprStatistcs(List<DateSum> hprs)
		{
			var report = new HprStatistcs ();
			report.MonthHpr = Math.Pow(hprs.TotalHpr(), 22.0 / hprs.Count);
			report.StDev = hprs.StDev();
			report.AVaR = hprs.CVaR();
			report.DayHprs = hprs;
			report.MonthHprs = hprs.ByPeriod(DateHelper.LastDayOfMonth);
			report.YearGeomHprs = hprs.ByPeriod(DateHelper.LastDayOfYear);
			report.YearArifHprs = report.MonthHprs
				.GroupBy (x => DateHelper.LastDayOfYear (x.Date))
				.OrderBy(gr => gr.Key)
				//.Select (gr => new DateSum(gr.Key, 1+gr.Sum(x => x.Sum-1)))
				//.Select(gr => new DateSum(gr.Key, gr.OrderBy(x => x.Date).Aggregate(1.0, (acc, item) => Math.Min(1.0, acc * item.Sum))))
				.Select(gr => new DateSum(gr.Key, ComputeHprWithOut(gr)))
				.ToList();
			report.DrawdownInfo = HistoryTestHelper.ComputeDrawdownInfo (report.DayHprs);
			return report;
		}

		static double ComputeHprWithOut(IEnumerable<DateSum> source)
		{
			double sum = 0;
			double hpr = 1;
			foreach (var item in source.OrderBy(x => x.Date)) {
				hpr *= item.Sum;
				if (hpr > 1) {
					sum += hpr-1;
					hpr = 1;
				}
			}
			sum += hpr - 1;
			return sum + 1;
		}
    }
}