using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CacheTower.Extensions;

namespace CacheTower
{
	/// <inheritdoc cref="CacheTower.IFlushableHashTableCacheStack" />
	public class HashTableCacheStack : IFlushableHashTableCacheStack, IAsyncDisposable
	{
		private bool Disposed;

		private IHashTableCacheLayer[] CacheLayers { get; }

		private HashTableCacheExtensionContainer Extensions { get; }

		/// <summary>
		/// Creates a new <see cref="CacheStack"/> with the given <paramref name="cacheLayers"/> and <paramref name="extensions"/>.
		/// </summary>
		/// <param name="cacheLayers">The cache layers to use for the current cache stack.
		/// The layers should be ordered from the highest priority to the lowest. At least one cache layer is required.
		/// </param>
		/// <param name="extensions">The cache extensions to use for the current cache stack.</param>
		public HashTableCacheStack(IHashTableCacheLayer[] cacheLayers, IHashTableCacheExtension[] extensions)
		{
			if (cacheLayers == null || cacheLayers.Length == 0)
			{
				throw new ArgumentException("There must be at least one cache layer", nameof(cacheLayers));
			}

			CacheLayers = cacheLayers;

			Extensions = new HashTableCacheExtensionContainer(extensions);
			Extensions.Register(this);
		}

		/// <summary>
		/// Helper for throwing if disposed.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void ThrowIfDisposed()
		{
			if (Disposed)
			{
				throw new ObjectDisposedException("CacheStack is disposed");
			}
		}

		/// <summary>
		/// Retrieves the <see cref="TValue"/> for a given <paramref name="hashTableKey"/> and <paramref name="elementKey"/>
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKey"></param>
		/// <typeparam name="TValue"></typeparam>
		/// <returns></returns>
		public async ValueTask<TValue?> GetValueAsync<TValue>(string hashTableKey, string elementKey)
		{
			ThrowIfDisposed();

			if (hashTableKey == null)
			{
				throw new ArgumentNullException(nameof(hashTableKey));
			}

			if (elementKey == null)
			{
				throw new ArgumentNullException(nameof(elementKey));
			}

			for (var layerIndex = 0; layerIndex < CacheLayers.Length; layerIndex++)
			{
				var layer = CacheLayers[layerIndex];
				if (await layer.IsAvailableAsync())
				{
					var result = await layer.GetValueAsync<TValue>(hashTableKey, elementKey);
					if (result is not null)
					{
						if (layerIndex > 0)
						{
							await BackPopulateValueCacheAsync(layerIndex, hashTableKey, elementKey, result);
						}
						return result;
					}
				}
			}

			return default;
		}

		/// <summary>
		/// Caches <paramref name="value"/> against the <paramref name="hashTableKey"/> and <paramref name="elementKey"/>
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKey"></param>
		/// <param name="value"></param>
		/// <typeparam name="TValue"></typeparam>
		/// <returns></returns>
		public async ValueTask SetValueAsync<TValue>(string hashTableKey, string elementKey, TValue value)
		{
			ThrowIfDisposed();

			if (hashTableKey == null)
			{
				throw new ArgumentNullException(nameof(hashTableKey));
			}

			if (elementKey == null)
			{
				throw new ArgumentNullException(nameof(elementKey));
			}

			for (int i = 0, l = CacheLayers.Length; i < l; i++)
			{
				var layer = CacheLayers[i];
				await layer.SetValueAsync(hashTableKey, elementKey, value);
			}

			await Extensions.OnHashUpdateElementAsync(hashTableKey, elementKey, null, CacheUpdateType.AddOrUpdateEntry);
		}

		/// <summary>
		/// Removes an entry with the corresponding <paramref name="hashTableKey"/> and <paramref name="elementKey"/> from the cache layer.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKey"></param>
		/// <returns></returns>
		public async ValueTask EvictValueAsync(string hashTableKey, string elementKey)
		{
			ThrowIfDisposed();

			if (hashTableKey == null)
			{
				throw new ArgumentNullException(nameof(hashTableKey));
			}

			if (elementKey == null)
			{
				throw new ArgumentNullException(nameof(elementKey));
			}

			for (int i = 0, l = CacheLayers.Length; i < l; i++)
			{
				var layer = CacheLayers[i];
				await layer.EvictValueAsync(hashTableKey, elementKey);
			}

			await Extensions.OnHashElementEvictionAsync(hashTableKey, elementKey);
		}

		/// <summary>
		/// Triggers the cleanup of any cache entries across the stack.
		/// </summary>
		/// <remarks>
		/// Some cache layers, like Redis, provide automatic cleanup of expired entries whereas other cache layers do not.
		/// <br/>
		/// This is used by <see cref="AutoCleanupExtension"/> where cache layers are cleaned up on a timer.
		/// </remarks>
		/// <returns></returns>
		public async ValueTask CleanupAsync()
		{
			ThrowIfDisposed();

			for (int i = 0, l = CacheLayers.Length; i < l; i++)
			{
				var layer = CacheLayers[i];
				await layer.CleanupAsync();
			}
		}

