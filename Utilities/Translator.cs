// <copyright file="Translator.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Utilities
{
    using System.Globalization;
    using System.Resources;
    using HitNGone.Properties;
    using HitNGone.UserInterface;

    internal static class Translator
    {
        private static readonly ResourceManager Resources = new(
            "HitNGone.Resources.Languages.lang", typeof(Menu).Assembly);

        private static CultureInfo? culture;

        internal static void Initialize()
        {
            if (string.IsNullOrEmpty(
                Settings.Default.CurrentCultureInfoName))
            {
                Settings.Default.CurrentCultureInfoName = "en";
                Settings.Default.Save();
            }

            culture = CultureInfo.CreateSpecificCulture(
                Settings.Default.CurrentCultureInfoName);
        }

        internal static string GetText(string id) => Resources.GetString(id, culture) ?? id;
    }
}
