using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using LinuxSimplify.Models;

namespace LinuxSimplify.UI
{
    public enum AppTheme
    {
        Retro,   // iOS-era skeuomorphic: linen, glossy green, metal slider
        Pmx      // PANMOX / PMX OS: near-black teal glow, serif display, cyan pills
    }

    public static class UIHelper
    {
        // Active theme. The whole UI re-renders when this flips.
        // PMX OS (PANMOX) is the default look; RETRO is the opt-in iOS-era skin.
        public static AppTheme Theme = AppTheme.Pmx;
        private static bool Pmx => Theme == AppTheme.Pmx;

        // === Retro colors ===
        private static readonly Color GreenTop = Color.FromRgb(126, 195, 72);
        private static readonly Color GreenMid = Color.FromRgb(92, 168, 42);
        private static readonly Color GreenBot = Color.FromRgb(110, 182, 58);
        private static readonly Color GreenBorder = Color.FromRgb(60, 110, 30);
        private static readonly Color GreenPressed = Color.FromRgb(72, 140, 32);

        private static readonly Color NavTop = Color.FromRgb(180, 190, 208);
        private static readonly Color NavMid = Color.FromRgb(140, 152, 178);
        private static readonly Color NavBot = Color.FromRgb(155, 165, 188);

        private static readonly Color DimLabel = Color.FromRgb(90, 100, 120);
        private static readonly Color RowLabel = Color.FromRgb(50, 60, 80);
        private static readonly Color RowValue = Color.FromRgb(80, 90, 110);

        // === PANMOX / PMX OS palette ===
        private static readonly Color PmxBg0 = Color.FromRgb(6, 9, 14);        // outer black
        private static readonly Color PmxBg1 = Color.FromRgb(11, 17, 26);      // mid
        private static readonly Color PmxBgGlow = Color.FromRgb(16, 34, 44);   // teal-tinted center
        private static readonly Color PmxNav = Color.FromRgb(9, 13, 20);
        private static readonly Color PmxPanel = Color.FromRgb(14, 22, 34);
        private static readonly Color PmxPanelBorder = Color.FromRgb(31, 46, 64);
        private static readonly Color PmxText = Color.FromRgb(232, 240, 245);
        private static readonly Color PmxMuted = Color.FromRgb(126, 140, 160);
        public static readonly Color PmxCyan = Color.FromRgb(63, 208, 217);
        private static readonly Color PmxCyanDim = Color.FromRgb(43, 168, 180);
        public static readonly Color PmxPink = Color.FromRgb(255, 45, 120);

        // High-contrast Didone-ish serif with graceful fallbacks; mono for labels.
        public static readonly FontFamily PmxSerif = new FontFamily("Bodoni MT, Didot, Constantia, Cambria, Georgia, serif");
        public static readonly FontFamily PmxMono = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");

