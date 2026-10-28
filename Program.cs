using System;
using System.Windows;
using LinuxSimplify.Services;

namespace LinuxSimplify
{
    public class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // Elevated copy started only for writing the USB drive (see ElevatedFlash): no UI
            if (args.Length > 0 && args[0] == ElevatedFlash.HelperArg)
                return ElevatedFlash.RunHelper(args);

            var app = new Application();
            return app.Run(new MainWindow());
        }
    }
}
