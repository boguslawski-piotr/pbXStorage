﻿using System;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using pbXStorage.Repositories;

namespace pbXStorage.Server.AspNetCore.Data
{
	public class RepositoriesDbPool : IDisposable
	{
		ConcurrentQueue<IDb> _pool = new ConcurrentQueue<IDb>();
		int _maxPoolSize;

		DbContextOptions _options;

		public RepositoriesDbPool(DbContextOptions options)
		{
			_options = options ?? throw new ArgumentNullException(nameof(options));
			_maxPoolSize = _options.FindExtension<CoreOptionsExtension>()?.MaxPoolSize ?? 128;
		}

		public void Dispose()
		{
			while (_pool.TryDequeue(out IDb db))
				db.Dispose();

			_options = null;
			_pool.Clear();
			_pool = null;
		}

		public virtual IDb Create()
		{
			var dbFactoryExtension = _options.FindExtension<DbFactoryExtension>() ?? throw new Exception("The database factory was not defined. Check your 'appsettings.json' file.");
			return dbFactoryExtension.Factory.Create(dbFactoryExtension.ConnectionString) ?? throw new Exception("The database factory did not create a valid object. Check your 'appsettings.json' file.");
		}

		public IDb Rent()
		{
			if (_pool.TryDequeue(out IDb db))
				return db;

			return Create();
		}

		public void Return(IDb db)
		{
			if (db != null)
			{
				if (_pool.Count < _maxPoolSize)
					_pool.Enqueue(db);
				else
				{
					db.Dispose();
				}
			}
		}
	}
}
