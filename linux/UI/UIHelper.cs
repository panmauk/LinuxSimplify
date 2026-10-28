using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LinuxSimplify.Models;

namespace LinuxSimplify.UI
{
    /// <summary>
    /// Avalonia port of the WPF UIHelper: same colours, sizes and behaviour, built in code (no XAML).
    /// </summary>
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

        private static IBrush Solid(Color c) => new SolidColorBrush(c);
        private static readonly RelativePoint Top = new RelativePoint(0, 0, RelativeUnit.Relative);
        private static readonly RelativePoint Bottom = new RelativePoint(0, 1, RelativeUnit.Relative);

        private static BoxShadows Shadow(double offsetY, double blur, double opacity)
        {
            return new BoxShadows(new BoxShadow
            {
                OffsetX = 0, OffsetY = offsetY, Blur = blur, Spread = 0,
                Color = Color.FromArgb((byte)(opacity * 255), 0, 0, 0)
            });
        }

        private static Cursor Hand => new Cursor(StandardCursorType.Hand);

        // === Backgrounds ===
        public static IBrush CreateLinenBackground()
        {
            var b = new LinearGradientBrush { StartPoint = Top, EndPoint = Bottom };
            b.GradientStops.Add(new GradientStop(Color.FromRgb(232, 233, 238), 0));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(220, 222, 230), 0.5));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(228, 230, 235), 1));
            return b;
        }

        public static IBrush CreateDarkGradientBackground()
        {
            var b = new LinearGradientBrush { StartPoint = Top, EndPoint = Bottom };
            b.GradientStops.Add(new GradientStop(Color.FromRgb(60, 65, 75), 0));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(85, 92, 108), 0.3));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(120, 128, 145), 0.6));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(90, 97, 112), 1));
            return b;
        }

        // === Navigation Bar ===
        public static Border CreateNavigationBar(string title)
        {
            var g = new LinearGradientBrush { StartPoint = Top, EndPoint = Bottom };
            g.GradientStops.Add(new GradientStop(NavTop, 0));
            g.GradientStops.Add(new GradientStop(NavMid, 0.5));
            g.GradientStops.Add(new GradientStop(NavBot, 1));

            var border = new Border
            {
                Height = 44,
                Background = g,
                BorderBrush = Solid(Color.FromRgb(90, 95, 105)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BoxShadow = Shadow(2, 5, 0.3),
                ZIndex = 1
            };
            border.Child = new TextBlock
            {
                Text = title, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { Color = Color.FromRgb(40, 50, 70), Opacity = 0.8, BlurRadius = 1, OffsetX = 0, OffsetY = 1 }
            };
            return border;
        }

        // === Grouped Section ===
        public static Border CreateGroupedSection(Control content, double topMargin = 15)
        {
            return new Border
            {
                Background = Brushes.White, CornerRadius = new CornerRadius(10),
                Margin = new Thickness(10, topMargin, 10, 0),
                BorderBrush = Solid(Color.FromRgb(170, 170, 180)), BorderThickness = new Thickness(1),
                BoxShadow = Shadow(2, 6, 0.15),
                Child = content
            };
        }

        // === List Row ===
        public static Border CreateListRow(string label, string value, bool isLast = false)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("80,*") };
            var lt = new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Solid(RowLabel), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(lt, 0); grid.Children.Add(lt);
            var vt = new TextBlock { Text = value, FontSize = 12, Foreground = Solid(RowValue), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(vt, 1); grid.Children.Add(vt);
            var c = new Border { MinHeight = 38, Child = grid, Padding = new Thickness(0, 4, 0, 4) };
            if (!isLast) { c.BorderBrush = Solid(Color.FromRgb(200, 200, 205)); c.BorderThickness = new Thickness(0, 0, 0, 1); }
            return c;
        }

        // === Section Header ===
        public static TextBlock CreateSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text.ToUpperInvariant(), FontSize = 12, FontWeight = FontWeight.SemiBold,
                Foreground = Solid(DimLabel), Margin = new Thickness(15, 15, 0, 6),
                Effect = new DropShadowEffect { Color = Colors.White, Opacity = 0.8, BlurRadius = 0, OffsetX = 0, OffsetY = 1 }
            };
        }

        // =============================================================
        //  SLIDE TO SCAN
        // =============================================================
        public static Border CreateSlideToUnlock(string labelText, Action onSlideComplete)
        {
            double troughH = 52, knobSz = 44, margin = 4;
            var tg = new LinearGradientBrush { StartPoint = Top, EndPoint = Bottom };
            tg.GradientStops.Add(new GradientStop(Color.FromRgb(30, 30, 32), 0));
            tg.GradientStops.Add(new GradientStop(Color.FromRgb(55, 57, 62), 0.5));
            tg.GradientStops.Add(new GradientStop(Color.FromRgb(40, 42, 46), 1));
            var trough = new Border
            {
                Height = troughH, CornerRadius = new CornerRadius(12), Margin = new Thickness(24, 0, 24, 0), ClipToBounds = true,
                Background = tg, BorderBrush = Solid(Color.FromRgb(20, 20, 22)), BorderThickness = new Thickness(1)
            };
            var canvas = new Canvas { Height = troughH, ClipToBounds = true };
            trough.Child = canvas;

            var shimmerBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
            };
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 180, 180, 180), 0.0));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.4));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.6));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 180, 180, 180), 1.0));
            var shimmer = new TextBlock { Text = labelText, FontSize = 20, Foreground = shimmerBrush, IsHitTestVisible = false };
            Canvas.SetTop(shimmer, (troughH - 28) / 2);
            canvas.Children.Add(shimmer);

            var kg = new LinearGradientBrush { StartPoint = Top, EndPoint = Bottom };
            kg.GradientStops.Add(new GradientStop(Color.FromRgb(190, 195, 205), 0));
            kg.GradientStops.Add(new GradientStop(Color.FromRgb(150, 155, 165), 0.45));
            kg.GradientStops.Add(new GradientStop(Color.FromRgb(130, 135, 145), 0.55));
            kg.GradientStops.Add(new GradientStop(Color.FromRgb(160, 165, 175), 1));
            var knob = new Border
            {
                Width = knobSz, Height = knobSz, CornerRadius = new CornerRadius(8), Cursor = Hand,
                Background = kg, BorderBrush = Solid(Color.FromRgb(80, 85, 95)), BorderThickness = new Thickness(1),
                BoxShadow = Shadow(1, 4, 0.4),
                Child = new TextBlock { Text = "▶", FontSize = 22, Foreground = Solid(Color.FromRgb(80, 85, 95)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            Canvas.SetLeft(knob, margin); Canvas.SetTop(knob, margin);
            canvas.Children.Add(knob);

            bool dragging = false; double dsx = 0, ksx = 0; bool done = false;

            void CenterShimmer()
            {
                shimmer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(shimmer, Math.Max(0, (canvas.Bounds.Width - shimmer.DesiredSize.Width) / 2));
            }
            canvas.SizeChanged += (s, e) => CenterShimmer();
            trough.AttachedToVisualTree += (s, e) => StartShimmer(trough, shimmerBrush);

            knob.PointerPressed += (s, e) =>
            {
                if (done || !e.GetCurrentPoint(knob).Properties.IsLeftButtonPressed) return;
                dragging = true; dsx = e.GetPosition(canvas).X; ksx = Canvas.GetLeft(knob);
                e.Pointer.Capture(knob); e.Handled = true;
            };
            knob.PointerMoved += (s, e) =>
            {
                if (!dragging || done) return;
                double travel = canvas.Bounds.Width - knobSz - 2 * margin;
                double mx = Math.Max(margin, Math.Min(ksx + e.GetPosition(canvas).X - dsx, canvas.Bounds.Width - knobSz - margin));
                Canvas.SetLeft(knob, mx);
                shimmer.Opacity = travel > 0 ? Math.Max(0, 1 - (mx - margin) / travel * 1.5) : 1;
            };
            knob.PointerReleased += (s, e) =>
            {
                if (!dragging || done) return;
                dragging = false; e.Pointer.Capture(null);
                double mx = Canvas.GetLeft(knob), maxX = canvas.Bounds.Width - knobSz - margin;
                if (maxX > margin && (mx - margin) / (maxX - margin) > 0.85)
                {
                    done = true; Canvas.SetLeft(knob, maxX); shimmer.Opacity = 0;
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                    t.Tick += (_, __) => { t.Stop(); onSlideComplete?.Invoke(); };
                    t.Start();
                }
                else { SmoothCanvasLeft(knob, mx, margin, 250); shimmer.Opacity = 1; }
            };
            knob.PointerCaptureLost += (s, e) =>
            {
                // capture lost without a release (window deactivated, etc.): snap back
                if (!dragging || done) return;
                dragging = false; SmoothCanvasLeft(knob, Canvas.GetLeft(knob), margin, 250); shimmer.Opacity = 1;
            };
            return trough;
        }

        private static double Clamp01(double v) => Math.Max(0, Math.Min(1, v));

        static void StartShimmer(Control owner, LinearGradientBrush b)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            double p = -0.5;
            t.Tick += (s, e) =>
            {
                p += 0.008; if (p > 1.5) p = -0.5;
                // Skia gradients want stops inside 0..1 and in order (WPF tolerated overshoot), so clamp
                b.GradientStops[0].Offset = Clamp01(p - 0.3);
                b.GradientStops[1].Offset = Clamp01(p);
                b.GradientStops[2].Offset = Clamp01(p + 0.1);
                b.GradientStops[3].Offset = Clamp01(p + 0.4);
            };
            t.Start();
            // stop when the control leaves the tree, otherwise the timer would keep ticking forever
            owner.DetachedFromVisualTree += (s, e) => t.Stop();
        }

        static void SmoothCanvasLeft(Control el, double from, double to, int ms)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            double s0 = Environment.TickCount64;
            t.Tick += (_, __) =>
            {
                double p2 = Math.Min(1, (Environment.TickCount64 - s0) / ms); p2 = 1 - (1 - p2) * (1 - p2);
                Canvas.SetLeft(el, from + (to - from) * p2);
                if (p2 >= 1) t.Stop();
            };
            t.Start();
        }

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
            var rightTextAll = new List<TextBlock>();

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
                    FontWeight = sel ? FontWeight.Bold : FontWeight.Normal,
                    Foreground = Solid(sel ? PickerTextSelected : PickerTextNormal),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(lt, 16);
                Canvas.SetTop(lt, r * RH + (RH - 26) / 2);
                canvas.Children.Add(lt);

                var rt = new TextBlock
                {
                    Text = rText, FontSize = 14,
                    FontWeight = sel ? FontWeight.SemiBold : FontWeight.Normal,
                    Foreground = Solid(sel ? StatusColor(rText) : PickerTextNormal),
                    IsHitTestVisible = false
                };
                Canvas.SetTop(rt, r * RH + (RH - 20) / 2);
                canvas.Children.Add(rt);

                rightTextAll.Add(rt);
                if (isData) { leftTexts.Add(lt); rightTexts.Add(rt); }
            }

            var outer = new Border
            {
                Height = pH, CornerRadius = new CornerRadius(10),
                Margin = new Thickness(10, 6, 10, 0), ClipToBounds = true,
                Background = Solid(PickerBg),
                BorderBrush = Solid(Color.FromRgb(50, 50, 55)), BorderThickness = new Thickness(1),
                BoxShadow = Shadow(3, 8, 0.4),
                Focusable = true
            };

            var overlay = new Grid { Background = Brushes.Transparent, ClipToBounds = true };
            outer.Child = overlay;
            // The rows canvas (taller than the viewport) sits at (0,0) of a viewport-sized Canvas: a plain Canvas places
            // children exactly, whereas a taller child in a Grid cell gets re-aligned (centred) by the layout system.
            var rowsHost = new Canvas { Height = pH, ClipToBounds = true, IsHitTestVisible = false };
            rowsHost.Children.Add(canvas);
            Canvas.SetLeft(canvas, 0); Canvas.SetTop(canvas, 0);
            overlay.Children.Add(rowsHost);

            var bg = new LinearGradientBrush { StartPoint = Top, EndPoint = Bottom };
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(120, 80, 85, 95), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromArgb(120, 55, 58, 65), 1));
            overlay.Children.Add(new Border
            {
                Height = RH, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, RH * 2, 0, 0),
                BorderBrush = Solid(PickerBarBorder),
                BorderThickness = new Thickness(0, 1, 0, 1),
                Background = bg,
                IsHitTestVisible = false
            });

            var tfg = new LinearGradientBrush { StartPoint = Top, EndPoint = Bottom };
            tfg.GradientStops.Add(new GradientStop(Color.FromArgb(160, 25, 25, 25), 0));
            tfg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 25, 25, 25), 1));
            overlay.Children.Add(new Border { Height = RH * 2, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Background = tfg });

            var bfg = new LinearGradientBrush { StartPoint = Top, EndPoint = Bottom };
            bfg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 25, 25, 25), 0));
            bfg.GradientStops.Add(new GradientStop(Color.FromArgb(160, 25, 25, 25), 1));
            overlay.Children.Add(new Border { Height = RH * 2, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Background = bfg });

            int sel_idx = initialSelectedIndex;
            bool animating = false;
            bool placed = false;

            void Layout()
            {
                double w = overlay.Bounds.Width > 0 ? overlay.Bounds.Width : outer.Bounds.Width - 2;
                if (w <= 0) return;
                canvas.Width = w;
                foreach (var rtb in rightTextAll)
                {
                    rtb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(rtb, w - rtb.DesiredSize.Width - 16);
                }
                if (!placed) { placed = true; GoTo(initialSelectedIndex, false); }
            }
            overlay.SizeChanged += (s, e) => Layout();

            void GoTo(int idx, bool animate)
            {
                if (distros.Count == 0) return; // nothing to pick (every distro was filtered out)
                idx = Math.Max(0, Math.Min(idx, distros.Count - 1));
                if (idx == sel_idx && Math.Abs(transform.Y - (-idx * RH)) < 1) return;
                sel_idx = idx;
                Highlight(leftTexts, rightTexts, sel_idx);
                onSelectionChanged?.Invoke(distros[sel_idx]);
                double target = -idx * RH;
                if (animate) { animating = true; SmoothTranslateY(transform, transform.Y, target, 150, () => animating = false); }
                else { transform.Y = target; }
            }

            overlay.PointerWheelChanged += (s, e) =>
            {
                if (animating) { e.Handled = true; return; }
                GoTo(sel_idx + (e.Delta.Y < 0 ? 1 : -1), true);
                e.Handled = true;
            };

            overlay.PointerPressed += (s, e) =>
            {
                outer.Focus();
                if (!e.GetCurrentPoint(overlay).Properties.IsLeftButtonPressed) return;
                if (animating) { e.Handled = true; return; }
                double y = e.GetPosition(overlay).Y;
                if (y < RH * 2) GoTo(sel_idx - 1, true);
                else if (y > RH * 3) GoTo(sel_idx + 1, true);
                e.Handled = true;
            };

            // keyboard: up/down arrows
            outer.KeyDown += (s, e) =>
            {
                if (animating) return;
                if (e.Key == Key.Down) { GoTo(sel_idx + 1, true); e.Handled = true; }
                else if (e.Key == Key.Up) { GoTo(sel_idx - 1, true); e.Handled = true; }
            };

            return outer;
        }

        static void Highlight(List<TextBlock> left, List<TextBlock> right, int sel)
        {
            for (int i = 0; i < left.Count; i++)
            {
                bool s = i == sel;
                left[i].Foreground = Solid(s ? PickerTextSelected : PickerTextNormal);
                left[i].FontWeight = s ? FontWeight.Bold : FontWeight.Normal;
                right[i].Foreground = Solid(s ? StatusColor(right[i].Text) : PickerTextNormal);
                right[i].FontWeight = s ? FontWeight.SemiBold : FontWeight.Normal;
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
            double st = Environment.TickCount64;
            t.Tick += (_, __) =>
            {
                double p = Math.Min(1, (Environment.TickCount64 - st) / (double)ms);
                p = 1 - (1 - p) * (1 - p);
                tr.Y = from + (to - from) * p;
                if (p >= 1) { t.Stop(); tr.Y = to; done?.Invoke(); }
            };
            t.Start();
        }

        // =============================================================
        //  BUTTONS — gradient, rounded, pressed/disabled states
        // =============================================================

        /// <summary>A push button drawn like the WPF templated ones: gradient body, rounded corners, text shadow.</summary>
        public class GlossButton : Border
        {
            private readonly TextBlock label;
            private readonly IBrush normalBg, pressedBg, disabledBg;
            private readonly IBrush normalBorder, disabledBorder;
            private readonly IBrush normalFg, disabledFg;
            private readonly BoxShadows normalShadow, pressedShadow;
            private bool pressed;

            public event EventHandler<RoutedEventArgs> Click;

            public GlossButton(string text, double fontSize, double cornerRadius,
                IBrush normalBg, IBrush pressedBg, IBrush disabledBg,
                IBrush normalBorder, IBrush disabledBorder,
                IBrush normalFg, IBrush disabledFg, Color textShadow, double textShadowOpacity,
                BoxShadows normalShadow, BoxShadows pressedShadow)
            {
                this.normalBg = normalBg; this.pressedBg = pressedBg; this.disabledBg = disabledBg;
                this.normalBorder = normalBorder; this.disabledBorder = disabledBorder;
                this.normalFg = normalFg; this.disabledFg = disabledFg;
                this.normalShadow = normalShadow; this.pressedShadow = pressedShadow;

                label = new TextBlock
                {
                    Text = text, FontSize = fontSize, FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Effect = new DropShadowEffect { Color = textShadow, Opacity = textShadowOpacity, BlurRadius = 1, OffsetX = 0, OffsetY = 1 }
                };
                Child = label;
                Height = 44; Margin = new Thickness(10, 6, 10, 6);
                CornerRadius = new CornerRadius(cornerRadius);
                BorderThickness = new Thickness(1);
                Focusable = true;
                ApplyState();

                PointerPressed += (s, e) =>
                {
                    if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
                    pressed = true; e.Pointer.Capture(this); ApplyState(); e.Handled = true;
                };
                PointerReleased += (s, e) =>
                {
                    if (!pressed) return;
                    pressed = false; e.Pointer.Capture(null); ApplyState();
                    var p = e.GetPosition(this);
                    if (IsEnabled && p.X >= 0 && p.Y >= 0 && p.X <= Bounds.Width && p.Y <= Bounds.Height)
                        Click?.Invoke(this, new RoutedEventArgs());
                };
                PointerCaptureLost += (s, e) => { if (pressed) { pressed = false; ApplyState(); } };
                KeyDown += (s, e) =>
                {
                    if (IsEnabled && (e.Key == Key.Enter || e.Key == Key.Space))
                    {
                        Click?.Invoke(this, new RoutedEventArgs());
                        e.Handled = true;
                    }
                };
            }

            public string Text { get => label.Text; set => label.Text = value; }
            public string Content { get => label.Text; set => label.Text = value; }
            public double FontSize { get => label.FontSize; set => label.FontSize = value; }

            protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
            {
                base.OnPropertyChanged(change);
                if (change.Property == IsEnabledProperty) ApplyState();
            }

            private void ApplyState()
            {
                if (label == null) return;
                if (!IsEnabled)
                {
                    Background = disabledBg; BorderBrush = disabledBorder; label.Foreground = disabledFg;
                    BoxShadow = pressedShadow; Cursor = new Cursor(StandardCursorType.Arrow);
                }
                else
                {
                    Background = pressed ? pressedBg : normalBg; BorderBrush = normalBorder; label.Foreground = normalFg;
                    BoxShadow = pressed ? pressedShadow : normalShadow; Cursor = Hand;
                }
            }
        }

        // =============================================================
        //  DARK NEXT BUTTON — matches iOS 5 screenshot style
        //  Dark recessed gradient, white text, rounded
        // =============================================================
        public static GlossButton CreateDarkNextButton(string text)
        {
            return new GlossButton(text, 16, 10,
                MakeGradient(Color.FromRgb(80, 82, 88), Color.FromRgb(50, 52, 58), Color.FromRgb(65, 67, 73)),
                MakeGradient(Color.FromRgb(55, 57, 63), Color.FromRgb(35, 37, 43), Color.FromRgb(45, 47, 53)),
                MakeGradient(Color.FromRgb(140, 142, 148), Color.FromRgb(120, 122, 128), Color.FromRgb(130, 132, 138)),
                Solid(Color.FromRgb(30, 32, 38)), Solid(Color.FromRgb(30, 32, 38)),
                Brushes.White, Solid(Color.FromRgb(180, 182, 188)), Colors.Black, 0.6,
                Shadow(2, 4, 0.4), Shadow(1, 2, 0.2));
        }

        // =============================================================
        //  GREEN ACTION BUTTON
        // =============================================================
        public static GlossButton CreateGreenActionButton(string text)
        {
            return new GlossButton(text, 18, 10,
                MakeGradient(GreenTop, GreenMid, GreenBot),
                MakeGradient(Color.FromRgb(100, 165, 52), GreenPressed, Color.FromRgb(80, 150, 38)),
                MakeGradient(Color.FromRgb(160, 170, 160), Color.FromRgb(140, 150, 140), Color.FromRgb(150, 160, 150)),
                Solid(GreenBorder), Solid(Color.FromRgb(120, 130, 120)),
                Brushes.White, Solid(Color.FromRgb(210, 215, 210)), Color.FromRgb(30, 70, 10), 0.7,
                Shadow(2, 4, 0.35), Shadow(1, 2, 0.2));
        }

        // === Glossy Button ===
        public static GlossButton CreateGlossyButton(string text, bool primary = true)
        {
            IBrush normalBg, pressedBg;
            if (primary) { normalBg = MakeGradient(Color.FromRgb(82, 145, 237), Color.FromRgb(52, 115, 207), Color.FromRgb(72, 135, 227)); pressedBg = MakeGradient(Color.FromRgb(62, 125, 217), Color.FromRgb(42, 95, 187), Color.FromRgb(52, 115, 207)); }
            else { normalBg = MakeGradient(Color.FromRgb(140, 150, 165), Color.FromRgb(110, 120, 135), Color.FromRgb(130, 140, 155)); pressedBg = MakeGradient(Color.FromRgb(120, 130, 145), Color.FromRgb(90, 100, 115), Color.FromRgb(110, 120, 135)); }
            var b = new GlossButton(text, 14, 8, normalBg, pressedBg,
                MakeGradient(Color.FromRgb(180, 185, 190), Color.FromRgb(160, 165, 170), Color.FromRgb(170, 175, 180)),
                Solid(Color.FromRgb(40, 50, 70)), Solid(Color.FromRgb(40, 50, 70)),
                Brushes.White, Solid(Color.FromRgb(210, 215, 220)), Color.FromRgb(20, 30, 50), 0.7,
                Shadow(2, 4, 0.3), Shadow(1, 2, 0.15));
            b.Height = 40; b.Margin = new Thickness(8, 6, 8, 6);
            return b;
        }

        static IBrush MakeGradient(Color top, Color mid, Color bot)
        {
            var g = new LinearGradientBrush { StartPoint = Top, EndPoint = Bottom };
            g.GradientStops.Add(new GradientStop(top, 0));
            g.GradientStops.Add(new GradientStop(mid, 0.5));
            g.GradientStops.Add(new GradientStop(bot, 1));
            return g;
        }

        // === Radio Row ===
        public static RadioButton CreateRadioRow(string text)
        {
            return new RadioButton
            {
                Content = text, Height = 40, FontSize = 14, Foreground = Solid(RowLabel),
                VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 0, 10, 0),
                BorderThickness = new Thickness(0), // Fluent draws the border around the label only, which looked like a stray line
                Background = Brushes.White, GroupName = "DistroSelection"
            };
        }

        // === Progress Bar ===
        public static ProgressBar CreateProgressBar()
        {
            var fg = new LinearGradientBrush { StartPoint = Top, EndPoint = Bottom };
            fg.GradientStops.Add(new GradientStop(Color.FromRgb(100, 170, 255), 0));
            fg.GradientStops.Add(new GradientStop(Color.FromRgb(50, 120, 215), 0.45));
            fg.GradientStops.Add(new GradientStop(Color.FromRgb(40, 100, 195), 0.55));
            fg.GradientStops.Add(new GradientStop(Color.FromRgb(70, 140, 235), 1));
            return new ProgressBar
            {
                Height = 20, MinHeight = 20, Minimum = 0, Maximum = 100,
                Margin = new Thickness(12, 8, 12, 4),
                BorderBrush = Solid(Color.FromRgb(120, 130, 150)),
                BorderThickness = new Thickness(1),
                Background = Solid(Color.FromRgb(230, 232, 238)),
                Foreground = fg,
                CornerRadius = new CornerRadius(3)
            };
        }

        // === Status Text ===
        public static TextBlock CreateStatusText(string text = "")
        {
            return new TextBlock
            {
                Text = text, FontSize = 13,
                Foreground = Solid(RowValue),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 4, 10, 8)
            };
        }
    }
}
