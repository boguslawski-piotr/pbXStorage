﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using pbXNet;

// register (web page, etc.) -> repositoryId, repositoryPublicKey
// app has:
//   repositoryId
//   repositoryPublicKey
// server has:
//   repositoryId
//   repositoryPublicKey
//   repositoryPrivateKey

// app run & generate:
//   appPrivateKey
//   appPublicKey

// registerapp (post repositoryId) <- (appPublicKey) encrypted with repositoryPublicKey -> (appToken)
// app has:
//   appToken
// server has
//   appPublicKey

// open (get appToken, storageId) <- nothing -> (storageToken, storagePublicKey) encrypted with appPublicKey, and signed with repositoryPrivateKey
// app has:
//   storageToken
//   storagePublicKey
// server has
//   storageToken
//   storagePrivateKey

// store (put storageToken, thingId) <- (data) encrypted with storagePublicKey, and signed with appPrivateKey -> OK or error

// getACopy (get storageToken, thingId) <- nothing -> (modifiedOn,data) encrypted with appPublicKey, and signed with storagePrivateKey

// findIds (get storageToken, pattern) <- nothing -> (ids list separated with |) encrypted with appPublicKey, and signed with storagePrivateKey

namespace pbXStorage.Repositories
{
	public class Manager
	{
		#region Properties

		public string Id { get; set; }

		ConcurrentDictionary<string, Repository> _repositories = new ConcurrentDictionary<string, Repository>();

		ConcurrentDictionary<string, App> _apps = new ConcurrentDictionary<string, App>();

		ConcurrentDictionary<string, Storage> _storages = new ConcurrentDictionary<string, Storage>();

		TimeSpan _objectsLifeTime;

		#endregion

		#region Constructor

		public Manager(string id, TimeSpan objectsLifeTime)
		{
			Id = id ?? throw new ArgumentException($"{nameof(Id)} must be valid object.");

			_objectsLifeTime = objectsLifeTime;
		}

		#endregion

		#region GC

		readonly SemaphoreSlim _gcLock = new SemaphoreSlim(1);

		int GCStorages()
		{
			int oc = 0;
			foreach (var s in _storages.Where((s) => DateTime.Now - s.Value.AccesedOn > _objectsLifeTime))
			{
				if (_storages.TryRemove(s.Key, out Storage _))
				{
					Log.I($"removed storage '{s.Value.App.Token}/{s.Value.Id}' from memory.", this);
					oc++;
				}
			}
			return oc;
		}

		int GCApps()
		{
			int oc = 0;
			foreach (var a in _apps.Where((a) => DateTime.Now - a.Value.AccesedOn > _objectsLifeTime
												 && !_storages.Values.Any((_storage) => (_storage.App.Token == a.Value.Token)))
			)
			{
				if (_apps.TryRemove(a.Key, out App _))
				{
					Log.I($"removed app '{a.Value.Repository.Id}/{a.Value.Token}' from memory.", this);
					oc++;
				}
			}
			return oc;
		}

		int GCRepositories()
		{
			int oc = 0;
			foreach (var r in _repositories.Where((r) => DateTime.Now - r.Value.AccesedOn > _objectsLifeTime
												 && !_apps.Values.Any((_app) => (_app.Repository.Id == r.Value.Id)))
			)
			{
				if (_repositories.TryRemove(r.Key, out Repository _))
				{
					Log.I($"removed repository '{r.Value.Id}' from memory.", this);
					oc++;
				}
			}
			return oc;
		}

		async Task GCAsync()
		{
			await _gcLock.WaitAsync().ConfigureAwait(false);
			try
			{
				int oc = GCStorages();
				oc += GCApps();
				oc += GCRepositories();
				if(oc > 64)
					GC.Collect();
			}
			catch (Exception ex)
			{
				Log.E(ex.Message, this);
				// no rethrow!
			}
			finally
			{
				_gcLock.Release();
			}
		}

		#endregion

		#region Repositories

		public async Task<Repository> NewRepositoryAsync(Context ctx, string name)
		{
			await GCAsync();

			try
			{
				Repository repository = await Repository.NewAsync(ctx, Id, name);
				_repositories[repository.Id] = repository;

				Log.D($"created '{name}, {repository.Id}'.", this);

				return repository;
			}
			catch (Exception ex)
			{
				Log.E(ex.Message, this);
				throw ex;
			}
		}

		public async Task<Repository> GetRepositoryAsync(Context ctx, string repositoryId)
		{
			try
			{
				if (!_repositories.TryGetValue(repositoryId, out Repository repository))
				{
					repository = await Repository.LoadAsync(ctx, Id, repositoryId);
					_repositories[repository.Id] = repository;
				}

				repository.AccesedOn = DateTime.Now;
				return repository;
			}
			catch (Exception ex)
			{
				Log.E(ex.Message, this);
				throw ex;
			}
		}

		public async Task RemoveRepositoryAsync(Context ctx, string repositoryId)
		{
			try
			{
				_repositories.TryRemove(repositoryId, out Repository _);
				await Repository.RemoveAsync(ctx, Id, repositoryId);

				Log.D($"removed repository '{repositoryId}'.", this);

				await GCAsync();
			}
			catch (Exception ex)
			{
				Log.E(ex.Message, this);
				throw ex;
			}
		}

		#endregion

		#region Apps

