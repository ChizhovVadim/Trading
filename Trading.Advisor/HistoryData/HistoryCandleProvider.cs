using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Trading.Advisor
{
    public class HistoryCandleProviderSettings
	{
		public List<SecurityCode> SecurityCodes { get; set; }

		public string MfdUrlTemplate { get; set; }

		public int RetryCount { get; set; }

		public TimeSpan Timeout { get; set; }

		public HistoryCandleProviderSettings ()
		{
			this.MfdUrlTemplate = "http://mfd.ru/export/handler.ashx/data.txt?TickerGroup=26&Tickers={0}&Alias=false&Period=2&timeframeValue=1&timeframeDatePart=day&StartDate={1:dd.MM.yyyy}&EndDate={2:dd.MM.yyyy}&SaveFormat=0&SaveMode=0&FileName=data.txt&FieldSeparator=%2C&DecimalSeparator=.&DateFormat=yyyyMMdd&TimeFormat=HHmmss&DateFormatCustom=&TimeFormatCustom=&AddHeader=true&RecordFormat=0&Fill=false";
			this.RetryCount = 3;
			this.Timeout = TimeSpan.FromSeconds (25);
		}
	}

	public class SecurityCode
	{
		[XmlAttribute]
		public string Code { get; set; }

		[XmlAttribute]
		public int FinamCode { get; set; }

		[XmlAttribute]
		public int MfdCode { get; set; }
	}

	public class HistoryCandleProvider
	{
		static Serilog.ILogger logger = Serilog.Log.Logger.ForContext<HistoryCandleProvider> ();

        const int FinamPeriodMinutes1 = 2;
		const int FinamPeriodMinutes5 = 3;
		const int FinamPeriodDay = 8;

		HistoryCandleProviderSettings settings;
		HttpClient client;

		public HistoryCandleProvider(HistoryCandleProviderSettings settings)
		{
			this.settings = settings;

            //Proxy false?
            this.client = new HttpClient()
            {
                Timeout = settings.Timeout
            };            
		}

		public Task<HistoryCandle[]> LoadCandles(string securityCode, DateTime beginDate, DateTime endDate)
		{
            if (securityCode == null)
                throw new ArgumentNullException(nameof(securityCode));
            if (beginDate > endDate)
                throw new ArgumentOutOfRangeException(nameof(beginDate));
            SecurityCode security = FindSecurityCode(securityCode);
			return LoadCandles(security, beginDate, endDate);
		}

        public void EnsureSupportSecurity(string securityCode)
        {
            FindSecurityCode(securityCode);
        }

        SecurityCode FindSecurityCode(string securityCode)
        {
            return settings.SecurityCodes.Single(x => x.Code == securityCode);
        }

		Task<HistoryCandle[]> LoadCandles(SecurityCode security, DateTime beginDate, DateTime endDate)
		{
			var candleProviders = new List<Func<Task<HistoryCandle[]>>> ();
			//TODO хорошо бы задавать приоритеты поставщиков в настройках
			if (security.MfdCode != 0)
			{
				candleProviders.Add(() => LoadMfdCandles(security.MfdCode, beginDate, endDate));
			}
			if (security.FinamCode != 0)
			{
				candleProviders.Add(() => LoadFinamCandles(security.FinamCode, beginDate, endDate));
			}
			if (candleProviders.Count == 0)
				throw new ArgumentOutOfRangeException("security");

			return LoadCandles(candleProviders);
		}

		public void Test(string securityCode)
		{
			DateTime endDate = DateTime.Today;
			DateTime beginDate = endDate.AddDays(-7);
			SecurityCode security = settings.SecurityCodes.Single(x => x.Code == securityCode);
			var c1 = LoadMfdCandles (security.MfdCode, beginDate, endDate).Result;
			Console.WriteLine ("Mfd: {0} - {1}.", c1[0], c1[c1.Length - 1]);
			var c2 = LoadFinamCandles (security.FinamCode, beginDate, endDate).Result;
			Console.WriteLine ("Finam: {0} - {1}.", c2[0], c2[c2.Length - 1]);
		}

		async Task<HistoryCandle[]> LoadMfdCandles(int mfdCode, DateTime beginDate, DateTime endDate)
		{
			var url = MfdUrl(beginDate, endDate, mfdCode);
			logger.Debug ("Request: {Url}", url);
			string s = await client.GetStringAsync (url).ConfigureAwait (false);
			HistoryCandle[] result = ParseCandles (s);
			return result;
		}

		async Task<HistoryCandle[]> LoadFinamCandles(int finamCode, DateTime beginDate, DateTime endDate)
		{
			var url = FinamUrl (beginDate, endDate, finamCode, FinamPeriodMinutes5);
			logger.Debug ("Request: {Url}", url);
			string s = await client.GetStringAsync (url).ConfigureAwait (false);
			HistoryCandle[] result = ParseCandles (s);
			return result;
		}

		async Task<HistoryCandle[]> LoadCandles(List<Func<Task<HistoryCandle[]>>> candleProviders)
		{
			int millisecondsDelay = 20000;

			for (int currentRetry = 1;; currentRetry++) {
				for (int i = 0; i < candleProviders.Count; i++) {
					try {
						HistoryCandle[] result = await candleProviders[i]().ConfigureAwait (false);
						if (result == null || result.Length == 0)
							throw new InvalidOperationException ("Отсутствуют исторические данные");
						return result;
					} catch (Exception e) {
						logger.Warning (e, "Неудачная попытка загрузки исторических данных");
						if (currentRetry >= settings.RetryCount
						    && i == candleProviders.Count - 1)
							throw;
					}
				}
				await Task.Delay (millisecondsDelay).ConfigureAwait (false);
				millisecondsDelay *= 2;
			}
		}

		Uri FinamUrl(DateTime beginDate, DateTime endDate, int finamSecurityCode, int finamPeriodCode)
		{
			string address = String.Format(CultureInfo.InvariantCulture,
                "http://export.finam.ru/{0}?d=d&market=14&em={8}&df={2}&mf={3}&yf={4}&dt={5}&mt={6}&yt={7}&p={9}&f={0}&e=.txt&cn={1}&dtf=1&tmf=1&MSOR=0&sep=1&sep2=1&datf=1&at=1",
				"data.txt", "data", beginDate.Day, beginDate.Month - 1, beginDate.Year, endDate.Day, endDate.Month - 1, endDate.Year,
				finamSecurityCode, finamPeriodCode);
			return new Uri(address);
		}

		Uri MfdUrl(DateTime beginDate, DateTime endDate, int mfdTicker)
		{
			string address = String.Format(CultureInfo.InvariantCulture, settings.MfdUrlTemplate, mfdTicker, beginDate, endDate);
			return new Uri(address);
		}

		HistoryCandle[] ParseCandles(string value)
		{
			return
				CsvHelper.Parse(value, ',')
				.Select(ParseCandle)
				.ToArray();
		}

		HistoryCandle ParseCandle(string[] line)
		{
			DateTime date = DateTime.ParseExact(line[2], "yyyyMMdd", CultureInfo.InvariantCulture);
			TimeSpan time = DateTime.ParseExact(line[3], "HHmmss", CultureInfo.InvariantCulture).TimeOfDay;
			return new HistoryCandle () {
				DateTime = date.Add(time),
				OpenPrice = Double.Parse(line[4], CultureInfo.InvariantCulture),
				HighPrice = Double.Parse(line[5], CultureInfo.InvariantCulture),
				LowPrice = Double.Parse(line[6], CultureInfo.InvariantCulture),
				ClosePrice = Double.Parse(line[7], CultureInfo.InvariantCulture),
				Volume = Double.Parse(line[8], CultureInfo.InvariantCulture)
			};
		}
	}
}
