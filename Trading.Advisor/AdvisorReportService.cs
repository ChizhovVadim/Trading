using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using Trading.Core;

namespace Trading.Advisor
{
    public class AdvisorReportService
    {
        const double Slippage = 0.0002;

        List<StrategyConfig> strategyConfigs;
        AdvisorProvider advisorProvider;
        HistoryDataService futureDataService;
        HistoryDataService stockDataService;
        CultureInfo reportCulture;

        public AdvisorReportService(List<StrategyConfig> strategyConfigs,
                                    AdvisorProvider advisorProvider,
                                    HistoryDataService futureDataService)
        {
            this.strategyConfigs = strategyConfigs;
            this.advisorProvider = advisorProvider;
            this.futureDataService = futureDataService;
            this.stockDataService = new HistoryDataService(
                new HistoryCandleRepository(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "TradingData", "Stock")),
                new HistoryCandleProvider(new HistoryCandleProviderSettings()
                {
                    SecurityCodes = new List<SecurityCode>() {
                    new SecurityCode() {
                        Code = "SBER",
                        FinamCode = 3
                    },
                    new SecurityCode() {
                        Code = "GAZP",
                        FinamCode = 16842
                    },
                    new SecurityCode() {
                        Code = "MICEX",
                        FinamCode = 13851
                    }}
                })
            );
            this.reportCulture = CreateCulture();
        }

        class Item
        {
            public string Name;
            public string Security;
            public bool IsFuture;
            public double Weight;
            public double Lever;
            public Advice LastAdvice;
            public List<DateSum> Hprs;
            public HprStatistcs Stat;
        }

        public void Report()
        {
            var items = new Item[] {
                new Item { Name = "Dual", Security= "Si", IsFuture=true, Weight=0.75 },
                new Item { Name = "Dual", Security= "Eu", IsFuture=true, Weight=0.25 },
                //new Item { Name = "Stock", Security= "SBER", IsFuture=false, Weight=0.25 },
            };

            foreach (var item in items)
            {
                if (item.IsFuture)
                {
                    List<DateSum> hprs;
                    List<Advice> recentAdvices;
                    ComputeHprs(new StrategyConfig()
                    {
                        SecurityCode = item.Security,
                        Name = item.Name
                    }, out hprs, out recentAdvices);
                    item.LastAdvice = recentAdvices.LastOrDefault();
                    item.Hprs = hprs;
                }
                else
                {
                    var advisor = advisorProvider.GetAdvisor(new StrategyConfig()
                    {
                        SecurityCode = item.Security,
                        Name = item.Name,
                    });
                    var advices = stockDataService.ReadCandles(item.Security)
                        .ToCandles(item.Security)
                        .Select(advisor)
                        .Where(x => x != null)
                        .RemoveHolidays(DateHelper.IsNewDayAfterHolidayStarted)
                        .ToList();
                    item.LastAdvice = advices.LastOrDefault();
                    item.Hprs = advices.ToHprs(Slippage).ToList();
                }

                item.Lever = item.Hprs.OptimalLever(HistoryTestHelper.LimitStDev(0.045));
                item.Hprs = item.Hprs.WithLever(item.Lever);
                item.Stat = HistoryTestHelper.ComputeHprStatistcs(item.Hprs);
            }

            Console.Write(TableHelper.Format(reportCulture,
                "Name,Sec,W:f2,Lev:f1,Pos:f2,Ret:p,Day:p1,Month:p1,Year:p0,High:yyy-MM-dd,dd:p1,max dd:p1",
                items.Select(x => new object[] {
                    x.Name,
                    x.Security,
                    x.Weight,
                    x.Lever,
                    x.LastAdvice.Position,
                    x.Stat.MonthHpr - 1,
                    x.Stat.DayHprs.Last ().Sum - 1,
                    x.Stat.MonthHprs.Last ().Sum - 1,
                    x.Stat.YearGeomHprs.Last ().Sum - 1,
                    x.Stat.DrawdownInfo.HighEquityDate,
                    x.Stat.DrawdownInfo.CurrentDrawdown - 1,
                    x.Stat.DrawdownInfo.MaxDrawdown - 1
                })));

            var totalHprs = HistoryTestHelper.CombineAdvisorHprs(
                items.Select(x => x.Hprs).ToList(),
                items.Select(x => x.Weight).ToList()
            );

            var stat = HistoryTestHelper.ComputeHprStatistcs(totalHprs);
            PrintHprReport(stat);
        }

