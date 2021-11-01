using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CacheTower
{
	/// <summary>
	/// Cache layers represent hashtables with individual types of caching solutions including in-memory, file-based and Redis.
	/// It is with cache layers that items are set, retrieved or evicted from the cache.
	/// </summary>
	public interface IHashTableCacheLayer
	{
		#region one element operations
		/// <summary>
		/// Retrieves the <see cref="TValue"/> for a given <paramref name="hashTableKey"/> and <paramref name="elementKey"/>
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKey"></param>
		/// <typeparam name="TValue"></typeparam>
		/// <returns></returns>
		ValueTask<TValue?> GetValueAsync<TValue>(string hashTableKey, string elementKey);
		
		/// <summary>
		/// Caches <paramref name="value"/> against the <paramref name="hashTableKey"/> and <paramref name="elementKey"/>
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKey"></param>
		/// <param name="value"></param>
		/// <typeparam name="TValue"></typeparam>
		/// <returns></returns>
		ValueTask SetValueAsync<TValue>(string hashTableKey, string elementKey, TValue value);
		
		/// <summary>
		/// Removes an entry with the corresponding <paramref name="hashTableKey"/> and <paramref name="elementKey"/> from the cache layer.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKey"></param>
		/// <returns></returns>
		ValueTask EvictValueAsync(string hashTableKey, string elementKey);
		#endregion

		#region all cache layer operations
		/// <summary>
		/// Flushes the cache layer, removing every item from the cache.
		/// </summary>
		/// <returns></returns>
		ValueTask FlushAsync();
		
		/// <summary>
		/// Triggers the cleanup of any cache entries that are expired.
		/// </summary>
		/// <returns></returns>
		ValueTask CleanupAsync();

		/// <summary>
		/// Retrieves the current availability status of the cache layer.
		/// This is used by <see cref="CacheStack"/> to determine whether a value can even be cached at that moment in time.
		/// </summary>
		/// <returns></returns>
		ValueTask<bool> IsAvailableAsync();
		#endregion

		#region hash tables operations
		/// <summary>
		/// Removes an entry with the corresponding <paramref name="hashTableKey"/> from the cache layer.
		/// </summary>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <returns></returns>
		ValueTask EvictHashAsync(string hashTableKey);

		/// <summary>
		/// Retrieves the <see cref="CacheEntry{T}"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <returns>The existing cache entry or <c>null</c> if no entry is found.</returns>
		ValueTask<CacheSetEntry<TValue?>?> GetHashAsync<TValue>(string hashTableKey);

		/// <summary>
		/// Caches <paramref name="cacheEntry"/> against the <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <param name="cacheEntry">The cache entry to store.</param>
		/// <returns></returns>
		ValueTask SetHashAsync<TValue>(string hashTableKey, CacheSetEntry<TValue?> cacheEntry);
		#endregion

		#region hash tables subset operations

		/// <summary>
		///  Removes an entries with the corresponding <paramref name="hashTableKey"/> and <paramref name="elementKeys"/> from the cache layer.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKeys"></param>
		/// <returns></returns>
		ValueTask EvictHashSubsetAsync(string hashTableKey, IEnumerable<string> elementKeys);

		/// <summary>
		/// Caches subset of <see cref="TValue"/> against the <paramref name="hashTableKey"/>.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="subset"></param>
		/// <typeparam name="TValue"></typeparam>
		/// <returns></returns>
		ValueTask SetHashSubsetAsync<TValue>(string hashTableKey, IEnumerable<KeyValuePair<string, TValue?>> subset);

		/// <summary>
		/// Retrieves the <see cref="CacheEntry{T}"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <param name="elementKeys"></param>
		/// <returns>The existing cache entry or <c>null</c> if no entry is found.</returns>
		ValueTask<IReadOnlyDictionary<string, TValue?>?> GetHashSubsetAsync<TValue>(string hashTableKey,
			IEnumerable<string> elementKeys);

		/// <summary>
		/// Set exparation time <paramref name="expiry"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="expiry"></param>
		/// <returns></returns>
		ValueTask SetHashExpiry<TValue>(string hashTableKey, DateTime expiry);

		#endregion
	}
}