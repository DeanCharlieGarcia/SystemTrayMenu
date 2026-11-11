// <copyright file="BoundedCache.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    internal sealed class BoundedCache<TKey, TValue>
        where TKey : notnull
    {
        private readonly object sync = new();
        private readonly Dictionary<TKey, (TValue Value, LinkedListNode<TKey> Node)> entries;
        private readonly LinkedList<TKey> order = new();
        private readonly Func<int> limit;

        internal BoundedCache(Func<int> limit, IEqualityComparer<TKey> comparer)
        {
            this.limit = limit;
            entries = new(comparer);
        }

        internal int Count
        {
            get
            {
                lock (sync)
                {
                    return entries.Count;
                }
            }
        }

        internal bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            lock (sync)
            {
                if (entries.TryGetValue(key, out var entry))
                {
                    value = entry.Value;
                    return true;
                }

                value = default;
                return false;
            }
        }

        internal bool TryAdd(TKey key, TValue value)
        {
            lock (sync)
            {
                if (entries.ContainsKey(key))
                {
                    return false;
                }

                entries.Add(key, (value, order.AddLast(key)));
                TrimToLimit();
                return true;
            }
        }

        internal bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            lock (sync)
            {
                if (entries.Remove(key, out var entry))
                {
                    order.Remove(entry.Node);
                    value = entry.Value;
                    return true;
                }

                value = default;
                return false;
            }
        }

        internal void Clear()
        {
            lock (sync)
            {
                entries.Clear();
                order.Clear();
            }
        }

        internal void TrimToLimit()
        {
            lock (sync)
            {
                int maximum = Math.Max(1, limit());
                while (entries.Count > maximum && order.First != null)
                {
                    entries.Remove(order.First.Value);
                    order.RemoveFirst();
                }
            }
        }
    }
}
