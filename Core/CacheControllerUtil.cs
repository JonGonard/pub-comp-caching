﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace PubComp.Caching.Core
{
    public class CacheControllerUtil
    {
        private readonly ConcurrentDictionary<string, bool> registeredCacheNames;
        private readonly ConcurrentDictionary<Tuple<string, string>, Func<object>> registeredCacheItems;

        public CacheControllerUtil()
        {
            this.registeredCacheNames = new ConcurrentDictionary<string, bool>();
            this.registeredCacheItems = new ConcurrentDictionary<Tuple<string, string>, Func<object>>();
        }

        /// <summary>
        /// Clears all data from a named cache instance
        /// </summary>
        /// <param name="cacheName"></param>
        public void ClearCache(string cacheName)
        {
            if (string.IsNullOrEmpty(cacheName))
            {
                throw new CacheException("Cache not cleared - received undefined cacheName");
            }

            var cache = CacheManager.GetCache(cacheName);
            
            if (cache == null)
            {
                throw new CacheException("Cache not cleared - cache not found: " + cacheName);
            }

            if (cache.Name != cacheName)
            {
                throw new CacheException("Cache not cleared - due to fallback to a general cache: " + cacheName);
            }

            bool doEnableClear;
            if (this.registeredCacheNames.TryGetValue(cacheName, out doEnableClear))
            {
                if (!doEnableClear)
                {
                    throw new CacheException("Cache not cleared - cache registered with doEnableClearEntireCache=False");
                }
            }

            cache.ClearAll();
        }

        /// <summary>
        /// Clears a specific cache item in a named cache instance by key
        /// </summary>
        /// <param name="cacheName"></param>
        /// <param name="itemKey"></param>
        public void ClearCacheItem(string cacheName, string itemKey)
        {
            if (string.IsNullOrEmpty(cacheName))
            {
                throw new CacheException("Cache item not cleared - received undefined cacheName");
            }

            if (string.IsNullOrEmpty(itemKey))
            {
                throw new CacheException("Cache item not cleared - received undefined itemKey");
            }

            var cache = CacheManager.GetCache(cacheName);

            if (cache == null)
            {
                throw new CacheException("Cache item not cleared - cache not found: " + cacheName);
            }

            cache.Clear(itemKey);
        }

        /// <summary>
        /// Register a named cache instance for remote clear access via controller
        /// </summary>
        /// <param name="cacheName"></param>
        /// <param name="doEnableClearEntireCache"></param>
        public void RegisterCache(string cacheName, bool doEnableClearEntireCache)
        {
            if (string.IsNullOrEmpty(cacheName))
            {
                throw new CacheException("Cache not registered - received undefined cacheName");
            }

            this.registeredCacheNames.AddOrUpdate(
                cacheName,
                name => doEnableClearEntireCache,
                (name, existingValue) => doEnableClearEntireCache);
        }

        /// <summary>
        /// Register a cache item for remote clear/refresh access via controller
        /// </summary>
        /// <typeparam name="TItem"></typeparam>
        /// <param name="getterExpression"></param>
        /// <param name="doInitialize"></param>
        public void RegisterCacheItem<TItem>(Expression<Func<TItem>> getterExpression, bool doInitialize)
            where TItem : class
        {
            if (getterExpression == null)
            {
                throw new CacheException("Cache item not registered - received undefined getterExpression");
            }

            MethodInfo method;
            object[] arguments;
            LambdaHelper.GetMethodInfoAndArguments(getterExpression, out method, out arguments);

            // CacheListAttribute is intentionally not supported, as cacheItemKey vary according to input
            // these should be dealt with by using a dedicated cache and clearing this entire dedicated cache
            var cacheName = method.GetCustomAttributesData()
                .Where(a =>
                    a.AttributeType.FullName == "PubComp.Caching.AopCaching.CacheAttribute"
                    && a.ConstructorArguments.Any()
                    && a.ConstructorArguments.First().ArgumentType == typeof(string))
                .Select(a => (a.ConstructorArguments.First().Value ?? string.Empty).ToString())
                .FirstOrDefault();

            var methodType = method.DeclaringType;

            if (methodType == null)
            {
                throw new CacheException("Cache item not registered - invalid getterExpression");
            }

            if (cacheName == null)
                cacheName = methodType.FullName;

            if (string.IsNullOrEmpty(cacheName))
            {
                throw new CacheException("Cache item not registered - received undefined cacheName");
            }

            var itemKey = CacheKey.GetKey(getterExpression);

            if (string.IsNullOrEmpty(itemKey))
            {
                throw new CacheException("Cache item not registered - received undefined itemKey");
            }

            this.registeredCacheNames.GetOrAdd(cacheName, false);

            var getter = getterExpression.Compile();

            Func<Tuple<string, string>, Func<object>, Func<object>> updateGetter
                = (k, o) => getter;

            this.registeredCacheItems.AddOrUpdate(
                Tuple.Create(cacheName, itemKey), getter, updateGetter);

            if (doInitialize)
            {
                if (!TrySetCacheItem(cacheName, itemKey, getter))
                {
                    throw new CacheException("Cache item not initialized - cache not defined: " + cacheName);
                }
            }
        }

        /// <summary>
        /// Register a cache item for remote clear access via controller
        /// </summary>
        /// <typeparam name="TItem"></typeparam>
        /// <param name="cacheName"></param>
        /// <param name="itemKey"></param>
        protected void RegisterCacheItem<TItem>(string cacheName, string itemKey)
            where TItem : class
        {
            RegisterCacheItem<TItem>(cacheName, itemKey, null, false);
        }

        /// <summary>
        /// Register a cache item for remote clear/refresh access via controller
        /// </summary>
        /// <typeparam name="TItem"></typeparam>
        /// <param name="cacheName"></param>
        /// <param name="itemKey"></param>
        /// <param name="getter"></param>
        /// <param name="doInitialize"></param>
        protected void RegisterCacheItem<TItem>(
            string cacheName, string itemKey, Func<TItem> getter, bool doInitialize)
            where TItem : class
        {
            if (string.IsNullOrEmpty(cacheName))
            {
                throw new CacheException("Cache item not registered - received undefined cacheName");
            }

            if (string.IsNullOrEmpty(itemKey))
            {
                throw new CacheException("Cache item not registered - received undefined itemKey");
            }

            this.registeredCacheNames.GetOrAdd(cacheName, false);

            Func<Tuple<string, string>, Func<object>, Func<object>> updateGetter
                = (k, o) => getter;

            this.registeredCacheItems.AddOrUpdate(
                Tuple.Create(cacheName, itemKey), getter, updateGetter);

            if (doInitialize)
            {
                if (!TrySetCacheItem(cacheName, itemKey, getter))
                {
                    throw new CacheException("Cache item not initialized - cache not defined: " + cacheName);
                }
            }
        }

        /// <summary>
        /// Gets names of all registered cache instances
        /// </summary>
        public IEnumerable<string> GetRegisteredCacheNames()
        {
            return this.registeredCacheNames.Keys.ToList();
        }

        /// <summary>
        /// Gets keys of all registered cache items a specific in a named cache instance
        /// </summary>
        /// <param name="cacheName"></param>
        public IEnumerable<string> GetRegisteredCacheItemKeys(string cacheName)
        {
            if (string.IsNullOrEmpty(cacheName))
            {
                throw new CacheException("Received undefined cacheName");
            }

            return this.registeredCacheItems.Keys.ToList()
                .Where(k => k.Item1 == cacheName)
                .Select(k => k.Item2)
                .ToList();
        }

        /// <summary>
        /// Refreshes a specific registered cache item in a named cache instance by key
        /// </summary>
        /// <param name="cacheName"></param>
        /// <param name="itemKey"></param>
        public void RefreshCacheItem(string cacheName, string itemKey)
        {
            if (string.IsNullOrEmpty(cacheName))
            {
                throw new CacheException("Cache item not refreshed - received undefined cacheName");
            }

            if (string.IsNullOrEmpty(itemKey))
            {
                throw new CacheException("Cache item not refreshed - received undefined itemKey");
            }

            Func<object> registeredGetter;

            if (this.registeredCacheItems.TryGetValue(Tuple.Create(cacheName, itemKey), out registeredGetter))
            {
                if (registeredGetter == null)
                {
                    throw new CacheException(string.Concat(
                        "Cache item not refreshed - getter not defined: ", cacheName, "/", itemKey));
                }

                if (!TrySetCacheItem(cacheName, itemKey, registeredGetter))
                {
                    throw new CacheException("Cache item not refresh - cache not defined: " + cacheName);
                }

                return;
            }

            throw new CacheException(string.Concat(
                "Cache item not refreshed - item is not registered: ", cacheName, "/", itemKey));
        }

        private bool TrySetCacheItem(string cacheName, string itemKey, Func<object> getter)
        {
            var cache = CacheManager.GetCache(cacheName);
            if (cache == null)
            {
                return false;
            }

            cache.Set(itemKey, getter());
            return true;
        }
    }
}
