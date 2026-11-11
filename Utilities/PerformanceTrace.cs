// <copyright file="PerformanceTrace.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Utilities
{
    using System;
    using System.Diagnostics;
    using System.Globalization;

    internal static class PerformanceTrace
    {
        private static readonly bool Enabled = Environment.GetEnvironmentVariable("HITNGONE_DIAGNOSTICS") == "1";

        internal static IDisposable? Measure(string operation) => Enabled ? new Measurement(operation) : null;

        private sealed class Measurement : IDisposable
        {
            private readonly string operation;
            private readonly Stopwatch watch = Stopwatch.StartNew();

            internal Measurement(string operation)
            {
                this.operation = operation;
            }

            public void Dispose()
            {
                watch.Stop();
                try
                {
                    using Process process = Process.GetCurrentProcess();
                    Log.Info(string.Format(
                        CultureInfo.InvariantCulture,
                        "Performance: {0}; elapsedMs={1:F2}; privateBytes={2}; workingSetBytes={3}; threads={4}; handles={5}",
                        operation,
                        watch.Elapsed.TotalMilliseconds,
                        process.PrivateMemorySize64,
                        process.WorkingSet64,
                        process.Threads.Count,
                        process.HandleCount));
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not collect performance diagnostics.", ex);
                }
            }
        }
    }
}
