using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Trading.Core;

namespace Trading
{
    class StrategyService : IStrategyService, IDisposable
    {
        class PortfolioInfo
        {
            public string Name;
            public double Amount;
            public double AvailableAmount;
        }

        static Serilog.ILogger logger = Serilog.Log.ForContext<StrategyService>();

        bool isLive;
        Client client;
        IAdvisorService advisorService;
        ITraderService traderService;
        CancellationTokenSource cts;

        public StrategyService(bool isLive, Client client,
                               IAdvisorService advisorService,
                               ITraderService traderService)
        {
            this.isLive = isLive;
            this.client = client;
            this.advisorService = advisorService;
            this.traderService = traderService;
        }

        public void Dispose()
        {
            Stop();
        }

        public void Start()
        {
            if (cts != null)
            {
                logger.Information("Стратегия уже запущена.");
                return;
            }

            logger.Information("Запускаем стратегию...");
            cts = new CancellationTokenSource();
            var securities = advisorService.GetSecurities();
            logger.Information("Список инструментов: {0}", string.Join(", ", securities));
            var initTraderResponse = traderService.Init(new InitTraderRequest()
            {
                Firm = client.Firm,
                Portfolio = client.Portfolio,
                Securities = securities
            }).GetAwaiter().GetResult();
            var portfolioInfo = initTraderResponse.Portfolios.FirstOrDefault(x => x.Portfolio == client.Portfolio);
            if (portfolioInfo == null)
            {
                logger
                    .ForContext("Portfolio", client.Portfolio)
                    .Error("Портфель не найден");
                return;
            }
            var portfolio = GetPortfolioInfo(portfolioInfo);
            logger
                    .ForContext("Portfolio", portfolio.Name)
                    .ForContext("Amount", portfolio.Amount)
                    .ForContext("AvailableAmount", portfolio.AvailableAmount)
                    .Information("Init portfolio");
            foreach (var security in securities)
            {
                Task.Run(() =>
                {
                    double? basePrice = null;
                    var advices = advisorService.GetAdvices(security, cts.Token);
                    foreach (var advice in advices.GetConsumingEnumerable(cts.Token))
                    {
                        if (basePrice == null)
                        {
                            basePrice = advice.Price;
                            logger
                                .ForContext("Security", security)
                                .ForContext("Price", basePrice)
                                .Information("Init base price");
                        }
                        if (advice.DateTime < DateTime.Now.AddMinutes(-4))
                        {
                            continue;
                        }

                        logger.Information("New advice {@Advice}", advice);

                        if (isLive)
                        {
                            traderService.OpenPosition(new OpenPositionRequest()
                            {
                                Portfolio = portfolio.Name,
                                Security = advice.SecurityCode,
                                Price = advice.Price,
                                Position = portfolio.AvailableAmount / basePrice.Value * advice.Position
                            });
                        }
                    }
                });
            };
            logger.Information("Стратегия запущена.");
        }

        public void Stop()
        {
            if (cts == null)
            {
                logger.Information("Стратегия уже остановлена.");
                return;
            }

            cts.Cancel();
            cts = null;
            logger.Information("Стратегия остановлена.");
        }

        PortfolioInfo GetPortfolioInfo(InitTraderResponse.PortfolioInfo p)
        {
            var availableAmount = p.Amount;
            if (client.Amount > 0)
            {
                availableAmount = client.Amount;
            }
            if (client.MaxAmount > 0)
            {
                availableAmount = Math.Min(availableAmount, client.MaxAmount);
            }
            if (0 < client.Weight && client.Weight < 1)
            {
                availableAmount *= client.Weight;
            }
            return new PortfolioInfo()
            {
                Name = p.Portfolio,
                Amount = p.Amount,
                AvailableAmount = availableAmount
            };
        }
    }
}
