// <copyright file="MenuData.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.DataClasses
{
    using System;
    using System.Collections.Generic;

    internal enum MenuDataDirectoryState
    {
        /// <summary>
        /// State not defined or data still loading.
        /// </summary>
        Undefined,

        /// <summary>
        /// Data is available.
        /// </summary>
        Valid,

        /// <summary>
        /// Loading finished but no data available.
        /// </summary>
        Empty,

        /// <summary>
        /// Loading failed, so no data available.
        /// </summary>
        NoAccess,
    }

    internal struct MenuData
    {
        public MenuData(RowData? rowDataParent, Guid requestId = default)
        {
            RowDataParent = rowDataParent;
            RequestId = requestId;
            if (rowDataParent != null)
            {
                Level = rowDataParent.Level + 1;
            }
            else
            {
                Level = 0;
            }
        }

        internal Guid RequestId { get; }

        internal int Level { get; }

        internal RowData? RowDataParent { get; }

        internal List<RowData> RowDatas { get; set; } = new ();

        internal MenuDataDirectoryState DirectoryState { get; set; } = MenuDataDirectoryState.Undefined;

        internal bool MatchesRequest(RowData? parent) =>
            parent != null && ReferenceEquals(RowDataParent, parent) && RequestId == parent.LoadRequestId;
    }
}
