// <copyright file="FileNameFilter.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Helpers
{
    using System;
    using System.IO.Enumeration;
    using System.Linq;

    internal sealed class FileNameFilter
    {
        private readonly string[] expressions;

        internal FileNameFilter(string patterns)
        {
            expressions = patterns.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(FileSystemName.TranslateWin32Expression).ToArray();
        }

        internal bool Matches(string name)
        {
            foreach (string expression in expressions)
            {
                if (FileSystemName.MatchesWin32Expression(expression, name))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
