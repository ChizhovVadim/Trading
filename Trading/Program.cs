using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Practices.Unity;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using Trading.Advisor;
using Trading.Core;
using Trading.QuikLuaTrader;

namespace Trading
{
	class AppFlags
	{
		public string ClientKey;
		public bool AutoStart;
	}

	class MainClass
	{
		public static void Main(string[] args)
		{
			var appFlags = ParseAppFlags();
			var appSettings = Serializer.Load<StrategySettings>("StrategySettings.config");

			Client client;
			int clientIndex;
			if (appSettings.Clients == null || appSettings.Clients.Count == 0)
			{
				Console.WriteLine("Clients not found.");
				return;
			}
			if (appSettings.Clients.Count == 1)
			{
				client = appSettings.Clients[0];
				clientIndex = 0;
			}
			else
			{
				if (String.IsNullOrEmpty(appFlags.ClientKey))
				{
					Console.Write("Enter client: ");
					appFlags.ClientKey = Console.ReadLine();
				}
				GetCurrentClient(appSettings.Clients, appFlags.ClientKey, out client, out clientIndex);
				if (client == null)
				{
					Console.WriteLine("Client '{0}' not found.", appFlags.ClientKey);
					return;
				}
			}

			Console.Title = Console.Title + "-" + client.Key;

			Serilog.Log.Logger = new LoggerConfiguration()
				.MinimumLevel.Debug()
				.WriteTo.Console(
				restrictedToMinimumLevel: LogEventLevel.Information,
				outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
			)
				.WriteTo.RollingFile(new JsonFormatter(), appSettings.LogPath.Replace("{ClientKey}", client.Key))
				.CreateLogger();

			var logger = Serilog.Log.ForContext<MainClass>();

			try
			{
				SetCulture();
				logger.Information("Application started.");
				LogEnvironment(logger);
				logger.Information("Client {Client}", client.Key);
				MailboxProcessor.OnError += ex => logger.Error(ex, "Mailbox.OnError");

				using (var container = new UnityContainer())
				{
					container.RegisterInstance(appSettings.StrategyConfigs);
					container.RegisterInstance(client);

					container.RegisterType<HistoryDataService>(new ContainerControlledLifetimeManager(), new InjectionFactory(c =>
						new HistoryDataService(
							new HistoryCandleRepository(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "TradingData", "Forts")),
							new HistoryCandleProvider(appSettings.HistoryCandleProviderSettings)
						)
					));

					container.RegisterType<ITraderService, QuikLuaTraderService>(new ContainerControlledLifetimeManager());
					container.RegisterType<ICandleService, QuikLuaTraderService>(new ContainerControlledLifetimeManager());

					container.RegisterType<IStrategyService, StrategyService>(
						new ContainerControlledLifetimeManager(),
						new InjectionFactory(c =>
							new StrategyService(appSettings.AdvisorInfo.IsLive, client, c.Resolve<IAdvisorService>(), c.Resolve<ITraderService>())
						)
					);

					if (appSettings.AdvisorInfo.IsLocal)
					{
						container.RegisterType<IAdvisorService, AdvisorService>(new ContainerControlledLifetimeManager());
					}
					else
					{
						container.RegisterType<IAdvisorService>(
							new ContainerControlledLifetimeManager(),
							new InjectionFactory(c =>
								new RestAdvisorService(client.PublishCandles, new AdvisorClient(appSettings.AdvisorInfo.Url), c.Resolve<ICandleService>())
							)
						);
					}

					var commandManager = container.Resolve<CommandManager>();
					commandManager.Run();
				}

				logger.Information("Application finished.");

			}
			catch (Exception e)
			{
				logger.Fatal(e, "Fatal error");
				throw;
			}
			finally
			{
				Serilog.Log.CloseAndFlush();
			}
		}

		static AppFlags ParseAppFlags()
		{
			var result = new AppFlags();
			string[] args = Environment.GetCommandLineArgs();
			for (int i = 1; i < args.Length; i++)
			{
				switch (args[i])
				{
					case "-client":
						result.ClientKey = args[i + 1];
						i++;
						break;
					case "-start":
						result.AutoStart = true;
						break;
				}
			}
			return result;
		}

		static void GetCurrentClient(List<Client> clients, string clientKey, out Client client, out int clientIndex)
		{
			client = null;
			clientIndex = -1;
			for (int i = 0; i < clients.Count; i++)
			{
				if (clients[i].Key == clientKey)
				{
					client = clients[i];
					clientIndex = i;
					return;
				}
			}
		}

		static void SetCulture()
		{
			//Stocksharp requires ru culture to parse candle datetime?
		}

		static void LogEnvironment(ILogger logger)
		{
			var assembly = typeof(MainClass).Assembly;
			Version version = assembly.GetName().Version;
			var buildDate = new DateTime(2000, 1, 1).AddDays(version.Build).AddSeconds(version.Revision * 2);
			logger.Information("Build {BuildDate:s}", buildDate);
		}
	}
}
