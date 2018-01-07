using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Trading.Core;

namespace Trading.Advisor
{
    public class AdvisorService : IAdvisorService
    {
        static Serilog.ILogger logger = Serilog.Log.ForContext<AdvisorService>();

        ICandleService candleService;
        List<StrategyConfig> strategyConfigs;
        AdvisorProvider advisorProvider;
        HistoryDataService historyDataService;

        public AdvisorService(ICandleService candleService,
                             List<StrategyConfig> strategyConfigs,
                              AdvisorProvider advisorProvider,
                              HistoryDataService historyDataService)
        {
            this.candleService = candleService;
            this.strategyConfigs = strategyConfigs;
            this.advisorProvider = advisorProvider;
            this.historyDataService = historyDataService;
        }

        public BlockingCollection<Advice> GetAdvices(string security, CancellationToken token)
        {
            var strategyConfig = strategyConfigs.FirstOrDefault(x => x.SecurityCode == security);
            if (strategyConfig == null)
            {
                throw new ArgumentException();
            }
            Func<Candle, Advice> advisor = advisorProvider.GetAdvisor(strategyConfig);
            historyDataService.UpdateCandles(strategyConfig.SecurityCode);
            var initAdvice =
                historyDataService.ReadCandles(strategyConfig.SecurityCode)
                        .Select(x => new Candle()
                        {
                            SecurityCode = strategyConfig.SecurityCode,
                            DateTime = x.DateTime,
                            ClosePrice = x.ClosePrice
                        })
                        .Select(advisor)
                        .Where(x => x != null)
                        .LastOrDefault();
            logger.Information("Init advice {@Advice}", initAdvice);
            var result = new BlockingCollection<Advice>();
            var candles = candleService.GetCandles(strategyConfig.SecurityCode, token);
            Task.Run(() =>
            {
                foreach (var candle in candles.GetConsumingEnumerable())
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                    var advice = advisor(candle);
                    if (advice != null)
                    {
                        result.Add(advice);
                    }
                }
                result.CompleteAdding();
            });
            return result;
        }

        public List<string> GetSecurities()
        {
            return strategyConfigs.Select(x => x.SecurityCode).ToList();
        }
    }
}
