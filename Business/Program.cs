// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace HitNGone
{
    using System;
    using System.Windows;
    using HitNGone.Utilities;

    internal static class Program
    {
        private static bool isStartup = true;

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                Log.Initialize();
                Translator.Initialize();
                Config.SetFolderByWindowsContextMenu(args);
                Config.LoadOrSetByUser();
                Config.Initialize();
                PrivilegeChecker.Initialize();

                // Without a valid path we cannot do anything, just close application
                if (string.IsNullOrEmpty(Config.Path))
                {
                    MessageBox.Show(
                        Translator.GetText("Your root directory for the app does not exist or is empty! Change the root directory or put some files, directories or shortcuts into the root directory."),
                        "HitNGone",
                        MessageBoxButton.OK);
                    return;
                }

                if (SingleAppInstance.Initialize())
                {
                    AppDomain currentDomain = AppDomain.CurrentDomain;
                    currentDomain.UnhandledException += (sender, args)
                        => AskUserSendError((Exception)args.ExceptionObject);

                    Scaling.Initialize();
                    FolderOptions.Initialize();

                    using App app = new ();
                    isStartup = false;
                    Log.WriteApplicationRuns();
                    app.Run();
                }
            }
            catch (Exception ex)
            {
                AskUserSendError(ex);
            }
            finally
            {
                SingleAppInstance.Unload();
                Log.Close();
            }

            static void AskUserSendError(Exception ex)
            {
                Log.Error("Application Crashed", ex);

                MessageBoxResult dialogResult = MessageBox.Show(
                    "HitNGone encountered an error. Details were saved to the local log." +
                    Environment.NewLine + Environment.NewLine +
                    "Restart HitNGone?",
                    "HitNGone",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (!isStartup && dialogResult == MessageBoxResult.Yes)
                {
                    AppRestart.ByThreadException();
                }
            }
        }
    }
}
