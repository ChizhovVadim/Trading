using System;
using System.Threading;
using System.Diagnostics;
using System.Linq;
using System.IO;
using ManagedWinapi.Windows;
using ManagedWinapi.Windows.Contents;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LuaTrader
{
	static class Ext
	{
		const uint SETTEXT = 0x000C;
		const uint COMMAND = 0x0111;

		[DllImport("user32.dll")]
		public static extern int SendMessage(this IntPtr hWnd, int wMsg, int wParam, [MarshalAs(UnmanagedType.LPStr)]string lParam);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern int SendMessage(this IntPtr hWnd, int wMsg, IntPtr wParam, IntPtr lParam);

		public static void SetText(this SystemWindow wnd, string text)
		{
			wnd.HWnd.SendMessage((int)SETTEXT, 0, text);
		}

		public static void Command(this SystemWindow wnd, SystemWindow elem)
		{
			wnd.HWnd.SendMessage((int)COMMAND, new IntPtr(elem.DialogID), IntPtr.Zero);
		}
	}
	
	class QuikTerminal
	{
		Serilog.ILogger logger = Serilog.Log.ForContext<QuikTerminal>();

		private const string _info = "info";
		private const string _infoExe = _info + ".exe";

		private static readonly string[] _loginWndTitles = new[]
		{
			"Идентификация пользователя",
			"Установка сетевого соединения",
			"Установка сетевого соединения (SSL-PRO)",
			"Двухфакторная аутентификация"
		};

		Client settings;

		public QuikTerminal (Client settings)
		{
			if (!Path.HasExtension (settings.Path)) {
				settings.Path = Path.Combine (settings.Path, _infoExe);
			}
			this.settings = settings;
		}

		public void RunTerminal()
		{
			var process = GetExistingQuik ();
			if (process != null) {
				logger.Information ("Найден запущенный процесс Quik.");
			} else	{
				logger.Information ("Запускаем Quik...");
				process = StartNewQuik ();
				logger.Information ("Quik запущен.");
			}
			if (!String.IsNullOrEmpty (settings.Login) &&
				!String.IsNullOrEmpty (settings.Password)) {
				Login (process, settings.Login, settings.Password);
			}
		}

		Process StartNewQuik()
		{
			var processStartInfo = new ProcessStartInfo { FileName = settings.Path };
			processStartInfo.Verb = "runas";
			processStartInfo.UseShellExecute = true;
			processStartInfo.WorkingDirectory = Path.GetDirectoryName(settings.Path);

			var process = Process.Start(processStartInfo);
			WaitFor(() => GetLoginWindows(GetQuikWindows(process)).FirstOrDefault()==null, "Запуск терминала", 360);
			return process;
		}

		void Login(Process process, string login, string password)
		{
			var wnd = GetLoginWindows(GetQuikWindows(process)).FirstOrDefault();
			if (wnd == null) {
				logger.Information ("Окно авторизации Quik не найдено.");
				return;
			}

			var loginCtrl = wnd.AllChildWindows.First(w => w.DialogID == 0x2775);
			var passwordCtrl = wnd.AllChildWindows.First(w => w.DialogID == 0x2776);

			loginCtrl.SetText(login);
			passwordCtrl.SetText(password);

			CloseOk(wnd);
			logger.Information ("Авторизация произведена.");
		}

		Process GetExistingQuik()
		{
			var processes = Process.GetProcessesByName (_info);
			var process = processes.FirstOrDefault(p => p.MainModule.FileName.Equals(settings.Path, StringComparison.OrdinalIgnoreCase));
			return process;
		}

		IEnumerable<SystemWindow> GetQuikWindows(Process process)
		{
			return SystemWindow.FilterToplevelWindows(wnd => wnd.Process.Id == process.Id);
		}

		IEnumerable<SystemWindow> GetLoginWindows(IEnumerable<SystemWindow> quikWindows)
		{
			return quikWindows.Where(q => _loginWndTitles.Any(t => q.Title.Contains(t))).ToArray();
		}

		void CloseOk(SystemWindow window)
		{
			CloseWindow(window, 1);
		}

		void CloseWindow(SystemWindow window, int id)
		{
			var btn = window.AllChildWindows.First(w => w.DialogID == id);
			window.Command(btn);
		}

		void WaitFor(Func<bool> condition, string action, int interval = 60)
		{
			var now = DateTime.Now;

			while (condition())
			{
				Thread.Sleep(5);

				if ((DateTime.Now - now) > TimeSpan.FromSeconds(interval))
					throw new TimeoutException($"Действие '{action}' с окном Quik превысило допустимое время.");
			}
		}
	}
}
