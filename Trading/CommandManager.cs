using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Trading.Advisor;
using Trading.Core;

namespace Trading
{
    class CommandManager
    {
        class Command
        {
            public string Name;
            public string Description;
            public Action<CommandArgs> Handler;
        }

        class CommandArgs
        {
            public string CommandLine;
            public string CommandName;
            public Dictionary<string, string> Parameters;
        }

        static Serilog.ILogger logger = Serilog.Log.ForContext<CommandManager>();
        Dictionary<string, Command> commands;

        public CommandManager(IStrategyService strategyService,
                               ITraderService traderService,
                               AdvisorReportService advisorReportService)
        {
            this.commands = new Command[] {
                new Command() {
                    Name = "terminal",
                    Description = "Запускает терминал",
                    Handler = args => traderService.Terminal()
                },
                new Command() {
                    Name = "status",
                    Description = "Показывает текущий статус",
                    Handler = args => traderService.ShowStatus()
                },
                new Command() {
                    Name = "start",
                    Description = "Запускает стратегию",
                    Handler = args => strategyService.Start()
                },
                new Command() {
                    Name = "stop",
                    Description = "Останавливает стратегию",
                    Handler = args => strategyService.Stop()
                },
                new Command() {
                    Name = "report",
                    Description = "Тестирует стратегию на истории",
                    Handler = args => advisorReportService.Report()
                },
				new Command() {
					Name = "test",
					Description = "Обновляет и тестирует текущий контракт на истории",
                    Handler = args => advisorReportService.Monitoring()
				},
                new Command() {
                    Name = "download",
                    Description = "Скачивает последние исторические бары с finam или mfd",
                    Handler = args => advisorReportService.UpdateHistoryData()
                },
                new Command() {
                    Name = "help",
                    Description = "Показывает справку",
                    Handler = HelpCommand
                },
            }.ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
        }

        void HelpCommand(CommandArgs args)
        {
            foreach (var cmd in commands.Values)
            {
                Console.WriteLine("{0} - {1}", cmd.Name, cmd.Description);
            }
        }

        public void Run()
        {
            while (true)
            {
                string commandLine = Console.ReadLine();
                if (commandLine == null)
                    return;
                if (commandLine == "quit")
                    return;
                ExecuteCommand(commandLine);
            }
        }

        void ExecuteCommand(string commandLine)
        {
            CommandArgs commandArgs;
            try
            {
                commandArgs = ParseCommand(commandLine);
            }
            catch (Exception)
            {
                Console.WriteLine("Bad command format");
                return;
            }

            Command command;
            if (!commands.TryGetValue(commandArgs.CommandName, out command))
            {
                Console.WriteLine("Command not found");
                return;
            }

            try
            {
                command.Handler(commandArgs);
            }
            catch (Exception e)
            {
                logger
                    .ForContext("CommandLine", commandLine)
                    .Error(e, "ExecuteCommand error");
            }
        }

        static CommandArgs ParseCommand(string commandLine)
        {
            var xeCommand = XElement.Parse("<" + commandLine + "/>");
            return new CommandArgs()
            {
                CommandLine = commandLine,
                CommandName = xeCommand.Name.LocalName,
                Parameters = xeCommand.Attributes()
                    .ToDictionary(x => x.Name.LocalName, x => x.Value, StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
