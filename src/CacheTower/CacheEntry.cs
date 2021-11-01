using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CacheTower.Internal;

namespace CacheTower
{
	/// <summary>
	/// Container for the cache entry expiry date.
	/// </summary>
	public abstract class CacheEntry
	{
		/// <summary>
		/// The expiry date for the cache entry.
		/// </summary>
		public DateTime? Expiry { get; }

		/// <summary>
		/// Creates a new <see cref="CacheEntry"/> with the given expiry date.
		/// </summary>
		/// <param name="expiry">The expiry date of the cache entry. This will be rounded down to the second.</param>
		protected CacheEntry(DateTime? expiry)
		{
			if (!expiry.HasValue) return;
			
			var value = expiry.Value;
			//Force the resolution of the expiry date to be to the second
			Expiry = new DateTime(
				value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, DateTimeKind.Utc
			);
		}

		/// <summary>
		/// Calculates the stale date for the cache entry using the provided <paramref name="cacheSettings"/>.
		/// </summary>
		/// <remarks>
		/// If <see cref="CacheSettings.StaleAfter"/> is not configured, the stale date is the expiry date.
		/// </remarks>
		/// <param name="cacheSettings">The cache settings to use for the calculation.</param>
		/// <returns>The date that the cache entry can be considered stale.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public DateTime? GetStaleDate(CacheSettings cacheSettings)
		{
			if (cacheSettings.StaleAfter.HasValue)
			{
				return Expiry - cacheSettings.TimeToLive + cacheSettings.StaleAfter;
			}

			return Expiry;
		}
	}

	/// <summary>
	/// Container for both the cached value and its expiry date.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public class CacheEntry<T> : CacheEntry, IEquatable<CacheEntry<T?>?>
	{
		/// <summary>
		/// The cached value.
		/// </summary>
		public T? Value { get; }

		/// <summary>
		/// Creates a new <see cref="CacheEntry"/> with the given <paramref name="value"/> and an expiry adjusted to the <paramref name="timeToLive"/>.
		/// </summary>
		/// <param name="value">The value to cache.</param>
		/// <param name="timeToLive">The amount of time before the cache entry expires.</param>
		public CacheEntry(T? value, TimeSpan? timeToLive) : this(value, DateTimeProvider.Now + timeToLive) { }
		/// <summary>
		/// Creates a new <see cref="CacheEntry"/> with the given <paramref name="value"/> and <paramref name="expiry"/>.
		/// </summary>
		/// <param name="value">The value to cache.</param>
		/// <param name="expiry">The expiry date of the cache entry. This will be rounded down to the second.</param>
		public CacheEntry(T? value, DateTime? expiry) : base(expiry)
		{
			Value = value;
		}

		/// <inheritdoc/>
		public bool Equals(CacheEntry<T?>? other)
		{
			if (other == null)
			{
				return false;
			}

			return Equals(Value, other.Value) &&
				Expiry == other.Expiry;
		}

		/// <inheritdoc/>
		public override bool Equals(object? obj)
		{
			if (obj is CacheEntry<T?> objOfType)
			{
				return Equals(objOfType);
			}

			return false;
		}

		/// <inheritdoc/>
		public override int GetHashCode()
		{
			return (Value?.GetHashCode() ?? 1) ^ (Expiry?.GetHashCode() ?? 2);
		}
	}

	/// <inheritdoc />
	public abstract class DeletableCacheSetEntry : CacheEntry
	{
		/// <summary>
		/// Creates a new <see cref="CacheEntry"/> with the given expiry date.
		/// </summary>
		/// <param name="expiry">The expiry date of the cache entry. This will be rounded down to the second.</param>
		public DeletableCacheSetEntry(DateTime? expiry) : base(expiry)
		{
		}

		/// <summary>
		/// Delete value from set
		/// </summary>
		/// <param name="key"></param>
		/// <returns></returns>
		public abstract bool TryRemove(string key);
		
		/// <summary>
		/// Delete values from set
		/// </summary>
		/// <param name="keys"></param>
		/// <returns></returns>
		public abstract bool TryRemove(IEnumerable<string> keys);
	}

	/// <summary>
	/// Container for both the cached values set and its expiry date.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public class CacheSetEntry<T> : DeletableCacheSetEntry, IEquatable<CacheSetEntry<T?>?>
	{
		/// <summary>
		/// The cached values set.
		/// </summary>
		public IReadOnlyDictionary<string, T> Values { get; }

		
		/// <summary>
		/// Creates a new <see cref="CacheEntry"/> with the given expiry date.
		/// </summary>
		/// <param name="values"></param>
		/// <param name="expiry">The expiry date of the cache entry. This will be rounded down to the second.</param>
		public CacheSetEntry(IEnumerable<KeyValuePair<string, T>> values, DateTime? expiry = null) : base(expiry)
		{
			Values = new ConcurrentDictionary<string, T>(values.ToDictionary(x => x.Key, x => x.Value));
		}
		
		/// <summary>
		/// Creates a new <see cref="CacheEntry"/> with the given expiry date.
		/// </summary>
		/// <param name="values"></param>
		/// <param name="expiry">The expiry date of the cache entry. This will be rounded down to the second.</param>
		public CacheSetEntry(IDictionary<string, T> values, DateTime? expiry = null) : base(expiry)
		{
			Values = values is ConcurrentDictionary<string, T> concurrentDictionary
				? concurrentDictionary
				: new ConcurrentDictionary<string, T>(values);
		}

		/// <summary>Indicates whether the current object is equal to another object of the same type.</summary>
		/// <param name="other">An object to compare with this object.</param>
		/// <returns>true if the current object is equal to the <paramref name="other">other</paramref> parameter; otherwise, false.</returns>
		public bool Equals(CacheSetEntry<T?>? other)
		{
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return Values!.SequenceEqual(other.Values!);
		}

		/// <summary>Determines whether the specified object is equal to the current object.</summary>
		/// <param name="obj">The object to compare with the current object.</param>
		/// <returns>true if the specified object  is equal to the current object; otherwise, false.</returns>
		public override bool Equals(object? obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != GetType()) return false;
			return Equals((CacheSetEntry<T?>?)obj);
		}

		/// <summary>Serves as the default hash function.</summary>
		/// <returns>A hash code for the current object.</returns>
		public override int GetHashCode()
		{
			return (Values?.GetHashCode() ?? 1) ^ (Expiry?.GetHashCode() ?? 2);
		}

		/// <inheritdoc />
		public override bool TryRemove(string key)
		{
			if (Values is ConcurrentDictionary<string, T?> dictionary)
			{
				return dictionary.TryRemove(key, out _);
			}

			return false;
		}

		/// <summary>
		/// Delete values from set
		/// </summary>
		/// <param name="keys"></param>
		/// <returns></returns>
		public override bool TryRemove(IEnumerable<string> keys)
		{
			if (Values is ConcurrentDictionary<string, T?> dictionary)
			{
				var removed = false;
				foreach (var key in keys)
				{
					removed = dictionary.TryRemove(key, out _) || removed;
				}
				return removed;
			}

			return false;
		}
	}
}
