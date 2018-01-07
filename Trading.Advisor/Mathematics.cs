using System;
using System.Collections.Generic;
using System.Linq;

namespace Trading.Advisor
{
	static class Mathematics
	{
		public static double Percentile(List<double> sortedList, double probability)
		{
			return sortedList[(int)(probability * (double)(sortedList.Count - 1))];
		}

		public static void PrintSummary(this IEnumerable<double> source)
		{
			var list = source.OrderBy(x => x).ToList();
			var low = list[0];
			var high = list[list.Count - 1];
			var moments = Moments(list);
			var median = Percentile(list, 0.5);
			var p5 = Percentile(list, 0.05);
			var p95 = Percentile(list, 0.95);
			var robustStDev = (p95 - p5) / 3.2;
			Console.WriteLine("High: {0} Low: {1} Mean: {2} StDev: {3}", high, low, moments.Item1, moments.Item2);
			Console.WriteLine("P95: {0} P5: {1} Median: {2} RobustStDev: {3}", p95, p5, median, robustStDev);
		}

		public static Tuple<double, double> Moments(this IEnumerable<double> source)
		{
			int n = 0;
			double mean = 0.0;
			double M2 = 0.0;

			foreach (var x in source)
			{
				n++;
				double delta = x - mean;
				mean += delta / n;
				M2 += delta * (x - mean);
			}

			if (n == 0)
				throw new Exception("Sequence contains no elements");

			double stDev = Math.Sqrt(M2 / n);
			return Tuple.Create(mean, stDev);
		}

		public static double StDev(this IEnumerable<double> source)
		{
			return source.Moments().Item2;
		}

		public static double InterpolateLinear(double x, double x_min, double x_max, double y_min, double y_max)
		{
			x = Math.Max(x_min, Math.Min(x_max, x));
			return (y_max - y_min) * (x - x_min) / (x_max - x_min) + y_min;
		}
	}
}