		/// <summary>
		/// Removes an entry with the corresponding <paramref name="hashTableKey"/> from the cache layer.
		/// </summary>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <returns></returns>
		public async ValueTask EvictHashAsync(string hashTableKey)
		{
			ThrowIfDisposed();

			if (hashTableKey == null)
			{
				throw new ArgumentNullException(nameof(hashTableKey));
			}

			for (int i = 0, l = CacheLayers.Length; i < l; i++)
			{
				var layer = CacheLayers[i];
				await layer.EvictHashAsync(hashTableKey);
			}

			await Extensions.OnCacheEvictionAsync(hashTableKey);
		}

		/// <summary>
		/// Retrieves the <see cref="CacheEntry{T}"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <returns>The existing cache entry or <c>null</c> if no entry is found.</returns>
		public async ValueTask<CacheSetEntry<TValue?>?> GetHashAsync<TValue>(string hashTableKey)
		{
			ThrowIfDisposed();

			if (hashTableKey == null)
			{
				throw new ArgumentNullException(nameof(hashTableKey));
			}

			var cacheSetEntry = await GetWithLayerIndexAsync<TValue?>(hashTableKey);
			if (cacheSetEntry.LayerIndex > 0)
			{
				await BackPopulateHashTableCacheAsync<TValue?>(cacheSetEntry.LayerIndex, hashTableKey,
					cacheSetEntry.CacheEntry);
			}

			return default;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private async ValueTask<(int LayerIndex, CacheSetEntry<TValue?> CacheEntry)> GetWithLayerIndexAsync<TValue>(
			string hashTableKey)
		{
			for (var layerIndex = 0; layerIndex < CacheLayers.Length; layerIndex++)
			{
				var layer = CacheLayers[layerIndex];
				if (await layer.IsAvailableAsync())
				{
					var cacheEntry = await layer.GetHashAsync<TValue>(hashTableKey);
					if (cacheEntry != default)
					{
						return (layerIndex, cacheEntry);
					}
				}
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
		public async ValueTask SetHashAsync<TValue>(string hashTableKey, CacheSetEntry<TValue?> cacheEntry)
		{
			ThrowIfDisposed();

			if (hashTableKey == null)
			{
				throw new ArgumentNullException(nameof(hashTableKey));
			}

			for (int i = 0, l = CacheLayers.Length; i < l; i++)
			{
				var layer = CacheLayers[i];
				await layer.SetHashAsync(hashTableKey, cacheEntry);
			}

			await Extensions.OnCacheUpdateAsync(hashTableKey, cacheEntry.Expiry, CacheUpdateType.AddOrUpdateEntry);
		}

		/// <summary>
		/// Set exparation time <paramref name="expiry"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="expiry"></param>
		/// <returns></returns>
		public async ValueTask SetHashExpiry<TValue>(string hashTableKey, DateTime expiry)
		{
			ThrowIfDisposed();

			if (hashTableKey == null)
			{
				throw new ArgumentNullException(nameof(hashTableKey));
			}

			for (int i = 0, l = CacheLayers.Length; i < l; i++)
			{
				var layer = CacheLayers[i];
				await layer.SetHashExpiry<TValue>(hashTableKey, expiry);
			}
		}

		/// <summary>
		///  Removes an entries with the corresponding <paramref name="hashTableKey"/> and <paramref name="elementKeys"/> from the cache layer.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKeys"></param>
		/// <typeparam name="TValue"></typeparam>
		/// <returns></returns>
		public async ValueTask EvictHashSubsetAsync<TValue>(string hashTableKey, ICollection<string> elementKeys)
		{
			ThrowIfDisposed();

			if (hashTableKey == null)
			{
				throw new ArgumentNullException(nameof(hashTableKey));
			}

			if (elementKeys == null)
			{
				throw new ArgumentNullException(nameof(elementKeys));
			}

			for (int i = 0, l = CacheLayers.Length; i < l; i++)
			{
				var layer = CacheLayers[i];
				await layer.EvictHashSubsetAsync(hashTableKey, elementKeys);
			}

			await Extensions.OnCacheEvictionAsync(hashTableKey);
		}

		/// <summary>
		/// Caches subset of <see cref="TValue"/> against the <paramref name="hashTableKey"/>.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="subset"></param>
		/// <typeparam name="TValue"></typeparam>
		/// <returns></returns>
		public async ValueTask SetHashSubsetAsync<TValue>(string hashTableKey,
			ICollection<KeyValuePair<string, TValue?>> subset)
		{
			ThrowIfDisposed();

			if (hashTableKey == null)
			{
				throw new ArgumentNullException(nameof(hashTableKey));
			}

			for (int i = 0, l = CacheLayers.Length; i < l; i++)
			{
				var layer = CacheLayers[i];
				await layer.SetHashSubsetAsync(hashTableKey, subset);
			}

			ICollection<string> elementKeys = subset is IDictionary<string, TValue?> dictionary
				? dictionary.Keys
				: subset.Select(x => x.Key).ToList();
			
			await Extensions.OnHashSubsetUpdateAsync(hashTableKey, elementKeys, null,
				CacheUpdateType.AddOrUpdateEntry);
		}

		/// <summary>
		/// Retrieves the <see cref="CacheEntry{T}"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <param name="elementKeys"></param>
		/// <returns>The existing cache entry or <c>null</c> if no entry is found.</returns>
		public async ValueTask<IReadOnlyDictionary<string, TValue?>> GetHashSubsetAsync<TValue>(string hashTableKey,
			ICollection<string> elementKeys)
		{
			ThrowIfDisposed();

			if (hashTableKey == null)
			{
				throw new ArgumentNullException(nameof(hashTableKey));
			}

			if (elementKeys == null)
			{
				throw new ArgumentNullException(nameof(elementKeys));
			}

			Dictionary<string, TValue?> resultDict = new(elementKeys.Count);
			IEnumerable<string>? remainingKeys = null;
			for (var layerIndex = 0; layerIndex < CacheLayers.Length; layerIndex++)
			{
				var layer = CacheLayers[layerIndex];
				if (await layer.IsAvailableAsync())
				{
					var oneLayerResult =
						await layer.GetHashSubsetAsync<TValue>(hashTableKey, remainingKeys ?? elementKeys);
					if (oneLayerResult is null)
					{
						continue;
					}

					if (layerIndex > 0)
					{
						await BackPopulateHashSubsetCacheAsync(layerIndex, hashTableKey, oneLayerResult);
					}

					if (oneLayerResult.Count == elementKeys.Count)
					{
						return oneLayerResult;
					}

					foreach (var value in oneLayerResult)
					{
						resultDict[value.Key] = value.Value;
					}

					if (resultDict.Count == elementKeys.Count)
					{
						return resultDict;
					}

					remainingKeys = (remainingKeys ?? elementKeys).Except(oneLayerResult.Keys);
				}
			}


			return resultDict;
		}

		/// <summary>
		/// Flushes all cache layers, removing every item from the cache.
		/// </summary>
		/// <remarks>
		/// Warning: Do not call this unless you understand the gravity of clearing all cache layers entirely.
		/// <br/>
		/// Additionally, when using the `RedisRemoteEvictionExtension`, the flushing of caches is co-ordinated across all instances.
		/// </remarks>
		/// <returns></returns>
		public async ValueTask FlushAsync()
		{
			ThrowIfDisposed();

			for (int i = 0, l = CacheLayers.Length; i < l; i++)
			{
				var layer = CacheLayers[i];
				await layer.FlushAsync();
			}

			await Extensions.OnCacheFlushAsync();
		}

		/// <inheritdoc />
		public async ValueTask DisposeAsync()
		{
			if (Disposed)
			{
				return;
			}

			foreach (var layer in CacheLayers)
			{
				// ReSharper disable once SuspiciousTypeConversion.Global
				if (layer is IDisposable disposableLayer)
				{
					disposableLayer.Dispose();
				}
				// ReSharper disable once SuspiciousTypeConversion.Global
				else if (layer is IAsyncDisposable asyncDisposableLayer)
				{
					await asyncDisposableLayer.DisposeAsync();
				}
			}

			await Extensions.DisposeAsync();

			Disposed = true;
		}

		private async ValueTask BackPopulateHashTableCacheAsync<TValue>(int fromIndexExclusive, string hashTableKey,
			CacheSetEntry<TValue?> cacheEntry)
		{
			ThrowIfDisposed();

			for (; --fromIndexExclusive >= 0;)
			{
				var previousLayer = CacheLayers[fromIndexExclusive];
				if (await previousLayer.IsAvailableAsync())
				{
					await previousLayer.SetHashAsync(hashTableKey, cacheEntry);
				}
			}
		}

		private async ValueTask BackPopulateHashSubsetCacheAsync<TValue>(int fromIndexExclusive, string hashTableKey,
			IReadOnlyDictionary<string, TValue?> hashSubset)
		{
			ThrowIfDisposed();

			for (; --fromIndexExclusive >= 0;)
			{
				var previousLayer = CacheLayers[fromIndexExclusive];
				if (await previousLayer.IsAvailableAsync())
				{
					await previousLayer.SetHashSubsetAsync(hashTableKey, hashSubset);
				}
			}
		}
		
		private async ValueTask BackPopulateValueCacheAsync<TValue>(int fromIndexExclusive, string hashTableKey,
			string elementKey, TValue? value)
		{
			ThrowIfDisposed();

			for (; --fromIndexExclusive >= 0;)
			{
				var previousLayer = CacheLayers[fromIndexExclusive];
				if (await previousLayer.IsAvailableAsync())
				{
					await previousLayer.SetValueAsync(hashTableKey, elementKey, value);
				}
			}
		}
		
	}
}