using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace CacheTower.Extensions
{
	internal class HashTableCacheExtensionContainer : IHashTableCacheChangeExtension, IAsyncDisposable
	{
		private bool Disposed;

		private bool HasCacheChangeExtensions { get; }
		private IHashTableCacheChangeExtension[] CacheChangeExtensions { get; }
		private IHashTableCacheExtension[] AllExtensions { get; }

		public HashTableCacheExtensionContainer(IHashTableCacheExtension[] extensions)
		{
			if (extensions is { Length: > 0 })
			{
				var cacheChangeExtensions = new List<IHashTableCacheChangeExtension>();

				foreach (var extension in extensions)
				{

					if (extension is IHashTableCacheChangeExtension cacheChangeExtension)
					{
						HasCacheChangeExtensions = true;
						cacheChangeExtensions.Add(cacheChangeExtension);
					}
				}

				CacheChangeExtensions = cacheChangeExtensions.ToArray();
				AllExtensions = extensions;
			}
			else
			{
				CacheChangeExtensions = Array.Empty<IHashTableCacheChangeExtension>();
				AllExtensions = CacheChangeExtensions;
			}
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Register(IHashTableCacheStack cacheStack)
		{
			foreach (var extension in AllExtensions)
			{
				extension.Register(cacheStack);
			}

		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public async ValueTask OnCacheUpdateAsync(string cacheKey, DateTime? expiry, CacheUpdateType cacheUpdateType)
		{
			if (HasCacheChangeExtensions)
			{
				foreach (var extension in CacheChangeExtensions)
				{
					await extension.OnCacheUpdateAsync(cacheKey, expiry, cacheUpdateType);
				}
			}
		}

		/// <summary>
		/// Triggers after a cache entry has been updated.
		/// </summary>
		/// <param name="hashTableKey">The cache key for the entry that was updated.</param>
		/// <param name="elementKey">The element key in hash table</param>
		/// <param name="expiry">The new expiry date for the cache entry.</param>
		/// <param name="updateType">The type of cache update that has occurred.</param>
		/// <returns></returns>
		public async ValueTask OnHashUpdateElementAsync(string hashTableKey, string elementKey, DateTime? expiry,
			CacheUpdateType updateType)
		{
			if (HasCacheChangeExtensions)
			{
				foreach (var extension in CacheChangeExtensions)
				{
					await extension.OnHashUpdateElementAsync(hashTableKey, elementKey, expiry, updateType);
				}
			}
		}

		/// <summary>
		/// Triggers after a cache entry has been updated.
		/// </summary>
		/// <param name="hashTableKey">The cache key for the entry that was updated.</param>
		/// <param name="elementKeys">The element key in hash table</param>
		/// <param name="expiry">The new expiry date for the cache entry.</param>
		/// <param name="updateType">The type of cache update that has occurred.</param>
		/// <returns></returns>
		public async ValueTask OnHashSubsetUpdateAsync(string hashTableKey, ICollection<string> elementKeys, DateTime? expiry,
			CacheUpdateType updateType)
		{
			if (HasCacheChangeExtensions)
			{
				foreach (var extension in CacheChangeExtensions)
				{
					await extension.OnHashSubsetUpdateAsync(hashTableKey, elementKeys, expiry, updateType);
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public async ValueTask OnCacheEvictionAsync(string cacheKey)
		{
			if (HasCacheChangeExtensions)
			{
				foreach (var extension in CacheChangeExtensions)
				{
					await extension.OnCacheEvictionAsync(cacheKey);
				}
			}
		}

		/// <summary>
		/// Triggers after a cache entry has been evicted.
		/// </summary>
		/// <param name="hashTableKey">The cache key for the entry that was evicted.</param>
		/// <param name="elementKey">The element key in hash table</param>
		/// <returns></returns>
		public async ValueTask OnHashElementEvictionAsync(string hashTableKey, string elementKey)
		{
			if (HasCacheChangeExtensions)
			{
				foreach (var extension in CacheChangeExtensions)
				{
					await extension.OnHashElementEvictionAsync(hashTableKey, elementKey);
				}
			}
		}

		/// <summary>
		/// Triggers after a cache entry has been evicted.
		/// </summary>
		/// <param name="hashTableKey">The cache key for the entry that was evicted.</param>
		/// <param name="elementKeys">The element key in hash table</param>
		/// <returns></returns>
		public async ValueTask OnHashSubsetEvictionAsync(string hashTableKey, ICollection<string> elementKeys)
		{
			if (HasCacheChangeExtensions)
			{
				foreach (var extension in CacheChangeExtensions)
				{
					await extension.OnHashSubsetEvictionAsync(hashTableKey, elementKeys);
				}
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public async ValueTask OnCacheFlushAsync()
		{
			if (HasCacheChangeExtensions)
			{
				foreach (var extension in CacheChangeExtensions)
				{
					await extension.OnCacheFlushAsync();
				}
			}
		}

		public async ValueTask DisposeAsync()
		{
			if (Disposed)
			{
				return;
			}
			
			foreach (var extension in AllExtensions)
			{
				if (extension is IDisposable disposable)
				{
					disposable.Dispose();
				}
				else if (extension is IAsyncDisposable asyncDisposable)
				{
					await asyncDisposable.DisposeAsync();
				}
			}

			Disposed = true;
		}
	}
}
