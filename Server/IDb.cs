﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace pbXStorage.Server
{
	public enum IdInDbType
	{
		Storage,
		Thing,
	}

	public struct IdInDb
	{
		// always as: repositoryId[/StorageId]
		public string StorageId;

		public IdInDbType Type;
		public string Id;
	}

	public interface IDb
	{
		bool Initialized { get; }
		Task InitializeAsync();

		// storageId is always in the following format:
		// repositoryId/storageId

		Task StoreThingAsync(string storageId, string thingId, string data, DateTime modifiedOn);
		Task<bool> ThingExistsAsync(string storageId, string thingId);
		Task<DateTime> GetThingModifiedOnAsync(string storageId, string thingId);
		Task<string> GetThingCopyAsync(string storageId, string thingId);
		Task DiscardThingAsync(string storageId, string thingId);
		Task<IEnumerable<IdInDb>> FindThingIdsAsync(string storageId, string pattern);

		// storageId can be in the following formats:
		// repositoryId/storageId
		// repositoryId

		Task DiscardAllAsync(string storageId);
		Task<IEnumerable<IdInDb>> FindIdsAsync(string storageId, string pattern);
	};
}
