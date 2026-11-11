// <copyright file="WindowsPath.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Helpers
{
    using System;

    internal static class WindowsPath
    {
        internal static bool IsSameOrDescendant(string path, string parent)
        {
            string normalizedParent = parent.TrimEnd('\\');
            string normalizedPath = path.TrimEnd('\\');
            return string.Equals(normalizedPath, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith(normalizedParent + "\\", StringComparison.OrdinalIgnoreCase);
        }

        internal static string ReplaceAncestor(string path, string oldParent, string newParent)
        {
            if (!IsSameOrDescendant(path, oldParent))
            {
                return path;
            }

            return newParent.TrimEnd('\\') + path[oldParent.TrimEnd('\\').Length..];
        }
    }
}
