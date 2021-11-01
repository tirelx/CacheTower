using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CacheTower
{
	/// <summary>
	/// An <see cref="IHashTableCacheStack"/> is the backbone for Cache Tower. It is the primary user-facing type for interacting with the cache.
	/// </summary>
	public interface IHashTableCacheStack
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
		/// Triggers the cleanup of any cache entries across the stack.
		/// </summary>
		/// <remarks>
		/// Some cache layers, like Redis, provide automatic cleanup of expired entries whereas other cache layers do not.
		/// <br/>
		/// This is used by <see cref="Extensions.AutoCleanupExtension"/> where cache layers are cleaned up on a timer.
		/// </remarks>
		/// <returns></returns>
		ValueTask CleanupAsync();
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
		ValueTask<CacheSetEntry<TValue?>?> GetHashAsync<TValue>(string hashTableKey); //todo TKey OR IEntity<T>?

		/// <summary>
		/// Caches <paramref name="cacheEntry"/> against the <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <param name="cacheEntry">The cache entry to store.</param>
		/// <returns></returns>
		ValueTask SetHashAsync<TValue>(string hashTableKey, CacheSetEntry<TValue?> cacheEntry);
		
		/// <summary>
		/// Set exparation time <paramref name="expiry"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="expiry"></param>
		/// <returns></returns>
		ValueTask SetHashExpiry<TValue>(string hashTableKey, DateTime expiry);
		#endregion
		
		#region hash tables subset operations

		/// <summary>
		///  Removes an entries with the corresponding <paramref name="hashTableKey"/> and <paramref name="elementKeys"/> from the cache layer.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKeys"></param>
		/// <typeparam name="TValue"></typeparam>
		/// <returns></returns>
		ValueTask EvictHashSubsetAsync<TValue>(string hashTableKey, ICollection<string> elementKeys);

		/// <summary>
		/// Caches subset of <see cref="TValue"/> against the <paramref name="hashTableKey"/>.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="subset"></param>
		/// <typeparam name="TValue"></typeparam>
		/// <returns></returns>
		ValueTask SetHashSubsetAsync<TValue>(string hashTableKey, ICollection<KeyValuePair<string, TValue?>> subset);

		/// <summary>
		/// Retrieves the <see cref="CacheEntry{T}"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <param name="elementKeys"></param>
		/// <returns>The existing cache entry or <c>null</c> if no entry is found.</returns>
		ValueTask<IReadOnlyDictionary<string, TValue?>> GetHashSubsetAsync<TValue>(string hashTableKey,
			ICollection<string> elementKeys); 
		
		#endregion
	}

	/// <remarks>
	/// An <see cref="IFlushableHashTableCacheStack"/> exposes an extra method to completely clear all the data from a cache stack.
	/// This is intentionally exposed as a separate interface in an attempt to prevent developers from inadvertently clearing all their cache data in production.
	/// </remarks>
	/// <inheritdoc/>
	public interface IFlushableHashTableCacheStack : IHashTableCacheStack
	{
		/// <summary>
		/// Flushes all cache layers, removing every item from the cache.
		/// </summary>
		/// <remarks>
		/// Warning: Do not call this unless you understand the gravity of clearing all cache layers entirely.
		/// <br/>
		/// Additionally, when using the `RedisRemoteEvictionExtension`, the flushing of caches is co-ordinated across all instances.
		/// </remarks>
		/// <returns></returns>
		ValueTask FlushAsync();
	}
}
