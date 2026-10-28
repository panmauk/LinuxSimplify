using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LinuxSimplify.UI
{
    /// <summary>Tiny modal message / confirm boxes (Avalonia has no MessageBox).</summary>
    public static class Dialogs
    {
        public static Task MessageAsync(Window owner, string text)
        {
            return ShowAsync(owner, text, yesNo: false);
        }

        public static Task<bool> ConfirmAsync(Window owner, string text)
        {
            return ShowAsync(owner, text, yesNo: true);
        }

        private static async Task<bool> ShowAsync(Window owner, string text, bool yesNo)
        {
            bool result = false;
            var dlg = new Window
            {
                Title = "LinuxSimplify",
                Width = 420, SizeToContent = SizeToContent.Height, CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = UIHelper.CreateLinenBackground()
            };

            var panel = new StackPanel { Margin = new Thickness(20, 20, 20, 10) };
            panel.Children.Add(new TextBlock
            {
                Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(50, 60, 80)),
                Margin = new Thickness(0, 0, 0, 16)
            });

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (yesNo)
            {
                var no = UIHelper.CreateGlossyButton("No", false); no.Width = 90;
                no.Click += (s, e) => { result = false; dlg.Close(); };
                var yes = UIHelper.CreateGlossyButton("Yes", true); yes.Width = 90;
                yes.Click += (s, e) => { result = true; dlg.Close(); };
                row.Children.Add(no); row.Children.Add(yes);
            }
            else
            {
                var ok = UIHelper.CreateGlossyButton("OK", true); ok.Width = 90;
                ok.Click += (s, e) => dlg.Close();
                row.Children.Add(ok);
            }
            panel.Children.Add(row);
            dlg.Content = panel;

            try
            {
                if (owner != null && owner.IsVisible) await dlg.ShowDialog(owner);
                else { dlg.Show(); var tcs = new TaskCompletionSource<bool>(); dlg.Closed += (s, e) => tcs.TrySetResult(true); await tcs.Task; }
            }
            catch { }
            return result;
        }
    }
}
