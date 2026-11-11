// <copyright file="App.xaml.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone
{
    using System;
    using System.Drawing;
    using System.IO;
    using System.Windows;
    using HitNGone.Business;
    using HitNGone.Helpers;
    using HitNGone.Properties;
    using HitNGone.Utilities;

    /// <summary>
    /// App contains the notifyicon, the taskbarform and the menus.
    /// </summary>
    public partial class App : Application, IDisposable
    {
        private Menus? menus;
        private JoystickHelper? joystickHelper;
        private bool isDisposed;

        public App()
        {
            AppContext.SetSwitch("Switch.System.Windows.Controls.Text.UseAdornerForTextboxSelectionRendering", false);

            InitializeComponent();

            AppRestart.BeforeRestarting += Dispose;

            Activated += (_, _) => IsActiveApp = true;
            Deactivated += (_, _) => IsActiveApp = false;
            Startup += (_, _) =>
            {
                IconReader.Startup();

                menus = new();
                menus.Startup();

                if (Settings.Default.SupportGamepad)
                {
                    joystickHelper = new();
                    joystickHelper.KeyPressed += menus.KeyPressed;
                }
            };
        }

        internal static bool IsActiveApp { get; private set; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Loads an Icon from the application's Resources.
        /// Note: Only allowed to be called after App's Startup event.
        /// </summary>
        /// <param name="resourceName">Absolute file path from root directory.</param>
        /// <returns>New Icon object.</returns>
        internal static Icon LoadIconFromResource(string resourceName)
        {
            using (Stream stream = GetResourceStream(new("pack://application:,,,/" + resourceName, UriKind.Absolute)).Stream)
            {
                return new(stream);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                IconReader.Shutdown();

                if (joystickHelper != null)
                {
                    if (menus != null)
                    {
                        joystickHelper.KeyPressed -= menus.KeyPressed;
                    }

                    joystickHelper.Dispose();
                }

                menus?.Dispose();

                isDisposed = true;
            }
        }
    }
}
