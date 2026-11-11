// <copyright file="DirectoryChangeState.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Helpers
{
    using System.Threading;

    internal sealed class DirectoryChangeState
    {
        private long version;
        private int notificationPending;

        internal long Version => Interlocked.Read(ref version);

        internal bool RequestNotification()
        {
            Interlocked.Increment(ref version);
            return Interlocked.Exchange(ref notificationPending, 1) == 0;
        }

        internal void AcceptNotification() => Interlocked.Exchange(ref notificationPending, 0);
    }
}
