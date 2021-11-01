using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ProtoBuf;
using StackExchange.Redis;

namespace CacheTower.Extensions.Redis
{
	
	/// <summary>
	/// The <see cref="RedisHashRemoteEvictionExtension"/> broadcasts cache updates, evictions and flushes to Redis to allow for remote eviction of old cache data.
	/// When one of these events is received, it will perform that action locally to the configured cache layers.
	/// </summary>
	public class RedisHashRemoteEvictionExtension : BaseRedisEvictionExtension, IHashTableCacheChangeExtension
	{
		private readonly IHashTableCacheLayer[] EvictFromLayers;
		private string EvictionHashKeyChannel { get; }
		private HashSet<HashKeyEvictionMessage> FlaggedEvictionMessages { get; }
		
		/// <summary>
		/// Creates a new instance of <see cref="RedisRemoteEvictionExtension"/>.
		/// </summary>
		/// <param name="connection">The primary connection to the Redis instance where the messages will be broadcast and received through.</param>
		/// <param name="evictFromLayers">The cache layers to either evict or flush when a message is received from Redis.</param>
		/// <param name="channelPrefix">The channel prefix to use for the Redis communication.</param>
		public RedisHashRemoteEvictionExtension(IConnectionMultiplexer connection, IHashTableCacheLayer[] evictFromLayers, string channelPrefix = "CacheTower") : base(connection, channelPrefix)
		{
			EvictFromLayers = evictFromLayers;
			EvictionHashKeyChannel = $"{channelPrefix}.RemoteHashKeyEviction";
			FlaggedEvictionMessages = new HashSet<HashKeyEvictionMessage>();
		}

		/// <summary>
		/// Registers the provided <paramref name="hashTableCacheStack"/> to the current cache extension.
		/// </summary>
		/// <param name="hashTableCacheStack">The cache stack you want to register.</param>
		public void Register(IHashTableCacheStack hashTableCacheStack)
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
							await EvictFromLayers[i].EvictHashAsync(cacheKey);
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
			
			Subscriber.Subscribe(EvictionHashKeyChannel)
				.OnMessage(async (channelMessage) =>
				{
					var evictionMessage = DeserializeMessage(channelMessage);
					if (evictionMessage is null)
					{
						return;
					}

					bool shouldEvictLocally;
					lock (LockObj)
					{
						shouldEvictLocally = FlaggedEvictionMessages.Remove(evictionMessage) == false;
					}

					if (shouldEvictLocally)
					{
						for (var i = 0; i < EvictFromLayers.Length; i++)
						{
							await EvictFromLayers[i].EvictHashSubsetAsync(evictionMessage.HashTableKey,
								evictionMessage.ElementKeys);
						}
					}
				});
		}

		/// <summary>
		/// Triggers after a cache entry has been updated.
		/// </summary>
		/// <param name="hashTableKey">The cache key for the entry that was updated.</param>
		/// <param name="elementKey">The element key in hash table</param>
		/// <param name="expiry">The new expiry date for the cache entry.</param>
		/// <param name="updateType">The type of cache update that has occurred.</param>
		/// <returns></returns>
		public ValueTask OnHashUpdateElementAsync(string hashTableKey, string elementKey, DateTime? expiry,
			CacheUpdateType updateType)
		{
			if (updateType == CacheUpdateType.AddOrUpdateEntry)
			{
				return FlagEvictionHashElementAsync(new HashKeyEvictionMessage(hashTableKey, elementKey));
			}
			return default;
		}

		/// <summary>
		/// Triggers after a cache entry has been updated.
		/// </summary>
		/// <param name="hashTableKey">The cache key for the entry that was updated.</param>
		/// <param name="elementKeys">The element key in hash table</param>
		/// <param name="expiry">The new expiry date for the cache entry.</param>
		/// <param name="updateType">The type of cache update that has occurred.</param>
		/// <returns></returns>
		public ValueTask OnHashSubsetUpdateAsync(string hashTableKey, ICollection<string> elementKeys, DateTime? expiry,
			CacheUpdateType updateType)
		{
			if (updateType == CacheUpdateType.AddOrUpdateEntry)
			{
				return FlagEvictionHashElementAsync(new HashKeyEvictionMessage(hashTableKey, elementKeys));
			}
			return default;
		}

		/// <summary>
		/// Triggers after a cache entry has been evicted.
		/// </summary>
		/// <param name="hashTableKey">The cache key for the entry that was evicted.</param>
		/// <param name="elementKey">The element key in hash table</param>
		/// <returns></returns>
		public ValueTask OnHashElementEvictionAsync(string hashTableKey, string elementKey)
		{
			return FlagEvictionHashElementAsync(new HashKeyEvictionMessage(hashTableKey, elementKey));
		}

		/// <summary>
		/// Triggers after a cache entry has been evicted.
		/// </summary>
		/// <param name="hashTableKey">The cache key for the entry that was evicted.</param>
		/// <param name="elementKeys">The element key in hash table</param>
		/// <returns></returns>
		public ValueTask OnHashSubsetEvictionAsync(string hashTableKey, ICollection<string> elementKeys)
		{
			return FlagEvictionHashElementAsync(new HashKeyEvictionMessage(hashTableKey, elementKeys));
		}

		private async ValueTask FlagEvictionHashElementAsync(HashKeyEvictionMessage message)
		{
			lock (LockObj)
			{
				FlaggedEvictionMessages.Add(message);
			}

			await Subscriber.PublishAsync(EvictionHashKeyChannel, SerializeMessage(message), CommandFlags.FireAndForget);
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static HashKeyEvictionMessage? DeserializeMessage(ChannelMessage message)
		{
			if (message.Message == RedisValue.Null)
			{
				return null;
			}
			using var internalStream = new MemoryStream(message.Message);
			var value = Serializer.Deserialize<HashKeyEvictionMessage?>(internalStream);
			return value;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static RedisValue SerializeMessage(HashKeyEvictionMessage message)
		{
			using var stream = new MemoryStream();
			Serializer.Serialize(stream, message);
			stream.Seek(0, SeekOrigin.Begin);
			var redisValue = RedisValue.CreateFrom(stream);
			return redisValue;
		}

	}
}