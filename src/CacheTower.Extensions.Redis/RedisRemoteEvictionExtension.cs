using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheTower.Extensions.Redis
{
	/// <summary>
	/// The <see cref="RedisRemoteEvictionExtension"/> broadcasts cache updates, evictions and flushes to Redis to allow for remote eviction of old cache data.
	/// When one of these events is received, it will perform that action locally to the configured cache layers.
	/// </summary>
	public class RedisRemoteEvictionExtension : BaseRedisEvictionExtension, ICacheChangeExtension
	{
		private ICacheLayer[] EvictFromLayers { get; }

		/// <summary>
		/// Creates a new instance of <see cref="RedisRemoteEvictionExtension"/>.
		/// </summary>
		/// <param name="connection">The primary connection to the Redis instance where the messages will be broadcast and received through.</param>
		/// <param name="evictFromLayers">The cache layers to either evict or flush when a message is received from Redis.</param>
		/// <param name="channelPrefix">The channel prefix to use for the Redis communication.</param>
		public RedisRemoteEvictionExtension(IConnectionMultiplexer connection, ICacheLayer[] evictFromLayers,
			string channelPrefix = "CacheTower") : base(connection, channelPrefix)
		{
			EvictFromLayers = evictFromLayers ?? throw new ArgumentNullException(nameof(evictFromLayers));
		}


		/// <inheritdoc/>
		public void Register(ICacheStack cacheStack)
		{
			if (IsRegistered)
			{
				throw new InvalidOperationException($"{nameof(RedisRemoteEvictionExtension)} can only be registered to one {nameof(ICacheStack)}");
			}
			IsRegistered = true;

			Subscriber.Subscribe(EvictionChannel)
				.OnMessage(async (channelMessage) =>
				{
					string cacheKey = channelMessage.Message;

					bool shouldEvictLocally;
					lock (LockObj)
					{
						shouldEvictLocally = FlaggedEvictions.Remove(cacheKey) == false;
					}

					if (shouldEvictLocally)
					{
						for (var i = 0; i < EvictFromLayers.Length; i++)
						{
							await EvictFromLayers[i].EvictAsync(cacheKey);
						}
					}
				});

			Subscriber.Subscribe(FlushChannel)
				.OnMessage(async (_) =>
				{
					bool shouldFlushLocally;
					lock (LockObj)
					{
						shouldFlushLocally = !HasFlushTriggered;
						HasFlushTriggered = false;
					}

					if (shouldFlushLocally)
					{
						for (var i = 0; i < EvictFromLayers.Length; i++)
						{
							await EvictFromLayers[i].FlushAsync();
						}
					}
				});
		}
	}
}
