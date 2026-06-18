using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace PmxInstaller
{
    public class MainWindow : Window
    {
        private readonly IsoDownloader _downloader = new();
        private PmxRelease _release;
        private string _releaseError;
        private HardwareInfo _hw;
        private string _isoPath;
        private UsbDrive _selectedUsb;
        private CancellationTokenSource _cts;

        private readonly string _isoDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "linuxsimplify-pmx");

        private Action _render;          // rebuilds the current static page
        private bool _allowToggle = true;
        private ScrollViewer _scroll;

        public MainWindow()
        {
            Title = "LinuxSimplify - PMX";
            Width = 560; Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _scroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            ShowWelcome();

            _ = Task.Run(async () =>
            {
                var hw = LinuxSystem.ScanHardware();
                Dispatcher.UIThread.Post(() => _hw = hw);
                try
                {
                    var r = await ReleaseResolver.ResolveLatestAsync();
                    Dispatcher.UIThread.Post(() => _release = r);
                }
                catch (Exception e) { Dispatcher.UIThread.Post(() => _releaseError = e.Message); }
            });
        }

        // ---- shell + theme ----
        private void Compose(Control page)
        {
            Background = Ui.WindowBackground();

            var header = new Border { Height = 44, Background = Ui.HeaderBackground() };
            if (Ui.Pmx) { header.BorderBrush = new SolidColorBrush(Color.Parse("#1F2E40")); header.BorderThickness = new Thickness(0, 0, 0, 1); }
            var hg = new Grid { Margin = new Thickness(8, 0, 8, 0) };
            var title = new TextBlock
            {
                Text = "LINUXSIMPLIFY - PMX",
                FontFamily = Ui.Pmx ? Ui.Mono : FontFamily.Default,
                FontSize = 14, FontWeight = FontWeight.SemiBold,
                Foreground = Ui.Pmx ? Ui.Cyan : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var toggle = Ui.Toggle(Ui.Pmx ? "RETRO" : "PMX OS", OnToggleTheme);
            ((Control)toggle).VerticalAlignment = VerticalAlignment.Center;
            ((Control)toggle).Opacity = _allowToggle ? 1.0 : 0.4;
            hg.Children.Add(title);
            hg.Children.Add(toggle);
            header.Child = hg;

            _scroll.Content = page;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetRow(header, 0); Grid.SetRow(_scroll, 1);
            root.Children.Add(header); root.Children.Add(_scroll);
            Content = root;
        }

        private void OnToggleTheme()
        {
            if (!_allowToggle) return;
            Ui.Pmx = !Ui.Pmx;
            _render?.Invoke();
        }

        private StackPanel PageBox(bool center = false)
        {
            var b = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
            if (center) b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        // ===================================================================
        //  Pages
        // ===================================================================
        private void ShowWelcome()
        {
            _allowToggle = true; _render = ShowWelcome;
            var b = PageBox(center: true);
            b.Children.Add(Ui.Kicker("PANMOX"));
            b.Children.Add(Ui.Headline("LinuxSimplify", 38, true));
            b.Children.Add(Ui.Body("Put PMX OS on a USB drive"));
            b.Children.Add(new Control { Height = 8 });
            b.Children.Add(Ui.PillButton("Scan my system", true, ShowSpecs));
            Compose(b);
        }

        private void ShowSpecs()
        {
            _allowToggle = true; _render = ShowSpecs;
            var b = PageBox();
            b.Children.Add(Ui.Section("Your System"));
            var card = new StackPanel();
            var hw = _hw;
            if (hw != null)
            {
                card.Children.Add(Ui.SpecRow("CPU", $"{hw.Cpu} ({hw.Cores}C/{hw.Threads}T)", false));
                card.Children.Add(Ui.SpecRow("RAM", $"{hw.RamGb} GB", false));
                if (hw.Gpus.Count == 0) card.Children.Add(Ui.SpecRow("GPU", "Unknown", false));
                for (int i = 0; i < hw.Gpus.Count; i++)
                    card.Children.Add(Ui.SpecRow(hw.Gpus.Count > 1 ? $"GPU {i + 1}" : "GPU", hw.Gpus[i], false));
                card.Children.Add(Ui.SpecRow("Storage", $"{hw.StorageGb:0} GB ({hw.StorageType})", false));
                card.Children.Add(Ui.SpecRow("Boot", hw.Boot, true));
            }
            else card.Children.Add(Ui.SpecRow("Scanning", "…", true));
            b.Children.Add(Ui.Card(card));

            var next = Ui.PillButton("Next", false, ShowPmx);
            ((Control)next).Margin = new Thickness(0, 8, 0, 0);
            b.Children.Add(next);
            Compose(b);
        }

        private void ShowPmx()
        {
            _allowToggle = true; _render = ShowPmx;
            var b = PageBox();
            b.Children.Add(Ui.Section("Install PMX OS"));

            var inner = new StackPanel { Spacing = 4, Margin = new Thickness(16) };
            inner.Children.Add(Ui.Kicker("PANMOX"));
            inner.Children.Add(Ui.Headline("PMX OS", 34, true));
            string ver = _release?.Version ?? "";
            inner.Children.Add(Ui.Body(string.IsNullOrEmpty(ver) ? "latest release" : $"latest release — {ver}",
                                          mono: true));
            inner.Children.Add(Ui.Body("A peer-to-peer internet, in an operating system.\n" +
                                          "Debian-based, KDE Plasma. No cloud. No telemetry. No boss."));
            b.Children.Add(Ui.Card(inner));

            var status = Ui.Body("", Ui.Err);
            b.Children.Add(status);
            if (_release == null)
                status.Text = _releaseError != null
                    ? $"Couldn't reach the latest release ({_releaseError})."
                    : "Couldn't reach the PMX OS release server.";

            var dl = Ui.PillButton("Download PMX OS", true, () =>
            {
                if (_release == null)
                {
                    status.Text = "Retrying…";
                    _ = RetryResolve(status);
                    return;
                }
                StartDownload();
            });
            ((Control)dl).Margin = new Thickness(0, 8, 0, 0);
            b.Children.Add(dl);
            Compose(b);
        }

        private async Task RetryResolve(TextBlock status)
        {
            try
            {
                _release = await ReleaseResolver.ResolveLatestAsync();
                ShowPmx();
            }
            catch (Exception e) { status.Text = $"Still couldn't reach the release ({e.Message})."; }
        }

        // ---- download ----
        private TextBlock _dlTitle, _dlSub, _dlVerify;
        private ProgressBar _dlBar;
        private StackPanel _dlExtra;

        private async void StartDownload()
        {
            _allowToggle = false; _render = null;
            _cts = new CancellationTokenSource();

            var b = PageBox(center: true);
            _dlTitle = BigText("Downloading…");
            _dlBar = Ui.Progress();
            _dlSub = Ui.Body("", null, true);
            _dlVerify = Ui.Body("");
            _dlExtra = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            b.Children.Add(_dlTitle); b.Children.Add(_dlBar); b.Children.Add(_dlSub);
            b.Children.Add(_dlVerify); b.Children.Add(_dlExtra);
            Compose(b);

            try
            {
                try { if (Directory.Exists(_isoDir)) foreach (var f in Directory.GetFiles(_isoDir, "*.iso")) File.Delete(f); } catch { }
                Directory.CreateDirectory(_isoDir);
                _isoPath = Path.Combine(_isoDir, _release.IsoFileName);

                var prog = new Progress<DownloadState>(s =>
                {
                    _dlBar.Value = s.Percent;
                    _dlSub.Text = Human(s.Downloaded) + (s.Total > 0 ? $" / {Human(s.Total)}" : "");
                });
                var urls = _release.Parts.Select(p => p.Url).ToList();
                bool ok = await _downloader.DownloadPartsAsync(urls, _isoPath, prog, _cts.Token);
                if (!ok) { DownloadFailed("Download failed"); return; }

                _dlTitle.Text = "Verifying…"; _dlBar.IsIndeterminate = true;
                bool? verified = null;
                if (!string.IsNullOrEmpty(_release.ChecksumUrl))
                {
                    var txt = await _downloader.FetchTextAsync(_release.ChecksumUrl);
                    var h = await _downloader.ComputeHashAsync(_isoPath, _release.ChecksumType, null);
                    if (txt != null && h != null) verified = _downloader.Verify(txt, _release.IsoFileName, h);
                }

                _dlBar.IsIndeterminate = false; _dlBar.Value = 100;
                _dlTitle.Text = "Download complete";
                if (verified == true) { _dlVerify.Text = "✓ Download is safe"; _dlVerify.Foreground = Ui.Ok; }
                else if (verified == false) { _dlVerify.Text = "⚠ Download might not be safe"; _dlVerify.Foreground = Ui.Warn; }
                else { _dlVerify.Text = "Could not verify download"; }

                var next = Ui.PillButton("Next", false, ShowFlashPrompt);
                ((Control)next).Margin = new Thickness(0, 12, 0, 0);
                _dlExtra.Children.Add(next);
            }
            catch (OperationCanceledException) { DownloadFailed("Cancelled"); }
            catch (Exception e) { DownloadFailed(e.Message); }
        }

        private void DownloadFailed(string msg)
        {
            _dlBar.IsIndeterminate = false;
            _dlTitle.Text = "Download failed";
            _dlSub.Text = msg; _dlSub.Foreground = Ui.Err;
            var retry = Ui.PillButton("Try Again", false, () => { _allowToggle = true; ShowPmx(); });
            ((Control)retry).Margin = new Thickness(0, 12, 0, 0);
            _dlExtra.Children.Add(retry);
        }

        // ---- flash prompt ----
        private void ShowFlashPrompt()
        {
            _allowToggle = true; _render = ShowFlashPrompt;
            var b = PageBox(center: true);
            b.Children.Add(Ui.PillButton("Flash to USB Drive", true, ShowUsbSelect));
            b.Children.Add(Ui.Body("⚠ This will erase everything on the USB drive", Ui.Err));
            Compose(b);
        }

        // ---- usb select ----
        private StackPanel _usbList;
        private Control _usbNext;

        private void ShowUsbSelect()
        {
            _allowToggle = true; _render = ShowUsbSelect;
            _selectedUsb = null;
            var b = PageBox();
            b.Children.Add(Ui.Section("Choose USB Drive"));
            _usbList = new StackPanel();
            b.Children.Add(Ui.Card(_usbList));

            var rescan = Ui.PillButton("Rescan", false, RefreshUsb);
            ((Control)rescan).Margin = new Thickness(0, 6, 0, 0);
            b.Children.Add(rescan);

            _usbNext = Ui.PillButton("Next", true, () => { if (_selectedUsb != null) StartFlash(); });
            ((Control)_usbNext).Margin = new Thickness(0, 6, 0, 0);
            ((Control)_usbNext).Opacity = 0.4;
            b.Children.Add(_usbNext);

            Compose(b);
            RefreshUsb();
        }

        private void RefreshUsb()
        {
            _usbList.Children.Clear();
            var drives = LinuxSystem.ListUsb();
            if (drives.Count == 0)
            {
                var empty = Ui.Body("Plug in a USB drive, then Rescan…");
                empty.Margin = new Thickness(0, 16, 0, 16);
                _usbList.Children.Add(empty);
                _selectedUsb = null;
                if (_usbNext != null) _usbNext.Opacity = 0.4;
                return;
            }
            foreach (var d in drives)
            {
                var rb = new RadioButton
                {
                    Content = d.ToString(), GroupName = "usb",
                    Foreground = Ui.Pmx ? new SolidColorBrush(Color.Parse("#E8F0F5")) : new SolidColorBrush(Color.Parse("#50607A")),
                    FontFamily = Ui.Pmx ? Ui.Mono : FontFamily.Default,
                    Margin = new Thickness(10, 4, 10, 4)
                };
                var drive = d;
                rb.IsCheckedChanged += (_, __) => { if (rb.IsChecked == true) { _selectedUsb = drive; if (_usbNext != null) _usbNext.Opacity = 1.0; } };
                _usbList.Children.Add(rb);
            }
        }

        // ---- flashing ----
        private TextBlock _flTitle, _flSub;
        private ProgressBar _flBar;
        private StackPanel _flExtra;

        private async void StartFlash()
        {
            if (_selectedUsb == null) return;
            _allowToggle = false; _render = null;
            _cts = new CancellationTokenSource();

            var b = PageBox(center: true);
            _flTitle = BigText("Flashing to USB…");
            _flBar = Ui.Progress();
            _flSub = Ui.Body("", null, true);
            _flExtra = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            b.Children.Add(_flTitle); b.Children.Add(_flBar); b.Children.Add(_flSub); b.Children.Add(_flExtra);
            Compose(b);

            var prog = new Progress<int>(p => _flBar.Value = p);
            var (ok, err) = await LinuxSystem.FlashAsync(_isoPath, _selectedUsb.Path, prog, _cts.Token);
            if (ok)
            {
                try { if (_isoPath != null && File.Exists(_isoPath)) File.Delete(_isoPath); } catch { }
                ShowDone();
            }
            else
            {
                _flTitle.Text = "Flash failed";
                _flSub.Text = err ?? "Unknown error"; _flSub.Foreground = Ui.Err;
                var retry = Ui.PillButton("Try Again", false, () => { _allowToggle = true; ShowFlashPrompt(); });
                ((Control)retry).Margin = new Thickness(0, 12, 0, 0);
                _flExtra.Children.Add(retry);
            }
        }

        // ---- done ----
        private void ShowDone()
        {
            _allowToggle = true; _render = ShowDone;
            var b = PageBox(center: true);
            b.Children.Add(Ui.Kicker("PANMOX"));
            b.Children.Add(Ui.Headline("PMX OS", 34, true));
            b.Children.Add(Ui.Body("Ready to boot from USB"));

            var link = new TextBlock
            {
                Text = "panmox.org", Foreground = Ui.Link, FontSize = 15,
                FontFamily = Ui.Pmx ? Ui.Mono : FontFamily.Default,
                HorizontalAlignment = HorizontalAlignment.Center, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                TextDecorations = TextDecorations.Underline, Margin = new Thickness(0, 4, 0, 0)
            };
            link.PointerReleased += (_, __) => OpenSite();
            b.Children.Add(link);

            var done = Ui.PillButton("DONE", false, () => Close());
            ((Control)done).Margin = new Thickness(0, 16, 0, 0);
            b.Children.Add(done);
            Compose(b);
        }

        private static void OpenSite()
        {
            try { Process.Start(new ProcessStartInfo("xdg-open", ReleaseResolver.SiteUrl) { UseShellExecute = false }); }
            catch { }
        }

        // ---- small helpers ----
        private TextBlock BigText(string t) => new()
        {
            Text = t, FontSize = 20, FontWeight = FontWeight.SemiBold,
            Foreground = Ui.Pmx ? Brushes.White : new SolidColorBrush(Color.Parse("#32384F")),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        private static string Human(long n)
        {
            if (n <= 0) return "0 B";
            double d = n; string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0;
            while (d >= 1024 && i < u.Length - 1) { d /= 1024; i++; }
            return i == 0 ? $"{d:0} {u[i]}" : $"{d:0.0} {u[i]}";
        }
    }
}
