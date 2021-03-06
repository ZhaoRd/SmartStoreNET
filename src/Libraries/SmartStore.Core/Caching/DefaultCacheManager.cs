using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using SmartStore.Utilities;
using SmartStore.Utilities.Threading;

namespace SmartStore.Core.Caching
{

	public static class ICacheManagerExtensions
	{
		public static T Get<T>(this ICacheManager cacheManager, string key)
		{
			return cacheManager.Get<T>(key, () => { return default(T); });
		}
	}

    public partial class CacheManager<TCache> : ICacheManager where TCache : ICache
    {
		private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim();
		private readonly ICache _cache;

		// Wwe put a special string into cache if value is null,
		// otherwise our 'Contains()' would always return false,
		// which is bad if we intentionally wanted to save NULL values.
		private const string FAKE_NULL = "__[NULL]__";

		public CacheManager(Func<Type, ICache> fn)
        {
            this._cache = fn(typeof(TCache));
        }

        public T Get<T>(string key, Func<T> acquirer, int? cacheTime = null)
        {
			Guard.ArgumentNotEmpty(() => key);
			
			if (_cache.Contains(key))
			{
				return GetExisting<T>(key);
			}

			using (EnterReadLock())
			{
				if (!_cache.Contains(key))
				{
					var value = acquirer();
					this.Set(key, value, cacheTime);

					return value;
				}
			}

			return GetExisting<T>(key);
		}

		private T GetExisting<T>(string key)
		{
			var value = _cache.Get(key);

			if (value.Equals(FAKE_NULL))
				return default(T);

			return (T)_cache.Get(key);
		}

		public void Set(string key, object value, int? cacheTime = null)
		{
			Guard.ArgumentNotEmpty(() => key);

			using (EnterWriteLock())
			{
				_cache.Set(key, value ?? FAKE_NULL, cacheTime);
			}
		}

        public bool Contains(string key)
        {
			Guard.ArgumentNotEmpty(() => key);
			
			return _cache.Contains(key);
        }

        public void Remove(string key)
        {
			Guard.ArgumentNotEmpty(() => key);

			using (EnterWriteLock())
			{
				_cache.Remove(key);
			}
        }

        public void RemoveByPattern(string pattern)
        {
			Guard.ArgumentNotEmpty(() => pattern);
			
			var regex = new Regex(pattern, RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var keysToRemove = new List<String>();

            foreach (var item in _cache.Entries)
            {
                if (regex.IsMatch(item.Key))
                {
                    keysToRemove.Add(item.Key);
                }
            }

			using (EnterWriteLock())
			{
				foreach (string key in keysToRemove)
				{
					_cache.Remove(key);
				}
			}
        }

        public void Clear()
        {
            var keysToRemove = new List<string>();
            foreach (var item in _cache.Entries)
            {
                keysToRemove.Add(item.Key);
            }

			using (EnterWriteLock())
			{
				foreach (string key in keysToRemove)
				{
					_cache.Remove(key);
				}
			}
        }

		private IDisposable EnterReadLock()
		{
			return _cache.IsSingleton ? _rwLock.GetUpgradeableReadLock() : ActionDisposable.Empty;
		}

		public IDisposable EnterWriteLock()
		{
			return _cache.IsSingleton ? _rwLock.GetWriteLock() : ActionDisposable.Empty;
		}

	}
}