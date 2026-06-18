using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace PmxInstaller
{
    // Two looks, chosen by Theme.Pmx. Pages are rebuilt on toggle, so factories
    // just read the current theme. PMX = PANMOX dark/serif/cyan, Retro = light.
    public static class Ui
    {
        public static bool Pmx = true;

        // fonts (resolved via fontconfig on Linux)
        public static readonly FontFamily Serif = new FontFamily("DejaVu Serif, Noto Serif, serif");
        public static readonly FontFamily Mono = new FontFamily("DejaVu Sans Mono, monospace");

        // palette
        static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
        public static IBrush Cyan => B("#3FD0D9");
        public static IBrush Pink => B("#FF2D78");
        static IBrush Text => Pmx ? B("#E8F0F5") : B("#2A2D3C");
        static IBrush Muted => Pmx ? B("#7E8CA0") : B("#6E788C");
        static IBrush Value => Pmx ? B("#E8F0F5") : B("#50607A");
        static IBrush Label => Pmx ? B("#3FA8B4") : B("#3C4660");
        static IBrush PanelBg => Pmx ? B("#0E1622") : B("#FFFFFF");
        static IBrush PanelBorder => Pmx ? B("#1F2E40") : B("#AAAAB4");
        public static IBrush Ok => Pmx ? B("#5FD06E") : B("#4E9632");
        public static IBrush Warn => Pmx ? B("#E6B43C") : B("#B4781E");
        public static IBrush Err => Pmx ? B("#EE6666") : B("#C83C3C");
        public static IBrush Link => Pmx ? B("#3FD0D9") : B("#2A6EDB");

        public static IBrush WindowBackground()
        {
            var g = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            };
            if (Pmx)
            {
                g.GradientStops.Add(new GradientStop(Color.Parse("#080C12"), 0));
                g.GradientStops.Add(new GradientStop(Color.Parse("#0E1C28"), 0.5));
                g.GradientStops.Add(new GradientStop(Color.Parse("#06090E"), 1));
            }
            else
            {
                g.GradientStops.Add(new GradientStop(Color.Parse("#E8E9EE"), 0));
                g.GradientStops.Add(new GradientStop(Color.Parse("#DCDEE6"), 1));
            }
            return g;
        }

        public static IBrush HeaderBackground() => Pmx ? B("#090D14") : B("#9BA5BC");

        // ---- text helpers ----
        public static TextBlock Kicker(string text)
            => new()
            {
                Text = string.Join(" ", text.ToUpper().ToCharArray()),
                FontFamily = Pmx ? Mono : FontFamily.Default,
                FontSize = 12, Foreground = Pmx ? Cyan : Muted,
                HorizontalAlignment = HorizontalAlignment.Center
            };

        public static TextBlock Section(string text)
            => new()
            {
                Text = Pmx ? "// " + text.ToUpper() : text.ToUpper(),
                FontFamily = Pmx ? Mono : FontFamily.Default,
                FontSize = 11, FontWeight = FontWeight.SemiBold,
                Foreground = Pmx ? Cyan : Muted,
                Margin = new Thickness(2, 6, 0, 6)
            };

        public static TextBlock Body(string text, IBrush color = null, bool mono = false)
            => new()
            {
                Text = text, FontSize = 13, Foreground = color ?? Muted,
                FontFamily = (mono && Pmx) ? Mono : FontFamily.Default,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

        public static Control Headline(string text, double size, bool dot)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            var h = new TextBlock
            {
                Text = text, FontSize = size,
                FontWeight = Pmx ? FontWeight.Bold : FontWeight.Light,
                Foreground = Pmx ? Brushes.White : Text,
                FontFamily = Pmx ? Serif : FontFamily.Default,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            row.Children.Add(h);
            if (dot)
                row.Children.Add(new TextBlock
                {
                    Text = ".", FontSize = size, FontWeight = FontWeight.Bold,
                    Foreground = Pmx ? Pink : Text,
                    FontFamily = Pmx ? Serif : FontFamily.Default,
                    VerticalAlignment = VerticalAlignment.Bottom
                });
            return row;
        }

        // ---- card ----
        public static Border Card(Control child)
            => new()
            {
                Background = PanelBg, BorderBrush = PanelBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Pmx ? 12 : 10),
                Margin = new Thickness(0, 6, 0, 6), Child = child
            };

        public static Control SpecRow(string label, string value, bool last)
        {
            var grid = new Grid { Margin = new Thickness(12, 8, 12, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition(86, GridUnitType.Pixel));
            grid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var l = new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = Label, FontFamily = Pmx ? Mono : FontFamily.Default, VerticalAlignment = VerticalAlignment.Center };
            var v = new TextBlock { Text = value, FontSize = 12, Foreground = Value, FontFamily = Pmx ? Mono : FontFamily.Default, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(l, 0); Grid.SetColumn(v, 1);
            grid.Children.Add(l); grid.Children.Add(v);
            if (last) return grid;
            var wrap = new StackPanel();
            wrap.Children.Add(grid);
            wrap.Children.Add(new Border { Height = 1, Background = PanelBorder, Margin = new Thickness(10, 0, 10, 0) });
            return wrap;
        }

        // ---- buttons (Border-based so theming is fully ours) ----
        public static Control PillButton(string text, bool primary, Action onClick)
        {
            IBrush normal, hover, fg, brd = null; double bt = 0;
            double radius = Pmx ? 22 : 10;
            if (primary)
            {
                if (Pmx) { normal = Cyan; hover = B("#5FE0E8"); fg = B("#06141A"); }
                else { normal = LinGrad("#7EC348", "#5CA82A"); hover = LinGrad("#8AD054", "#67B636"); fg = Brushes.White; brd = B("#3C6E1E"); bt = 1; }
            }
            else
            {
                if (Pmx) { normal = Brushes.Transparent; hover = new SolidColorBrush(Color.FromArgb(40, 63, 208, 217)); fg = Cyan; brd = Cyan; bt = 1.5; }
                else { normal = LinGrad("#5A5C68", "#34363E"); hover = LinGrad("#666874", "#3E404A"); fg = Brushes.White; }
            }
            return Clickable(text, fg, normal, hover, brd, bt, radius, primary ? 16 : 15, onClick);
        }

        public static Control Toggle(string label, Action onClick)
        {
            IBrush normal, hover, fg, brd; double bt = 1;
            if (Pmx) { normal = Brushes.Transparent; hover = new SolidColorBrush(Color.FromArgb(40, 63, 208, 217)); fg = Cyan; brd = Cyan; }
            else { normal = B("#5A5C68"); hover = B("#666874"); fg = Brushes.White; brd = Brushes.Transparent; bt = 0; }
            var c = Clickable(label.ToUpper(), fg, normal, hover, brd, bt, 14, 11, onClick);
            ((Border)c).Padding = new Thickness(12, 3, 12, 3);
            ((Border)c).HorizontalAlignment = HorizontalAlignment.Right;
            return c;
        }

        static Control Clickable(string text, IBrush fg, IBrush normal, IBrush hover, IBrush brd, double bt, double radius, double fontSize, Action onClick)
        {
            var tb = new TextBlock
            {
                Text = text, Foreground = fg, FontSize = fontSize, FontWeight = FontWeight.Bold,
                FontFamily = Pmx ? Mono : FontFamily.Default,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var border = new Border
            {
                Background = normal, Child = tb, CornerRadius = new CornerRadius(radius),
                Padding = new Thickness(22, 10, 22, 10), Cursor = new Cursor(StandardCursorType.Hand),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            if (brd != null && bt > 0) { border.BorderBrush = brd; border.BorderThickness = new Thickness(bt); }
            border.PointerEntered += (_, __) => border.Background = hover;
            border.PointerExited += (_, __) => border.Background = normal;
            border.PointerReleased += (_, __) => onClick?.Invoke();
            return border;
        }

        static IBrush LinGrad(string top, string bot)
        {
            var g = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
            };
            g.GradientStops.Add(new GradientStop(Color.Parse(top), 0));
            g.GradientStops.Add(new GradientStop(Color.Parse(bot), 1));
            return g;
        }

        public static ProgressBar Progress()
        {
            return new ProgressBar
            {
                Minimum = 0, Maximum = 100, Height = 18, ShowProgressText = true,
                Foreground = Pmx ? Cyan : B("#3C78D7"),
                Background = Pmx ? B("#0A1018") : B("#E6E8EE"),
                Margin = new Thickness(0, 10, 0, 6)
            };
        }
    }
}
