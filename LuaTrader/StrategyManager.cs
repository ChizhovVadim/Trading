using System;
using System.Threading.Tasks;
using System.Threading;

namespace LuaTrader
{
	class StrategyManager : IDisposable
	{
		static Serilog.ILogger logger = Serilog.Log.ForContext<StrategyManager> ();

		IAdvisorService advisorService;
		ITraderService traderService;
		ICandleService candleService;
		Client config;
		CancellationTokenSource cts;

		public StrategyManager (IAdvisorService advisorService, ITraderService traderService,
			ICandleService candleService, Client config)
		{
			this.advisorService = advisorService;
			this.traderService = traderService;
			this.candleService = candleService;
			this.config = config;
		}

		public void Dispose()
		{
			Stop();
		}

		public void Start ()
		{
			if (cts != null) {
				logger.Information ("Стратегия уже запущена.");
				return;
			}

			logger.Information ("Запускаем стратегию...");
			cts = new CancellationTokenSource ();

			traderService.Terminal ();

			var amount = GetAvailableAmount ();
			if (amount == 0) {
				logger
					.ForContext ("Portfolio", config.Portfolio)
					.Error ("Портфель не найден");
				return;
			}
			logger
				.ForContext ("Portfolio", config.Portfolio)
				.ForContext ("AvailableAmount", amount)
				.Information ("Init portfolio");			

			var securities = advisorService.GetSecurities ();
			logger.Information ("Список инструментов: {0}", string.Join (", ", securities));

			foreach (var security in securities) {
				var executionStrategy = new ExecutionStrategy (traderService, config.Portfolio, SecurityHelper.EncodeSecurity (security));
				if (config.PublishCandles) {
					logger.Information ("Настройка публикации баров...");
					var candles = candleService.GetCandles (security, cts.Token);
					Task.Run (() => {
						try {
							advisorService.PublishCandles(candles, cts.Token);
						} catch (OperationCanceledException) {
						} catch (Exception e) {
							logger.Fatal (e, "PublishCandles error");
						}
					});
					logger.Information ("Настройка публикации баров завершена.");
				}

				var advices = advisorService.GetAdvices (security, cts.Token);
				Task.Run (() => {
					try
					{
						executionStrategy.Execute (advices, amount, cts.Token);
					} catch (OperationCanceledException) {
					} catch (Exception e) {
						logger.Fatal (e, "ExecuteAdvices error");
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

		double GetAvailableAmount ()
		{
			double availableAmount;
			if (config.Amount > 0) {
				availableAmount = config.Amount;
			} else {
				availableAmount = traderService.GetAmount (config.Portfolio);
			}
			if (config.AmountReduction > 0) {
				availableAmount = Math.Max(0, availableAmount - config.AmountReduction);
			}
			if (config.MaxAmount > 0) {
				availableAmount = Math.Min (availableAmount, config.MaxAmount);
			}
			if (0 < config.Weight && config.Weight < 1) {
				availableAmount *= config.Weight;
			}
			return availableAmount;
		}

		public void AutoStart(TimeSpan startTime, TimeSpan minDelay)
		{
			TimeSpan ts = minDelay;
			TimeSpan delta = startTime - DateTime.Now.TimeOfDay;
			if (delta > TimeSpan.Zero) {
				ts += delta;
			}

			Task.Run(async () =>
				{
					logger.Warning("Autostart after {Delay}.", ts);
					await Task.Delay(ts).ConfigureAwait(false);
					try
					{
						Start ();
					}
					catch (Exception e)
					{
						logger.Error(e, "AutoStartCommand");
					}
				});
		}
	}
}