		public async Task<string> RegisterAppAsync(Context ctx, string repositoryId, string appPublicKey)
		{
			await GCAsync();

			Repository repository = null;
			try
			{
				repository = await GetRepositoryAsync(ctx, repositoryId);
			}
			catch (Exception)
			{
				return ERROR(PbXStorageErrorCode.RepositoryDoesNotExist, Localized.T("SOPXS_RepoDoesntExist"));
			}

			try
			{
				appPublicKey = FROMBODY(appPublicKey);

				App app = _apps.Values.ToList().Find((_app) => (_app.PublicKey == appPublicKey && _app.Repository.Id == repository.Id));
				if (app == null)
				{
					app = new App(repository, appPublicKey);
					_apps[app.Token] = app;

					Log.D($"created '{repository.Id}/{app.Token}'.", this);
				}
				else
					Log.D($"using '{repository.Id}/{app.Token}'.", this);

				app.AccesedOn = DateTime.Now;
				return OK(app.Token);
			}
			catch (Exception ex)
			{
				return ERROR(PbXStorageErrorCode.AppRegistrationFailed, ex);
			}
		}

		#endregion

		#region Storages

		public async Task<string> OpenStorageAsync(Context ctx, string appToken, string storageId)
		{
			await GCAsync();

			if (!_apps.TryGetValue(appToken, out App app))
				return ERROR(PbXStorageErrorCode.IncorrectAppToken, Localized.T("SOPXS_IncorrectAppToken"));

			try
			{
				Storage storage = _storages.Values.ToList().Find((_storage) => (_storage.App.Token == appToken && _storage.Id == storageId));
				if (storage == null)
				{
					storage = new Storage(app, storageId);
					_storages[storage.Token] = storage;

					Log.D($"created '{app.Token}/{storageId}'.", this);
				}
				else
					Log.D($"opened '{app.Token}/{storageId}'.", this);

				storage.AccesedOn = DateTime.Now;
				return OK(storage.TokenAndPublicKey);
			}
			catch (Exception ex)
			{
				return ERROR(PbXStorageErrorCode.OpenStorageFailed, ex);
			}
		}

		#endregion

		#region Things

		async Task<string> ExecuteInStorage(string storageToken, Func<Storage, Task<string>> action, [CallerMemberName]string callerName = null)
		{
			if (!_storages.TryGetValue(storageToken, out Storage storage))
				return ERROR(PbXStorageErrorCode.IncorrectStorageToken, Localized.T("SOPXS_IncorrectStorageToken"), callerName);

			try
			{
				Task<string> task = action(storage);
				return await task.ConfigureAwait(false);
			}
			catch (StorageThingNotFoundException ex)
			{
				return ERROR(PbXStorageErrorCode.ThingNotFound, ex, callerName);
			}
			catch (Exception ex)
			{
				return ERROR(PbXStorageErrorCode.ThingOperationFailed, ex, callerName);
			}
		}

		public async Task<string> StoreThingAsync(Context ctx, string storageToken, string thingId, string data)
		{
			return await ExecuteInStorage(storageToken, async (Storage storage) =>
			{
				await storage.StoreAsync(ctx, thingId, FROMBODY(data)).ConfigureAwait(false);
				return OK();
			}).ConfigureAwait(false);
		}

		public async Task<string> ThingExistsAsync(Context ctx, string storageToken, string thingId)
		{
			return await ExecuteInStorage(storageToken, async (Storage storage) =>
			{
				return OK(await storage.ExistsAsync(ctx, thingId).ConfigureAwait(false));
			}).ConfigureAwait(false);
		}

		public async Task<string> GetThingModifiedOnAsync(Context ctx, string storageToken, string thingId)
		{
			return await ExecuteInStorage(storageToken, async (Storage storage) =>
			{
				return OK(await storage.GetModifiedOnAsync(ctx, thingId).ConfigureAwait(false));
			}).ConfigureAwait(false);
		}

		public async Task<string> GetThingCopyAsync(Context ctx, string storageToken, string thingId)
		{
			return await ExecuteInStorage(storageToken, async (Storage storage) =>
			{
				return OK(await storage.GetACopyAsync(ctx, thingId).ConfigureAwait(false));
			}).ConfigureAwait(false);
		}

		public async Task<string> DiscardThingAsync(Context ctx, string storageToken, string thingId)
		{
			return await ExecuteInStorage(storageToken, async (Storage storage) =>
			{
				await storage.DiscardAsync(ctx, thingId).ConfigureAwait(false);
				return OK();
			}).ConfigureAwait(false);
		}

		public async Task<string> FindThingIdsAsync(Context ctx, string storageToken, string pattern)
		{
			return await ExecuteInStorage(storageToken, async (Storage storage) =>
			{
				return OK(await storage.FindIdsAsync(ctx, pattern).ConfigureAwait(false));
			}).ConfigureAwait(false);
		}

		#endregion

		#region Tools

		string FROMBODY(string data)
		{
			return Obfuscator.DeObfuscate(data);
		}

		string OK(string data = null, [CallerMemberName]string callerName = null)
		{
			data = "OK" + (data != null ? $",{data}" : "");
			Log.D(data, this, callerName);
			return Obfuscator.Obfuscate(data);
		}

		string ERROR(PbXStorageErrorCode error, Exception ex, [CallerMemberName]string callerName = null)
		{
			string message = ex.Message;

			if (ex.InnerException != null)
				message += $" {ex.InnerException.Message + (ex.InnerException.Message.EndsWith(".") ? "" : ".")}";

			return ERROR(error, message, callerName);
		}

		string ERROR(PbXStorageErrorCode error, string message, [CallerMemberName]string callerName = null)
		{
			message = $"{(int)error},{message}";

			if (error != PbXStorageErrorCode.ThingNotFound)
				Log.E(message, this, callerName);
			else
				Log.D(message, this, callerName);

			return Obfuscator.Obfuscate($"ERROR,{message}");
		}

		#endregion
	}
}
