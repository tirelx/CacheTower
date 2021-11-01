using System;
using System.Threading;
using System.Threading.Tasks;

namespace CacheTower.Extensions
{
	/// <summary>
	/// A basic delay-based cleanup extension for removing expired entries from cache layers.
	/// </summary>
	/// <remarks>
	/// Not all cache layers manage their own cleanup of expired entries.
	/// This calls <see cref="ICacheStack.CleanupAsync"/> which triggers the cleanup on each layer.
	/// </remarks>
	public class AutoCleanupExtension : ICacheExtension, IAsyncDisposable
	{
		/// <summary>
		/// The frequency at which an automatic cleanup is performed.
		/// </summary>
		public TimeSpan Frequency { get; }

		private Task? BackgroundTask { get; set; }

		private CancellationTokenSource TokenSource { get; }

		/// <summary>
		/// Creates a new <see cref="AutoCleanupExtension"/> with the given <paramref name="frequency"/>.
		/// </summary>
		/// <param name="frequency">The frequency at which an automatic cleanup is performed.</param>
		/// <param name="cancellationToken">Optional cancellation token to end automatic cleanups.</param>
		public AutoCleanupExtension(TimeSpan frequency, CancellationToken cancellationToken = default)
		{
			if (frequency <= TimeSpan.Zero)
			{
				throw new ArgumentOutOfRangeException(nameof(frequency), "Frequency must be greater than zero");
			}

			Frequency = frequency;
			TokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		}

		/// <inheritdoc/>
		public void Register(ICacheStack cacheStack)
		{
			if (BackgroundTask is not null)
			{
				throw new InvalidOperationException($"{nameof(AutoCleanupExtension)} can only be registered to one {nameof(ICacheStack)}");
			}

			BackgroundTask = BackgroundCleanup(cacheStack);
		}

		/// <summary>
		/// Registers the provided <paramref name="cacheStack"/> to the current cache extension.
		/// </summary>
		/// <param name="cacheStack">The cache stack you want to register.</param>
		public void Register(IHashTableCacheStack cacheStack)
		{
			if (BackgroundTask is not null)
			{
				throw new InvalidOperationException($"{nameof(AutoCleanupExtension)} can only be registered to one {nameof(ICacheStack)}");
			}

			BackgroundTask = BackgroundCleanup(cacheStack);
		}

		private async Task BackgroundCleanup(ICacheStack cacheStack)
		{
			try
			{
				var cancellationToken = TokenSource.Token;
				while (!cancellationToken.IsCancellationRequested)
				{
					await Task.Delay(Frequency, cancellationToken);
					cancellationToken.ThrowIfCancellationRequested();
					await cacheStack.CleanupAsync();
				}
			}
			catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
			{
			}
		}
		
		private async Task BackgroundCleanup(IHashTableCacheStack cacheStack)
		{
			try
			{
				var cancellationToken = TokenSource.Token;
				while (!cancellationToken.IsCancellationRequested)
				{
					await Task.Delay(Frequency, cancellationToken);
					cancellationToken.ThrowIfCancellationRequested();
					await cacheStack.CleanupAsync();
				}
			}
			catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
			{
			}
		}

		/// <summary>
		/// Cancels the automatic cleanup and releases all resources that were being used.
		/// </summary>
		/// <returns></returns>
		public async ValueTask DisposeAsync()
		{
			TokenSource.Cancel();

			if (BackgroundTask is not null)
			{
				await BackgroundTask;
			}
		}
	}
}
