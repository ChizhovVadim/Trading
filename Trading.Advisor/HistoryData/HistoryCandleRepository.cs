using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Trading.Advisor
{
    public class HistoryCandleRepository
	{
		const char Separator = ',';
		const string DateFormat = "yyyyMMdd";
		const string TimeFormat = "HHmmss";

		string workingFolder;

		public HistoryCandleRepository(string workingFolder)
		{
			this.workingFolder = workingFolder;
			if (!Directory.Exists(workingFolder))
				Directory.CreateDirectory(workingFolder);
		}

		public IEnumerable<HistoryCandle> ReadCandles(string securityCode)
		{
			string filePath = GetFilePath(securityCode);
			if (!File.Exists (filePath))
				return Enumerable.Empty<HistoryCandle> ();
			return VerifyCandles (
				CsvHelper.Load(filePath, Separator)
				.Select (ParseCandle)
			);
		}

		public void Save(string securityCode, IEnumerable<HistoryCandle> candles)
		{
			string filePath = GetFilePath(securityCode);
			CsvHelper.Save(filePath,
			                       new string[] { "<TICKER>", "<PER>", "<DATE>", "<TIME>", "<OPEN>", "<HIGH>", "<LOW>", "<CLOSE>", "<VOL>" },
			VerifyCandles(candles).Select(c => new string[]
			                              {
				securityCode,
				"5",
				c.DateTime.ToString(DateFormat),
				c.DateTime.ToString(TimeFormat),
				c.OpenPrice.ToString(CultureInfo.InvariantCulture),
				c.HighPrice.ToString(CultureInfo.InvariantCulture),
				c.LowPrice.ToString(CultureInfo.InvariantCulture),
				c.ClosePrice.ToString(CultureInfo.InvariantCulture), 
				c.Volume.ToString(CultureInfo.InvariantCulture)
			}),
			Separator.ToString());
		}

		string GetFilePath(string securityCode)
		{
			return Path.Combine(workingFolder, securityCode + ".txt");
		}

		HistoryCandle ParseCandle(string[] line)
		{
			DateTime date = DateTime.ParseExact(line[2], DateFormat, CultureInfo.InvariantCulture);
			TimeSpan time = DateTime.ParseExact(line[3], TimeFormat, CultureInfo.InvariantCulture).TimeOfDay;
            double o = Double.Parse(line[4], CultureInfo.InvariantCulture);
            double h = Double.Parse(line[5], CultureInfo.InvariantCulture);
            double l = Double.Parse(line[6], CultureInfo.InvariantCulture);
            double c = Double.Parse(line[7], CultureInfo.InvariantCulture);
            double v = Double.Parse(line[8], CultureInfo.InvariantCulture);
			return new HistoryCandle () {
				DateTime = date.Add(time), 
				OpenPrice = o,
				HighPrice = h,
				LowPrice = l,
				ClosePrice = c,
				Volume = v
			};
		}

		IEnumerable<HistoryCandle> VerifyCandles(IEnumerable<HistoryCandle> candles)
		{
			HistoryCandle lastCandle = null;
			foreach (var candle in candles)
			{
				if (lastCandle == null || lastCandle.DateTime < candle.DateTime)
				{
					yield return candle;
					lastCandle = candle;
				}
			}
		}
	}
}
