using System;
using System.Linq;
using Avalonia;
using LinuxSimplify.Services;

namespace LinuxSimplify
{
    public class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // Headless helper modes (no display needed) — used by packaging checks and handy for bug reports
            if (args.Length > 0 && args[0].StartsWith("--"))
            {
                int rc = Cli.Run(args);
                if (rc != Cli.NotACliCommand) return rc;
            }

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }
    }
}
