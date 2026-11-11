namespace HitNGone.RegressionTests
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Threading;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;
    using HitNGone.Utilities;
    using HitNGone.Business;
    using HitNGone.DataClasses;
    using HitNGone.Helpers;

    internal static class Program
    {
        private static int passed;

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Contains("--benchmark"))
            {
                SearchBenchmark.Run();
                return;
            }

            Run("IPC round trips empty, Unicode, 256-byte and large messages", RoundTrips);
            Run("IPC reads fragmented headers and payloads", FragmentedRead);
            Run("IPC handles repeated pipe connections and a long handshake", PipeConnections);
            Run("IPC rejects truncated headers and payloads", TruncatedRead);
            Run("IPC rejects oversized, negative and odd byte lengths", InvalidLengths);
            Run("Rename respects directory boundaries and Windows casing", RenameBoundaries);
            Run("Rename changes only the ancestor and preserves directory separators", RenameAncestor);
            Run("Old submenu result cannot target another row at the same level", DifferentParent);
            Run("Old submenu result cannot target a reopened instance of the same row", ReopenedParent);
            Run("Search matches all terms without case sensitivity", SearchTerms);
            Run("Search retains wildcard separators and empty query behavior", SearchSeparators);
            Run("File filters match Windows enumeration without overlapping duplicates", FileFilters);
            Run("Directory scan supports recursion and cancellation", ScanCancellation);
            Run("Directory scan reports missing directories and preserves hidden entries", ScanErrors);
            Run("Network shares handle pages, types, cancellation and errors", NetworkSharePages);
            Run("Cache evicts oldest entries, handles invalidation and shrinks", CacheLimits);
            Run("Cache remains bounded under concurrent writers", ConcurrentCache);
            Run("Watcher bursts are coalesced without losing snapshot versions", WatcherBursts);
            Run("Watchers recover from unavailable paths and ignore obsolete errors", WatcherRecovery);
            Run("Watcher timer reconnects a missing directory and receives changes", WatcherTimerRecovery);
            Run("HitNGone branding and translated resources resolve", BrandingResources);
            Run("Legacy settings import preserves values and existing configuration", SettingsMigration);
            Run("Icons and virtualized menu work without visible windows", IconWorkers);
            Console.WriteLine($"Passed {passed} regression groups.");
        }

        private static void BrandingResources()
        {
            var assembly = typeof(RowData).Assembly;
            Equal("HitNGone", assembly.GetName().Name);
            Equal("HitNGone", System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyProductAttribute>(assembly)!.Product);
            var resources = new System.Resources.ResourceManager("HitNGone.Resources.Languages.lang", assembly);
            Equal("Über HitNGone", resources.GetString("About HitNGone", System.Globalization.CultureInfo.GetCultureInfo("de")));
            Equal(true, assembly.GetManifestResourceNames().Contains("HitNGone.g.resources"));
        }

        private static void SettingsMigration() => WithDirectory(root =>
        {
            string source = Path.Combine(root, "legacy.config");
            string destination = Path.Combine(root, "HitNGone", "user.config");
            string xml = "<configuration><userSettings><SystemTrayMenu.Properties.Settings><setting name=\"PathDirectory\" serializeAs=\"String\">C:\\Fotos &amp; Musik</setting></SystemTrayMenu.Properties.Settings></userSettings></configuration>";
            File.WriteAllText(source, xml);
            HitNGone.Properties.LegacySettings.Import(source, destination);
            var migrated = System.Xml.Linq.XDocument.Load(destination);
            Equal("C:\\Fotos & Musik", migrated.Root!.Element("userSettings")!.Element("HitNGone.Properties.Settings")!.Element("setting")!.Value);
            Equal(xml, File.ReadAllText(source));
            string first = File.ReadAllText(destination);
            File.WriteAllText(source, "invalid");
            HitNGone.Properties.LegacySettings.Import(source, destination);
            Equal(first, File.ReadAllText(destination));
            HitNGone.Properties.LegacySettings.Normalize(migrated);
            Equal(1, migrated.Root!.Element("userSettings")!.Elements().Count());
            string oldInstall = Directory.CreateDirectory(Path.Combine(root, "old-app")).FullName;
            string files = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
            var relative = System.Xml.Linq.XDocument.Parse(xml);
            relative.Descendants("setting").Single().Value = "..\\files";
            HitNGone.Properties.LegacySettings.Normalize(relative, oldInstall);
            Equal(files, relative.Descendants("setting").Single().Value);
        });

        private static void WatcherTimerRecovery()
        {
            string path = Path.Combine(Path.GetTempPath(), "HitNGone-watcher-" + Guid.NewGuid());
            using ManualResetEventSlim failed = new();
            using ManualResetEventSlim connected = new();
            using ManualResetEventSlim changed = new();
            using RecoveringDirectoryWatcher registration = new(
                invalidated =>
                {
                    FileSystemWatcher watcher = new();
                    try
                    {
                        watcher.Path = path;
                        watcher.Created += (_, _) => changed.Set();
                        watcher.Error += (_, _) => invalidated();
                        watcher.EnableRaisingEvents = true;
                        return watcher;
                    }
                    catch
                    {
                        watcher.Dispose();
                        throw;
                    }
                },
                () => connected.Set(),
                _ => failed.Set());
            try
            {
                registration.Start();
                Equal(true, failed.Wait(TimeSpan.FromSeconds(5)));
                Directory.CreateDirectory(path);
                Equal(true, connected.Wait(TimeSpan.FromSeconds(15)));
                File.WriteAllText(Path.Combine(path, "new.txt"), "test");
                Equal(true, changed.Wait(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                registration.Dispose();
                File.Delete(Path.Combine(path, "new.txt"));
                if (Directory.Exists(path))
                {
                    Directory.Delete(path);
                }
            }
        }

        private static void WatcherRecovery()
        {
            int attempts = 0;
            int refreshes = 0;
            int errors = 0;
            Action? fail = null;
            MemoryStream? active = null;
            using RecoveringDirectoryWatcher registration = new(
                failed =>
                {
                    attempts++;
                    if (attempts == 1)
                    {
                        throw new IOException("Share unavailable");
                    }

                    fail = failed;
                    active = new MemoryStream();
                    return active;
                },
                () => refreshes++,
                _ => errors++);
            registration.Connect();
            Equal(1, errors);
            registration.Connect();
            Equal(1, refreshes);
            registration.Connect();
            Equal(2, attempts);
            Action obsolete = fail!;
            obsolete();
            Equal(false, active!.CanRead);
            Equal(2, refreshes);
            registration.Connect();
            Equal(3, attempts);
            obsolete();
            Equal(true, active!.CanRead);
            Equal(3, refreshes);
            registration.Dispose();
            Equal(false, active.CanRead);
            fail!();
            registration.Connect();
            Equal(3, attempts);

            using ManualResetEventSlim entered = new();
            using ManualResetEventSlim release = new();
            MemoryStream late = new();
            int lateRefreshes = 0;
            using RecoveringDirectoryWatcher pending = new(
                _ =>
                {
                    entered.Set();
                    release.Wait();
                    return late;
                },
                () => lateRefreshes++,
                _ => { });
            Task connecting = Task.Run(pending.Connect);
            try
            {
                Equal(true, entered.Wait(TimeSpan.FromSeconds(5)));
                pending.Connect();
                pending.Dispose();
            }
            finally
            {
                release.Set();
            }

            Equal(true, connecting.Wait(TimeSpan.FromSeconds(5)));
            Equal(false, late.CanRead);
            Equal(0, lateRefreshes);
        }

        private static void NetworkSharePages()
        {
            int calls = 0;
            var paths = NetworkShares.Discover(@"\\server", () => false, (server, resume) =>
            {
                Equal(@"\\server", server);
                calls++;
                return resume == 0
                    ? new NetworkShares.SharePage(234, 7, new[] { new NetworkShares.Share("Fotos Urlaub", 0), new NetworkShares.Share("Printer", 1), new NetworkShares.Share("IPC$", 3), new NetworkShares.Share("Admin", 0x80000000) })
                    : new NetworkShares.SharePage(0, 0, new[] { new NetworkShares.Share("FOTOS URLAUB", 0), new NetworkShares.Share("Daten", 0) });
            });
            Equal(2, calls);
            Equal(2, paths.Count);
            Equal(@"\\server\Fotos Urlaub", paths[0]);
            Equal(@"\\server\Daten", paths[1]);
            Equal(0, NetworkShares.Discover(@"\\server", () => true, (_, _) => throw new Exception("Cancelled call reached API")).Count);
            bool cancelled = false;
            Equal(0, NetworkShares.Discover(@"\\server", () => cancelled, (_, _) =>
            {
                cancelled = true;
                return new NetworkShares.SharePage(234, 1, new[] { new NetworkShares.Share("Daten", 0) });
            }).Count);
            try
            {
                NetworkShares.Discover(@"\\server", () => false, (_, _) => new NetworkShares.SharePage(5, 0, Array.Empty<NetworkShares.Share>()));
                throw new Exception("Access denial was ignored");
            }
            catch (UnauthorizedAccessException)
            {
            }

            try
            {
                NetworkShares.Discover(@"\\server", () => false, (_, _) => new NetworkShares.SharePage(234, 0, Array.Empty<NetworkShares.Share>()));
                throw new Exception("Non-progressing enumeration was accepted");
            }
            catch (IOException)
            {
            }
        }

        private static void Run(string name, Action test)
        {
            test();
            passed++;
            Console.WriteLine("PASS " + name);
        }

        private static void RoundTrips()
        {
            foreach (string value in new[] { string.Empty, "wakeup", "Grüße 東京 🚀", new string('x', 128), new string('z', 40000) })
            {
                using MemoryStream stream = new();
                var protocol = new IpcPipe.IpcPipeStream(stream);
                int written = protocol.WriteString(value);
                Equal(stream.Length, (long)written);
                stream.Position = 0;
                Equal(value, protocol.ReadString());
                Equal(stream.Length, stream.Position);
            }
        }

        private static void PipeConnections()
        {
            string name = "HitNGone-test-" + Guid.NewGuid().ToString("N") + new string('x', 90);
            using var server = new IpcPipe(1, name);
            server.StartServer(value => value);
            for (int i = 0; i < 5; i++)
            {
                using var client = new IpcPipe(1, name);
                string value = new string('ä', 500) + i;
                Task<string?> response = Task.Run(() => client.SendToServer(value));
                if (!response.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Pipe request did not complete.");
                }

                Equal(value, response.Result);
            }
        }

        private static void FragmentedRead()
        {
            using MemoryStream output = new();
            var writer = new IpcPipe.IpcPipeStream(output);
            string value = new string('ä', 500);
            writer.WriteString(value);
            writer.WriteString("next frame");
            using var input = new FragmentedStream(output.ToArray());
            var reader = new IpcPipe.IpcPipeStream(input);
            Equal(value, reader.ReadString());
            Equal("next frame", reader.ReadString());
        }

        private static void TruncatedRead()
        {
            foreach (byte[] bytes in new[] { Array.Empty<byte>(), new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 0, 4, 65, 0 } })
            {
                using MemoryStream stream = new(bytes);
                Throws<EndOfStreamException>(() => new IpcPipe.IpcPipeStream(stream).ReadString());
            }
        }

        private static void InvalidLengths()
        {
            foreach (byte[] header in new[] { new byte[] { 0, 16, 0, 2 }, new byte[] { 255, 255, 255, 254 }, new byte[] { 0, 0, 0, 1 } })
            {
                using MemoryStream stream = new(header);
                Throws<InvalidDataException>(() => new IpcPipe.IpcPipeStream(stream).ReadString());
            }

            using MemoryStream output = new();
            Throws<InvalidDataException>(() => new IpcPipe.IpcPipeStream(output).WriteString(new string('x', 524289)));
            Equal(0L, output.Length);
        }

        private static void RenameBoundaries()
        {
            Equal(false, WindowsPath.IsSameOrDescendant(@"C:\Root\Foobar\file.txt", @"C:\Root\Foo"));
            Equal(true, WindowsPath.IsSameOrDescendant(@"c:\root\FOO\file.txt", @"C:\Root\Foo"));
            Equal(true, WindowsPath.IsSameOrDescendant(@"C:\Root\Foo\", @"C:\Root\Foo"));
            Equal(true, WindowsPath.IsSameOrDescendant(@"\\server\share\folder", @"\\server\share"));
        }

        private static void RenameAncestor()
        {
            Equal(@"C:\New\sub\", WindowsPath.ReplaceAncestor(@"C:\Old\sub\", @"C:\Old", @"C:\New"));
            Equal(@"C:\Foobar\x", WindowsPath.ReplaceAncestor(@"C:\Foobar\x", @"C:\Foo", @"C:\Other"));
            Equal(@"C:\New\", WindowsPath.ReplaceAncestor(@"C:\Old\", @"C:\Old\", @"C:\New\"));
        }

        private static RowData MakeParent() => (RowData)RuntimeHelpers.GetUninitializedObject(typeof(RowData));

        private static void DifferentParent()
        {
            // Constructors resolve files and shell state; this test only needs request identity.
            RowData first = MakeParent();
            RowData second = MakeParent();
            first.LoadRequestId = second.LoadRequestId = Guid.NewGuid();
            MenuData result = new(first, first.LoadRequestId);
            Equal(true, result.MatchesRequest(first));
            Equal(false, result.MatchesRequest(second));
            Equal(false, result.MatchesRequest(null));
        }

        private static void ReopenedParent()
        {
            RowData parent = MakeParent();
            parent.LoadRequestId = Guid.NewGuid();
            MenuData oldResult = new(parent, parent.LoadRequestId);
            parent.LoadRequestId = Guid.NewGuid();
            Equal(false, oldResult.MatchesRequest(parent));
            Equal(true, new MenuData(parent, parent.LoadRequestId).MatchesRequest(parent));
        }

        private static void SearchTerms()
        {
            Equal(true, MenuSearch.Matches("Sommer Urlaub 2026.JPG", MenuSearch.Prepare("urlaub sommer")));
            Equal(false, MenuSearch.Matches("Sommer Urlaub.JPG", MenuSearch.Prepare("urlaub winter")));
            Equal(true, MenuSearch.Matches("Grüße aus Köln", MenuSearch.Prepare("GRÜßE köln")));
        }

        private static void SearchSeparators()
        {
            Equal(2, MenuSearch.Prepare("  sommer%*urlaub  ").Length);
            Equal(true, MenuSearch.Matches("anything", MenuSearch.Prepare("% * ")));
            Equal(true, MenuSearch.Matches("anything", MenuSearch.Prepare(string.Empty)));
        }

        private static void WithDirectory(Action<string> test)
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "HitNGone-regression-" + Guid.NewGuid().ToString("N")));
            if (!root.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Test directory is outside temporary storage.");
            }

            Directory.CreateDirectory(root);
            try
            {
                test(root);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static void FileFilters() => WithDirectory(root =>
        {
            foreach (string name in new[] { "readme", "a.txt", "B.TXT", "photo.jpg", "a.b.txt" })
            {
                File.WriteAllText(Path.Combine(root, name), string.Empty);
            }

            foreach (string patterns in new[] { "*.*", "*.txt|*.TXT|a.*", "?.txt|*.jpg", "*|*.txt" })
            {
                var expected = patterns.Split('|').SelectMany(pattern => Directory.GetFiles(root, pattern))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                FileNameFilter filter = new(patterns);
                var actual = Directory.GetFiles(root).Where(file => filter.Matches(Path.GetFileName(file))).ToArray();
                Equal(expected.Count, actual.Length);
                Equal(true, expected.SetEquals(actual));
            }
        });

        private static void ScanCancellation() => WithDirectory(root =>
        {
            string child = Directory.CreateDirectory(Path.Combine(root, "child")).FullName;
            File.WriteAllText(Path.Combine(root, "root.txt"), string.Empty);
            File.WriteAllText(Path.Combine(child, "nested.txt"), string.Empty);
            int count = 0;
            DirectoryScanner.Visit(root, true, () => false, _ => count++);
            Equal(3, count);
            count = 0;
            DirectoryScanner.Visit(root, false, () => false, _ => count++);
            Equal(2, count);
            count = 0;
            DirectoryScanner.Visit(root, true, () => count > 0, _ => count++);
            Equal(1, count);
            DirectoryScanner.Visit(Path.Combine(root, "missing"), true, () => true, _ => throw new Exception("Cancelled scan visited an entry."));
        });

        private static void ScanErrors() => WithDirectory(root =>
        {
            int errors = 0;
            DirectoryScanner.Visit(Path.Combine(root, "missing"), true, () => false, _ => { }, (_, _) => errors++);
            Equal(1, errors);
            Throws<DirectoryNotFoundException>(() => DirectoryScanner.Visit(Path.Combine(root, "missing"), false, () => false, _ => { }));
            string hidden = Path.Combine(root, "hidden.txt");
            File.WriteAllText(hidden, string.Empty);
            File.SetAttributes(hidden, FileAttributes.Hidden);
            int count = 0;
            DirectoryScanner.Visit(root, false, () => false, _ => count++);
            Equal(1, count);
        });

        private static void CacheLimits()
        {
            int limit = 2;
            BoundedCache<string, int> cache = new(() => limit, StringComparer.OrdinalIgnoreCase);
            cache.TryAdd("A", 1);
            cache.TryAdd("B", 2);
            cache.TryAdd("C", 3);
            Equal(false, cache.TryGetValue("a", out _));
            Equal(true, cache.TryGetValue("b", out int value));
            Equal(2, value);
            cache.TryRemove("B", out _);
            cache.TryAdd("B", 4);
            cache.TryAdd("D", 5);
            Equal(false, cache.TryGetValue("C", out _));
            Equal(2, cache.Count);
            limit = 1;
            cache.TrimToLimit();
            Equal(1, cache.Count);
            Equal(true, cache.TryGetValue("d", out _));
        }

        private static void ConcurrentCache()
        {
            BoundedCache<int, int> cache = new(() => 16, System.Collections.Generic.EqualityComparer<int>.Default);
            Parallel.For(0, 1000, i =>
            {
                cache.TryAdd(i, i);
                cache.TryGetValue(i, out _);
                if (cache.Count > 16)
                {
                    throw new InvalidOperationException("Cache exceeded its limit.");
                }
            });
        }

        private static void WatcherBursts()
        {
            DirectoryChangeState state = new();
            int queued = 0;
            Parallel.For(0, 1000, _ =>
            {
                if (state.RequestNotification())
                {
                    Interlocked.Increment(ref queued);
                }
            });
            Equal(1, queued);
            Equal(1000L, state.Version);
            long snapshotVersion = state.Version;
            state.AcceptNotification();
            Equal(true, state.RequestNotification());
            Equal(false, snapshotVersion == state.Version);
        }

        private static void IconWorkers() => WithDirectory(root =>
        {
            Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            BitmapSource bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            bitmap.Freeze();
            app.Resources["LoadingIconImage"] = bitmap;
            app.Resources["NotFoundIconImage"] = bitmap;
            app.Resources["LinkArrowIconImage"] = bitmap;
            app.Resources[typeof(Window)] = new Style(typeof(Window));
            app.Resources[typeof(ListBoxItem)] = new Style(typeof(ListBoxItem));
            app.Resources["ButtonHoverColor"] = Brushes.Gray;
            int originalLimit = HitNGone.Properties.Settings.Default.ClearCacheIfMoreThanThisNumberOfItems;
            HitNGone.Properties.Settings.Default.ClearCacheIfMoreThanThisNumberOfItems = 4;
            IconReader.Startup();
            try
            {
                // The main thread deliberately does not pump the dispatcher: workers must finish without it.
                using CountdownEvent complete = new(20);
                int wrongThread = 0;
                for (int i = 0; i < 20; i++)
                {
                    string folder = Directory.CreateDirectory(Path.Combine(root, "folder" + i)).FullName;
                    IconReader.GetIconAsync(true, folder, folder, false, false, icon =>
                    {
                        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA || !icon.IsFrozen)
                        {
                            Interlocked.Increment(ref wrongThread);
                        }

                        complete.Signal();
                    });
                }

                if (!complete.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("Icon workers depended on the blocked UI dispatcher.");
                }

                Equal(0, wrongThread);
                Equal(true, IconReader.CachedIconCount <= 8);
                Equal(false, IconReader.GetIconAsync(true, root, root, false, false, _ => throw new Exception("Cancelled request completed."), () => true));
                Run("Localized hotkeys round trip across cultures", HotkeyParsing);
                Run("Native hotkeys preserve registrations on invalid input and conflicts", HotkeyRegistration);
                VirtualizedMenu(root);
                AdoptedIcons(bitmap);
            }
            finally
            {
                IconReader.Shutdown();
                HitNGone.Properties.Settings.Default.ClearCacheIfMoreThanThisNumberOfItems = originalLimit;
                app.Shutdown();
            }
        });

        private static void HotkeyParsing()
        {
            var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
            var originalUiCulture = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                foreach (string culture in new[] { "de-DE", "en-US", "tr-TR" })
                {
                    System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
                    System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
                    Equal((System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Windows, System.Windows.Input.Key.LWin), GlobalHotkeys.ModifiersAndKeyFromInvariantString(" Strg + LWin "));
                    Equal((System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift, System.Windows.Input.Key.F12), GlobalHotkeys.ModifiersAndKeyFromInvariantString("sTrG + uMsChAlT + F12"));
                    foreach (string text in new[] { "Ctrl+Alt+F24", "Strg+LWin", "Umschalt+F12", "Win+F12", "F9", "" })
                    {
                        var parsed = GlobalHotkeys.ModifiersAndKeyFromInvariantString(text);
                        string serialized = GlobalHotkeys.ModifiersAndKeyToInvariantString(parsed.Modifiers, parsed.Key);
                        Equal(parsed, GlobalHotkeys.ModifiersAndKeyFromInvariantString(serialized));
                        Equal(false, serialized.Contains("Strg"));
                        Equal(false, serialized.Contains("Umschalt"));
                    }

                    foreach (string invalid in new[] { "StrgUnknown+F12", "Ctrl++F12", "+F12", "Ctrl+", "Ctrl+NoSuchKey" })
                    {
                        Equal((System.Windows.Input.ModifierKeys.None, System.Windows.Input.Key.None), GlobalHotkeys.ModifiersAndKeyFromInvariantString(invalid));
                    }
                }
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = originalCulture;
                System.Globalization.CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        private static void HotkeyRegistration()
        {
            var first = GlobalHotkeys.Create();
            var second = GlobalHotkeys.Create();
            try
            {
                bool registered = false;
                foreach (var key in new[] { System.Windows.Input.Key.F24, System.Windows.Input.Key.F23, System.Windows.Input.Key.F22 })
                {
                    try
                    {
                        first.Register(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Shift, key);
                        registered = true;
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                Equal(true, registered);
                string original = first.GetHotkeyInvariantString();
                Throws<InvalidOperationException>(() => first.Register("StrgUnknown+F12"));
                Equal(original, first.GetHotkeyInvariantString());
                Throws<InvalidOperationException>(() => second.Register(original));
                Equal(original, first.GetHotkeyInvariantString());
                Equal(System.Windows.Input.Key.None, second.GetKey());
                first.Unregister();
                second.Register(original);
                Equal(original, second.GetHotkeyInvariantString());
                second.Register("");
                Equal(System.Windows.Input.Key.None, second.GetKey());
                first.Register(original);
                first.Register(System.Windows.Input.ModifierKeys.None, System.Windows.Input.Key.None);
                Equal(System.Windows.Input.Key.None, first.GetKey());
            }
            finally
            {
                first.Unregister();
                second.Unregister();
            }
        }

        private static void AdoptedIcons(BitmapSource first)
        {
            RowData target = MakeParent();
            target.HiddenEntry = true;
            RowData oldSource = MakeParent();
            RowData newSource = MakeParent();
            BitmapSource latest = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 1, 1, 1, 255 }, 4);
            latest.Freeze();
            var loading = typeof(RowData).GetProperty("IconLoading", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            loading.SetValue(oldSource, true);
            target.AdoptIcon(oldSource);
            newSource.ColumnIcon = latest;
            target.AdoptIcon(newSource);
            Equal(true, ReferenceEquals(latest, target.ColumnIcon));
            loading.SetValue(oldSource, false);
            oldSource.ColumnIcon = first;
            Equal(true, ReferenceEquals(latest, target.ColumnIcon));
        }

        private static void VirtualizedMenu(string root)
        {
            var menu = new HitNGone.UserInterface.Menu(null, root);
            try
            {
                var rows = Enumerable.Range(0, 2000).Select(i =>
                {
                    RowData row = MakeParent();
                    row.ColumnText = "Entry " + i;
                    return row;
                }).ToList();
                menu.AddItemsToMenu(rows, null);
                ListView view = menu.GetDataGridView();
                view.Width = 300;
                view.Height = 200;
                void Layout()
                {
                    view.ApplyTemplate();
                    view.Measure(new Size(300, 200));
                    view.Arrange(new Rect(0, 0, 300, 200));
                    view.UpdateLayout();
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                    view.UpdateLayout();
                }

                Layout();
                int realized = Enumerable.Range(0, rows.Count).Count(i => view.ItemContainerGenerator.ContainerFromIndex(i) != null);
                Equal(true, realized > 0 && realized < 100);
                view.ScrollIntoView(rows[1500]);
                Layout();
                Rect target = menu.GetDataGridViewChildRect(rows[1500]);
                Equal(true, target.Top >= -1 && target.Top < view.Height);
                CollectionViewSource.GetDefaultView(view.ItemsSource).Filter = item => ((RowData)item).ColumnText.EndsWith("0");
                view.ScrollIntoView(rows[1500]);
                Layout();
                Equal(200, view.Items.Count);
                target = menu.GetDataGridViewChildRect(rows[1500]);
                Equal(true, target.Top >= -1 && target.Top < view.Height);
            }
            finally
            {
                menu.Close();
            }
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException($"Expected {expected}, got {actual}.");
            }
        }

        private static void Throws<T>(Action action)
            where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }

            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }

        private sealed class FragmentedStream : MemoryStream
        {
            internal FragmentedStream(byte[] buffer)
                : base(buffer)
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 1));
        }
    }
}
