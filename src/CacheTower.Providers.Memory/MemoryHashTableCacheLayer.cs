using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace CacheTower.Providers.Memory
{
	/// <summary>
	/// In Memory cache based on <see cref="IMemoryCache"/>
	/// </summary>
	public class MemoryHashTableCacheLayer : IHashTableCacheLayer
	{
		private readonly MemoryCacheEntryOptions EntryOptions;
		private readonly MemoryCacheOptions CacheOptions;
		private MemoryCache MemoryCache;

		/// <summary>Initializes a new instance of the <see cref="T:System.Object"></see> class.</summary>
		public MemoryHashTableCacheLayer(MemoryCacheEntryOptions? entryOptions = null, MemoryCacheOptions? cacheOptions = null)
		{
			EntryOptions = entryOptions ?? new MemoryCacheEntryOptions();
			CacheOptions = cacheOptions ?? new MemoryCacheOptions();
			MemoryCache = CreateMemoryCache();
		}

		private MemoryCache CreateMemoryCache()
		{
			return new MemoryCache(CacheOptions);
		}

		/// <inheritdoc />
		public ValueTask<TValue?> GetValueAsync<TValue>(string hashTableKey, string elementKey)
		{
			if (MemoryCache.TryGetValue(hashTableKey, out var cache) && cache is CacheSetEntry<TValue?> { Values: { } } cacheSetEntry)
			{
				if (cacheSetEntry.Values.TryGetValue(elementKey, out var res))
				{
					return new ValueTask<TValue?>(res);
				}
			}

			return default;
		}

		/// <inheritdoc />
		public ValueTask SetValueAsync<TValue>(string hashTableKey, string elementKey, TValue value)
		{
			var cache = MemoryCache.GetOrCreate(hashTableKey,
				entry =>
				{
					entry.SetOptions(EntryOptions);
					return new CacheSetEntry<TValue>(new ConcurrentDictionary<string, TValue>());
				});
			if (cache.Values is ConcurrentDictionary<string, TValue?> dictionary)
			{
				dictionary.AddOrUpdate(elementKey, value, (_, _) => value);
			}
			return default;
		}

		/// <inheritdoc />
		public ValueTask EvictValueAsync(string hashTableKey,  string elementKey)
		{
			if (MemoryCache.TryGetValue(hashTableKey, out var cache) && cache is DeletableCacheSetEntry deletableCacheSetEntry)
			{
				deletableCacheSetEntry.TryRemove(elementKey);
			}

			return default;
		}

		/// <summary>
		/// Flushes the cache layer, removing every item from the cache.
		/// </summary>
		/// <returns></returns>
		public ValueTask FlushAsync()
		{
			MemoryCache.Dispose();
			MemoryCache = CreateMemoryCache();
			return default;
		}

		/// <summary>
		/// Triggers the cleanup of any cache entries that are expired.
		/// </summary>
		/// <returns></returns>
		public ValueTask CleanupAsync()
		{
			MemoryCache.Compact(0);
			return default;
		}

		/// <summary>
		/// Removes an entry with the corresponding <paramref name="hashTableKey"/> from the cache layer.
		/// </summary>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <returns></returns>
		public ValueTask EvictHashAsync(string hashTableKey)
		{
			MemoryCache.Remove(hashTableKey);
			return new ValueTask();
		}

		/// <summary>
		/// Retrieves the <see cref="CacheSetEntry{T}"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <returns>The existing cache entry or <c>null</c> if no entry is found.</returns>
		public ValueTask<CacheSetEntry<TValue?>?> GetHashAsync<TValue>(string hashTableKey)
		{
			if (MemoryCache.TryGetValue(hashTableKey, out var cache))
			{
				return new ValueTask<CacheSetEntry<TValue?>?>((CacheSetEntry<TValue?>?)cache);
			}

			return default;
		}

		/// <summary>
		/// Caches <paramref name="cacheEntry"/> against the <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <param name="cacheEntry">The cache entry to store.</param>
		/// <returns></returns>
		public ValueTask SetHashAsync<TValue>(string hashTableKey, CacheSetEntry<TValue?> cacheEntry)
		{
			if (cacheEntry.Expiry.HasValue)
			{
				MemoryCache.Set(hashTableKey, cacheEntry, cacheEntry.Expiry.Value);
			}
			else
			{
				MemoryCache.Set(hashTableKey, cacheEntry, EntryOptions);
			}
			return new ValueTask();
		}

		/// <summary>
		///  Removes an entries with the corresponding <paramref name="hashTableKey"/> and <paramref name="elementKeys"/> from the cache layer.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKeys"></param>
		/// <returns></returns>
		public ValueTask EvictHashSubsetAsync(string hashTableKey, IEnumerable<string> elementKeys)
		{
			if (MemoryCache.TryGetValue(hashTableKey, out var cache) && cache is DeletableCacheSetEntry deletableCacheSetEntry)
			{
				deletableCacheSetEntry.TryRemove(elementKeys);
			}

			return default;
		}

		/// <summary>
		/// Caches subset of {TValue} gainst the <paramref name="hashTableKey"/>.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="subset"></param>
		/// <typeparam name="TValue"></typeparam>
		/// <returns></returns>
		public ValueTask SetHashSubsetAsync<TValue>(string hashTableKey, IEnumerable<KeyValuePair<string, TValue?>> subset)
		{
			var cache = MemoryCache.GetOrCreate(hashTableKey,
				entry =>
				{
					entry.SetOptions(EntryOptions);
					return new CacheSetEntry<TValue?>(new ConcurrentDictionary<string, TValue?>());
				});
			if (cache.Values is ConcurrentDictionary<string, TValue?> dictionary)
			{
				foreach (var pair in subset)
				{
					dictionary.AddOrUpdate(pair.Key, pair.Value, (_, _) => pair.Value);
				}
			}
			return default;
		}

		/// <summary>
		/// Retrieves the <see cref="CacheEntry{T}"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <param name="elementKeys"></param>
		/// <returns>The existing cache entry or <c>null</c> if no entry is found.</returns>
		public ValueTask<IReadOnlyDictionary<string, TValue?>?> GetHashSubsetAsync<TValue>(string hashTableKey,
			IEnumerable<string> elementKeys)
		{
			if (MemoryCache.TryGetValue(hashTableKey, out var cache) && cache is CacheSetEntry<TValue?> { Values: { } } cacheSetEntry)
			{
				var readOnlyDictionary = cacheSetEntry.Values;
				var subset = new ConcurrentDictionary<string, TValue?>();
				foreach (var elementKey in elementKeys)
				{
					if (readOnlyDictionary.TryGetValue(elementKey, out var element))
					{
						subset.AddOrUpdate(elementKey, element, (_, _) => element);
					}
				}
				return new ValueTask<IReadOnlyDictionary<string, TValue?>?>(subset);
			}

			return default;
		}

		/// <summary>
		/// Set exparation time <paramref name="expiry"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="expiry"></param>
		/// <returns></returns>
		public async ValueTask SetHashExpiry<TValue>(string hashTableKey, DateTime expiry)
		{
			if (MemoryCache.TryGetValue(hashTableKey, out var cache) && cache is CacheSetEntry<TValue?> { Values: { } } cacheSetEntry)
			{
				await SetHashAsync(hashTableKey, new CacheSetEntry<TValue?>(cacheSetEntry.Values, expiry));
			}
		}

		/// <summary>
		/// Retrieves the current availability status of the cache layer.
		/// This is used by <see cref="CacheStack"/> to determine whether a value can even be cached at that moment in time.
		/// </summary>
		/// <returns></returns>
		public ValueTask<bool> IsAvailableAsync()
		{
			return new ValueTask<bool>(true);
		}
	}
}