using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuikSharp;
using QuikSharp.DataStructures.Transaction;
using Trading.Core;
namespace Trading.QuikLuaTrader
{
	public class QuikLuaTraderService : ITraderService, ICandleService, IDisposable
	{
		class PositionInfo
		{
			public string Portfolio;
			public string Security;
			public int StrategyPosition;
			public double? RequiredPosition;
			//public Order LastOrder;
		}

		const string ClassCode = "SPBFUT";
		const double Slippage = 0.001;
		Serilog.ILogger logger = Serilog.Log.ForContext<QuikLuaTraderService>();

		Dispatcher dispatcher;
		bool initialized;
		Quik quik;
		InitTraderRequest initTraderRequest;
		Dictionary<string, PositionInfo> positions;
		CancellationTokenSource cts;

		public QuikLuaTraderService()
		{
			this.cts = new CancellationTokenSource();
			this.dispatcher = new Dispatcher();
			this.quik = new Quik(Quik.DefaultPort, new InMemoryStorage());
			cts.Token.Register(dispatcher.Dispose);
		}

		public void Dispose()
		{
			cts.Cancel();
		}

		public BlockingCollection<Candle> GetCandles(string security, CancellationToken token)
		{
			return dispatcher.Invoke(() =>
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
				var candles = quik.Candles.GetAllCandles(ClassCode, secCode, tf).GetAwaiter().GetResult();
				//TODO последний бар за сегодня может быть не завершен
				foreach (var candle in candles)
				{
					result.Add(new Candle()
					{
						SecurityCode = security,
						DateTime = (DateTime)candle.Datetime,
						ClosePrice = (double)candle.Close
					});
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
			});
		}

		public Task<InitTraderResponse> Init(InitTraderRequest request)
		{
			return dispatcher.InvokeAsync(() =>
			{
				logger.Information("Init trader...");
				this.initTraderRequest = request;
				logger.Debug("Ожидаем подключение...");
				if (!quik.Service.IsConnected().GetAwaiter().GetResult())
				{
					throw new Exception("Отсутствует соединение со шлюзом");
				}
				logger.Debug("Подключение установлено.");

				var portfolios = new List<InitTraderResponse.PortfolioInfo>();
				this.positions = new Dictionary<string, PositionInfo>();

				var portfolioInfo = quik.Trading.GetPortfolioInfoEx(request.Firm, request.Portfolio, 0).GetAwaiter().GetResult();
				if (portfolioInfo == null)
				{
					throw new Exception("Портфель не найден");
				}

				portfolios.Add(new InitTraderResponse.PortfolioInfo()
				{
					Portfolio = request.Portfolio,
					Amount = double.Parse(portfolioInfo.StartLimitOpenPos, CultureInfo.InvariantCulture)
				});

				foreach (var s in request.Securities)
				{
					var securityCode = SecurityHelper.EncodeSecurity(s);
					int initPosition = GetQuikPosition(request.Portfolio, securityCode);
					logger
						.ForContext("Portfolio", request.Portfolio)
						.ForContext("Security", securityCode)
						.ForContext("Position", initPosition)
						.Information("Init position");
					positions[ComputeKey(request.Portfolio, securityCode)] = new PositionInfo()
					{
						Portfolio = request.Portfolio,
						Security = securityCode,
						StrategyPosition = initPosition
					};
				}

				initialized = true;
				logger.Information("Init trader finished.");
				return new InitTraderResponse()
				{
					Portfolios = portfolios
				};
			});
		}

		public void OpenPosition(OpenPositionRequest request)
		{
			var task = dispatcher.InvokeAsync(() =>
			{
				var securityCode = SecurityHelper.EncodeSecurity(request.Security);
				PositionInfo position;
				if (!positions.TryGetValue(ComputeKey(request.Portfolio, securityCode), out position))
				{
					return 0;
				}
				position.RequiredPosition = request.Position;
				var volume = (int)(position.RequiredPosition.Value - position.StrategyPosition);
				if (volume == 0)
				{
					return 0;
				}
				if (position.StrategyPosition != GetQuikPosition(position.Portfolio, position.Security))
				{
					return 0;
				}
				//CancelOrder(position.LastOrder);
				RegisterOrder(request.Portfolio, securityCode, volume, request.Price);
				position.StrategyPosition += volume;
				Task.Delay(TimeSpan.FromSeconds(30))
					.ContinueWith(_ => ShowStatus());
				return 0;
			});
		}

		public void ShowStatus()
		{
			dispatcher.Invoke(() =>
			{
				var result = new TraderSnapshotMessage();
				result.Portfolios = new List<TraderSnapshotMessage.PortfolioSnapshot>();
				result.Positions = new List<TraderSnapshotMessage.PositionSnapshot>();
				var portfolioInfo = quik.Trading.GetPortfolioInfoEx(initTraderRequest.Firm, initTraderRequest.Portfolio, 0).GetAwaiter().GetResult();
				if (portfolioInfo != null)
				{
					result.Portfolios.Add(new TraderSnapshotMessage.PortfolioSnapshot()
					{
						Portfolio = initTraderRequest.Portfolio,
						BeginAmount = double.Parse(portfolioInfo.StartLimitOpenPos, CultureInfo.InvariantCulture),
						VariationMargin = double.Parse(portfolioInfo.VarMargin, CultureInfo.InvariantCulture),
						CurrentAmount = double.Parse(portfolioInfo.TotalLimitOpenPos, CultureInfo.InvariantCulture),
					});
					result.Positions = positions.Values
									  .Select(x => new TraderSnapshotMessage.PositionSnapshot()
									  {
										  Portfolio = x.Portfolio,
										  Security = x.Security,
										  Position = x.StrategyPosition,
										  RequiredPosition = x.RequiredPosition,
										  QuikPosition = GetQuikPosition(x.Portfolio, x.Security)
									  })
									  .ToList();
				}
				result.UpdateTime = DateTime.Now;
				TraderSnapshotHelper.Write(result);
				return 0;
			});
		}

		public void Terminal()
		{

		}

		long RegisterOrder(string portfolio, string secCode, int volume, double price)
		{
			//TODO security minPrice/maxPrice, priceStep
			if (volume > 0)
			{
				price = price * (1 + Slippage);
			}
			else
			{
				price = price * (1 - Slippage);
			}
			price = Math.Round(price, MidpointRounding.AwayFromZero);

			var order = new Order();
			order.ClassCode = ClassCode;
			order.SecCode = secCode;
			order.Operation = volume > 0 ? QuikSharp.DataStructures.Operation.Buy :
				QuikSharp.DataStructures.Operation.Sell;
			order.Price = (decimal)price;
			order.Quantity = Math.Abs(volume);//TODO lot size
			order.Account = portfolio;

			//ownOrders[order] = true;
			logger
				.ForContext("Price", price)
				.ForContext("Volume", volume)
				.ForContext("Portfolio", portfolio)
				.ForContext("Security", secCode)
				.Information("Register order");
			try
			{
				return quik.Orders.CreateOrder(order).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				logger.Error(ex, "Register order error");
				return 0;
			}
		}

		int GetQuikPosition(string portfolio, string security)
		{
			var position = quik.Trading.GetFuturesHolding(initTraderRequest.Firm, portfolio, security, 0).GetAwaiter().GetResult();
			if (position == null)
			{
				return 0;
			}
			return (int)position.totalNet;
		}

		static string ComputeKey(string portfolio, string security)
		{
			return portfolio + "*" + security;
		}
	}
}
