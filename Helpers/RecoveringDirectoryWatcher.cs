// <copyright file="RecoveringDirectoryWatcher.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Helpers
{
    using System;
    using System.Threading;

    internal sealed class RecoveringDirectoryWatcher : IDisposable
    {
        private readonly object gate = new();
        private readonly Func<Action, IDisposable> connect;
        private readonly Action refresh;
        private readonly Action<Exception> reportError;
        private readonly Timer timer;
        private IDisposable? watcher;
        private bool connecting;
        private bool disposed;
        private long version;
        private int retrySeconds = 5;

        internal RecoveringDirectoryWatcher(Func<Action, IDisposable> connect, Action refresh, Action<Exception> reportError)
        {
            this.connect = connect;
            this.refresh = refresh;
            this.reportError = reportError;
            timer = new Timer(_ => Connect(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Dispose()
        {
            IDisposable? previous;
            lock (gate)
            {
                disposed = true;
                timer.Dispose();
                previous = watcher;
                watcher = null;
            }

            previous?.Dispose();
        }

        internal void Start() => timer.Change(0, Timeout.Infinite);

        internal void Connect()
        {
            long attempt;
            lock (gate)
            {
                if (disposed || connecting || watcher != null)
                {
                    return;
                }

                connecting = true;
                attempt = version;
            }

            IDisposable? created = null;
            bool connected = false;
            try
            {
                created = connect(() => Invalidate(attempt));
                lock (gate)
                {
                    if (!disposed && attempt == version)
                    {
                        watcher = created;
                        created = null;
                        retrySeconds = 5;
                        connected = true;
                    }
                }
            }
            catch (Exception ex)
            {
                reportError(ex);
            }
            finally
            {
                created?.Dispose();
                lock (gate)
                {
                    connecting = false;
                    if (!disposed && watcher == null)
                    {
                        timer.Change(TimeSpan.FromSeconds(retrySeconds), Timeout.InfiniteTimeSpan);
                        retrySeconds = Math.Min(60, retrySeconds * 2);
                    }
                }
            }

            if (connected)
            {
                refresh();
            }
        }

        private void Invalidate(long expectedVersion)
        {
            IDisposable? previous;
            lock (gate)
            {
                if (disposed || expectedVersion != version)
                {
                    return;
                }

                version++;
                previous = watcher;
                watcher = null;
                timer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
            }

            previous?.Dispose();
            refresh();
        }
    }
}
