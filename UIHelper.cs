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
    public static class UIHelper
    {
        // === Colors ===
        private static readonly Color PickerBg = Color.FromRgb(25, 25, 25);
        private static readonly Color PickerTextNormal = Color.FromRgb(140, 140, 140);
        private static readonly Color PickerTextSelected = Color.FromRgb(255, 255, 255);
        private static readonly Color PickerBarBorder = Color.FromRgb(100, 105, 115);

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

        // === Backgrounds ===
        public static Brush CreateLinenBackground()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop(Color.FromRgb(232, 233, 238), 0));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(220, 222, 230), 0.5));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(228, 230, 235), 1));
            return b;
        }

        public static Brush CreateDarkGradientBackground()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop(Color.FromRgb(60, 65, 75), 0));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(85, 92, 108), 0.3));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(120, 128, 145), 0.6));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(90, 97, 112), 1));
            return b;
        }

        // === Navigation Bar ===
        public static Border CreateNavigationBar(string title)
        {
            var border = new Border
            {
                Height = 44,
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.3, BlurRadius = 5, ShadowDepth = 2, Direction = 270 }
            };
            var g = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            g.GradientStops.Add(new GradientStop(NavTop, 0));
            g.GradientStops.Add(new GradientStop(NavMid, 0.5));
            g.GradientStops.Add(new GradientStop(NavBot, 1));
            border.Background = g;
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(90, 95, 105));
            border.BorderThickness = new Thickness(0, 0, 0, 1);
            border.Child = new TextBlock
            {
                Text = title, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = Color.FromRgb(40, 50, 70), Opacity = 0.8, BlurRadius = 1, ShadowDepth = 1, Direction = 270 }
            };
            return border;
        }

        // === Grouped Section ===
        public static Border CreateGroupedSection(UIElement content, double topMargin = 15)
        {
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
            var lt = new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(RowLabel), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(lt, 0); grid.Children.Add(lt);
            var vt = new TextBlock { Text = value, FontSize = 12, Foreground = new SolidColorBrush(RowValue), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(vt, 1); grid.Children.Add(vt);
            var c = new Border { MinHeight = 38, Child = grid, Padding = new Thickness(0, 4, 0, 4) };
            if (!isLast) { c.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 205)); c.BorderThickness = new Thickness(0, 0, 0, 1); }
            return c;
        }

        // === Section Header ===
        public static TextBlock CreateSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text.ToUpper(), FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(DimLabel), Margin = new Thickness(15, 15, 0, 6),
                Effect = new DropShadowEffect { Color = Colors.White, Opacity = 0.8, BlurRadius = 0, ShadowDepth = 1, Direction = 90 }
            };
        }

        // === Fade In ===
        public static void FadeIn(UIElement element, int delayMs = 0, int durationMs = 300)
        {
            element.Opacity = 0;
            element.RenderTransform = new TranslateTransform(0, 12);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs > 0 ? delayMs : 1) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                double start = Environment.TickCount;
                anim.Tick += (_, __) =>
                {
                    double p = Math.Min(1, (Environment.TickCount - start) / (double)durationMs);
                    double ease = 1 - (1 - p) * (1 - p);
                    element.Opacity = ease;
                    ((TranslateTransform)element.RenderTransform).Y = 12 * (1 - ease);
                    if (p >= 1) anim.Stop();
                };
                anim.Start();
            };
            timer.Start();
        }

        // =============================================================
        //  SLIDE TO SCAN
        // =============================================================
        public static Border CreateSlideToUnlock(string labelText, Action onSlideComplete)
        {
            double troughH = 52, knobSz = 44, margin = 4;
            var trough = new Border { Height = troughH, CornerRadius = new CornerRadius(12), Margin = new Thickness(24, 0, 24, 0), ClipToBounds = true };
            var tg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            tg.GradientStops.Add(new GradientStop(Color.FromRgb(30, 30, 32), 0));
            tg.GradientStops.Add(new GradientStop(Color.FromRgb(55, 57, 62), 0.5));
            tg.GradientStops.Add(new GradientStop(Color.FromRgb(40, 42, 46), 1));
            trough.Background = tg;
            trough.BorderBrush = new SolidColorBrush(Color.FromRgb(20, 20, 22));
            trough.BorderThickness = new Thickness(1);
            trough.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.5, BlurRadius = 8, ShadowDepth = 2, Direction = 270 };
            var canvas = new Canvas { Height = troughH, ClipToBounds = true };
            trough.Child = canvas;

            var shimmerBrush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5), MappingMode = BrushMappingMode.RelativeToBoundingBox };
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 180, 180, 180), 0.0));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.4));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.6));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 180, 180, 180), 1.0));
            var shimmer = new TextBlock { Text = labelText, FontSize = 20, Foreground = shimmerBrush, IsHitTestVisible = false };
            Canvas.SetTop(shimmer, (troughH - 28) / 2);
            canvas.Children.Add(shimmer);

            var knob = new Border { Width = knobSz, Height = knobSz, CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand };
            var kg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            kg.GradientStops.Add(new GradientStop(Color.FromRgb(190, 195, 205), 0));
            kg.GradientStops.Add(new GradientStop(Color.FromRgb(150, 155, 165), 0.45));
            kg.GradientStops.Add(new GradientStop(Color.FromRgb(130, 135, 145), 0.55));
            kg.GradientStops.Add(new GradientStop(Color.FromRgb(160, 165, 175), 1));
            knob.Background = kg;
            knob.BorderBrush = new SolidColorBrush(Color.FromRgb(80, 85, 95));
            knob.BorderThickness = new Thickness(1);
            knob.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.4, BlurRadius = 4, ShadowDepth = 1 };
            knob.Child = new TextBlock { Text = "\u25B6", FontSize = 22, Foreground = new SolidColorBrush(Color.FromRgb(80, 85, 95)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
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
        //  DISTRO PICKER — Canvas based
        // =============================================================
        public static Border CreateDistroPickerWidget(
            List<DistroCompatibility> distros,
            Action<DistroCompatibility> onSelectionChanged,
            int initialSelectedIndex = 0)
        {
            const double RH = 44;
            const int VIS = 5;
            double pH = RH * VIS;
            int totalRows = distros.Count + 4;

            var canvas = new Canvas { Background = Brushes.Transparent, Width = 600, Height = totalRows * RH };
            var transform = new TranslateTransform();
            canvas.RenderTransform = transform;

            var leftTexts = new List<TextBlock>();
            var rightTexts = new List<TextBlock>();

            for (int r = 0; r < totalRows; r++)
            {
                int di = r - 2;
                bool isData = di >= 0 && di < distros.Count;
                string lText = isData ? distros[di].Name : "";
                string rText = isData ? distros[di].CompatibilityStatus : "";
                bool sel = isData && di == initialSelectedIndex;

                var lt = new TextBlock
                {
                    Text = lText, FontSize = 20,
                    FontWeight = sel ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = new SolidColorBrush(sel ? PickerTextSelected : PickerTextNormal),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(lt, 16);
                Canvas.SetTop(lt, r * RH + (RH - 26) / 2);
                canvas.Children.Add(lt);

                var rt = new TextBlock
                {
                    Text = rText, FontSize = 14,
                    FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = new SolidColorBrush(sel ? StatusColor(rText) : PickerTextNormal),
                    IsHitTestVisible = false
                };
                Canvas.SetTop(rt, r * RH + (RH - 20) / 2);
                canvas.Children.Add(rt);

                if (isData) { leftTexts.Add(lt); rightTexts.Add(rt); }
            }

            var outer = new Border
            {
                Height = pH, CornerRadius = new CornerRadius(10),
                Margin = new Thickness(10, 6, 10, 0), ClipToBounds = true,
                Background = new SolidColorBrush(PickerBg),
                BorderBrush = new SolidColorBrush(Color.FromRgb(50, 50, 55)), BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.4, BlurRadius = 8, ShadowDepth = 3 }
            };

            var overlay = new Grid { Background = Brushes.Transparent };
            outer.Child = overlay;
            overlay.Children.Add(canvas);

            var bar = new Border
            {
                Height = RH, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, RH * 2, 0, 0),
                BorderBrush = new SolidColorBrush(PickerBarBorder),
                BorderThickness = new Thickness(0, 1, 0, 1),
                IsHitTestVisible = false
            };
            var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(120, 80, 85, 95), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(120, 55, 58, 65), 1));
            bar.Background = bg;
            overlay.Children.Add(bar);

            var tf = new Border { Height = RH * 2, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            var tfg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            tfg.GradientStops.Add(new GradientStop(Color.FromArgb(160, 25, 25, 25), 0));
            tfg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 25, 25, 25), 1));
            tf.Background = tfg; overlay.Children.Add(tf);

            var bf = new Border { Height = RH * 2, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
            var bfg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            bfg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 25, 25, 25), 0));
            bfg.GradientStops.Add(new GradientStop(Color.FromArgb(160, 25, 25, 25), 1));
            bf.Background = bfg; overlay.Children.Add(bf);

            int sel_idx = initialSelectedIndex;
            bool animating = false;

            var rightTextAll = new List<TextBlock>();
            for (int r = 0; r < totalRows; r++)
                rightTextAll.Add(canvas.Children[r * 2 + 1] as TextBlock);

            outer.Loaded += (s, e) =>
            {
                double w = overlay.ActualWidth > 0 ? overlay.ActualWidth : outer.ActualWidth - 2;
                canvas.Width = w;
                foreach (var rtb in rightTextAll)
                {
                    rtb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(rtb, w - rtb.DesiredSize.Width - 16);
                }
                GoTo(initialSelectedIndex, false);
            };

            void GoTo(int idx, bool animate)
            {
                idx = Math.Max(0, Math.Min(idx, distros.Count - 1));
                if (idx == sel_idx && Math.Abs(transform.Y - (-idx * RH)) < 1) return;
                sel_idx = idx;
                Highlight(leftTexts, rightTexts, sel_idx);
                onSelectionChanged?.Invoke(distros[sel_idx]);
                double target = -idx * RH;
                if (animate) { animating = true; SmoothTranslateY(transform, transform.Y, target, 150, () => animating = false); }
                else { transform.Y = target; }
            }

            overlay.PreviewMouseWheel += (s, e) =>
            {
                if (animating) { e.Handled = true; return; }
                GoTo(sel_idx + (e.Delta < 0 ? 1 : -1), true);
                e.Handled = true;
            };

            overlay.MouseLeftButtonDown += (s, e) =>
            {
                if (animating) { e.Handled = true; return; }
                double y = e.GetPosition(overlay).Y;
                if (y < RH * 2) GoTo(sel_idx - 1, true);
                else if (y > RH * 3) GoTo(sel_idx + 1, true);
                e.Handled = true;
            };

            return outer;
        }

        static void Highlight(List<TextBlock> left, List<TextBlock> right, int sel)
        {
            for (int i = 0; i < left.Count; i++)
            {
                bool s = i == sel;
                left[i].Foreground = new SolidColorBrush(s ? PickerTextSelected : PickerTextNormal);
                left[i].FontWeight = s ? FontWeights.Bold : FontWeights.Normal;
                right[i].Foreground = new SolidColorBrush(s ? StatusColor(right[i].Text) : PickerTextNormal);
                right[i].FontWeight = s ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        static Color StatusColor(string s)
        {
            if (s == "Recommended") return Color.FromRgb(120, 200, 80);
            if (s == "Compatible") return Color.FromRgb(220, 220, 220);
            if (s == "Not Compatible") return Color.FromRgb(220, 80, 80);
            return Color.FromRgb(180, 180, 100);
        }

        static void SmoothTranslateY(TranslateTransform tr, double from, double to, int ms, Action done)
        {
            if (Math.Abs(from - to) < 0.5) { tr.Y = to; done?.Invoke(); return; }
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            double st = Environment.TickCount;
            t.Tick += (_, __) =>
            {
                double p = Math.Min(1, (Environment.TickCount - st) / (double)ms);
                p = 1 - (1 - p) * (1 - p);
                tr.Y = from + (to - from) * p;
                if (p >= 1) { t.Stop(); tr.Y = to; done?.Invoke(); }
            };
            t.Start();
        }

        // =============================================================
        //  DARK NEXT BUTTON — matches iOS 5 screenshot style
        //  Dark recessed gradient, white text, rounded
        // =============================================================
        public static Button CreateDarkNextButton(string text)
        {
            var btn = new Button
            {
                Content = text, Height = 44, Margin = new Thickness(10, 6, 10, 6),
                FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Cursor = Cursors.Hand
            };

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
        //  GREEN ACTION BUTTON
        // =============================================================
        public static Button CreateGreenActionButton(string text)
        {
            var btn = new Button
            {
                Content = text, Height = 44, Margin = new Thickness(10, 6, 10, 6),
                FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Cursor = Cursors.Hand
            };

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

        // === Glossy Button ===
        public static Button CreateGlossyButton(string text, bool primary = true)
        {
            var btn = new Button
            {
                Content = text, Height = 40, Margin = new Thickness(8, 6, 8, 6),
                FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Cursor = Cursors.Hand
            };

            LinearGradientBrush normalBg, pressedBg;
            if (primary) { normalBg = MakeGradient(Color.FromRgb(82, 145, 237), Color.FromRgb(52, 115, 207), Color.FromRgb(72, 135, 227)); pressedBg = MakeGradient(Color.FromRgb(62, 125, 217), Color.FromRgb(42, 95, 187), Color.FromRgb(52, 115, 207)); }
            else { normalBg = MakeGradient(Color.FromRgb(140, 150, 165), Color.FromRgb(110, 120, 135), Color.FromRgb(130, 140, 155)); pressedBg = MakeGradient(Color.FromRgb(120, 130, 145), Color.FromRgb(90, 100, 115), Color.FromRgb(110, 120, 135)); }
            var disabledBg = MakeGradient(Color.FromRgb(180, 185, 190), Color.FromRgb(160, 165, 170), Color.FromRgb(170, 175, 180));

            var tmpl = new ControlTemplate(typeof(Button));
            var bdrF = new FrameworkElementFactory(typeof(Border), "btnBorder");
            bdrF.SetValue(Border.BackgroundProperty, normalBg);
            bdrF.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            bdrF.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(40, 50, 70)));
            bdrF.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bdrF.SetValue(Border.EffectProperty, new DropShadowEffect { Color = Colors.Black, Opacity = 0.3, BlurRadius = 4, ShadowDepth = 2 });
            var cpF = new FrameworkElementFactory(typeof(ContentPresenter));
            cpF.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cpF.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cpF.SetValue(ContentPresenter.EffectProperty, new DropShadowEffect { Color = Color.FromRgb(20, 30, 50), Opacity = 0.7, BlurRadius = 1, ShadowDepth = 1 });
            bdrF.AppendChild(cpF);
            tmpl.VisualTree = bdrF;

            var pt = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pt.Setters.Add(new Setter(Border.BackgroundProperty, pressedBg, "btnBorder"));
            pt.Setters.Add(new Setter(Border.EffectProperty, new DropShadowEffect { Color = Colors.Black, Opacity = 0.15, BlurRadius = 2, ShadowDepth = 1 }, "btnBorder"));
            tmpl.Triggers.Add(pt);

            var dt = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            dt.Setters.Add(new Setter(Border.BackgroundProperty, disabledBg, "btnBorder"));
            dt.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(210, 215, 220))));
            dt.Setters.Add(new Setter(Button.CursorProperty, Cursors.Arrow));
            tmpl.Triggers.Add(dt);

            btn.Template = tmpl;
            return btn;
        }

        public static LinearGradientBrush MakePubGradient(Color top, Color mid, Color bot)
        {
            return MakeGradient(top, mid, bot);
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
            return new RadioButton { Content = text, Height = 40, FontSize = 14, Foreground = new SolidColorBrush(RowLabel), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 0, 10, 0), BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 205)), BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.White, GroupName = "DistroSelection" };
        }

        // === Progress Bar ===
        public static ProgressBar CreateProgressBar()
        {
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
                Foreground = new SolidColorBrush(RowValue),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 4, 10, 8)
            };
        }
    }
}
