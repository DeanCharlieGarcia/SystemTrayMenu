// <copyright file="NetworkShares.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Runtime.InteropServices;

    internal static class NetworkShares
    {
        internal static List<string> Discover(string server, Func<bool> isCancelled) => Discover(server, isCancelled, ReadPage);

        internal static List<string> Discover(string server, Func<bool> isCancelled, Func<string, uint, SharePage> readPage)
        {
            List<string> paths = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            uint resume = 0;
            while (!isCancelled())
            {
                SharePage page = readPage(server, resume);
                if (page.Status != 0 && page.Status != 234)
                {
                    if (page.Status == 5)
                    {
                        throw new UnauthorizedAccessException(new Win32Exception(page.Status).Message);
                    }

                    throw new Win32Exception(page.Status);
                }

                foreach (Share share in page.Shares)
                {
                    if (isCancelled())
                    {
                        return paths;
                    }

                    // Match visible disk shares; exclude printers, IPC and administrative shares.
                    if ((share.Type & 0x800000ff) == 0 && !share.Name.EndsWith('$') && seen.Add(share.Name))
                    {
                        paths.Add(Path.Combine(server, share.Name));
                    }
                }

                if (page.Status == 0)
                {
                    break;
                }

                if (page.Resume == resume)
                {
                    throw new IOException("Network share enumeration made no progress.");
                }

                resume = page.Resume;
            }

            return paths;
        }

        private static SharePage ReadPage(string server, uint resume)
        {
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int status = NativeMethods.NetShareEnum(server, 1, out buffer, 65536, out uint count, out _, ref resume);
                List<Share> shares = new();
                if (status == 0 || status == 234)
                {
                    int size = Marshal.SizeOf<ShareInfo>();
                    for (int i = 0; i < count; i++)
                    {
                        ShareInfo info = Marshal.PtrToStructure<ShareInfo>(IntPtr.Add(buffer, checked(i * size)));
                        string? name = Marshal.PtrToStringUni(info.Name);
                        if (!string.IsNullOrEmpty(name))
                        {
                            shares.Add(new Share(name, info.Type));
                        }
                    }
                }

                return new SharePage(status, resume, shares);
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    NativeMethods.NetApiBufferFree(buffer);
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShareInfo
        {
            internal IntPtr Name;
            internal uint Type;
            internal IntPtr Remark;
        }

        internal readonly record struct Share(string Name, uint Type);

        internal sealed record SharePage(int Status, uint Resume, IReadOnlyList<Share> Shares);

        private static class NativeMethods
        {
            [DllImport("Netapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
            internal static extern int NetShareEnum(string servername, uint level, out IntPtr buffer, uint preferredLength, out uint entriesRead, out uint totalEntries, ref uint resume);

            [DllImport("Netapi32.dll", ExactSpelling = true)]
            internal static extern int NetApiBufferFree(IntPtr buffer);
        }
    }
}
