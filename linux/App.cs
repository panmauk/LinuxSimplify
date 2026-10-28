using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace LinuxSimplify
{
    public class App : Application
    {
        public override void Initialize()
        {
            // Fluent supplies the base control templates (scroll bar, radio button, progress bar);
            // the app draws everything else itself. Light variant regardless of the desktop's dark mode,
            // because the app's own colours assume a light background.
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Light;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();
            base.OnFrameworkInitializationCompleted();
        }
    }
}
