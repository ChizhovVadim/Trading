using System;
using System.Collections.Generic;
using System.Linq;

namespace LuaTrader
{
	class Command
	{
		public string Name;
		public string Description;
		public Action<CommandArgs> Handler;
	}

	class CommandArgs
	{
		public string Name;
		public Dictionary<string, string> Params;
	}

	class CommandManager
	{
		static Serilog.ILogger logger = Serilog.Log.ForContext<CommandManager> ();

		public static void Run (List<Command> commands)
		{
			while (true) {
				string commandLine = Console.ReadLine ();
				if (commandLine == null)
					return;
				if (commandLine == "quit")
					return;
				var args = ParseCommandArgs (commandLine);
				var cmd = commands.FirstOrDefault (x => x.Name == args.Name);
				if (cmd == null) {
					Console.WriteLine ("Command not found");
					continue;
				}
				try {
					cmd.Handler (args);
				} catch (Exception e) {
					logger
						.ForContext ("CommandLine", commandLine)
						.Error (e, "ExecuteCommand error");
				}
			}
		}

		static CommandArgs ParseCommandArgs(string commandLine) {
			var tokens = commandLine.Split (' ');
			if (tokens.Length == 0) {
				return new CommandArgs ();
			}
			var param = new Dictionary<string, string>();
			for (var i = 1; i < tokens.Length-1; i++) {
				var token = tokens [i];
				if (token.StartsWith ("-")) {
					param [token.TrimStart('-')] = tokens [i + 1];
				}
			}
			return new CommandArgs () {
				Name = tokens[0],
				Params = param,
			};
		}
	}
}
