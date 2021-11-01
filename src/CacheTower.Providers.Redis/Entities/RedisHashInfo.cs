using System;
using ProtoBuf;

namespace CacheTower.Providers.Redis.Entities
{
	[ProtoContract]
	internal class RedisHashInfo
	{
		/// <summary>
		/// The expiry date of the cache entry.
		/// </summary>
		[ProtoMember(1)]
		public DateTime? Expiry { get; set; }
	}
}
