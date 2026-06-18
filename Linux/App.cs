using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace PmxInstaller
{
    public class App : Application
    {
        public override void Initialize()
        {
            // Fluent gives us sane defaults for ProgressBar, ScrollViewer, etc.
            // Our own theme styles (Theme.cs) layer on top, scoped by window class.
            Styles.Add(new FluentTheme());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();
            base.OnFrameworkInitializationCompleted();
        }
    }
}
