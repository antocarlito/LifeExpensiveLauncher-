using System;
using System.IO;
using System.Windows;

namespace LifeExpensiveLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Log toutes les exceptions non gerees
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                File.WriteAllText("crash.log", $"[{DateTime.Now}] FATAL:\n{ex}");
                MessageBox.Show($"Erreur fatale:\n{ex?.Message}\n\nDetails dans crash.log",
                    "LifeExpensive Launcher - Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                File.WriteAllText("crash.log", $"[{DateTime.Now}] UI ERROR:\n{args.Exception}");
                MessageBox.Show($"Erreur:\n{args.Exception.Message}\n\nDetails dans crash.log",
                    "LifeExpensive Launcher - Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
