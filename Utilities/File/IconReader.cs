// <copyright file="IconReader.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>
// see also: https://www.codeproject.com/Articles/2532/Obtaining-and-managing-file-and-folder-icons-using.

namespace HitNGone.Utilities
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Drawing;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Windows;
    using System.Windows.Interop;
    using System.Windows.Media.Imaging;
    using HitNGone.DllImports;
    using HitNGone.Helpers;

    /// <summary>
    /// Provides static methods to read system icons for folders and files.
    /// </summary>
    internal static class IconReader
    {
        internal static readonly BitmapSource LoadingImage = (BitmapSource)Application.Current.Resources["LoadingIconImage"];
        internal static readonly BitmapSource NotFoundImage = (BitmapSource)Application.Current.Resources["NotFoundIconImage"];
        private static readonly BitmapSource OverlayImage = (BitmapSource)Application.Current.Resources["LinkArrowIconImage"];

        private static readonly BoundedCache<string, BitmapSource> IconDictPersistent = new(
            () => Properties.Settings.Default.ClearCacheIfMoreThanThisNumberOfItems, StringComparer.OrdinalIgnoreCase);

        private static readonly BoundedCache<string, BitmapSource> IconDictCache = new(
            () => Properties.Settings.Default.ClearCacheIfMoreThanThisNumberOfItems, StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, Lazy<BitmapSource>> PendingIcons = new(StringComparer.OrdinalIgnoreCase);
        private static readonly BlockingCollection<Action> IconFactoryQueueSTA = new(256);
        private static readonly List<Thread> IconFactoryThreadPoolSTA = new(2);
        private static readonly CancellationTokenSource ShutdownToken = new();
        private static readonly object CacheSync = new();
        private static long cacheGeneration;

        internal static int CachedIconCount => IconDictCache.Count + IconDictPersistent.Count;

        internal static void Startup()
        {
            LoadingImage.Freeze();
            NotFoundImage.Freeze();
            OverlayImage.Freeze();
            for (int i = 0; i < IconFactoryThreadPoolSTA.Capacity; i++)
            {
                Thread thread = new(IconFactoryWorkerSTA)
                {
                    Name = "IconFactory STA #" + i.ToString(),
                    IsBackground = true,
                };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                IconFactoryThreadPoolSTA.Add(thread);
            }

            static void IconFactoryWorkerSTA()
            {
                foreach (Action work in IconFactoryQueueSTA.GetConsumingEnumerable())
                {
                    if (ShutdownToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        work();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Failed to load an icon.", ex);
                    }
                }
            }
        }

        internal static void Shutdown()
        {
            ShutdownToken.Cancel();
            IconFactoryQueueSTA.CompleteAdding();
            foreach (Thread thread in IconFactoryThreadPoolSTA)
            {
                thread.Join(50);
            }

            IconFactoryThreadPoolSTA.Clear();
            while (IconFactoryQueueSTA.TryTake(out _))
            {
            }

            PendingIcons.Clear();
        }

        internal static void ClearCacheWhenLimitReached()
        {
            IconDictCache.TrimToLimit();
            IconDictPersistent.TrimToLimit();
        }

        internal static void InvalidateCache()
        {
            lock (CacheSync)
            {
                Interlocked.Increment(ref cacheGeneration);
                IconDictPersistent.Clear();
                IconDictCache.Clear();
            }
        }

        internal static void RemoveIconFromCache(string path)
        {
            lock (CacheSync)
            {
                Interlocked.Increment(ref cacheGeneration);
                IconDictPersistent.TryRemove(path, out _);
                IconDictCache.TryRemove(path, out _);
            }
        }

        /// <summary>
        /// Loads an Icon requested by parameters.
        /// </summary>
        /// <param name="isFolder">Icon is a folder or file icon.</param>
        /// <param name="path">Path to the file or directory entry.</param>
        /// <param name="resolvedPath">Path to the file or directory entry which 'path' is pointing at.</param>
        /// <param name="linkOverlay">Apply the link overlay to the icon.</param>
        /// <param name="persistentEntry">Load from or into persistent cache.</param>
        /// <param name="onIconLoaded">Callback called when icon got loaded.</param>
        /// <param name="isCancelled">Whether the requesting menu no longer needs the icon.</param>
        /// <returns>True = Icon was loaded synchronously, False = Icon will be loaded in the background.</returns>
        internal static bool GetIconAsync(
            bool isFolder,
            string path,
            string resolvedPath,
            bool linkOverlay,
            bool persistentEntry,
            Action<BitmapSource> onIconLoaded,
            Func<bool>? isCancelled = null)
        {
            string key = path;

            if (!isFolder)
            {
                string extension = Path.GetExtension(path);
                if (IsExtensionWithSameIcon(extension))
                {
                    // Generic file extension
                    key = extension + ":" + linkOverlay;
                    persistentEntry = true; // Always store them in persistent cache
                }
            }

            if (ShutdownToken.IsCancellationRequested || isCancelled?.Invoke() == true)
            {
                return false;
            }

            if (DictIconCache(persistentEntry).TryGetValue(key, out BitmapSource? cached) ||
                DictIconCache(!persistentEntry).TryGetValue(key, out cached))
            {
                onIconLoaded(cached);
                return true;
            }

            long generation = Interlocked.Read(ref cacheGeneration);
            string pendingKey = generation.ToString() + ":" + key;
            Lazy<BitmapSource> pending = PendingIcons.GetOrAdd(pendingKey, _ => new(() =>
            {
                BitmapSource icon;
                try
                {
                    icon = GetIconAsBitmapSourceSTA(path, resolvedPath, linkOverlay, isFolder);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Failed to read icon: {path}", ex);
                    icon = NotFoundImage;
                }

                lock (CacheSync)
                {
                    if (generation == Interlocked.Read(ref cacheGeneration))
                    {
                        DictIconCache(persistentEntry).TryAdd(key, icon);
                    }
                }

                return icon;
            }));

            try
            {
                // Producers run during directory discovery, never on the UI thread.
                // Waiting here bounds outstanding work without dropping icons from large menus.
                while (!IconFactoryQueueSTA.TryAdd(CompleteRequest, 50, ShutdownToken.Token))
                {
                    if (isCancelled?.Invoke() == true)
                    {
                        RemovePending();
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                RemovePending();
            }
            catch (InvalidOperationException) when (IconFactoryQueueSTA.IsAddingCompleted)
            {
                RemovePending();
            }

            return false;

            void RemovePending() => ((ICollection<KeyValuePair<string, Lazy<BitmapSource>>>)PendingIcons)
                .Remove(new(pendingKey, pending));

            void CompleteRequest()
            {
                try
                {
                    if (isCancelled?.Invoke() != true && !ShutdownToken.IsCancellationRequested)
                    {
                        onIconLoaded(pending.Value);
                    }
                }
                finally
                {
                    RemovePending();
                }
            }
        }

        internal static Icon? GetRootFolderIcon(string path)
        {
            NativeMethods.SHFILEINFO shFileInfo = default;
            bool linkOverlay = false;
            bool largeIcon = false;
            uint flags = GetFlags(linkOverlay, largeIcon);
            uint attribute = NativeMethods.FileAttributeDirectory;
            IntPtr imageList = NativeMethods.Shell32SHGetFileInfo(path, attribute, ref shFileInfo, (uint)Marshal.SizeOf(shFileInfo), flags);
            return TryGetIcon(path, linkOverlay, shFileInfo, imageList);
        }

        private static BitmapSource? TryCreateBitmapSourceFromIcon(string path, Icon icon)
        {
            BitmapSource? bitmap = null;
            try
            {
                bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to {nameof(TryCreateBitmapSourceFromIcon)}: {path} ", ex);
            }

            return bitmap;
        }

        private static BitmapSource? TryGetIconAsBitmapSourceSTA(string path, string resolvedPath, bool linkOverlay, bool isFolder)
        {
            BitmapSource? result = null;

            if (!isFolder &&
                Path.GetExtension(path).Equals(".ico", StringComparison.InvariantCultureIgnoreCase))
            {
                if (TryGetIconExtractAssociatedIcon(path, out Icon? icon))
                {
                    result = TryCreateBitmapSourceFromIcon(path, icon);
                    icon.Dispose();
                }
            }
            else if (!isFolder && File.Exists(resolvedPath) &&
                Path.GetExtension(resolvedPath).Equals(".ico", StringComparison.InvariantCultureIgnoreCase))
            {
                if (TryGetIconExtractAssociatedIcon(resolvedPath, out Icon? icon))
                {
                    result = TryCreateBitmapSourceFromIcon(resolvedPath, icon);
                    icon.Dispose();
                    if (result != null && linkOverlay)
                    {
                        result = ImagingHelper.CreateIconWithOverlay(result, OverlayImage);
                    }
                }
            }
            else
            {
                // This code block must run in an STA thread otherwise the results may be incorrectly loaded icons!
                NativeMethods.SHFILEINFO shFileInfo = default;
                bool largeIcon = // Note: large returns another folder icon than windows explorer
                    Scaling.Factor >= 1.25f ||
                    Scaling.FactorByDpi >= 1.25f ||
                    Properties.Settings.Default.IconSizeInPercent / 100f >= 1.25f;
                uint flags = GetFlags(linkOverlay, largeIcon);
                uint attribute = isFolder ? NativeMethods.FileAttributeDirectory : NativeMethods.FileAttributeNormal;
                IntPtr imageList = NativeMethods.Shell32SHGetFileInfo(path, attribute, ref shFileInfo, (uint)Marshal.SizeOf(shFileInfo), flags);
                Icon? icon = TryGetIcon(path, linkOverlay, shFileInfo, imageList);
                if (icon != null)
                {
                    result = TryCreateBitmapSourceFromIcon(path, icon);
                    icon.Dispose();
                }
            }

            return result;
        }

        private static bool TryGetIconExtractAssociatedIcon(string path, [NotNullWhen(true)] out Icon? icon)
        {
            Icon? iconOrNull = null;
            try
            {
                iconOrNull = Icon.ExtractAssociatedIcon(path);
            }
            catch (Exception ex)
            {
                Log.Warn($"Failed to {nameof(TryGetIconExtractAssociatedIcon)}: {path}", ex);
            }

            if (iconOrNull != null)
            {
                icon = iconOrNull;
                return true;
            }

            icon = null;
            return false;
        }

        private static BitmapSource GetIconAsBitmapSourceSTA(string path, string resolvedPath, bool linkOverlay, bool isFolder)
        {
            BitmapSource? bitmapSource = TryGetIconAsBitmapSourceSTA(path, resolvedPath, linkOverlay, isFolder);
            bitmapSource ??= NotFoundImage;
            bitmapSource.Freeze(); // Make it accessible for any thread
            return bitmapSource;
        }

        private static BoundedCache<string, BitmapSource> DictIconCache(bool checkPersistentFirst)
            => checkPersistentFirst ? IconDictPersistent : IconDictCache;

        private static bool IsExtensionWithSameIcon(string fileExtension)
        {
            return fileExtension.Length > 0 &&
                !fileExtension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                !fileExtension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
                !fileExtension.Equals(".ico", StringComparison.OrdinalIgnoreCase) &&
                !fileExtension.Equals(".url", StringComparison.OrdinalIgnoreCase);
        }

        private static uint GetFlags(bool linkOverlay, bool largeIcon)
        {
            uint flags = NativeMethods.ShgfiIcon | NativeMethods.ShgfiSYSICONINDEX;
            if (linkOverlay)
            {
                flags += NativeMethods.ShgfiLINKOVERLAY;
            }

            if (!largeIcon)
            {
                flags += NativeMethods.ShgfiSMALLICON;
            }
            else
            {
                flags += NativeMethods.ShgfiLARGEICON;
            }

            return flags;
        }

        private static Icon? TryGetIcon(
            string path, bool linkOverlay, NativeMethods.SHFILEINFO shFileInfo, IntPtr imageList)
        {
            Icon? icon = null;
            if (imageList != IntPtr.Zero)
            {
                IntPtr hIcon;
                if (linkOverlay)
                {
                    hIcon = shFileInfo.hIcon;
                }
                else
                {
                    hIcon = NativeMethods.ImageList_GetIcon(imageList, shFileInfo.iIcon, NativeMethods.IldTransparent);
                }

                try
                {
                    // Note: Destroying hIcon after FromHandle will invalidate the Icon, so we do NOT destroy it, despite the request from documentation:
                    //       https://learn.microsoft.com/en-us/dotnet/api/system.drawing.icon.fromhandle?view=dotnet-plat-ext-7.0
                    //       Reason is https://referencesource.microsoft.com/#System.Drawing/commonui/System/Drawing/Icon.cs,555 (FromHandle)
                    //       It is not taking over the ownership, data will be deleted upon destroying the original icon, so a clone is required.
                    //       With Clone we actually get a new handle, so we can free up the original handle without killing our copy.
                    //       Using Clone will also restore the ownership of the new icon handle, so we do not have to call DestroyIcon on it by ourself.
                    icon = (Icon)Icon.FromHandle(hIcon).Clone();
                }
                catch (Exception ex)
                {
                    Log.Warn($"path:'{path}'", ex);
                }

                if (!linkOverlay)
                {
                    _ = NativeMethods.User32DestroyIcon(hIcon);
                }

                _ = NativeMethods.User32DestroyIcon(shFileInfo.hIcon);
            }

            return icon;
        }
    }
}
