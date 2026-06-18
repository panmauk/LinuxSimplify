using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LinuxSimplify
{
    public class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application();

            // Turn unexpected errors into a visible message instead of a hard
            // crash. The full details are also written to a log next to the
            // user's temp folder so anything that slips through is diagnosable.
            app.DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;

            app.Run(new MainWindow());
        }

        private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Report(e.Exception);
            e.Handled = true; // keep the app alive
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Report(e.ExceptionObject as Exception);
        }

        private static void Report(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string log = Path.Combine(Path.GetTempPath(), "LinuxSimplify-PMX-error.log");
                File.AppendAllText(log, $"[{DateTime.Now:u}] {ex}\n\n");
            }
            catch { }

            try
            {
                MessageBox.Show(
                    ex.Message,
                    "LinuxSimplify - PMX",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch { }
        }
    }
}
