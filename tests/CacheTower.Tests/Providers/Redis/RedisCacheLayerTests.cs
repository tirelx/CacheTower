using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CacheTower.Extensions.Redis;
using CacheTower.Providers.Memory;
using CacheTower.Providers.Redis;
using CacheTower.Tests.Utils;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using StackExchange.Redis;

namespace CacheTower.Tests.Providers.Redis
{
	[TestClass]
	public class RedisCacheLayerTests : BaseCacheLayerTests
	{
		[TestInitialize]
		public void Setup()
		{
			RedisHelper.ResetState();
		}

		[TestMethod]
		public async Task GetSetCache()
		{
			await AssertGetSetCacheAsync(new RedisCacheLayer(RedisHelper.GetConnection()));
		}

		[TestMethod]
		public async Task IsCacheAvailable()
		{
			await AssertCacheAvailabilityAsync(new RedisCacheLayer(RedisHelper.GetConnection()), true);

			var connectionMock = new Mock<IConnectionMultiplexer>();
			var databaseMock = new Mock<IDatabase>();
			connectionMock.Setup(cm => cm.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
				.Returns(databaseMock.Object);
			databaseMock.Setup(db => db.PingAsync(It.IsAny<CommandFlags>())).Throws<Exception>();

			await AssertCacheAvailabilityAsync(new RedisCacheLayer(connectionMock.Object), false);
		}

		[TestMethod]
		public async Task EvictFromCache()
		{
			await AssertCacheEvictionAsync(new RedisCacheLayer(RedisHelper.GetConnection()));
		}

		[TestMethod]
		public async Task FlushFromCache()
		{
			await AssertCacheFlushAsync(new RedisCacheLayer(RedisHelper.GetConnection()));
		}

		[TestMethod]
		public async Task CacheCleanup()
		{
			await AssertCacheCleanupAsync(new RedisCacheLayer(RedisHelper.GetConnection()));
		}

		[TestMethod]
		public async Task CachingComplexTypes()
		{
			await AssertComplexTypeCachingAsync(new RedisCacheLayer(RedisHelper.GetConnection()));
		}

		[TestMethod]
		public async Task TestHash()
		{
			var cacheLayer = new RedisHashTableCacheLayer(RedisHelper.GetConnection());
			var dictionary = new Dictionary<string, ComplexTypeCaching_TypeOne>();
			for (var i = 0; i < 10_000; i++)
			{
				dictionary.Add(i.ToString(),
					new ComplexTypeCaching_TypeOne
					{
						ExampleString = "Hello World",
						ExampleNumber = 99,
						ListOfNumbers = new List<int>() { 1, 2, 4, 8 }
					});
			}

			dictionary.Add("10000", null);


			var entry = new CacheSetEntry<ComplexTypeCaching_TypeOne>(dictionary);
			await cacheLayer.SetHashAsync("test", entry);
			await cacheLayer.SetHashAsync("test2", entry);

			var hash = await cacheLayer.GetHashAsync<ComplexTypeCaching_TypeOne>("test");

			var el = await cacheLayer.GetValueAsync<ComplexTypeCaching_TypeOne>("test", "10000");

			var subset =
				await cacheLayer.GetHashSubsetAsync<ComplexTypeCaching_TypeOne>("test",
					Enumerable.Range(9990, 100).Select(x => x.ToString()));
		}

		[TestMethod]
		public async Task TestCacheStack()
		{
			var services = new ServiceCollection();
			var memoryCacheLayer = new MemoryCacheLayer();
			var redisCacheLayer = new RedisCacheLayer(RedisHelper.GetConnection());
			var extension = new RedisRemoteEvictionExtension(RedisHelper.GetConnection(),
				new ICacheLayer[] { memoryCacheLayer, redisCacheLayer });
			services.AddCacheStack(new ICacheLayer[] { memoryCacheLayer, redisCacheLayer },
				new ICacheExtension[] { extension });
			var provider = services.BuildServiceProvider();

			var cacheStack = provider.GetRequiredService<ICacheStack>();
			var obj = new ComplexTypeCaching_TypeOne
			{
				ExampleString = "Hello World", ExampleNumber = 99, ListOfNumbers = new List<int>() { 1, 2, 4, 8 }
			};

			
			await cacheStack.SetAsync(cacheKey, obj);

			var services2 = new ServiceCollection();
			var memoryCacheLayer2 = new MemoryCacheLayer();
			var redisCacheLayer2 = new RedisCacheLayer(RedisHelper.GetConnection());
			var extension2 = new RedisRemoteEvictionExtension(RedisHelper.GetConnection(),
				new ICacheLayer[] { memoryCacheLayer2, redisCacheLayer2 });
			services2.AddCacheStack(new ICacheLayer[] { memoryCacheLayer2, redisCacheLayer2 },
				new ICacheExtension[] { extension2 });
			var provider2 = services2.BuildServiceProvider();

			var cacheStack2 = provider2.GetRequiredService<ICacheStack>();

			var cache = await cacheStack2.GetAsync<ComplexTypeCaching_TypeOne>(cacheKey);

			await cacheStack2.EvictAsync(cacheKey);

			var value = await cacheStack.GetAsync<ComplexTypeCaching_TypeOne>(cacheKey);
		}

		[TestMethod]
		public async Task TestCachingMemoryLayer()
		{
			var memoryCache =
				new MemoryHashTableCacheLayer(new MemoryCacheEntryOptions()
				{
					SlidingExpiration = TimeSpan.FromSeconds(1)
				});

			var obj = new ComplexTypeCaching_TypeOne
			{
				ExampleString = "Hello World", ExampleNumber = 99, ListOfNumbers = new List<int>() { 1, 2, 4, 8 }
			};


			var keyValuePairs = new List<KeyValuePair<string, ComplexTypeCaching_TypeOne>>();
			keyValuePairs.Add(new KeyValuePair<string, ComplexTypeCaching_TypeOne>("98", obj));
			await memoryCache.SetHashAsync(cacheKey, new CacheSetEntry<ComplexTypeCaching_TypeOne>(keyValuePairs));

			await memoryCache.SetValueAsync(cacheKey, "99", obj);

			var res = await memoryCache.GetHashAsync<ComplexTypeCaching_TypeOne>(cacheKey);

			Assert.IsNotNull(res);
			await Task.Delay(1_100);

			var cacheSetEntry = await memoryCache.GetHashAsync<ComplexTypeCaching_TypeOne>(cacheKey);
			Assert.IsNull(cacheSetEntry);
		}

		[TestMethod]
		public async Task TestHashTableStack()
		{
			var services = new ServiceCollection();
			var memoryCacheLayer =
				new MemoryHashTableCacheLayer(new MemoryCacheEntryOptions
				{
					SlidingExpiration = TimeSpan.FromSeconds(1)
				});
			var redisCacheLayer = new RedisHashTableCacheLayer(RedisHelper.GetConnection());

			services.AddHashTableCacheStack(new IHashTableCacheLayer[] { memoryCacheLayer, redisCacheLayer },
				Array.Empty<IHashTableCacheExtension>());
			var provider = services.BuildServiceProvider();

			var cacheStack = provider.GetRequiredService<IHashTableCacheStack>();
			var dictionary = new Dictionary<string, ComplexTypeCaching_TypeOne>();
			for (var i = 0; i < 10_000; i++)
			{
				dictionary.Add(i.ToString(),
					new ComplexTypeCaching_TypeOne
					{
						ExampleString = "Hello World",
						ExampleNumber = i,
						ListOfNumbers = new List<int> { 1, 2, 4, 8 }
					});
			}

			await cacheStack.SetHashSubsetAsync(cacheKey, dictionary);

			await Task.Delay(1_000);

			var value = await redisCacheLayer.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "1");
			Assert.IsNotNull(value);

			value = await memoryCacheLayer.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "1");
			Assert.IsNull(value);

