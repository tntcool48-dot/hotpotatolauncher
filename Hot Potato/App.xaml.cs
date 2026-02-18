#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;

namespace HotPotatoLauncher
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "HotPotatoLauncher_Instance_Mutex";
            bool createdNew;

            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // App is already running! Close this instance silently.
                Environment.Exit(0);
                return;
            }

            base.OnStartup(e);

            // Register background activation for Native Windows Toasts
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                ToastArguments args = ToastArguments.Parse(toastArgs.Argument);
                if (args.TryGetValue("action", out string action) && action == "hostRequest")
                {
                    if (args.TryGetValue("response", out string response) && response == "accept")
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            MainWindow mainWindow = (MainWindow)System.Windows.Application.Current.MainWindow;
                            if (mainWindow != null)
                            {
                                _ = mainWindow.StartHostingSequenceAsync();
                            }
                        });
                    }
                }
            };
        }
    }
}