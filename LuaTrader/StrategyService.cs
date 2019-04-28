using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using QuikSharp;
using QuikSharp.DataStructures.Transaction;

namespace LuaTrader
{
	class StrategyService: IDisposable
	{
		class RequiredPosition
		{
			public string portfolio;
			public string security;
			public int strategyPosition;
			public int traderPosition;

			public bool hasError { get { return strategyPosition != traderPosition; } }
		}

		const string ClassCode = "SPBFUT";
		const double Slippage = 0.001;

		Serilog.ILogger logger = Serilog.Log.ForContext<StrategyService> ();

		IAdvisorService advisorService;
		Client config;
		Quik quik;
		CancellationTokenSource cts;

		public StrategyService (IAdvisorService advisorService, Client config)
		{
			this.advisorService = advisorService;
			this.config = config;
			this.quik = new Quik (config.Port == 0 ? Quik.DefaultPort : config.Port, new InMemoryStorage ());
		}

		public void Dispose ()
		{
			Stop ();
		}

		public void Terminal ()
		{
			var terminal = new QuikTerminal (config);
			terminal.RunTerminal ();
		}

		public void Start ()
		{
			if (cts != null) {
				logger.Information ("Стратегия уже запущена.");
				return;
			}

			logger.Information ("Запускаем стратегию...");
			cts = new CancellationTokenSource ();

			Terminal ();
			if (!quik.Service.IsConnected ().GetAwaiter ().GetResult ()) {
				throw new Exception ("Отсутствует соединение со шлюзом");
			}

			var quikAmount = GetAmount (config.Portfolio);
			var amount = GetAvailableAmount (quikAmount);
			if (amount == 0) {
				throw new Exception ("Денег нет, но вы держитесь");
			}
			logger
				.ForContext ("Portfolio", config.Portfolio)
				.ForContext ("QuikAmount", quikAmount)
				.ForContext ("AvailableAmount", amount)
				.Information ("Init portfolio");

			var securities = advisorService.GetSecurities ();
			logger.Information ("Список инструментов: {0}", string.Join (", ", securities));

			if (config.PublishCandles) {
				logger.Information ("Настройка публикации баров...");

				var candles = new BlockingCollection<Candle> ();

				Task.Run (() => {
					try {
						advisorService.PublishCandles (candles, cts.Token);
					} catch (OperationCanceledException) {
					} catch (Exception e) {
						logger.Error (e, "PublishCandles error");
					}
				});

				foreach (var security in securities) {
					GetCandles (security, candles, cts.Token);
				}

				logger.Information ("Настройка публикации баров завершена.");
			}

			var requiredPositions = new BlockingCollection<RequiredPosition> ();
			Task.Run (() => {
				try {
					Monitoring (requiredPositions, cts.Token);
				} catch (OperationCanceledException) {
				} catch (Exception e) {
					logger.Error (e, "Monitoring error");
				}
			});

			foreach (var security in securities) {
				var advices = new BlockingCollection<Advice>();

				Task.Run (() => {
					try {
						ExecuteAdvices (config.Portfolio, SecurityHelper.EncodeSecurity (security), amount, advices, requiredPositions, cts.Token);
					} catch (OperationCanceledException) {
					} catch (Exception e) {
						logger.Error (e, "ExecuteAdvices error");
					}
				});

				Task.Run (() => {
					try {
						advisorService.GetAdvices(security, advices, cts.Token);
					} catch (OperationCanceledException) {
					} catch (Exception e) {
						logger.Error (e, "GetAdvices error");
					}
				});
			}

			logger.Information ("Стратегия запущена.");
		}

		public void Stop ()
		{
			if (cts == null) {
				logger.Information ("Стратегия уже остановлена.");
				return;
			}

			cts.Cancel ();
			cts = null;
			logger.Information ("Стратегия остановлена.");
		}

