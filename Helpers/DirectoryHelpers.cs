// <copyright file="DirectoryHelpers.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Data;
    using System.IO;
    using System.Linq;
    using HitNGone.DataClasses;
    using HitNGone.Utilities;

    internal static class DirectoryHelpers
    {
        internal static void DiscoverItems(BackgroundWorker? worker, string path, ref MenuData menuData)
        {
            bool isNetworkRoot = false;
            try
            {
                isNetworkRoot = FileLnk.IsNetworkRoot(path);
                if (isNetworkRoot)
                {
                    DiscoverNetworkRootDirectories(worker, path, ref menuData);
                }
                else
                {
                    DiscoverLocalDirectories(worker, path, ref menuData);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"path:'{path}'", ex);
                if (ex is UnauthorizedAccessException)
                {
                    menuData.DirectoryState = MenuDataDirectoryState.NoAccess;
                    return;
                }
            }

            if (worker?.CancellationPending == true)
            {
                return;
            }

            if (menuData.Level == 0)
            {
                foreach (var additionalPath in GetAddionalPathsForMainMenu())
                {
                    if (worker?.CancellationPending == true)
                    {
                        return;
                    }

                    DiscoverDirectory(worker, additionalPath.Path, true, additionalPath.OnlyFiles, additionalPath.Recursive, ref menuData);
                }
            }

            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            menuData.RowDatas.RemoveAll(row => !paths.Add(row.Path));
            RemoveHiddenOrReadIcons(worker, isNetworkRoot, ref menuData);
            if (worker?.CancellationPending == true)
            {
                return;
            }

            if (menuData.RowDatas.Count == 0)
            {
                menuData.DirectoryState = MenuDataDirectoryState.Empty;
            }
            else
            {
                if (worker?.CancellationPending == true)
                {
                    return;
                }

                menuData.RowDatas = SortItems(menuData.RowDatas);
                menuData.DirectoryState = MenuDataDirectoryState.Valid;
            }
        }

        internal static IEnumerable<(string Path, bool Recursive, bool OnlyFiles)> GetAddionalPathsForMainMenu()
        {
            foreach (string pathAndRecursivString in Properties.Settings.Default.PathsAddToMainMenu.Split(@"|"))
            {
                if (string.IsNullOrEmpty(pathAndRecursivString))
                {
                    continue;
                }

                string pathAddForMainMenu = pathAndRecursivString.Split("recursiv:")[0].Trim();
                bool recursive = pathAndRecursivString.Split("recursiv:")[1].StartsWith("True");
                bool onlyFiles = pathAndRecursivString.Split("onlyFiles:")[1].StartsWith("True");
                yield return (Path: pathAddForMainMenu, Recursive: recursive, OnlyFiles: onlyFiles);
            }
        }

        internal static List<RowData> SortItems(List<RowData> rowDatas)
        {
            if (Properties.Settings.Default.SortByTypeAndNameWindowsExplorerSort)
            {
                rowDatas = rowDatas.OrderByDescending(x => x.IsFolder)
                    .ThenBy(x => x.ColumnText, new WindowsExplorerSort()).ToList();
            }
            else if (Properties.Settings.Default.SortByTypeAndDate)
            {
                rowDatas = rowDatas.OrderByDescending(x => x.IsFolder)
                    .ThenByDescending(x => x.FileInfo.LastWriteTime).ToList();
            }
            else if (Properties.Settings.Default.SortByFileExtensionAndName)
            {
                rowDatas = rowDatas.OrderBy(x => x.FileExtension).ThenBy(x => x.ColumnText).ToList();
            }
            else if (Properties.Settings.Default.SortByName)
            {
                rowDatas = rowDatas.OrderBy(x => x.ColumnText).ToList();
            }
            else if (Properties.Settings.Default.SortByDate)
            {
                rowDatas = rowDatas.OrderByDescending(x => x.FileInfo.LastWriteTime).ToList();
            }

            return rowDatas;
        }

        private static void RemoveHiddenOrReadIcons(BackgroundWorker? worker, bool isNetworkRoot, ref MenuData menuData)
        {
            HashSet<RowData> rowDatasToRemove = new();
            foreach (RowData rowData in menuData.RowDatas)
            {
                if (worker?.CancellationPending == true)
                {
                    return;
                }

                if (!isNetworkRoot)
                {
                    FolderOptions.ReadHiddenAttributes(rowData.Path, out bool hasHiddenFlag, out bool isDirectoryToHide);
                    if (isDirectoryToHide)
                    {
                        rowDatasToRemove.Add(rowData);
                        continue;
                    }

                    rowData.HiddenEntry = hasHiddenFlag;
                }

                rowData.LoadIcon(menuData.Level == 0, () => worker?.CancellationPending == true);
            }

            menuData.RowDatas.RemoveAll(rowDatasToRemove.Contains);
        }

        private static void DiscoverNetworkRootDirectories(BackgroundWorker? worker, string path, ref MenuData menuData)
        {
            foreach (string directory in NetworkShares.Discover(path, () => worker?.CancellationPending == true))
            {
                menuData.RowDatas.Add(new RowData(true, false, menuData.Level, directory));
            }
        }

        private static void DiscoverLocalDirectories(BackgroundWorker? worker, string path, ref MenuData menuData)
        {
            if (!Directory.Exists(path))
            {
                // Happens most likely when a shortcut is pointing to an absent target path
                Log.Info($"path:'{path}' does not exist");
                return;
            }

            DiscoverDirectory(worker, path, false, false, false, ref menuData);
        }

        private static void DiscoverDirectory(
            BackgroundWorker? worker,
            string path,
            bool isAdditional,
            bool onlyFiles,
            bool recursive,
            ref MenuData menuData)
        {
            List<RowData> rows = menuData.RowDatas;
            int level = menuData.Level;
            FileNameFilter filter = new(Config.SearchPattern);
            DirectoryScanner.Visit(
                path,
                recursive,
                () => worker?.CancellationPending == true,
                entry =>
                {
                    bool isFolder = entry is DirectoryInfo;
                    if ((isFolder && !onlyFiles) || (!isFolder && filter.Matches(entry.Name)))
                    {
                        rows.Add(new RowData(isFolder, isAdditional, level, entry.FullName));
                    }
                },
                isAdditional ? (failedPath, ex) => Log.Warn($"Failed to scan directory: {failedPath}", ex) : null);
        }
    }
}