        // === Backgrounds ===
        public static Brush CreateLinenBackground()
        {
            if (Pmx) return CreatePmxBackground();
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop(Color.FromRgb(232, 233, 238), 0));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(220, 222, 230), 0.5));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(228, 230, 235), 1));
            return b;
        }

        public static Brush CreateDarkGradientBackground()
        {
            if (Pmx) return CreatePmxBackground();
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop(Color.FromRgb(60, 65, 75), 0));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(85, 92, 108), 0.3));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(120, 128, 145), 0.6));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(90, 97, 112), 1));
            return b;
        }

        // Near-black with a soft teal glow radiating from the centre.
        public static Brush CreatePmxBackground()
        {
            var rg = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.52),
                Center = new Point(0.5, 0.52),
                RadiusX = 0.85, RadiusY = 0.85
            };
            rg.GradientStops.Add(new GradientStop(PmxBgGlow, 0));
            rg.GradientStops.Add(new GradientStop(PmxBg1, 0.55));
            rg.GradientStops.Add(new GradientStop(PmxBg0, 1));
            return rg;
        }

        // === Navigation Bar ===
        public static Border CreateNavigationBar(string title, FrameworkElement rightElement = null)
        {
            var border = new Border { Height = 44 };
            var grid = new Grid();

            if (Pmx)
            {
                border.Background = new SolidColorBrush(PmxNav);
                border.BorderBrush = new SolidColorBrush(PmxPanelBorder);
                border.BorderThickness = new Thickness(0, 0, 0, 1);
                grid.Children.Add(new TextBlock
                {
                    Text = title.ToUpper(), FontFamily = PmxMono, FontSize = 13, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(PmxCyan),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                border.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.3, BlurRadius = 5, ShadowDepth = 2, Direction = 270 };
                var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                g.GradientStops.Add(new GradientStop(NavTop, 0));
                g.GradientStops.Add(new GradientStop(NavMid, 0.5));
                g.GradientStops.Add(new GradientStop(NavBot, 1));
                border.Background = g;
                border.BorderBrush = new SolidColorBrush(Color.FromRgb(90, 95, 105));
                border.BorderThickness = new Thickness(0, 0, 0, 1);
                grid.Children.Add(new TextBlock
                {
                    Text = title, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Effect = new DropShadowEffect { Color = Color.FromRgb(40, 50, 70), Opacity = 0.8, BlurRadius = 1, ShadowDepth = 1, Direction = 270 }
                });
            }

            if (rightElement != null)
            {
                rightElement.HorizontalAlignment = HorizontalAlignment.Right;
                rightElement.VerticalAlignment = VerticalAlignment.Center;
                rightElement.Margin = new Thickness(0, 0, 8, 0);
                grid.Children.Add(rightElement);
            }

            border.Child = grid;
            return border;
        }

        // === Theme toggle button (sits in nav / corners) ===
        // Label is the theme it switches TO ("PMX OS" or "RETRO").
        public static Button CreateThemeToggleButton(string label)
        {
            var btn = new Button
            {
                Content = label.ToUpper(), Height = 28, MinWidth = 78,
                Padding = new Thickness(12, 0, 12, 0), Cursor = Cursors.Hand,
                FontSize = 11, FontWeight = FontWeights.Bold
            };

            Color bg, border, fg, pressed;
            if (Pmx)
            {
                btn.FontFamily = PmxMono;
                bg = Colors.Transparent; border = PmxCyan; fg = PmxCyan;
                pressed = Color.FromArgb(40, PmxCyan.R, PmxCyan.G, PmxCyan.B);
            }
            else
            {
                bg = Color.FromRgb(60, 62, 68); border = Color.FromRgb(30, 32, 38);
                fg = Colors.White; pressed = Color.FromRgb(40, 42, 48);
            }

            var tmpl = new ControlTemplate(typeof(Button));
            var bdr = new FrameworkElementFactory(typeof(Border), "tBorder");
            bdr.SetValue(Border.BackgroundProperty, new SolidColorBrush(bg));
            bdr.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            bdr.SetValue(Border.BorderBrushProperty, new SolidColorBrush(border));
            bdr.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bdr.AppendChild(cp);
            tmpl.VisualTree = bdr;

            var pt = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pt.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(pressed), "tBorder"));
            tmpl.Triggers.Add(pt);

            btn.Foreground = new SolidColorBrush(fg);
            btn.Template = tmpl;
            return btn;
        }

        // === Grouped Section (card) ===
        public static Border CreateGroupedSection(UIElement content, double topMargin = 15)
        {
            if (Pmx)
            {
                return new Border
                {
                    Background = new SolidColorBrush(PmxPanel), CornerRadius = new CornerRadius(12),
                    Margin = new Thickness(10, topMargin, 10, 0),
                    BorderBrush = new SolidColorBrush(PmxPanelBorder), BorderThickness = new Thickness(1),
                    Child = content
                };
            }
            return new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(10),
                Margin = new Thickness(10, topMargin, 10, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(170, 170, 180)), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.15, BlurRadius = 6, ShadowDepth = 2 },
                Child = content
            };
        }

        // === List Row ===
        public static Border CreateListRow(string label, string value, bool isLast = false)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Color labelColor = Pmx ? PmxCyanDim : RowLabel;
            Color valueColor = Pmx ? PmxText : RowValue;
            Color divider = Pmx ? PmxPanelBorder : Color.FromRgb(200, 200, 205);

            var lt = new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(labelColor), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            if (Pmx) lt.FontFamily = PmxMono;
            Grid.SetColumn(lt, 0); grid.Children.Add(lt);
            var vt = new TextBlock { Text = value, FontSize = 12, Foreground = new SolidColorBrush(valueColor), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(vt, 1); grid.Children.Add(vt);
            var c = new Border { MinHeight = 38, Child = grid, Padding = new Thickness(0, 4, 0, 4) };
            if (!isLast) { c.BorderBrush = new SolidColorBrush(divider); c.BorderThickness = new Thickness(0, 0, 0, 1); }
            return c;
        }

        // === Section Header ===
        public static TextBlock CreateSectionHeader(string text)
        {
            if (Pmx)
            {
                return new TextBlock
                {
                    Text = "// " + text.ToUpper(), FontFamily = PmxMono, FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(PmxCyan), Margin = new Thickness(16, 16, 0, 6)
                };
            }
            return new TextBlock
            {
                Text = text.ToUpper(), FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(DimLabel), Margin = new Thickness(15, 15, 0, 6),
                Effect = new DropShadowEffect { Color = Colors.White, Opacity = 0.8, BlurRadius = 0, ShadowDepth = 1, Direction = 90 }
            };
        }

        // =============================================================
        //  SLIDE TO SCAN
        // =============================================================
        public static Border CreateSlideToUnlock(string labelText, Action onSlideComplete)
        {
            bool pmx = Pmx;
            double troughH = 52, knobSz = 44, margin = 4;
            var trough = new Border { Height = troughH, CornerRadius = new CornerRadius(12), Margin = new Thickness(24, 0, 24, 0), ClipToBounds = true };
            var tg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            if (pmx)
            {
                tg.GradientStops.Add(new GradientStop(Color.FromRgb(10, 16, 24), 0));
                tg.GradientStops.Add(new GradientStop(Color.FromRgb(14, 22, 34), 1));
                trough.Background = tg;
                trough.BorderBrush = new SolidColorBrush(PmxCyanDim);
                trough.BorderThickness = new Thickness(1);
            }
            else
            {
                tg.GradientStops.Add(new GradientStop(Color.FromRgb(30, 30, 32), 0));
                tg.GradientStops.Add(new GradientStop(Color.FromRgb(55, 57, 62), 0.5));
                tg.GradientStops.Add(new GradientStop(Color.FromRgb(40, 42, 46), 1));
                trough.Background = tg;
                trough.BorderBrush = new SolidColorBrush(Color.FromRgb(20, 20, 22));
                trough.BorderThickness = new Thickness(1);
                trough.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.5, BlurRadius = 8, ShadowDepth = 2, Direction = 270 };
            }
            var canvas = new Canvas { Height = troughH, ClipToBounds = true };
            trough.Child = canvas;

            var shimmerBrush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5), MappingMode = BrushMappingMode.RelativeToBoundingBox };
            if (pmx)
            {
                shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(90, 63, 208, 217), 0.0));
                shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 150, 240, 246), 0.4));
                shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 150, 240, 246), 0.6));
                shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(90, 63, 208, 217), 1.0));
            }
            else
            {
                shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 180, 180, 180), 0.0));
                shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.4));
                shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.6));
                shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 180, 180, 180), 1.0));
            }
            var shimmer = new TextBlock { Text = labelText, FontSize = 20, Foreground = shimmerBrush, IsHitTestVisible = false };
            if (pmx) shimmer.FontFamily = PmxMono;
            Canvas.SetTop(shimmer, (troughH - 28) / 2);
            canvas.Children.Add(shimmer);

            var knob = new Border { Width = knobSz, Height = knobSz, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand };
            var kg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            if (pmx)
            {
                kg.GradientStops.Add(new GradientStop(Color.FromRgb(63, 208, 217), 0));
                kg.GradientStops.Add(new GradientStop(Color.FromRgb(40, 160, 172), 1));
                knob.Background = kg;
                knob.BorderBrush = new SolidColorBrush(Color.FromRgb(20, 90, 98));
                knob.BorderThickness = new Thickness(1);
                knob.Child = new TextBlock { Text = "▶", FontSize = 22, Foreground = new SolidColorBrush(Color.FromRgb(6, 18, 22)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            else
            {
                kg.GradientStops.Add(new GradientStop(Color.FromRgb(190, 195, 205), 0));
                kg.GradientStops.Add(new GradientStop(Color.FromRgb(150, 155, 165), 0.45));
                kg.GradientStops.Add(new GradientStop(Color.FromRgb(130, 135, 145), 0.55));
                kg.GradientStops.Add(new GradientStop(Color.FromRgb(160, 165, 175), 1));
                knob.Background = kg;
                knob.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 85, 95));
                knob.BorderThickness = new Thickness(1);
                knob.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.4, BlurRadius = 4, ShadowDepth = 1 };
                knob.Child = new TextBlock { Text = "▶", FontSize = 22, Foreground = new SolidColorBrush(Color.FromRgb(80, 85, 95)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            }
            Canvas.SetLeft(knob, margin); Canvas.SetTop(knob, margin);
            canvas.Children.Add(knob);

            bool dragging = false; double dsx = 0, ksx = 0; bool done = false;
            trough.Loaded += (s, e) => { shimmer.Measure(new Size(9999, 9999)); Canvas.SetLeft(shimmer, (canvas.ActualWidth - shimmer.DesiredSize.Width) / 2); StartShimmer(shimmerBrush); };
            knob.MouseLeftButtonDown += (s, e) => { if (done) return; dragging = true; dsx = e.GetPosition(canvas).X; ksx = Canvas.GetLeft(knob); knob.CaptureMouse(); e.Handled = true; };
            knob.MouseMove += (s, e) => { if (!dragging || done) return; double mx = Math.Max(margin, Math.Min(ksx + e.GetPosition(canvas).X - dsx, canvas.ActualWidth - knobSz - margin)); Canvas.SetLeft(knob, mx); shimmer.Opacity = Math.Max(0, 1 - (mx - margin) / (canvas.ActualWidth - knobSz - 2 * margin) * 1.5); };
            knob.MouseLeftButtonUp += (s, e) =>
            {
                if (!dragging || done) return; dragging = false; knob.ReleaseMouseCapture();
                double mx = Canvas.GetLeft(knob), maxX = canvas.ActualWidth - knobSz - margin;
                if ((mx - margin) / (maxX - margin) > 0.85) { done = true; Canvas.SetLeft(knob, maxX); shimmer.Opacity = 0; var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; t.Tick += (_, __) => { t.Stop(); onSlideComplete?.Invoke(); }; t.Start(); }
                else { SmoothCanvasLeft(knob, mx, margin, 250); shimmer.Opacity = 1; }
            };
            return trough;
        }

        static void StartShimmer(LinearGradientBrush b) { var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) }; double p = -0.5; t.Tick += (s, e) => { p += 0.008; if (p > 1.5) p = -0.5; b.GradientStops[0].Offset = p - 0.3; b.GradientStops[1].Offset = p; b.GradientStops[2].Offset = p + 0.1; b.GradientStops[3].Offset = p + 0.4; }; t.Start(); }
        static void SmoothCanvasLeft(UIElement el, double from, double to, int ms) { var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) }; double s = Environment.TickCount; t.Tick += (_, __) => { double p2 = Math.Min(1, (Environment.TickCount - s) / ms); p2 = 1 - (1 - p2) * (1 - p2); Canvas.SetLeft(el, from + (to - from) * p2); if (p2 >= 1) t.Stop(); }; t.Start(); }

        // =============================================================
        //  SECONDARY BUTTON ("Next" / "DONE" / "Try Again")
        // =============================================================
        public static Button CreateDarkNextButton(string text)
        {
            var btn = new Button
            {
                Content = text, Height = 44, Margin = new Thickness(10, 6, 10, 6),
                FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Cursor = Cursors.Hand
            };

            if (Pmx)
            {
                btn.FontFamily = PmxMono;
                ApplyPmxButton(btn, filled: false);
                return btn;
            }

            var normalBg = MakeGradient(
                Color.FromRgb(80, 82, 88),
                Color.FromRgb(50, 52, 58),
                Color.FromRgb(65, 67, 73));
            var pressedBg = MakeGradient(
                Color.FromRgb(55, 57, 63),
                Color.FromRgb(35, 37, 43),
                Color.FromRgb(45, 47, 53));
            var disabledBg = MakeGradient(
                Color.FromRgb(140, 142, 148),
                Color.FromRgb(120, 122, 128),
                Color.FromRgb(130, 132, 138));

            var tmpl = new ControlTemplate(typeof(Button));
            var bdrF = new FrameworkElementFactory(typeof(Border), "btnBorder");
            bdrF.SetValue(Border.BackgroundProperty, normalBg);
            bdrF.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            bdrF.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(30, 32, 38)));
            bdrF.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bdrF.SetValue(Border.EffectProperty, new DropShadowEffect { Color = Colors.Black, Opacity = 0.4, BlurRadius = 4, ShadowDepth = 2 });
            var cpF = new FrameworkElementFactory(typeof(ContentPresenter));
            cpF.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cpF.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cpF.SetValue(ContentPresenter.EffectProperty, new DropShadowEffect { Color = Colors.Black, Opacity = 0.6, BlurRadius = 1, ShadowDepth = 1 });
            bdrF.AppendChild(cpF);
            tmpl.VisualTree = bdrF;

            var pt = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pt.Setters.Add(new Setter(Border.BackgroundProperty, pressedBg, "btnBorder"));
            pt.Setters.Add(new Setter(Border.EffectProperty, new DropShadowEffect { Color = Colors.Black, Opacity = 0.2, BlurRadius = 2, ShadowDepth = 1 }, "btnBorder"));
            tmpl.Triggers.Add(pt);

            var dt = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            dt.Setters.Add(new Setter(Border.BackgroundProperty, disabledBg, "btnBorder"));
            dt.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(180, 182, 188))));
            dt.Setters.Add(new Setter(Button.CursorProperty, Cursors.Arrow));
            tmpl.Triggers.Add(dt);

            btn.Template = tmpl;
            return btn;
        }

        // =============================================================
        //  PRIMARY ACTION BUTTON ("Download" / "Flash")
        // =============================================================
        public static Button CreateGreenActionButton(string text)
        {
            var btn = new Button
            {
                Content = text, Height = 44, Margin = new Thickness(10, 6, 10, 6),
                FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Cursor = Cursors.Hand
            };

            if (Pmx)
            {
                btn.FontFamily = PmxMono;
                ApplyPmxButton(btn, filled: true);
                return btn;
            }

            var normalBg = MakeGradient(GreenTop, GreenMid, GreenBot);
            var pressedBg = MakeGradient(Color.FromRgb(100, 165, 52), GreenPressed, Color.FromRgb(80, 150, 38));
            var disabledBg = MakeGradient(Color.FromRgb(160, 170, 160), Color.FromRgb(140, 150, 140), Color.FromRgb(150, 160, 150));

            var tmpl = new ControlTemplate(typeof(Button));
            var bdrF = new FrameworkElementFactory(typeof(Border), "btnBorder");
            bdrF.SetValue(Border.BackgroundProperty, normalBg);
            bdrF.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            bdrF.SetValue(Border.BorderBrushProperty, new SolidColorBrush(GreenBorder));
            bdrF.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bdrF.SetValue(Border.EffectProperty, new DropShadowEffect { Color = Colors.Black, Opacity = 0.35, BlurRadius = 4, ShadowDepth = 2 });
            var cpF = new FrameworkElementFactory(typeof(ContentPresenter));
            cpF.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cpF.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cpF.SetValue(ContentPresenter.EffectProperty, new DropShadowEffect { Color = Color.FromRgb(30, 70, 10), Opacity = 0.7, BlurRadius = 1, ShadowDepth = 1 });
            bdrF.AppendChild(cpF);
            tmpl.VisualTree = bdrF;

            var pt = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pt.Setters.Add(new Setter(Border.BackgroundProperty, pressedBg, "btnBorder"));
            pt.Setters.Add(new Setter(Border.EffectProperty, new DropShadowEffect { Color = Colors.Black, Opacity = 0.2, BlurRadius = 2, ShadowDepth = 1 }, "btnBorder"));
            tmpl.Triggers.Add(pt);

            var dt = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            dt.Setters.Add(new Setter(Border.BackgroundProperty, disabledBg, "btnBorder"));
            dt.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(120, 130, 120)), "btnBorder"));
            dt.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(210, 215, 210))));
            dt.Setters.Add(new Setter(Button.CursorProperty, Cursors.Arrow));
            tmpl.Triggers.Add(dt);

            btn.Template = tmpl;
            return btn;
        }

        // Pill-style PMX button. Filled = cyan background with dark text;
        // outline = transparent with a cyan border and cyan text.
        private static void ApplyPmxButton(Button btn, bool filled)
        {
            Color fillTop = PmxCyan, fillBot = Color.FromRgb(40, 170, 182);
            Color pressedFill = Color.FromRgb(36, 150, 160);

            var tmpl = new ControlTemplate(typeof(Button));
            var bdrF = new FrameworkElementFactory(typeof(Border), "btnBorder");
            bdrF.SetValue(Border.CornerRadiusProperty, new CornerRadius(22));
            bdrF.SetValue(Border.BorderThicknessProperty, new Thickness(filled ? 0 : 1.4));

            if (filled)
            {
                var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                g.GradientStops.Add(new GradientStop(fillTop, 0));
                g.GradientStops.Add(new GradientStop(fillBot, 1));
                bdrF.SetValue(Border.BackgroundProperty, g);
                bdrF.SetValue(Border.BorderBrushProperty, new SolidColorBrush(fillBot));
                btn.Foreground = new SolidColorBrush(Color.FromRgb(6, 16, 20));
                bdrF.SetValue(Border.EffectProperty, new DropShadowEffect { Color = PmxCyan, Opacity = 0.45, BlurRadius = 14, ShadowDepth = 0 });
            }
            else
            {
                bdrF.SetValue(Border.BackgroundProperty, new SolidColorBrush(Colors.Transparent));
                bdrF.SetValue(Border.BorderBrushProperty, new SolidColorBrush(PmxCyan));
                btn.Foreground = new SolidColorBrush(PmxCyan);
            }

            var cpF = new FrameworkElementFactory(typeof(ContentPresenter));
            cpF.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cpF.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bdrF.AppendChild(cpF);
            tmpl.VisualTree = bdrF;

            var pt = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pt.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(filled ? pressedFill : Color.FromArgb(40, PmxCyan.R, PmxCyan.G, PmxCyan.B)), "btnBorder"));
            tmpl.Triggers.Add(pt);

            var dt = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            dt.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(28, 38, 50)), "btnBorder"));
            dt.Setters.Add(new Setter(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(50, 64, 80)), "btnBorder"));
            dt.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(90, 105, 120))));
            dt.Setters.Add(new Setter(Button.CursorProperty, Cursors.Arrow));
            tmpl.Triggers.Add(dt);

            btn.Template = tmpl;
        }

        static LinearGradientBrush MakeGradient(Color top, Color mid, Color bot)
        {
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            g.GradientStops.Add(new GradientStop(top, 0));
            g.GradientStops.Add(new GradientStop(mid, 0.5));
            g.GradientStops.Add(new GradientStop(bot, 1));
            return g;
        }

        // === Radio Row ===
        public static RadioButton CreateRadioRow(string text)
        {
            if (Pmx)
            {
                return new RadioButton
                {
                    Content = text, Height = 40, FontSize = 14, FontFamily = PmxMono,
                    Foreground = new SolidColorBrush(PmxText), VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(10, 0, 10, 0),
                    BorderBrush = new SolidColorBrush(PmxPanelBorder), BorderThickness = new Thickness(0, 0, 0, 1),
                    Background = Brushes.Transparent, GroupName = "DistroSelection"
                };
            }
            return new RadioButton { Content = text, Height = 40, FontSize = 14, Foreground = new SolidColorBrush(RowLabel), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 0, 10, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 205)), BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.White, GroupName = "DistroSelection" };
        }

        // === Progress Bar ===
        public static ProgressBar CreateProgressBar()
        {
            if (Pmx)
            {
                var pbx = new ProgressBar
                {
                    Height = 18, Minimum = 0, Maximum = 100,
                    Margin = new Thickness(12, 8, 12, 4),
                    BorderBrush = new SolidColorBrush(PmxCyanDim),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromRgb(10, 16, 24))
                };
                var fgx = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                fgx.GradientStops.Add(new GradientStop(Color.FromRgb(110, 230, 238), 0));
                fgx.GradientStops.Add(new GradientStop(Color.FromRgb(45, 180, 192), 1));
                pbx.Foreground = fgx;
                return pbx;
            }
            var pb = new ProgressBar
            {
                Height = 20, Minimum = 0, Maximum = 100,
                Margin = new Thickness(12, 8, 12, 4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(120, 130, 150)),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(230, 232, 238))
            };
            var fg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fg.GradientStops.Add(new GradientStop(Color.FromRgb(100, 170, 255), 0));
            fg.GradientStops.Add(new GradientStop(Color.FromRgb(50, 120, 215), 0.45));
            fg.GradientStops.Add(new GradientStop(Color.FromRgb(40, 100, 195), 0.55));
            fg.GradientStops.Add(new GradientStop(Color.FromRgb(70, 140, 235), 1));
            pb.Foreground = fg;
            pb.Effect = new DropShadowEffect { Color = Color.FromRgb(40, 60, 100), Opacity = 0.15, BlurRadius = 3, ShadowDepth = 1, Direction = 270 };
            return pb;
        }

        // === Status Text ===
        public static TextBlock CreateStatusText(string text = "")
        {
            return new TextBlock
            {
                Text = text, FontSize = 13,
                FontFamily = Pmx ? PmxMono : null,
                Foreground = new SolidColorBrush(Pmx ? PmxMuted : RowValue),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 4, 10, 8)
            };
        }
    }
}
