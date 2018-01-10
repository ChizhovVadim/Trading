using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Trading.Core
{
	public class Dispatcher : IDisposable
	{
		BlockingCollection<Action> queue;
		CancellationTokenSource cts;

		public Dispatcher()
		{
			this.queue = new BlockingCollection<Action>();
			this.cts = new CancellationTokenSource();
			Task.Factory.StartNew(() =>
			{
				foreach (var action in queue.GetConsumingEnumerable())
				{
					if (cts.IsCancellationRequested)
					{
						break;
					}
					action();
				}
			}, TaskCreationOptions.LongRunning);
		}

		public void Dispose()
		{
			cts.Cancel();
		}

		public Task<T> InvokeAsync<T>(Func<T> f)
		{
			var tcs = new TaskCompletionSource<T>();
			queue.Add(() =>
			{
				try
				{
					var result = f();
					tcs.TrySetResult(result);
				}
				catch (Exception e)
				{
					tcs.TrySetException(e);
				}
			});
			return tcs.Task;
		}

		public Task<T> InvokeAsync<T>(Func<Task<T>> f)
		{
			return InvokeAsync(() => f().GetAwaiter().GetResult());
		}

		public T Invoke<T>(Func<T> f)
		{
			return InvokeAsync(f).GetAwaiter().GetResult();
		}

		public T Invoke<T>(Func<Task<T>> f)
		{
			return InvokeAsync(f).GetAwaiter().GetResult();
		}
	}
}