		void ExecuteAdvices (string portfolio, string security, double amount,
		                     BlockingCollection<Advice> inAdvices,
		                     BlockingCollection<RequiredPosition> outPositions,
		                     CancellationToken token)
		{
			var strategyPosition = GetPosition (portfolio, security);
			logger
				.ForContext ("Portfolio", portfolio)
				.ForContext ("Security", security)
				.ForContext ("Position", strategyPosition)
				.Information ("Init position");
			var lastOrderTime = DateTime.MinValue;
			double? basePrice = null;
			foreach (var advice in inAdvices.GetConsumingEnumerable(token)) {
				if (basePrice == null) {
					basePrice = advice.Price;
					logger
						.ForContext ("Advice", advice, destructureObjects: true)
						.Information ("Init base price");
				}
				if (advice.DateTime < DateTime.Now.AddMinutes (-9)) {
					continue;
				}
				var position = amount / basePrice.Value * advice.Position;
				var volume = (int)(position - strategyPosition);
				if (volume == 0) {
					continue;
				}
				logger
					.ForContext ("Advice", advice, destructureObjects: true)
					.Information ("New advice");

				// проверяем, что со времени предыдущей заявки прошла 1 мин, т.к. текущая позиция в терминале может не успеть обновиться
				if (DateTime.Now <= lastOrderTime.Add (TimeSpan.FromMinutes (1))) {
					logger.Information ("Skip advice: too fast");
					continue;
				}
				var traderPosition = GetPosition (portfolio, security);
				if (strategyPosition != traderPosition) {
					logger
						.ForContext ("Portfolio", portfolio)
						.ForContext ("Security", security)
						.ForContext ("StrategyPosition", strategyPosition)
						.ForContext ("TraderPosition", traderPosition)
						.Warning ("Wrong position");
					continue;
				}

				try {
					RegisterOrder (portfolio, security, volume, advice.Price);
				} catch (Exception ex) {
					logger.Error (ex, "Register order error");
				}
				lastOrderTime = DateTime.Now;
				strategyPosition += volume;

				outPositions.Add (new RequiredPosition () {
					portfolio = portfolio,
					security = security,
					strategyPosition = strategyPosition,
				});
			}
		}

		void RegisterOrder (string portfolio, string security, int volume, double price)
		{
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
			order.Price = (decimal)price;//TODO минимальный шаг цены, планка
			order.Quantity = Math.Abs (volume);//TODO lot size
			order.Account = portfolio;

			logger
				.ForContext ("Portfolio", portfolio)
				.ForContext ("Security", security)
				.ForContext ("Price", price)
				.ForContext ("Volume", volume)
				.Information ("Register order");
			
			quik.Orders.CreateOrder (order).GetAwaiter ().GetResult ();
		}

		void Monitoring (BlockingCollection<RequiredPosition> inPositions, CancellationToken token)
		{
			var positions = new Dictionary<string, RequiredPosition> ();
			var hasChanges = false;
			while (!inPositions.IsCompleted) {
				RequiredPosition item = null;
				if (inPositions.TryTake (out item, TimeSpan.FromSeconds (30))) {
					var key = item.portfolio + "#" + item.security;
					positions [key] = item;
					hasChanges = true;
				} else {
					if (hasChanges) {
						Summary (positions.Values);
						hasChanges = false;
					}
				}
			}
		}

