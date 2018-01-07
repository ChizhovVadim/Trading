using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Trading.Advisor
{
    public class HistoryDataService
    {
		static Serilog.ILogger logger = Serilog.Log.Logger.ForContext<HistoryDataService> ();

        HistoryCandleRepository historyCandleRepository;
        HistoryCandleProvider historyCandleProvider;

		public HistoryDataService(HistoryCandleRepository historyCandleRepository,
			HistoryCandleProvider historyCandleProvider)
        {
			this.historyCandleRepository = historyCandleRepository;
			this.historyCandleProvider = historyCandleProvider;
        }

		public IEnumerable<HistoryCandle> ReadCandles(string securityCode)
		{
			return historyCandleRepository.ReadCandles (securityCode);
		}

		public void UpdateCandlesSimple(string security)
		{
            logger.Information("UpdateCandlesSimple started...");
			var totalCandles = new List<HistoryCandle>();
			var minDate = new DateTime(2013, 1, 1);
			var maxDate = DateTime.Today;

			DateTime beginDate = minDate;
			while (true)
			{
				DateTime endDate = beginDate.AddMonths(3);
				if (endDate > maxDate)
					endDate = maxDate;

				IEnumerable<HistoryCandle> candles;
				try
				{
					candles = historyCandleProvider.LoadCandles(security, beginDate, endDate).GetAwaiter().GetResult();
				}
				catch (Exception e)
				{
					Console.WriteLine(e);
					break;
				}

				totalCandles.AddRange(candles);

				if (endDate >= maxDate)
				{
					break;
				}

				beginDate = endDate.AddDays(1);
				Task.Delay(5000);
			}

			historyCandleRepository.Save(security, totalCandles);
            logger.Information("UpdateCandlesSimple finished.");
		}

        public void UpdateCandles (string securityCode, bool isGlued = false)
        {
            if (isGlued)
            {
                UpdateGluedContract(securityCode).GetAwaiter().GetResult();
            }
            else
            {
                //TODO удалить?
                UpdateSimpleContract(securityCode);
            }
        }

        void UpdateSimpleContract(string securityCode)
        {
            //TODO Поддержать обновление серии инструментов

            historyCandleProvider.EnsureSupportSecurity(securityCode);

            DateTime baseDate = BaseExpirationDate(securityCode);
			DateTime beginDate = DateHelper.FirstDayOfMonth(baseDate.AddMonths(-3));
            //DateTime endDate = baseDate.AddDays(-1);
			DateTime endDate = DateTime.Today;

            //не отслеживаем новые контракты
            if (DateTime.Today < beginDate)
                return;

			List<HistoryCandle> candles = historyCandleRepository.ReadCandles(securityCode).ToList();

            //Если в старом контракте есть данные, то не перекачиваем его
			if (baseDate < DateTime.Today && candles != null && candles.Count > 0) {
				Console.WriteLine ("Не докачиваем старый контракт");
				return;
			}

            if (candles.Count > 0)
                beginDate = candles[candles.Count - 1].DateTime.Date;
            if (DateTime.Today < endDate)
                endDate = DateTime.Today;

			logger.Information("Downloading {SecurityCode} {BeginDate:d} {EndDate:d}...",
				securityCode, beginDate, endDate);
            var newCandles = historyCandleProvider.LoadCandles(securityCode, beginDate, endDate).GetAwaiter().GetResult();
            if (newCandles != null && newCandles.Length > 0)
            {
				logger.Information("Downloaded {First} - {Last}.", newCandles[0], newCandles[newCandles.Length - 1]);

                //Последний бар за сегодня может быть еще не завершен
                bool skipLast = newCandles[newCandles.Length - 1].DateTime.Date == DateTime.Today;

                historyCandleRepository.Save(securityCode,
                    candles.Concat(skipLast ? newCandles.Take(newCandles.Length - 1) : newCandles));
            }
        }

        DateTime BaseExpirationDate(string securityCode)
        {
			//С 1 июля 2015, для новых серий по кот нет открытых позиций, все основные фьючерсы и опционы должны исполняться в 3-й четверг месяца
            int index = 1 + securityCode.IndexOf('-');
            DateTime month = DateTime.ParseExact(securityCode.Substring(index), "M.yy", CultureInfo.InvariantCulture);
			var d = BaseExpirationDate_Old (month.Year, month.Month);
			var d2 = BaseExpirationDate_New (month.Year, month.Month);
			if (d2 > d) {
				d = d2;
			}
			return d;
        }

		static DateTime BaseExpirationDate_Old(int year, int month)
		{
			return new DateTime(year, month, 15);
		}

		//С 1 июля 2015, для новых серий по кот нет открытых позиций, все основные фьючерсы и опционы должны исполняться в 3-й четверг месяца
		static DateTime BaseExpirationDate_New(int year, int month)
		{
			return NextWeekday (new DateTime (year, month, 1), DayOfWeek.Thursday).AddDays(14);
		}

		static DateTime NextWeekday (DateTime start, DayOfWeek day)
		{
			// The (... + 7) % 7 ensures we end up with a value in the range [0, 6]
			int daysToAdd = ((int)day - (int)start.DayOfWeek + 7) % 7;
			return start.AddDays (daysToAdd);
		}

        async Task UpdateGluedContract(string securityCode)
        {
            while (true)
            {
				List<HistoryCandle> candles = historyCandleRepository.ReadCandles(securityCode).ToList();

                DateTime beginDate = candles.Count > 0 ? candles[candles.Count - 1].DateTime.Date : new DateTime(2009, 1, 1);
                DateTime endDate = beginDate.AddYears(1);
                if (endDate > DateTime.Today)
                    endDate = DateTime.Today;

				logger.Information("Downloading {SecurityCode} {BeginDate:d} {EndDate:d}...",
					securityCode, beginDate, endDate);
                var newCandles = await historyCandleProvider.LoadCandles(securityCode, beginDate, endDate).ConfigureAwait(false);
                if (newCandles != null && newCandles.Length > 0)
                {
					logger.Information("Downloaded {First} - {Last}.", newCandles[0], newCandles[newCandles.Length - 1]);

                    //Последний бар за сегодня может быть еще не завершен
                    bool skipLast = newCandles[newCandles.Length - 1].DateTime.Date == DateTime.Today;

                    historyCandleRepository.Save(securityCode,
                        candles.Concat(skipLast ? newCandles.Take(newCandles.Length - 1) : newCandles));
                }

                if (endDate >= DateTime.Today)
                    break;

                await Task.Delay(200).ConfigureAwait(false);
            }
        }
    }
}

