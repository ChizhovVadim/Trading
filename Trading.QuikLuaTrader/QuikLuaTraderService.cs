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
        class InitMessage
        {
            public InitTraderRequest request;
            public TaskCompletionSource<InitTraderResponse> response;
        }

        class GetCandlesMessage
        {
            public string security;
            public CancellationToken token;
            public BlockingCollection<Candle> result;
        }

        class ShowStatusMessage
        {
            public bool Delay;
        }

        class PositionInfo
        {
            public string Portfolio;
            public string Security;
            public int StrategyPosition;
            public double? RequiredPosition;//заменить? на int? PlannedPosition
            //public Order LastOrder;
        }

        const string ClassCode = "SPBFUT";
        const double Slippage = 0.001;
        Serilog.ILogger logger = Serilog.Log.ForContext<QuikLuaTraderService>();
        MailboxProcessor mailboxProcessor;
        bool initialized;
        Quik quik;
        InitTraderRequest initTraderRequest;
        Dictionary<string, PositionInfo> positions;
        CancellationTokenSource cts;

        public QuikLuaTraderService()
        {
            this.mailboxProcessor = MailboxProcessor.Start(Receive);
            this.cts = new CancellationTokenSource();
        }

        public void Dispose()
        {
            mailboxProcessor.Dispose();
            cts.Cancel();
        }

        public BlockingCollection<Candle> GetCandles(string security, CancellationToken token)
        {
            var msg = new GetCandlesMessage()
            {
                security = security,
                token = token,
                result = new BlockingCollection<Candle>()
            };
            mailboxProcessor.Post(msg);
            return msg.result;
        }

        public Task<InitTraderResponse> Init(InitTraderRequest request)
        {
            var msg = new InitMessage()
            {
                request = request,
                response = new TaskCompletionSource<InitTraderResponse>()
            };
            mailboxProcessor.Post(msg);
            return msg.response.Task;
        }

        public void OpenPosition(OpenPositionRequest request)
        {
            mailboxProcessor.Post(request);
        }

        public void ShowStatus()
        {
            mailboxProcessor.Post(new ShowStatusMessage());
        }

        public void Terminal()
        {

        }

        void Receive(object o)
        {
			//TODO хорошо бы GetCandlesMessage мог работать без init

			if (!initialized && !(o is InitMessage))
            {
                return;
            }

            Switch.On(o)
                  .Case<InitMessage>(msg =>
                  {
                      logger.Information("Init trader...");
                      this.initTraderRequest = msg.request;
                      if (quik == null)
                      {
                          quik = new Quik(Quik.DefaultPort, new InMemoryStorage());
                      }
                      logger.Debug("Ожидаем подключение...");
                      var isServerConnected = quik.Service.IsConnected().Result;
                      if (!isServerConnected)
                      {
                          //logger.Error("Отсутствует соединение со шлюзом");
                          msg.response.TrySetException(new Exception("Отсутствует соединение со шлюзом"));
                          return;
                      }
                      logger.Debug("Подключение установлено.");

                      var portfolios = new List<InitTraderResponse.PortfolioInfo>();
                      this.positions = new Dictionary<string, PositionInfo>();

                      var portfolioInfo = quik.Trading.GetPortfolioInfoEx(msg.request.Firm, msg.request.Portfolio, 0).GetAwaiter().GetResult();
                      if (portfolioInfo != null)
                      {
                          portfolios.Add(new InitTraderResponse.PortfolioInfo()
                          {
                              Portfolio = msg.request.Portfolio,
                              Amount = double.Parse(portfolioInfo.StartLimitOpenPos, CultureInfo.InvariantCulture)
                          });

                          foreach (var s in msg.request.Securities)
                          {
                              var securityCode = SecurityHelper.EncodeSecurity(s);
                              int initPosition = GetQuikPosition(msg.request.Portfolio, securityCode);
                              logger
                                  .ForContext("Portfolio", msg.request.Portfolio)
                                  .ForContext("Security", securityCode)
                                  .ForContext("Position", initPosition)
                                  .Information("Init position");
                              positions[ComputeKey(msg.request.Portfolio, securityCode)] = new PositionInfo()
                              {
                                  Portfolio = msg.request.Portfolio,
                                  Security = securityCode,
                                  StrategyPosition = initPosition
                              };
                          }
                      }

                      msg.response.TrySetResult(new InitTraderResponse()
                      {
                          Portfolios = portfolios
                      });

                      initialized = true;
                      logger.Information("Init trader finished.");
                  }).Case<OpenPositionRequest>(msg =>
                  {
                      var securityCode = SecurityHelper.EncodeSecurity(msg.Security);
                      PositionInfo position;
                      if (!positions.TryGetValue(ComputeKey(msg.Portfolio, securityCode), out position))
                      {
                          return;
                      }
                      position.RequiredPosition = msg.Position;
                      var volume = (int)(position.RequiredPosition.Value - position.StrategyPosition);
                      if (volume == 0)
                      {
                          return;
                      }
                      if (position.StrategyPosition != GetQuikPosition(position.Portfolio, position.Security))
                      {
                          return;
                      }
                      //CancelOrder(position.LastOrder);
                      RegisterOrder(msg.Portfolio, msg.Security, volume, msg.Price);
                      position.StrategyPosition += volume;
                      Task.Delay(TimeSpan.FromSeconds(30))
                                            .ContinueWith(_ => mailboxProcessor.Post(new ShowStatusMessage()
                                            {
                                                Delay = true
                                            }));
                  }).Case<GetCandlesMessage>(msg =>
                  {
                      var secCode = SecurityHelper.EncodeSecurity(msg.security);
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
                          msg.result.Add(new Candle()
                          {
                              SecurityCode = msg.security,
                              DateTime = (DateTime)candle.Datetime,
                              ClosePrice = (double)candle.Close
                          });
                      }
                      CandleFunctions.CandleHandler onNewCandle = candle =>
                      {
                          if (candle.SecCode == secCode && candle.Interval == tf)
                          {
                              msg.result.Add(new Candle()
                              {
                                  SecurityCode = msg.security,
                                  DateTime = (DateTime)candle.Datetime,
                                  ClosePrice = (double)candle.Close
                              });
                          }
                      };
                      quik.Candles.NewCandle += onNewCandle;
                      cts.Token.Register(() => quik.Candles.NewCandle -= onNewCandle);
                  }).Case<ShowStatusMessage>(msg =>
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
                  });
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
