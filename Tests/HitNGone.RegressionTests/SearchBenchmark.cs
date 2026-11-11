namespace HitNGone.RegressionTests
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Text.Json;
    using HitNGone.Helpers;

    internal static class SearchBenchmark
    {
        internal static void Run()
        {
            string[] rows = Enumerable.Range(0, 2000).Select(i => $"Projekt Sommer Urlaub {i:D5}.jpg").ToArray();
            string query = "sommer urlaub";
            string[] terms = MenuSearch.Prepare(query);
            bool Legacy(string text)
            {
                foreach (string term in query.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!text.ToLower().Contains(term))
                    {
                        return false;
                    }
                }

                return true;
            }

            object Measure(Func<string, bool> match)
            {
                foreach (string row in rows)
                {
                    _ = match(row);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                Stopwatch watch = Stopwatch.StartNew();
                int matches = 0;
                for (int iteration = 0; iteration < 100; iteration++)
                {
                    foreach (string row in rows)
                    {
                        if (match(row))
                        {
                            matches++;
                        }
                    }
                }

                watch.Stop();
                return new { milliseconds = watch.Elapsed.TotalMilliseconds, allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before, matches };
            }

            Console.WriteLine(JsonSerializer.Serialize(new
            {
                measuredUtc = DateTime.UtcNow,
                framework = RuntimeInformation.FrameworkDescription,
                rows = rows.Length,
                iterations = 100,
                legacy = Measure(Legacy),
                optimized = Measure(text => MenuSearch.Matches(text, terms)),
                scope = "Search predicate only; excludes WPF rendering, file IO and full application opening.",
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
