using System;
using System.Collections.Generic;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace LuaTrader
{
	class AppFlags
	{
		public string ClientKey;
		public bool AutoStart;
	}

	class MainClass
	{
		public static void Main (string[] args)
		{
			var appFlags = ParseAppFlags ();
			var appSettings = Serializer.Load<StrategySettings> ("StrategySettings.config");

			Client client;
			int clientIndex;
			if (appSettings.Clients == null || appSettings.Clients.Count == 0) {
				Console.WriteLine ("Clients not found.");
				return;
			}
			if (appSettings.Clients.Count == 1) {
				client = appSettings.Clients [0];
				clientIndex = 0;
			} else {
				if (String.IsNullOrEmpty (appFlags.ClientKey)) {
					Console.Write ("Enter client: ");
					appFlags.ClientKey = Console.ReadLine ();
				}
				GetCurrentClient (appSettings.Clients, appFlags.ClientKey, out client, out clientIndex);
				if (client == null) {
					Console.WriteLine ("Client '{0}' not found.", appFlags.ClientKey);
					return;
				}
			}

			Console.Title = Console.Title + "-" + client.Key;

			Serilog.Log.Logger = new LoggerConfiguration ()
				.MinimumLevel.Debug ()
				.WriteTo.Console (
				restrictedToMinimumLevel: LogEventLevel.Information,
				outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}"
			)
				.WriteTo.RollingFile (new JsonFormatter (), appSettings.LogPath.Replace ("{ClientKey}", client.Key))
				.CreateLogger ();

			var logger = Serilog.Log.Logger.ForContext<MainClass> ();
			StrategyManager strategyManager = null;

			try {
				SetCulture ();
				logger.Information ("Application started.");
				LogEnvironment (logger);
				logger.Information ("Client {Client}", client.Key);

				var advisor = new RestAdvisorService (new AdvisorClient (appSettings.AdvisorUrl));
				var trader = new QuikLuaTraderService (client);
				strategyManager = new StrategyManager (advisor, trader, trader, client);

				var commands = new List<Command> ();
				commands.Add (new Command () {
					Name = "terminal",
					Description = "Запускает терминал",
					Handler = trader.Terminal
				});
				commands.Add (new Command () {
					Name = "start",
					Description = "Запускает стратегию",
					Handler = strategyManager.Start
				});
				commands.Add (new Command () {
					Name = "stop",
					Description = "Останавливает стратегию",
					Handler = strategyManager.Stop
				});

				if (appFlags.AutoStart) {
					strategyManager.AutoStart();
				}

				CommandManager.Run (commands);

				logger.Information ("Application finished.");
			} catch (Exception e) {
				logger.Fatal (e, "Fatal error.");
				throw;
			} finally {
				if (strategyManager != null) {
					strategyManager.Dispose ();
				}
				Serilog.Log.CloseAndFlush ();
			}
		}

		static AppFlags ParseAppFlags ()
		{
			var result = new AppFlags ();
			string[] args = Environment.GetCommandLineArgs ();
			for (int i = 1; i < args.Length; i++) {
				switch (args [i]) {
				case "-client":
					result.ClientKey = args [i + 1];
					i++;
					break;
				case "-start":
					result.AutoStart = true;
					break;
				}
			}
			return result;
		}

		static void GetCurrentClient (List<Client> clients, string clientKey, out Client client, out int clientIndex)
		{
			client = null;
			clientIndex = -1;
			for (int i = 0; i < clients.Count; i++) {
				if (clients [i].Key == clientKey) {
					client = clients [i];
					clientIndex = i;
					return;
				}
			}
		}

		static void SetCulture ()
		{
			
		}

		static void LogEnvironment (ILogger logger)
		{
			var assembly = typeof(MainClass).Assembly;
			Version version = assembly.GetName ().Version;
			var buildDate = new DateTime (2000, 1, 1).AddDays (version.Build).AddSeconds (version.Revision * 2);
			logger.Information ("Build {BuildDate:s}", buildDate);
		}
	}
}
