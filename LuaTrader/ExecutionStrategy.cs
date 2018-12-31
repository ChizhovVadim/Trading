using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace LuaTrader
{
	class ExecutionStrategy
	{
		static Serilog.ILogger logger = Serilog.Log.ForContext<ExecutionStrategy> ();

		ITraderService traderService;
		string portfolio;
		string security;
		int strategyPosition;

		public ExecutionStrategy (ITraderService traderService, string portfolio, string security)
		{
			this.traderService = traderService;
			this.portfolio = portfolio;
			this.security = security;
			this.strategyPosition = traderService.GetPosition (portfolio, security);
			logger
				.ForContext ("Portfolio", portfolio)
				.ForContext ("Security", security)
				.ForContext ("Position", strategyPosition)
				.Information ("Init position");
		}

		public void Execute(BlockingCollection<Advice> advices, double amount, CancellationToken token)
		{
			double? basePrice = null;
			foreach (var advice in advices.GetConsumingEnumerable(token)) {
				if (basePrice == null) {
					basePrice = advice.Price;
					logger
						.ForContext ("Advice", advice, destructureObjects: true)
						.Information ("Init base price");
				}
				if (advice.DateTime < DateTime.Now.AddMinutes (-9)) {
					continue;
				}
				logger
					.ForContext ("Advice", advice, destructureObjects: true)
					.Information ("New advice");
				OpenPosition (advice.Price, amount / basePrice.Value * advice.Position);
			}
		}

		void OpenPosition (double price, double position)
		{
			var volume = (int)(position - strategyPosition);
			if (volume == 0) {
				return;
			}
			if (!IsPositionOk ()) {
				return;
			}
			traderService.RegisterOrder (portfolio, security, volume, price, CancellationToken.None);
			strategyPosition += volume;
			Task.Delay (TimeSpan.FromSeconds (30))
				.ContinueWith (_ => IsPositionOk ());
		}

		bool IsPositionOk ()
		{
			var traderPosition = traderService.GetPosition (portfolio, security);
			if (strategyPosition == traderPosition) {
				return true;
			}
			logger
					.ForContext ("Portfolio", portfolio)
					.ForContext ("Security", security)
					.ForContext ("StrategyPosition", strategyPosition)
					.ForContext ("TraderPosition", traderPosition)
					.Warning ("Wrong position");
			return false;
		}
	}
}