			value = await cacheStack.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "1");
			Assert.IsNotNull(value);

			value = await memoryCacheLayer.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "1");
			Assert.IsNotNull(value);

			for (var i = 0; i < 10; i++)
			{
				await Task.Delay(150);
				Assert.IsNotNull(await cacheStack.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "1"));
			}

			await Task.Delay(1_000);
			value = await memoryCacheLayer.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "1");
			Assert.IsNull(value);
		}

		[TestMethod]
		[DataRow("key")]
		[DataRow("subset")]
		[DataRow("full")]
		public async Task TestSpeedRedisHashTableStack(string type)
		{
			var redisCacheLayer = new RedisHashTableCacheLayer(RedisHelper.GetConnection());

			var hashTableCacheLayers = new IHashTableCacheLayer[] { redisCacheLayer };
			var cacheStack = await PrepareAndWriteTestData(hashTableCacheLayers);

			switch (type)
			{
				case "key": await ReadKeys(cacheStack); break;
				case "subset": await ReadSubsetKeys(cacheStack); break;
				case "full": await ReadHash(cacheStack); break;
			}
		}

		[TestMethod]
		[DataRow("key")]
		[DataRow("subset")]
		[DataRow("full")]
		public async Task TestSpeedComplexHashTableStack(string type)
		{
			var redisCacheLayer = new RedisHashTableCacheLayer(RedisHelper.GetConnection());
			var memoryCacheLayer =
				new MemoryHashTableCacheLayer(new MemoryCacheEntryOptions
				{
					SlidingExpiration = TimeSpan.FromSeconds(100)
				});

			var hashTableCacheLayers = new IHashTableCacheLayer[] { memoryCacheLayer, redisCacheLayer };
			var cacheStack = await PrepareAndWriteTestData(hashTableCacheLayers);

			switch (type)
			{
				case "key": await ReadKeys(cacheStack); break;
				case "subset": await ReadSubsetKeys(cacheStack); break;
				case "full": await ReadHash(cacheStack); break;
			}
		}

		[TestMethod]
		public async Task TestRedisExtensionHashTable()
		{
			var cacheStack = HashTableCacheStack();
			var cacheStack2 = HashTableCacheStack();

			var item = new ComplexTypeCaching_TypeOne
			{
				ExampleString = "Hello World", ExampleNumber = 10, ListOfNumbers = new List<int> { 1, 2, 4, 8 }
			};

			await cacheStack.SetValueAsync(cacheKey, "0", item);
			
			var res = await cacheStack.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "0");
			Assert.AreEqual(item, res);
			res = await cacheStack2.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "0");
			Assert.AreEqual(item, res);

			item.ExampleNumber = 1100;
			await cacheStack.SetValueAsync(cacheKey, "0", item);
			await Task.Delay(50);
			
			res = await cacheStack.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "0");
			Assert.AreEqual(item, res);
			res = await cacheStack2.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "0");
			Assert.AreEqual(item, res);

			await cacheStack.EvictValueAsync(cacheKey, "0");
			Assert.IsNull(await cacheStack.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "0"));
			
			await Task.Delay(50);
			Assert.IsNull(await cacheStack2.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, "0"));
		}
		
		[TestMethod]
		public async Task TestSpeedRedisExtensionHashTable()
		{
			var cacheStack = HashTableCacheStack();
			var cacheStack2 = HashTableCacheStack();

			var dictionary = new Dictionary<string, ComplexTypeCaching_TypeOne>();
			for (var i = 0; i < 100_000; i++)
			{
				dictionary.Add(i.ToString(),
					new ComplexTypeCaching_TypeOne
					{
						ExampleString = "Hello World",
						ExampleNumber = i,
						ListOfNumbers = new List<int> { 1, 2, 4, 8 }
					});
			}


			await cacheStack.SetHashSubsetAsync(cacheKey, dictionary);

			var res = await cacheStack2.GetHashSubsetAsync<ComplexTypeCaching_TypeOne>(cacheKey, dictionary.Keys);
			Assert.IsTrue(res.SequenceEqual(dictionary));
			
			foreach (var key in dictionary.Keys)
			{
				dictionary[key].ExampleNumber += 1_000_000;
			}
			
			await cacheStack.SetHashSubsetAsync(cacheKey, dictionary);
			await Task.Delay(500);
			
			res = await cacheStack2.GetHashSubsetAsync<ComplexTypeCaching_TypeOne>(cacheKey, dictionary.Keys);
			Assert.IsTrue(res.SequenceEqual(dictionary));

		}

		private static IHashTableCacheStack HashTableCacheStack()
		{
			var redisCacheLayer = new RedisHashTableCacheLayer(RedisHelper.GetConnection());
			var memoryCacheLayer =
				new MemoryHashTableCacheLayer(new MemoryCacheEntryOptions
				{
					SlidingExpiration = TimeSpan.FromSeconds(100)
				});
			var hashTableCacheLayers = new IHashTableCacheLayer[] { memoryCacheLayer, redisCacheLayer };

			var redisExtension =
				new RedisHashRemoteEvictionExtension(RedisHelper.GetConnection(),
					new IHashTableCacheLayer[] { memoryCacheLayer });

			var services = new ServiceCollection();
			services.AddHashTableCacheStack(hashTableCacheLayers, new IHashTableCacheExtension[] { redisExtension });
			var provider = services.BuildServiceProvider();

			var cacheStack = provider.GetRequiredService<IHashTableCacheStack>();
			return cacheStack;
		}


		[TestMethod]
		[DataRow("key")]
		[DataRow("subset")]
		[DataRow("full")]
		public async Task TestSpeedRedisCacheWrite(string type)
		{
			var redisCacheLayer = new RedisHashTableCacheLayer(RedisHelper.GetConnection());
			var hashTableCacheLayers = new IHashTableCacheLayer[] { redisCacheLayer };

			var count = 10_000;
			var cacheStack = PrepareTestData(hashTableCacheLayers, count, out var dictionary);

			var st = Stopwatch.StartNew();
			switch (type)
			{
				case "key":
				{
					foreach (var pair in dictionary)
					{
						await cacheStack.SetValueAsync(cacheKey, pair.Key, pair.Value);
					}
					break;
				}
				case "subset":
				{
					await cacheStack.SetHashSubsetAsync(cacheKey, dictionary);
					break;
				}
				case "full":
				{
					await cacheStack.SetHashAsync(cacheKey, new CacheSetEntry<ComplexTypeCaching_TypeOne>(dictionary));
					break;
				}
			}
			Console.WriteLine($"Скорость записи {type}: {(int)(count/st.Elapsed.TotalSeconds)} key/sec");
		}


		private static string cacheKey = "test_key";
		
		private static async Task<IHashTableCacheStack> PrepareAndWriteTestData(IHashTableCacheLayer[] hashTableCacheLayers, int count = 10_000)
		{
			var cacheStack = PrepareTestData(hashTableCacheLayers, count, out var dictionary);

			var st = Stopwatch.StartNew();
			await cacheStack.SetHashSubsetAsync(cacheKey, dictionary);
			st.Stop();
			Console.WriteLine($"Скорость записи: {(int)(count/st.Elapsed.TotalSeconds)} key/sec");
			return cacheStack;
		}

		private static IHashTableCacheStack PrepareTestData(IHashTableCacheLayer[] hashTableCacheLayers, int count,
			out Dictionary<string, ComplexTypeCaching_TypeOne> dictionary)
		{
			var services = new ServiceCollection();
			services.AddHashTableCacheStack(hashTableCacheLayers,
				Array.Empty<IHashTableCacheExtension>());
			var provider = services.BuildServiceProvider();

			var cacheStack = provider.GetRequiredService<IHashTableCacheStack>();
			dictionary = new Dictionary<string, ComplexTypeCaching_TypeOne>();
			for (var i = 0; i < count; i++)
			{
				dictionary.Add(i.ToString(),
					new ComplexTypeCaching_TypeOne
					{
						ExampleString = "Hello World",
						ExampleNumber = i,
						ListOfNumbers = new List<int> { 1, 2, 4, 8 }
					});
			}

			return cacheStack;
		}

		private static async Task ReadKeys(IHashTableCacheStack cacheStack)
		{
			var st = Stopwatch.StartNew();
			for (var i = 0; i < 10_000; i++)
			{
				await cacheStack.GetValueAsync<ComplexTypeCaching_TypeOne>(cacheKey, i.ToString());
			}

			st.Stop();
			Console.WriteLine(
				$"Получение 10K ключей: {st.Elapsed}, avg.speed: {(int)(10_000d / st.Elapsed.TotalSeconds)} key/sec ");
		}
		
		private static async Task ReadSubsetKeys(IHashTableCacheStack cacheStack)
		{
			var st = Stopwatch.StartNew();

			await cacheStack.GetHashSubsetAsync<ComplexTypeCaching_TypeOne>(cacheKey,
				Enumerable.Range(0, 10_000).Select(x => x.ToString()).ToList());

			st.Stop();
			Console.WriteLine(
				$"Получение 10K subset ключей: {st.Elapsed}, avg.speed: {(int)(10_000d / st.Elapsed.TotalSeconds)} key/sec ");
		}
		
		private static async Task ReadHash(IHashTableCacheStack cacheStack)
		{
			var st = Stopwatch.StartNew();

			await cacheStack.GetHashAsync<ComplexTypeCaching_TypeOne>(cacheKey);

			st.Stop();
			Console.WriteLine(
				$"Получение 10K hashAll ключей: {st.Elapsed}, avg.speed: {(int)(10_000d / st.Elapsed.TotalSeconds)} key/sec ");
		}
	}
}