using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CacheTower.Providers.Redis.Entities;
using ProtoBuf;
using StackExchange.Redis;

namespace CacheTower.Providers.Redis
{
	/// <remarks>
	/// The <see cref="RedisHashTableCacheLayer"/> allows caching data in Redis. Data will be serialized to Protobuf using <a href="https://github.com/protobuf-net/protobuf-net">protobuf-net</a>.
	/// <para>
	/// When caching custom types, you will need to <a href="https://github.com/protobuf-net/protobuf-net#1-first-decorate-your-classes">decorate your class</a> with <c>[ProtoContact]</c> and <c>[ProtoMember]</c> attributes per protobuf-net's documentation.<br/>
	/// Additionally, as the Protobuf format doesn't have a way to represent an empty collection, these will be returned as <c>null</c>.
	/// </para>
	/// <para>
	/// While this can be inconvienent, using Protobuf ensures high performance and low allocations for serializing.
	/// </para>
	/// </remarks>
	/// <inheritdoc cref="IHashTableCacheLayer"/>
	public class RedisHashTableCacheLayer : BaseRedisLayer, IHashTableCacheLayer
	{

		/// <summary>
		/// Creates a new instance of <see cref="RedisCacheLayer"/> with the given <paramref name="connection"/> and <paramref name="databaseIndex"/>.
		/// </summary>
		/// <param name="connection">The primary connection to Redis where the cache will be stored.</param>
		/// <param name="databaseIndex">
		/// The database index to use for Redis.
		/// If not specified, uses the default database as configured on the <paramref name="connection"/>.
		/// </param>
		public RedisHashTableCacheLayer(IConnectionMultiplexer connection, int databaseIndex = -1) : base(connection, databaseIndex)
		{
		}


		/// <inheritdoc />
		public async ValueTask<TValue?> GetValueAsync<TValue>(string hashTableKey, string elementKey)
		{
			var redisValue = await Database.HashGetAsync(GetHashKeyName(hashTableKey), elementKey);
			return redisValue.IsNull ? default : DeserializeValue<TValue>(redisValue);
		}

		/// <inheritdoc />
		public async ValueTask SetValueAsync<TValue>(string hashTableKey, string elementKey, TValue value)
		{
			await Database.HashSetAsync(GetHashKeyName(hashTableKey), elementKey, GetRedisValue(value));
		}

		/// <summary>
		/// Removes an entry with the corresponding <paramref name="hashTableKey"/> and <paramref name="elementKey"/> from the cache layer.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKey"></param>
		/// <returns></returns>
		public async ValueTask EvictValueAsync(string hashTableKey, string elementKey)
		{
			await Database.HashDeleteAsync(GetHashKeyName(hashTableKey), elementKey);
		}


		/// <summary>
		/// Retrieves the <see cref="CacheSetEntry{T}"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <returns>The existing cache entry or <c>null</c> if no entry is found.</returns>
		public async ValueTask<CacheSetEntry<TValue?>?> GetHashAsync<TValue>(string hashTableKey)
		{
			var transaction = Database.CreateTransaction();
			var taskGetAll = transaction.HashGetAllAsync(GetHashKeyName(hashTableKey));
			var taskRedisCacheInfo = transaction.StringGetAsync(GetInfoKeyName(hashTableKey));

			var res = await transaction.ExecuteAsync();
			if (!res)
			{
				throw new RedisCommandException("Error: the transaction was not committed (MULTI command)");
			}
			
			var hashEntries = taskGetAll.Result;
			if (hashEntries == null) return default;

			var redisCacheInfo = DeserializeValue<RedisHashInfo>(taskRedisCacheInfo.Result);
			var dict = new Dictionary<string, TValue?>(hashEntries.Length);
			foreach (var hashEntry in hashEntries)
			{
				dict[hashEntry.Name] = DeserializeValue<TValue>(hashEntry.Value);
			}

			return new CacheSetEntry<TValue?>(dict, redisCacheInfo?.Expiry);
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
			var expiryOffset = cacheEntry.Expiry - DateTime.UtcNow;
			if (expiryOffset < TimeSpan.Zero)
			{
				return;
			}

			var redisCacheEntry = new RedisHashInfo
			{
				Expiry = cacheEntry.Expiry
			};

			var transaction = Database.CreateTransaction();
			var hashKeyName = GetHashKeyName(hashTableKey);
			var infoKeyName = GetInfoKeyName(hashTableKey);
			
#pragma warning disable 4014
			
			transaction.KeyDeleteAsync(hashTableKey);

			var redisValue = GetRedisValue(redisCacheEntry);
			transaction.StringSetAsync(infoKeyName, redisValue, expiryOffset);
			foreach (var pair in cacheEntry.Values)
			{
				transaction.HashSetAsync(hashKeyName, pair.Key, GetRedisValue(pair.Value));
			}
			transaction.KeyExpireAsync(hashKeyName, expiryOffset);
			
#pragma warning restore 4014
			var res = await transaction.ExecuteAsync();
			if (!res)
			{
				throw new RedisCommandException("Error: the transaction was not committed (MULTI command)");
			}
		}

