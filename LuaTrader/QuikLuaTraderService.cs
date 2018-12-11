using System;
using System.Threading;
using System.Linq;
using System.Collections.Concurrent;
using QuikSharp;
using QuikSharp.DataStructures.Transaction;
using System.Threading.Tasks;
using System.Globalization;

namespace LuaTrader
{
	class QuikLuaTraderService : ITraderService, ICandleService
	{
		const string ClassCode = "SPBFUT";
		const double Slippage = 0.001;

		Serilog.ILogger logger = Serilog.Log.ForContext<QuikLuaTraderService> ();

		Client config;
		Quik quik;

		public QuikLuaTraderService (Client config)
		{
			this.config = config;
			//TODO get port from config
			this.quik = new Quik(Quik.DefaultPort, new InMemoryStorage());
		}

		public void Terminal ()
		{
			var terminal = new QuikTerminal (config);
			terminal.RunTerminal ();
			//TODO wait for quik.Service.IsConnected()
		}

		public double GetAmount (string portfolio)
		{
			if (!quik.Service.IsConnected().GetAwaiter().GetResult())
			{
				throw new Exception("Отсутствует соединение со шлюзом");
			}
			var portfolioInfo = quik.Trading.GetPortfolioInfoEx(config.Firm, portfolio, 0).GetAwaiter().GetResult();
			if (portfolioInfo == null)
			{
				throw new Exception("Портфель не найден");
			}
			return double.Parse (portfolioInfo.StartLimitOpenPos, CultureInfo.InvariantCulture);
		}

		public int GetPosition (string portfolio, string security)
		{
			var position = quik.Trading.GetFuturesHolding(config.Firm, portfolio, security, 0).GetAwaiter().GetResult();
			if (position == null)
			{
				return 0;
			}
			return (int)position.totalNet;
		}

		public void RegisterOrder (string portfolio, string security, int volume, double price, CancellationToken token)
		{
			//TODO security minPrice/maxPrice, priceStep
			if (volume > 0) {
				price = price * (1 + Slippage);
			} else {
				price = price * (1 - Slippage);
			}
			price = Math.Round (price, MidpointRounding.AwayFromZero);

			var order = new Order ();
			order.ClassCode = ClassCode;
			order.SecCode = security;
			order.Operation = volume > 0 ? QuikSharp.DataStructures.Operation.Buy :
				QuikSharp.DataStructures.Operation.Sell;
			order.Price = (decimal)price;
			order.Quantity = Math.Abs (volume);//TODO lot size
			order.Account = portfolio;

			//ownOrders[order] = true;
			logger
				.ForContext ("Price", price)
				.ForContext ("Volume", volume)
				.ForContext ("Portfolio", portfolio)
				.ForContext ("Security", security)
				.Information ("Register order");
			try {
				quik.Orders.CreateOrder (order).GetAwaiter ().GetResult ();
			} catch (Exception ex) {
				logger.Error (ex, "Register order error");
			}
		}

		public BlockingCollection<Candle> GetCandles (string security, CancellationToken token)
		{
			if (!quik.Service.IsConnected().GetAwaiter().GetResult())
			{
				throw new Exception("Отсутствует соединение со шлюзом");
			}

			var result = new BlockingCollection<Candle>();
			var secCode = SecurityHelper.EncodeSecurity(security);
			var tf = QuikSharp.DataStructures.CandleInterval.M5;
			var isSubscribed = quik.Candles.IsSubscribed(ClassCode, secCode, tf).GetAwaiter().GetResult();
			if (!isSubscribed)
			{
				quik.Candles.Subscribe(ClassCode, secCode, tf).GetAwaiter().GetResult();
				for (int i = 0; i < 3; i++)
				{
					Task.Delay(10000).GetAwaiter().GetResult();
					isSubscribed = quik.Candles.IsSubscribed(ClassCode, secCode, tf).GetAwaiter().GetResult();
					if (isSubscribed)
					{
						break;
					}
				}
				if (!isSubscribed)
				{
					logger
						.ForContext("Security", secCode)
						.Warning("GetCandles error");
				}
			}
			var candles = quik.Candles.GetAllCandles(ClassCode, secCode, tf)
				.GetAwaiter().GetResult()
				.Select(candle => new Candle()
					{
						SecurityCode = security,
						DateTime = (DateTime)candle.Datetime,
						ClosePrice = (double)candle.Close
					})
				.ToList();
			if (candles.Count > 0 && candles[candles.Count - 1].DateTime.Date == DateTime.Today)
			{
				//последний бар за сегодня может быть не завершен
				candles.RemoveAt(candles.Count - 1);
			}
			logger
				.ForContext("First", candles[0], destructureObjects: true)
				.ForContext("Last", candles[candles.Count-1], destructureObjects: true)
				.Information("Ready candles");
			foreach (var candle in candles)
			{
				result.Add(candle);
			}
			CandleFunctions.CandleHandler onNewCandle = candle =>
			{
				if (candle.SecCode == secCode && candle.Interval == tf)
				{
					result.Add(new Candle()
						{
							SecurityCode = security,
							DateTime = (DateTime)candle.Datetime,
							ClosePrice = (double)candle.Close
						});
				}
			};
			quik.Candles.NewCandle += onNewCandle;
			token.Register(() => quik.Candles.NewCandle -= onNewCandle);
			return result;
		}
	}
}
