using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Trading.Core
{
    public class Switch
	{
		object o;
		bool handled;

		private Switch (object o)
		{
			this.o = o;
		}

		public static Switch On (object o)
		{
			return new Switch (o);
		}

		public Switch Case<T> (Action<T> handler)
		{
			if (!handled) {
				if (o is T) {
					handler ((T)o);
					handled = true;
				}
			}
			return this;
		}

		public void Default (Action<object> handler)
		{
			if (!handled) {
				handler (o);
			}
		}
	}

	public class MailboxProcessor : IDisposable
	{
		public static event Action<Exception> OnError;

		Action<object> handler;
		CancellationTokenSource cts;
		BlockingCollection<object> queue;

		public static MailboxProcessor Start (Action<object> handler)
		{
			var agent = new MailboxProcessor (handler);
			agent.Run ();
			return agent;
		}

		public MailboxProcessor (Action<object> handler)
		{
			this.handler = handler;
			this.cts = new CancellationTokenSource ();
			this.queue = new BlockingCollection<object> ();
		}

		public void Dispose ()
		{
			cts.Cancel ();
		}

		public void Run ()
		{
			Task.Factory.StartNew (() => {
				foreach (var item in queue.GetConsumingEnumerable()) {
					if (cts.Token.IsCancellationRequested) {
						return;
					}
					try {
						handler (item);
					} catch (Exception e) {
						OnError?.Invoke (e);
						return;
					}
				}
			}, TaskCreationOptions.LongRunning);
		}

		public void Post (object message)
		{
			if (!cts.Token.IsCancellationRequested) {
				queue.Add (message);
			}
		}
	}
}
