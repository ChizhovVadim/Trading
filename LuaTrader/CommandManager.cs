using System;
using System.Collections.Generic;
using System.Linq;

namespace LuaTrader
{
	class Command
	{
		public string Name;
		public string Description;
		public Action Handler;
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
				var cmd = commands.FirstOrDefault (x => x.Name == commandLine);
				if (cmd == null) {
					Console.WriteLine ("Command not found");
					continue;
				}
				try {
					cmd.Handler ();
				} catch (Exception e) {
					logger
						.ForContext ("CommandLine", cmd)
						.Error (e, "ExecuteCommand error");
				}
			}
		}
	}
}
