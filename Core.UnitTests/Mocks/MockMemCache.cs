﻿using System;
using System.Threading.Tasks;
using PubComp.Caching.SystemRuntime;

namespace PubComp.Caching.Core.UnitTests.Mocks
{
    public class MockMemCache : ICache
    {
        private readonly MockCacheInner innerCache;

        public MockMemCache(string name)
        {
            this.innerCache = new MockCacheInner(name);
        }

        public int Hits { get { return this.innerCache.Hits; } }

        public int Misses { get { return this.innerCache.Misses; } }

        public string Name { get { return this.innerCache.Name; } }

        public bool TryGet<TValue>(string key, out TValue value)
        {
            return this.innerCache.TryGet(key, out value);
        }

        public async Task<TryGetResult<TValue>> TryGetAsync<TValue>(string key)
        {
            return await this.innerCache.TryGetAsync<TValue>(key);
        }

        public void Set<TValue>(string key, TValue value)
        {
            this.innerCache.Set(key, value);
        }

        public TValue Get<TValue>(string key, Func<TValue> getter)
        {
            return this.innerCache.Get(key, getter);
        }

        public async Task<TValue> GetAsync<TValue>(string key, Func<Task<TValue>> getter)
        {
            return await this.innerCache.GetAsync(key, getter);
        }

        public void Clear(string key)
        {
            this.innerCache.Clear(key);
        }

        public void ClearAll()
        {
            this.innerCache.ClearAll();
            this.innerCache.ResetCounters();
        }

        public void ClearAll(bool doResetCounters)
        {
            this.innerCache.ClearAll();

            if (doResetCounters)
                this.innerCache.ResetCounters();
        }

        public class MockCacheInner : ObjectCache
        {
            private int hits;
            private int misses;

            public MockCacheInner(string name)
                : base(name, new System.Runtime.Caching.MemoryCache(name),
                    new InMemoryPolicy
                    {
                        SlidingExpiration = new TimeSpan(0, 2, 0),
                    })
            {
            }

            protected override bool TryGetInner<TValue>(string key, out TValue value)
            {
                var item = InnerCache.Get(key, null) as CacheItem;

                if (item != null)
                {
                    hits++;
                    // ReSharper disable once CanBeReplacedWithTryCastAndCheckForNull
                    value = item.Value is TValue ? (TValue)item.Value : default(TValue);
                    return true;
                }

                misses++;
                value = default(TValue);
                return false;
            }

            public int Hits { get { return this.hits; } }

            public int Misses { get { return this.misses; } }

            internal void ResetCounters()
            {
                this.hits = 0;
                this.misses = 0;
            }
        }
    }
}