		/// <summary>
		///  Removes an entries with the corresponding <paramref name="hashTableKey"/> and <paramref name="elementKeys"/> from the cache layer.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKeys"></param>
		/// <returns></returns>
		public async ValueTask EvictHashSubsetAsync(string hashTableKey, IEnumerable<string> elementKeys)
		{
			var transaction = Database.CreateTransaction();
			foreach (var elementKey in elementKeys)
			{
#pragma warning disable 4014
				transaction.HashDeleteAsync(GetHashKeyName(hashTableKey), elementKey);
#pragma warning restore 4014
			}

			var res = await transaction.ExecuteAsync();
			if (!res)
			{
				throw new RedisCommandException("Error: the transaction was not committed (MULTI command)");
			}
		}

		/// <summary>
		/// Caches subset of {TValue} against the <paramref name="hashTableKey"/>.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="subset"></param>
		/// <typeparam name="TValue"></typeparam>
		/// <returns></returns>
		public async ValueTask SetHashSubsetAsync<TValue>(string hashTableKey, IEnumerable<KeyValuePair<string, TValue?>> subset)
		{
			var transaction = Database.CreateTransaction();
			var hashKeyName = GetHashKeyName(hashTableKey);

			foreach (var pair in subset)
			{
#pragma warning disable 4014
				transaction.HashSetAsync(hashKeyName, pair.Key, GetRedisValue(pair.Value));
#pragma warning restore 4014
			}

			var res = await transaction.ExecuteAsync();
			if (!res)
			{
				throw new RedisCommandException("Error: the transaction was not committed (MULTI command)");
			}
		}

		/// <summary>
		/// Retrieves the <see cref="CacheEntry{T}"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <typeparam name="TValue"></typeparam>
		/// <param name="hashTableKey">The cache entry's key.</param>
		/// <param name="elementKeys"></param>
		/// <returns>The existing cache entry or <c>null</c> if no entry is found.</returns>
		public async ValueTask<IReadOnlyDictionary<string, TValue?>?> GetHashSubsetAsync<TValue>(string hashTableKey,
			IEnumerable<string> elementKeys)
		{
			var hashKeyName = GetHashKeyName(hashTableKey);
			var redisHash = elementKeys.Select(x => (RedisValue)x).ToArray();
			var subset = await Database.HashGetAsync(hashKeyName, redisHash);

			var enumerable = redisHash.Zip(subset,
				(key, redisValue) => new KeyValuePair<string, TValue?>(key, DeserializeValue<TValue>(redisValue)));
			var dict = new Dictionary<string, TValue?>(subset.Length);
			foreach (var pair in enumerable)
			{
				dict[pair.Key] = pair.Value;
			}

			return dict;
		}

		/// <summary>
		/// Set exparation time <paramref name="expiry"/> for a given <paramref name="hashTableKey"/>.
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="expiry"></param>
		/// <returns></returns>
		public async ValueTask SetHashExpiry<TValue>(string hashTableKey, DateTime expiry)
		{
			var transaction = Database.CreateTransaction();
			var redisCacheEntry = new RedisHashInfo
			{
				Expiry = expiry
			};
			
			var expiryOffset = expiry - DateTime.UtcNow;
			if (expiryOffset < TimeSpan.Zero)
			{
				expiryOffset = TimeSpan.Zero;
			}

			var hashKeyName = GetHashKeyName(hashTableKey);
			if (!Database.KeyExists(hashKeyName))
			{
				return;
			}
			
#pragma warning disable 4014
			transaction.StringSetAsync(GetInfoKeyName(hashTableKey), GetRedisValue(redisCacheEntry), expiryOffset);
			transaction.KeyExpireAsync(hashKeyName, expiryOffset);
#pragma warning restore 4014
			var res = await transaction.ExecuteAsync();
			if (!res)
			{
				throw new RedisCommandException("Error: the transaction was not committed (MULTI command)");
			}
		}

		/// <inheritdoc cref="IHashTableCacheLayer.EvictHashAsync" />
		public async ValueTask EvictHashAsync(string hashTableKey)
		{
			var transaction = Database.CreateTransaction();
#pragma warning disable 4014
			transaction.KeyDeleteAsync(GetHashKeyName(hashTableKey));
			transaction.KeyDeleteAsync(GetInfoKeyName(hashTableKey));
#pragma warning restore 4014
			var res = await transaction.ExecuteAsync();
			if (!res)
			{
				throw new RedisCommandException("Error: the transaction was not committed (MULTI command)");
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static RedisValue GetRedisValue<TValue>(TValue? value)
		{
			if (value is null)
			{
				return RedisValue.Null;
			}
			using var stream = new MemoryStream();
			//todo ISerializer: разные реализации
			Serializer.Serialize(stream, value);
			stream.Seek(0, SeekOrigin.Begin);
			var redisValue = RedisValue.CreateFrom(stream);
			return redisValue;
		}
		
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static TValue? DeserializeValue<TValue>(RedisValue redisValue)
		{
			if (redisValue == RedisValue.Null)
			{
				return default;
			}
			using var internalStream = new MemoryStream(redisValue);
			var value = Serializer.Deserialize<TValue?>(internalStream);
			return value;
		}

		private static string GetInfoKeyName(string key) => $"{key}:info";
		private static string GetHashKeyName(string key) => $"{key}:hash";
	}
}