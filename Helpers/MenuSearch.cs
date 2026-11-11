// <copyright file="MenuSearch.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Helpers
{
    using System;

    internal static class MenuSearch
    {
        internal static string[] Prepare(string pattern) => pattern.Replace('%', ' ').Replace('*', ' ')
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        internal static bool Matches(string text, string[] terms)
        {
            foreach (string term in terms)
            {
                if (!text.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
