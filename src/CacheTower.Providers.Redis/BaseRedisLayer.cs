using System.Threading.Tasks;
using StackExchange.Redis;

namespace CacheTower.Providers.Redis
{
	/// <summary>
	/// Base implementation cache layer for Redis
	/// </summary>
	public abstract class BaseRedisLayer
	{
		/// <summary>
		/// Connection to Redis
		/// </summary>
		protected IConnectionMultiplexer Connection { get; }
		
		/// <summary>
		/// Redis database
		/// </summary>
		protected IDatabase Database { get; }
		
		/// <summary>
		/// Redis database number
		/// </summary>
		protected int DatabaseIndex { get; }
		
		/// <summary>
		/// Creates a new instance of <see cref="RedisCacheLayer"/> with the given <paramref name="connection"/> and <paramref name="databaseIndex"/>.
		/// </summary>
		/// <param name="connection">The primary connection to Redis where the cache will be stored.</param>
		/// <param name="databaseIndex">
		/// The database index to use for Redis.
		/// If not specified, uses the default database as configured on the <paramref name="connection"/>.
		/// </param>
		public BaseRedisLayer(IConnectionMultiplexer connection, int databaseIndex = -1)
		{
			Connection = connection;
			Database = connection.GetDatabase(databaseIndex);
			DatabaseIndex = databaseIndex;
		}

		/// <remarks>
		/// Cleanup is unnecessary for the <see cref="RedisCacheLayer"/> as Redis handles removing expired keys automatically.
		/// </remarks>
		public ValueTask CleanupAsync()
		{
			//Noop as Redis handles this directly
			return new ValueTask();
		}
		
		/// <remarks>
		/// Flushing the <see cref="RedisCacheLayer"/> performs a database flush in Redis.
		/// Every key associated to the database index will be removed.
		/// </remarks>
		public async ValueTask FlushAsync()
		{
			var redisEndpoints = Connection.GetEndPoints();
			foreach (var endpoint in redisEndpoints)
			{
				await Connection.GetServer(endpoint).FlushDatabaseAsync(DatabaseIndex);
			}
		}
		
		/// <summary>
		/// Retrieves the current availability status of the cache layer.
		/// This is used by <see cref="CacheStack"/> to determine whether a value can even be cached at that moment in time.
		/// </summary>
		/// <returns></returns>
		public ValueTask<bool> IsAvailableAsync(string cacheKey)
		{
			return IsAvailableAsync();
		}

		/// <summary>
		/// Retrieves the current availability status of the cache layer.
		/// This is used by <see cref="CacheStack"/> to determine whether a value can even be cached at that moment in time.
		/// </summary>
		/// <returns></returns>
		public ValueTask<bool> IsAvailableAsync()
		{
			return new ValueTask<bool>(Connection.IsConnected);
		}

	}
}