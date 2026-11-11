// <copyright file="LegacySettings.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone.Properties
{
    using System.IO;
    using System.Xml.Linq;

    internal static class LegacySettings
    {
        internal static void Import(string source, string destination, string? legacyDirectory = null)
        {
            if (File.Exists(destination) || !File.Exists(source))
            {
                return;
            }

            XDocument document = XDocument.Load(source);
            Normalize(document, legacyDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write);
            document.Save(output);
        }

        internal static void Normalize(XDocument document, string? legacyDirectory = null)
        {
            XElement? settings = document.Root?.Element("userSettings");
            XElement? legacy = settings?.Element("SystemTrayMenu.Properties.Settings");
            if (legacy != null && settings?.Element("HitNGone.Properties.Settings") == null)
            {
                legacy.Name = "HitNGone.Properties.Settings";
                if (legacyDirectory != null)
                {
                    foreach (XElement setting in legacy.Elements("setting"))
                    {
                        string? name = (string?)setting.Attribute("name");
                        if ((name == "PathDirectory" || name == "PathIcoDirectory") &&
                            !string.IsNullOrWhiteSpace(setting.Value) && !Path.IsPathRooted(setting.Value))
                        {
                            string absolute = Path.GetFullPath(setting.Value, legacyDirectory);
                            if (Directory.Exists(absolute))
                            {
                                setting.Value = absolute;
                            }
                        }
                    }
                }
            }
        }
    }
}