        public void Monitoring()
        {
            var results = strategyConfigs.Select(strategyConfig =>
            {
                futureDataService.UpdateCandles(strategyConfig.SecurityCode);

                var advisor = advisorProvider.GetAdvisor(strategyConfig);
                var advices = futureDataService.ReadCandles(strategyConfig.SecurityCode)
                    .ToCandles(strategyConfig.SecurityCode)
                    .Select(advisor)
                    .Where(x => x != null)
                    .RemoveHolidays(DateHelper.IsNewDayAfterHolidayStarted)
                    .ToList();
                var hprs = advices.ToHprs(Slippage).ToList();
                return new { strategyConfig, advices, hprs };
            }).ToList();

            Console.Write(TableHelper.Format(reportCulture,
                "Name,Sec,Pos:f2,W:f2,Lev:f1",
                results.Select(x => new object[] {
                    x.strategyConfig.Name,
                    x.strategyConfig.SecurityCode,
                    x.advices.Last().Position,
                    x.strategyConfig.Weight,
                    x.strategyConfig.Lever
                })));

            var totalHprs = HistoryTestHelper.CombineAdvisorHprs(
                results.Select(x => x.hprs).ToList(),
                results.Select(_ => 1.0).ToList()
            );

            var stat = HistoryTestHelper.ComputeHprStatistcs(totalHprs);
            PrintHprReport(stat);
        }

        public void UpdateHistoryData()
        {
            futureDataService.UpdateCandles("Si-3.18");
            futureDataService.UpdateCandles("Eu-3.18");
            //stockDataService.UpdateCandlesSimple("SBER");
        }

        void ComputeHprs(StrategyConfig strategyConfig, out List<DateSum> hprs, out List<Advice> recentAdvices)
        {
            var allAdvices = HistoryTestHelper.EnumerateSecurities(strategyConfig.SecurityCode)
                .AsParallel()
                .AsOrdered()
                .Select(securityCode =>
                {
                    var advisor = advisorProvider.GetAdvisor(new StrategyConfig()
                    {
                        SecurityCode = securityCode,
                        Name = strategyConfig.Name,
                        StdVolatility = strategyConfig.StdVolatility,
                    });
                    return futureDataService.ReadCandles(securityCode)
                            .ToCandles(securityCode)
                            .Select(advisor)
                            .Where(x => x != null)
                            .RemoveHolidays(DateHelper.IsNewDayAfterHolidayStarted)
                            .ToList();
                })
                .AsSequential()
                .Where(x => x.Count > 0)
                .ToList();

            hprs = allAdvices
                .SelectMany(x => x.ToHprs(Slippage))
                .Verify(x => x.Date)
                .ToList();

            recentAdvices = allAdvices[allAdvices.Count - 1];//TODO показывает самый последний контракт, даже если не начали его еще торговать
        }

        void PrintHprReport(HprStatistcs report)
        {
            Console.WriteLine("Ежемесячная доходность: {0:p}", report.MonthHpr - 1);
            Console.WriteLine("Среднеквадратичное отклонение доходности за день: {0:p}", report.StDev);
            Console.WriteLine("Средний убыток в день среди 5% худших дней: {0:p}", report.AVaR - 1);

            WriteHprs(report.DayHprs.Tail(21).ToList());
            WriteHprs(report.MonthHprs);
            WriteHprs(report.YearGeomHprs);
            WriteHprs(report.YearArifHprs);

            Console.WriteLine("Продолжительная просадка: {0} дн.", report.DrawdownInfo.LongestDrawdown);
            Console.WriteLine("Максимальная просадка: {0:p1}", report.DrawdownInfo.MaxDrawdown - 1);
            Console.WriteLine("Текущая просадка: {0:p1} {1} дн.", report.DrawdownInfo.CurrentDrawdown - 1, report.DrawdownInfo.CurrentDrawdownDays);
        }

        void WriteHprs(List<DateSum> hprs)
        {
            Console.Write(TableHelper.Format(reportCulture, "Date:d,PnL:p1",
                hprs.Reverse<DateSum>().Select(x => new object[] { x.Date, x.Sum - 1 })));
        }

        static CultureInfo CreateCulture()
        {
            var ci = (CultureInfo)(new CultureInfo("ru-RU").Clone());
            ci.NumberFormat.NumberGroupSeparator = " ";
            ci.NumberFormat.PercentGroupSeparator = " ";
            return ci;
        }
    }
}