		void GetCandles (string security, BlockingCollection<Candle> outCandles, CancellationToken token)
		{
			var secCode = SecurityHelper.EncodeSecurity (security);
			var tf = QuikSharp.DataStructures.CandleInterval.M5;
			var isSubscribed = quik.Candles.IsSubscribed (ClassCode, secCode, tf).GetAwaiter ().GetResult ();
			if (!isSubscribed) {
				quik.Candles.Subscribe (ClassCode, secCode, tf).GetAwaiter ().GetResult ();
				for (int i = 0; i < 3; i++) {
					Task.Delay (10000).GetAwaiter ().GetResult ();
					isSubscribed = quik.Candles.IsSubscribed (ClassCode, secCode, tf).GetAwaiter ().GetResult ();
					if (isSubscribed) {
						break;
					}
				}
				if (!isSubscribed) {
					logger
						.ForContext ("Security", secCode)
						.Warning ("GetCandles error");
				}
			}
			var candles = quik.Candles.GetAllCandles (ClassCode, secCode, tf)
				.GetAwaiter ().GetResult ()
				.Select (candle => new Candle () {
				SecurityCode = security,
				DateTime = (DateTime)candle.Datetime,
				ClosePrice = (double)candle.Close
			})
				.ToList ();
			if (candles.Count > 0 && candles [candles.Count - 1].DateTime.Date == DateTime.Today) {
				//последний бар за сегодня может быть не завершен
				candles.RemoveAt (candles.Count - 1);
			}
			logger
				.ForContext ("First", candles [0], destructureObjects: true)
				.ForContext ("Last", candles [candles.Count - 1], destructureObjects: true)
				.Information ("Ready candles");
			foreach (var candle in candles) {
				outCandles.Add (candle);
			}
			CandleFunctions.CandleHandler onNewCandle = candle => {
				if (candle.SecCode == secCode && candle.Interval == tf) {
					outCandles.Add (new Candle () {
						SecurityCode = security,
						DateTime = (DateTime)candle.Datetime,
						ClosePrice = (double)candle.Close
					});
				}
			};
			quik.Candles.NewCandle += onNewCandle;
			token.Register (() => quik.Candles.NewCandle -= onNewCandle);
		}

		double GetAmount (string portfolio)
		{
			var portfolioInfo = quik.Trading.GetPortfolioInfoEx (config.Firm, portfolio, 0).GetAwaiter ().GetResult ();
			if (portfolioInfo == null) {
				throw new Exception ("Портфель не найден");
			}
			return double.Parse (portfolioInfo.StartLimitOpenPos, CultureInfo.InvariantCulture);
		}

		int GetPosition (string portfolio, string security)
		{
			var position = quik.Trading.GetFuturesHolding (config.Firm, portfolio, security, 0).GetAwaiter ().GetResult ();
			if (position == null) {
				return 0;
			}
			return (int)position.totalNet;
		}

		double GetAvailableAmount (double quikAmount)
		{
			double amount;
			if (config.Amount > 0) {
				amount = config.Amount;
			} else {
				amount = quikAmount;
			}
			if (config.AmountReduction > 0) {
				amount = Math.Max (0, amount - config.AmountReduction);
			}
			if (config.MaxAmount > 0) {
				amount = Math.Min (amount, config.MaxAmount);
			}
			if (0 < config.Weight && config.Weight < 1) {
				amount *= config.Weight;
			}
			return amount;
		}

		void Summary (IEnumerable<RequiredPosition> source)
		{
			foreach (var item in source) {
				item.traderPosition = GetPosition (item.portfolio, item.security);
			}
			var errorCount = source.Count (x => x.hasError);
			if (errorCount > 0) {
				logger.Error ("Текущая позиция содержит {ErrorCount} ошибок", errorCount);
			}
			Console.Write (TableHelper.Format (
				"Portfolio,Security,Required,Quik,Status",
				source.Select (x => new object[] {
					x.portfolio,
					x.security,
					x.strategyPosition,
					x.traderPosition,
					x.hasError ? "!" : "+"
				})));
		}

		public void AutoStart (TimeSpan startTime, TimeSpan minDelay)
		{
			TimeSpan ts = minDelay;
			TimeSpan delta = startTime - DateTime.Now.TimeOfDay;
			if (delta > TimeSpan.Zero) {
				ts += delta;
			}

			Task.Run (async () => {
				logger.Warning ("Autostart after {Delay}.", ts);
				await Task.Delay (ts).ConfigureAwait (false);
				try {
					Start ();
				} catch (Exception e) {
					logger.Error (e, "AutoStartCommand");
				}
			});
		}
	}
}
