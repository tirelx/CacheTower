using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace CacheTower.Extensions.Redis
{
	/// <summary>
	/// Information about evicted key in hash table
	/// </summary>
	[ProtoContract]
	public class HashKeyEvictionMessage : IEquatable<HashKeyEvictionMessage>
	{
		/// <summary>Indicates whether the current object is equal to another object of the same type.</summary>
		/// <param name="other">An object to compare with this object.</param>
		/// <returns>true if the current object is equal to the <paramref name="other">other</paramref> parameter; otherwise, false.</returns>
		public bool Equals(HashKeyEvictionMessage? other)
		{
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;
			return HashTableKey == other.HashTableKey && ElementKeys.SequenceEqual(other.ElementKeys);
		}

		/// <summary>Determines whether the specified object is equal to the current object.</summary>
		/// <param name="obj">The object to compare with the current object.</param>
		/// <returns>true if the specified object  is equal to the current object; otherwise, false.</returns>
		public override bool Equals(object? obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != this.GetType()) return false;
			return Equals((HashKeyEvictionMessage)obj);
		}

		/// <summary>Serves as the default hash function.</summary>
		/// <returns>A hash code for the current object.</returns>
		public override int GetHashCode()
		{
			unchecked
			{
				return (HashTableKey.GetHashCode() * 397) ^ GetOrderIndependentHashCode(ElementKeys);
			}
		}

		private static int GetOrderIndependentHashCode<T>(IEnumerable<T> source)
		{
			return source.Aggregate(0,
				(current, element) => current ^ EqualityComparer<T>.Default.GetHashCode(element));
		}

		/// <summary>Returns a value that indicates whether the values of two <see cref="T:CacheTower.Extensions.Redis.HashKeyEvictionMessage" /> objects are equal.</summary>
		/// <param name="left">The first value to compare.</param>
		/// <param name="right">The second value to compare.</param>
		/// <returns>true if the <paramref name="left" /> and <paramref name="right" /> parameters have the same value; otherwise, false.</returns>
		public static bool operator ==(HashKeyEvictionMessage? left, HashKeyEvictionMessage? right)
		{
			return Equals(left, right);
		}

		/// <summary>Returns a value that indicates whether two <see cref="T:CacheTower.Extensions.Redis.HashKeyEvictionMessage" /> objects have different values.</summary>
		/// <param name="left">The first value to compare.</param>
		/// <param name="right">The second value to compare.</param>
		/// <returns>true if <paramref name="left" /> and <paramref name="right" /> are not equal; otherwise, false.</returns>
		public static bool operator !=(HashKeyEvictionMessage? left, HashKeyEvictionMessage? right)
		{
			return !Equals(left, right);
		}

		// ReSharper disable once UnusedMember.Local
#pragma warning disable 8618
		private HashKeyEvictionMessage() //for protobuf
#pragma warning restore 8618
		{
		}

		/// <summary>
		/// Constructor of type <see cref="HashKeyEvictionMessage"/> 
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKey"></param>
		public HashKeyEvictionMessage(string hashTableKey, string elementKey)
		{
			HashTableKey = hashTableKey;
			ElementKeys = new List<string> { elementKey };
		}

		/// <summary>
		/// Constructor of type <see cref="HashKeyEvictionMessage"/> 
		/// </summary>
		/// <param name="hashTableKey"></param>
		/// <param name="elementKeys"></param>
		public HashKeyEvictionMessage(string hashTableKey, ICollection<string> elementKeys)
		{
			HashTableKey = hashTableKey;
			ElementKeys = elementKeys;
		}

		/// <summary>
		/// Hash table key
		/// </summary>
		[ProtoMember(1)]
		public string HashTableKey { get; }

		/// <summary>
		/// Element key
		/// </summary>
		[ProtoMember(2)]
		public ICollection<string> ElementKeys { get; }

		/// <summary>Returns a string that represents the current object.</summary>
		/// <returns>A string that represents the current object.</returns>
		/// <footer><a href="https://docs.microsoft.com/en-us/dotnet/api/System.Object.ToString?view=netstandard-2.0">`Object.ToString` on docs.microsoft.com</a></footer>
		public override string ToString()
		{
			return $"{nameof(HashTableKey)}: {HashTableKey}, {nameof(ElementKeys)}: {string.Join(",", ElementKeys)}";
		}
	}
}