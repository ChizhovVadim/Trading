using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
namespace LuaTrader
{
    class RestAdvisorService : IAdvisorService
    {
        static Serilog.ILogger logger = Serilog.Log.ForContext<RestAdvisorService>();
        AdvisorClient advisorClient;

		public RestAdvisorService(string url)
        {
			this.advisorClient = new AdvisorClient(url);
        }

		public void PublishCandles(BlockingCollection<Candle> candles, CancellationToken token)
		{
			var candlesToSend = new List<Candle>();
			foreach (var candle in candles.GetConsumingEnumerable(token))
			{
				candlesToSend.Add(candle);
				if (candle.DateTime > DateTime.Now.AddMinutes(-9))
				{
					try
					{
						advisorClient.PostCandles(candlesToSend).GetAwaiter().GetResult();
						candlesToSend.Clear();
					}
					catch (Exception e)
					{
						logger.Warning(e, "PublishCandles error");
					}
				}
			}
		}

		public void GetAdvices (string security, BlockingCollection<Advice> outAdvices, CancellationToken token)
		{
			const int Timeout = 90;
			var since = DateTime.MinValue;
			while (!token.IsCancellationRequested) {
				try {
					var advice = advisorClient.GetAdvice (security, since, Timeout).GetAwaiter ().GetResult ();
					if (advice != null) {
						if (advice.DateTime > since) {
							outAdvices.Add (advice);
							since = advice.DateTime;
						}
					}
				} catch (Exception e) {
					logger.Error (e, "GetAdvices error");
					Task.Delay (TimeSpan.FromMinutes (3), token).GetAwaiter ().GetResult ();
				}
			}
		}

        public List<string> GetSecurities()
        {
            return advisorClient.GetAdvisors().GetAwaiter().GetResult().ToList();
        }
    }

    class AdvisorClient
    {
        const string DateTimeFormat = "yyyy-MM-dd'T'HH:mm:ss";
        HttpClient client;

        public AdvisorClient(string url)
        {
            this.client = new HttpClient()
            {
                BaseAddress = new Uri(url)
            };
        }

        public async Task PostCandles(List<Candle> candles)
        {
            var ser = new DataContractJsonSerializer(typeof(List<Candle>),
                          new DataContractJsonSerializerSettings
                          {
                              DateTimeFormat = new DateTimeFormat(DateTimeFormat)
                          });
            string s = null;
            using (MemoryStream ms = new MemoryStream())
            {
                ser.WriteObject(ms, candles);
                s = Encoding.Default.GetString(ms.ToArray());
            }
            var content = new StringContent(s, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/candles", content);
            response.EnsureSuccessStatusCode();
        }

        public async Task<Advice> GetAdvice(string securityCode, DateTime since, int timeout)
        {
            var ser = new DataContractJsonSerializer(typeof(Advice),
                          new DataContractJsonSerializerSettings
                          {
                              DateTimeFormat = new DateTimeFormat(DateTimeFormat)
                          });
            string uri = string.Format("/api/advisors/{0}?since={1}&timeout={2}",
                             HttpUtility.UrlEncode(securityCode),
                             HttpUtility.UrlEncode(since.ToString(DateTimeFormat)),
                             HttpUtility.UrlEncode(timeout.ToString())
                         );
            HttpResponseMessage response = await client.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();
            var r = (Advice)ser.ReadObject(stream);
            return r;
        }

        public async Task<string[]> GetAdvisors()
        {
            var ser = new DataContractJsonSerializer(typeof(string[]),
                          new DataContractJsonSerializerSettings
                          {
                              DateTimeFormat = new DateTimeFormat(DateTimeFormat)
                          });
            string uri = "/api/advisors";
            HttpResponseMessage response = await client.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();
            var r = (string[])ser.ReadObject(stream);
            return r;
        }
    }
}
