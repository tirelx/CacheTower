﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CacheTower.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace CacheTower.Tests.Extensions
{
	[TestClass]
	public class ExtensionContainerTests
	{
		[TestMethod]
		public async Task RefreshWrapperExtension()
		{
			var cacheStackMock = new Mock<ICacheStack>();
			var refreshWrapperMock = new Mock<IRefreshWrapperExtension>();
			var container = new ExtensionContainer(new[] { refreshWrapperMock.Object });

			container.Register(cacheStackMock.Object);

			var cacheEntry = new CacheEntry<int>(1, DateTime.UtcNow, TimeSpan.FromDays(1));

			var refreshedValue = await container.RefreshValueAsync("WrapperTestRequestId", "WrapperTestCacheKey", () =>
			{
				return Task.FromResult(cacheEntry);
			}, new CacheSettings(TimeSpan.FromDays(1)));

			refreshWrapperMock.Verify(e => e.Register(cacheStackMock.Object), Times.Once);
			refreshWrapperMock.Verify(e => e.RefreshValueAsync(
					"WrapperTestRequestId", "WrapperTestCacheKey",
					It.IsAny<Func<Task<CacheEntry<int>>>>(), new CacheSettings(TimeSpan.FromDays(1))
				),
				Times.Once
			);
		}

		[TestMethod]
		public async Task ValueRefreshExtension()
		{
			var cacheStackMock = new Mock<ICacheStack>();
			var valueRefreshMock = new Mock<IValueRefreshExtension>();
			var container = new ExtensionContainer(new[] { valueRefreshMock.Object });

			container.Register(cacheStackMock.Object);

			await container.OnValueRefreshAsync("ValueRefreshTestRequestId", "ValueRefreshTestCacheKey", TimeSpan.FromDays(1));

			valueRefreshMock.Verify(e => e.Register(cacheStackMock.Object), Times.Once);
			valueRefreshMock.Verify(e => 
				e.OnValueRefreshAsync("ValueRefreshTestRequestId", "ValueRefreshTestCacheKey", TimeSpan.FromDays(1)),
				Times.Once
			);
		}
	}
}