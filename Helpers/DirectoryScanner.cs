// <copyright file="DirectoryScanner.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    internal static class DirectoryScanner
    {
        internal static void Visit(
            string path,
            bool recursive,
            Func<bool> isCancelled,
            Action<FileSystemInfo> visit,
            Action<string, Exception>? onError = null)
        {
            Stack<string> pending = new();
            pending.Push(path);
            EnumerationOptions options = new()
            {
                AttributesToSkip = 0,
                IgnoreInaccessible = false,
                RecurseSubdirectories = false,
            };

            while (pending.Count > 0 && !isCancelled())
            {
                string current = pending.Pop();
                try
                {
                    foreach (FileSystemInfo entry in new DirectoryInfo(current).EnumerateFileSystemInfos("*", options))
                    {
                        if (isCancelled())
                        {
                            return;
                        }

                        visit(entry);

                        // Show junctions and symbolic links, but do not follow them recursively.
                        if (recursive && entry is DirectoryInfo && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(entry.FullName);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (onError == null)
                    {
                        throw;
                    }

                    onError(current, ex);
                }
            }
        }
    }
}
