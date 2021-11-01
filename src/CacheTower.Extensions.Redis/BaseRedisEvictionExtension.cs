using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Redis;
#pragma warning disable 1591 //todo summary

namespace CacheTower.Extensions.Redis
{
	public abstract class BaseRedisEvictionExtension
	{
		protected ISubscriber Subscriber { get; }
		protected string FlushChannel { get; }
		protected string EvictionChannel { get; }

		protected bool IsRegistered { get; set; }

		protected readonly object LockObj = new();
		protected HashSet<string> FlaggedEvictions { get; }
		protected bool HasFlushTriggered { get; set; }
		
		/// <summary>
		/// Creates a new instance of <see cref="RedisRemoteEvictionExtension"/>.
		/// </summary>
		/// <param name="connection">The primary connection to the Redis instance where the messages will be broadcast and received through.</param>
		/// <param name="channelPrefix">The channel prefix to use for the Redis communication.</param>
		protected BaseRedisEvictionExtension(IConnectionMultiplexer connection, string channelPrefix = "CacheTower")
		{
			if (connection == null)
			{
				throw new ArgumentNullException(nameof(connection));
			}

			if (channelPrefix == null)
			{
				throw new ArgumentNullException(nameof(channelPrefix));
			}

			Subscriber = connection.GetSubscriber();
			FlushChannel = $"{channelPrefix}.RemoteFlush";
			EvictionChannel = $"{channelPrefix}.RemoteEviction";
			FlaggedEvictions = new HashSet<string>(StringComparer.Ordinal);
		}
		
		/// <remarks>
		/// This will broadcast to Redis that the cache entry belonging to <paramref name="cacheKey"/> is now out-of-date and should be evicted.
		/// </remarks>
		public ValueTask OnCacheUpdateAsync(string cacheKey, DateTime? expiry, CacheUpdateType cacheUpdateType)
		{
			if (cacheUpdateType == CacheUpdateType.AddOrUpdateEntry)
			{
				return FlagEvictionAsync(cacheKey);
			}
			return default;
		}
		/// <remarks>
		/// This will broadcast to Redis that the cache entry belonging to <paramref name="cacheKey"/> is to be evicted.
		/// </remarks>
		public ValueTask OnCacheEvictionAsync(string cacheKey)
		{
			return FlagEvictionAsync(cacheKey);
		}

		private async ValueTask FlagEvictionAsync(string cacheKey)
		{
			lock (LockObj)
			{
				FlaggedEvictions.Add(cacheKey);
			}

			await Subscriber.PublishAsync(EvictionChannel, cacheKey, CommandFlags.FireAndForget);
		}

		/// <remarks>
		/// This will broadcast to Redis that the cache should be flushed.
		/// </remarks>
		public async ValueTask OnCacheFlushAsync()
		{
			lock (LockObj)
			{
				HasFlushTriggered = true;
			}

			await Subscriber.PublishAsync(FlushChannel, RedisValue.EmptyString, CommandFlags.FireAndForget);
		}
	}
}