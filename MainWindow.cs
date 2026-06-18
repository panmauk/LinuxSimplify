using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Navigation;
using System.Windows.Threading;
using LinuxSimplify.Models;
using LinuxSimplify.Services;
using LinuxSimplify.UI;

namespace LinuxSimplify
{
    public partial class MainWindow : Window
    {
        private readonly HardwareScanner hardwareScanner;
        private readonly IsoDownloader isoDownloader;
        private readonly UsbFlasher usbFlasher;

        private readonly PmxReleaseResolver releaseResolver;

        private HardwareInfo currentHardware;
        private PmxReleaseResolver.PmxRelease pmxRelease;
        private DockPanel rootPanel;
        private StackPanel mainContent;
        private ScrollViewer scrollViewer;
        private CancellationTokenSource downloadCts;
        private UsbDrive selectedUsbDrive;
        private DownloadState currentDownloadState;
        private string isoFolderPath;
        private string downloadedIsoPath;
        private bool scanComplete = false;
        private bool isDownloading = false;
        private bool isFlashing = false;
        private DispatcherTimer usbPollTimer;

        // Theme switching: renderCurrentPage rebuilds whatever page is showing
        // when the look is toggled; allowThemeToggle gates it during download/flash.
        private Action renderCurrentPage;
        private bool allowThemeToggle = true;

        // PMX OS lives here. The releases are mirrored on GitHub.
        private const string PmxSiteUrl = "https://panmox.org";

        public MainWindow()
        {
            hardwareScanner = new HardwareScanner();
            isoDownloader = new IsoDownloader();
            usbFlasher = new UsbFlasher();
            releaseResolver = new PmxReleaseResolver();
            currentDownloadState = new DownloadState();

            // Environment.ProcessPath is the real .exe location and works even
            // for single-file publishes (Assembly.Location is empty there).
            string exeDir = Path.GetDirectoryName(Environment.ProcessPath)
                            ?? AppContext.BaseDirectory;
            // Keep ISOs in a writable per-user spot rather than next to the exe,
            // which may sit in Program Files / a read-only location.
            string baseDir = string.IsNullOrWhiteSpace(exeDir) ? AppContext.BaseDirectory : exeDir;
            try
            {
                var testProbe = Path.Combine(baseDir, ".pmx_write_test");
                File.WriteAllText(testProbe, "");
                File.Delete(testProbe);
            }
            catch
            {
                // Not writable (e.g. Program Files) — fall back to LocalAppData.
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "LinuxSimplify-PMX");
            }
            isoFolderPath = Path.Combine(baseDir, "ISO");

            InitializeComponent();
            Closing += OnWindowClosing;
            _ = ScanInBackground();
            ShowLockScreen();
        }

        private void InitializeComponent()
        {
            Title = "LinuxSimplify - PMX";
            Width = 550; Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = UIHelper.CreateLinenBackground();
            rootPanel = new DockPanel();
            Content = rootPanel;
        }

        private async Task ScanInBackground()
        {
            // Scan the hardware and find the latest PMX OS release in parallel.
            var hwTask = hardwareScanner.ScanHardwareAsync();
            var relTask = releaseResolver.ResolveLatestAsync();

            currentHardware = await hwTask;
            try { pmxRelease = await relTask; } catch { pmxRelease = null; }

            scanComplete = true;
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            StopUsbPolling();

            if (isFlashing)
            {
                if (MessageBox.Show("Flashing is still in progress. Quitting now could corrupt the USB drive. Quit anyway?", "LinuxSimplify",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                { e.Cancel = true; return; }
            }
            else if (isDownloading && downloadCts != null && !downloadCts.IsCancellationRequested)
            {
                if (MessageBox.Show("A download is still going. Quit anyway?", "LinuxSimplify",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.No)
                { e.Cancel = true; return; }
                downloadCts?.Cancel();
            }
        }

        private static readonly System.Net.Http.HttpClient connectivityClient =
            new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private async Task<bool> CheckInternetAsync()
        {
            try
            {
                var resp = await connectivityClient.GetAsync("https://www.google.com/generate_204");
                return resp.IsSuccessStatusCode;
            }
            catch { }
            try
            {
                var resp = await connectivityClient.GetAsync("https://captive.apple.com");
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private void SetupPage(string title = "LinuxSimplify - PMX")
        {
            StopUsbPolling();
            rootPanel.Children.Clear();
            Background = UIHelper.CreateLinenBackground();
            var nav = UIHelper.CreateNavigationBar(title, BuildThemeToggle());
            DockPanel.SetDock(nav, Dock.Top);
            rootPanel.Children.Add(nav);
            scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            mainContent = new StackPanel();
            scrollViewer.Content = mainContent;
            rootPanel.Children.Add(scrollViewer);
        }

        // =============================================================
        //  THEME TOGGLE (RETRO <-> PMX OS)
        // =============================================================
        private string ThemeButtonLabel()
            => UIHelper.Theme == AppTheme.Retro ? "PMX OS" : "RETRO";

        private Button BuildThemeToggle()
        {
            var btn = UIHelper.CreateThemeToggleButton(ThemeButtonLabel());
            btn.IsEnabled = allowThemeToggle;
            btn.Click += (s, e) => ToggleTheme();
            return btn;
        }

        private void ToggleTheme()
        {
            if (!allowThemeToggle) return;
            UIHelper.Theme = UIHelper.Theme == AppTheme.Retro ? AppTheme.Pmx : AppTheme.Retro;
            renderCurrentPage?.Invoke();
        }

        // Page-heading text colour ("Downloading…", "Flashing to USB…") per theme.
        private Brush HeadingBrush()
            => new SolidColorBrush(UIHelper.Theme == AppTheme.Pmx
                ? Color.FromRgb(232, 240, 245)
                : Color.FromRgb(50, 60, 80));

        // Big brand headline — serif + accent dot in PMX, light sans in Retro.
        private FrameworkElement BrandHeadline(string text, double fontSize, string kicker, Color dotColor, bool onLightCard = false)
        {
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            if (UIHelper.Theme == AppTheme.Pmx)
            {
                if (!string.IsNullOrEmpty(kicker))
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = string.Join(" ", kicker.ToUpper().ToCharArray()),
                        FontFamily = UIHelper.PmxMono, FontSize = 12,
                        Foreground = new SolidColorBrush(UIHelper.PmxCyan),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                }
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                row.Children.Add(new TextBlock
                {
                    Text = text, FontFamily = UIHelper.PmxSerif, FontSize = fontSize, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Bottom
                });
                row.Children.Add(new System.Windows.Controls.Border
                {
                    Width = fontSize * 0.16, Height = fontSize * 0.16,
                    Background = new SolidColorBrush(dotColor),
                    CornerRadius = new CornerRadius(1),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(7, 0, 0, fontSize * 0.13)
                });
                stack.Children.Add(row);
            }
            else
            {
                stack.Children.Add(new TextBlock
                {
                    Text = text, FontSize = fontSize, FontWeight = FontWeights.Light,
                    Foreground = onLightCard ? new SolidColorBrush(Color.FromRgb(40, 45, 60)) : Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Effect = onLightCard ? null : new DropShadowEffect { Color = Colors.Black, Opacity = 0.6, BlurRadius = 4, ShadowDepth = 2 }
                });
            }
            return stack;
        }

        // =============================================================
        //  PAGE 1: LOCK SCREEN
        // =============================================================
        private void ShowLockScreen()
        {
            allowThemeToggle = true;
            renderCurrentPage = ShowLockScreen;

            rootPanel.Children.Clear();
            var lockScreen = new Grid { Background = UIHelper.CreateDarkGradientBackground() };

            var titleStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            titleStack.Children.Add(BrandHeadline("LinuxSimplify", 42, "PANMOX", UIHelper.PmxPink));
            lockScreen.Children.Add(titleStack);

            var sliderContainer = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 30)
            };
            var slider = UIHelper.CreateSlideToUnlock("slide to scan", async () =>
            {
                SoundHelper.PlayUnlock();
                while (!scanComplete) await Task.Delay(50);
                ShowSpecsPage();
            });
            sliderContainer.Children.Add(slider);
            lockScreen.Children.Add(sliderContainer);

            lockScreen.Children.Add(BuildCornerToggle());

            rootPanel.Children.Add(lockScreen);
        }

        // Theme toggle for the full-screen pages (lock / done) that have no nav bar.
        private Button BuildCornerToggle()
        {
            var btn = UIHelper.CreateThemeToggleButton(ThemeButtonLabel());
            btn.IsEnabled = allowThemeToggle;
            btn.Click += (s, e) => ToggleTheme();
            btn.HorizontalAlignment = HorizontalAlignment.Right;
            btn.VerticalAlignment = VerticalAlignment.Top;
            btn.Margin = new Thickness(0, 12, 12, 0);
            return btn;
        }

        // =============================================================
        //  PAGE 2: SPECS — factual only, no labels
        // =============================================================
        private void ShowSpecsPage()
        {
            allowThemeToggle = true;
            renderCurrentPage = ShowSpecsPage;
            SetupPage();

            mainContent.Children.Add(UIHelper.CreateSectionHeader("Your System"));

            var hw = new StackPanel();
            if (currentHardware != null)
            {
                hw.Children.Add(UIHelper.CreateListRow("CPU",
                    $"{currentHardware.CpuModel} ({currentHardware.CpuCores}C/{currentHardware.CpuThreads}T)"));
                hw.Children.Add(UIHelper.CreateListRow("RAM", $"{currentHardware.RamGB} GB"));

                for (int i = 0; i < currentHardware.Gpus.Count; i++)
                {
                    var gpu = currentHardware.Gpus[i];
                    string label = currentHardware.Gpus.Count > 1 ? $"GPU {i + 1}" : "GPU";
                    string val = gpu.Model;
                    if (gpu.VramGB > 0) val += $" ({gpu.VramGB:F0} GB)";
                    hw.Children.Add(UIHelper.CreateListRow(label, val));
                }

                bool nvme = currentHardware.StorageDevices.Any(s => s.Type == "NVMe");
                bool ssd = currentHardware.StorageDevices.Any(s => s.Type == "SSD");
                var total = currentHardware.StorageDevices.Sum(s => s.SizeGB);
                hw.Children.Add(UIHelper.CreateListRow("Storage",
                    $"{total:F0} GB ({(nvme ? "NVMe" : ssd ? "SSD" : "HDD")})"));
                hw.Children.Add(UIHelper.CreateListRow("Boot", currentHardware.BootMode, true));
            }
            mainContent.Children.Add(UIHelper.CreateGroupedSection(hw));

            var btnP = new StackPanel { Margin = new Thickness(0, 20, 0, 20) };
            var next = UIHelper.CreateDarkNextButton("Next");
            next.Click += (s, e) => ShowPmxPage();
            btnP.Children.Add(next);
            mainContent.Children.Add(btnP);
        }

        // =============================================================
        //  PAGE 3: PMX OS — the one and only distro
        // =============================================================
        private void ShowPmxPage()
        {
            allowThemeToggle = true;
            renderCurrentPage = ShowPmxPage;
            SetupPage();

            mainContent.Children.Add(UIHelper.CreateSectionHeader("Install PMX OS"));

            var card = new StackPanel();
            var headline = BrandHeadline("PMX OS", 32, "PANMOX", UIHelper.PmxPink, onLightCard: true);
            headline.Margin = new Thickness(0, 16, 0, 2);
            card.Children.Add(headline);

            bool pmx = UIHelper.Theme == AppTheme.Pmx;
            var verColor = pmx ? UIHelper.PmxCyan : Color.FromRgb(110, 120, 140);
            var taglineColor = pmx ? Color.FromRgb(150, 162, 180) : Color.FromRgb(90, 100, 120);

            string ver = (pmxRelease != null && pmxRelease.Resolved && !string.IsNullOrEmpty(pmxRelease.Version))
                ? pmxRelease.Version : "";
            card.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(ver) ? "latest release" : $"latest release — {ver}",
                FontSize = 13,
                FontFamily = pmx ? UIHelper.PmxMono : null,
                Foreground = new SolidColorBrush(verColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            });
            card.Children.Add(new TextBlock
            {
                Text = "A peer-to-peer internet, in an operating system.\nDebian-based, KDE Plasma. No cloud. No telemetry. No boss.",
                FontSize = 13, FontStyle = FontStyles.Italic,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(taglineColor),
                Margin = new Thickness(16, 0, 16, 16)
            });
            mainContent.Children.Add(UIHelper.CreateGroupedSection(card));

            // Status / error line — used when the release can't be reached.
            var status = new TextBlock
            {
                Text = "", FontSize = 12, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 90, 60)),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 10, 20, 0)
            };
            mainContent.Children.Add(status);

            if (pmxRelease == null || !pmxRelease.Resolved)
            {
                status.Text = (pmxRelease != null && !string.IsNullOrEmpty(pmxRelease.Error))
                    ? $"Couldn't reach the latest release ({pmxRelease.Error}). Check your connection and try again."
                    : "Couldn't reach the PMX OS release server. Check your connection and try again.";
            }

            var btnP = new StackPanel { Margin = new Thickness(0, 14, 0, 20) };
            var dl = UIHelper.CreateGreenActionButton("Download PMX OS");
            dl.Click += (s, e) =>
            {
                if (pmxRelease == null || !pmxRelease.Resolved || pmxRelease.Parts.Count == 0)
                {
                    // Network may have just come back — try resolving once more.
                    _ = RetryResolveThenDownload(status);
                    return;
                }
                _ = DoDownloadAsync();
            };
            btnP.Children.Add(dl);
            mainContent.Children.Add(btnP);
        }

        private async Task RetryResolveThenDownload(TextBlock status)
        {
            status.Text = "Looking for the latest release…";
            try { pmxRelease = await releaseResolver.ResolveLatestAsync(); } catch { }
            if (pmxRelease != null && pmxRelease.Resolved && pmxRelease.Parts.Count > 0)
            {
                _ = DoDownloadAsync();
            }
            else
            {
                status.Text = (pmxRelease != null && !string.IsNullOrEmpty(pmxRelease.Error))
                    ? $"Still couldn't reach the latest release ({pmxRelease.Error})."
                    : "Still couldn't reach the PMX OS release server.";
            }
        }

        // =============================================================
        //  PAGE 4: DOWNLOADING — just progress bar + "Downloading…"
        // =============================================================
        private async Task DoDownloadAsync()
        {
            allowThemeToggle = false;
            renderCurrentPage = null;
            SetupPage();
            isDownloading = true;

            // Check internet first
            if (!await CheckInternetAsync())
            {
                isDownloading = false;
                mainContent.Children.Add(new TextBlock
                {
                    Text = "No internet connection", FontSize = 18, FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 60, 0, 10)
                });
                mainContent.Children.Add(new TextBlock
                {
                    Text = "Connect to the internet and try again",
                    FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(100, 110, 130)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 0)
                });
                AddRetry();
                return;
            }

            if (pmxRelease == null || !pmxRelease.Resolved || pmxRelease.Parts.Count == 0)
            {
                isDownloading = false;
                mainContent.Children.Add(new TextBlock
                {
                    Text = "Couldn't find the PMX OS release", FontSize = 18, FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 60, 0, 10)
                });
                AddRetry();
                return;
            }

            TextBlock al = null, et = null;
            try
            {
                try { Directory.CreateDirectory(isoFolderPath); } catch { }

                // Clean old ISOs — move any existing .iso files to recycle bin
                CleanOldIsos();

                string fn = string.IsNullOrEmpty(pmxRelease.IsoFileName) ? "PMX-OS-amd64.iso" : pmxRelease.IsoFileName;
                downloadedIsoPath = Path.Combine(isoFolderPath, fn);

                al = new TextBlock
                {
                    Text = "Downloading…", FontSize = 18, FontWeight = FontWeights.Medium,
                    Foreground = HeadingBrush(),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 40, 0, 20)
                };
                mainContent.Children.Add(al);

                var ps = new StackPanel();
                var pb = UIHelper.CreateProgressBar(); ps.Children.Add(pb);
                var st = UIHelper.CreateStatusText(""); ps.Children.Add(st);
                et = UIHelper.CreateStatusText(""); et.Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60)); et.FontWeight = FontWeights.SemiBold; ps.Children.Add(et);
                mainContent.Children.Add(UIHelper.CreateGroupedSection(ps));

                // Verification status text (shown after download)
                var verifyText = new TextBlock
                {
                    Text = "", FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 12, 0, 0)
                };
                mainContent.Children.Add(verifyText);

                downloadCts = new CancellationTokenSource();

                var prog = new Progress<DownloadState>(s => Dispatcher.Invoke(() =>
                {
                    currentDownloadState = s; pb.Value = s.Percentage;
                    if (!string.IsNullOrEmpty(s.CurrentAction)) st.Text = s.CurrentAction;
                    if (!string.IsNullOrEmpty(s.ErrorMessage)) et.Text = s.ErrorMessage;
                }));

                var urls = pmxRelease.Parts.Select(p => p.Url).ToList();
                bool ok = await isoDownloader.DownloadPartsAsync(urls, downloadedIsoPath, prog, downloadCts.Token);
                if (!ok) { Dispatcher.Invoke(() => { al.Text = "Download failed"; }); isDownloading = false; AddRetry(); return; }

                Dispatcher.Invoke(() => { al.Text = "Verifying…"; st.Text = ""; pb.IsIndeterminate = true; });

                bool verified = false;
                bool checksumAvailable = false;

                if (!string.IsNullOrEmpty(pmxRelease.ChecksumUrl))
                {
                    checksumAvailable = true;
                    var cf = await isoDownloader.DownloadChecksumFileAsync(pmxRelease.ChecksumUrl);
                    var sp = new Progress<string>(_ => { });
                    string algo = pmxRelease.ChecksumType ?? "sha256";
                    var h = await isoDownloader.ComputeChecksumAsync(downloadedIsoPath, algo, sp);
                    if (cf != null && h != null)
                    {
                        verified = isoDownloader.VerifySha256(h, cf, downloadedIsoPath);
                    }
                }

                isDownloading = false;

                Dispatcher.Invoke(() =>
                {
                    pb.IsIndeterminate = false;
                    pb.Value = 100;
                    al.Text = "Download complete";

                    if (checksumAvailable)
                    {
                        if (verified)
                        {
                            verifyText.Text = "✓ Download is safe";
                            verifyText.Foreground = new SolidColorBrush(Color.FromRgb(80, 150, 50));
                            verifyText.FontWeight = FontWeights.SemiBold;
                        }
                        else
                        {
                            verifyText.Text = "⚠ Download might not be safe";
                            verifyText.Foreground = new SolidColorBrush(Color.FromRgb(200, 150, 30));
                            verifyText.FontWeight = FontWeights.SemiBold;
                        }
                    }
                    else
                    {
                        verifyText.Text = "Could not verify download";
                        verifyText.Foreground = new SolidColorBrush(Color.FromRgb(140, 145, 155));
                    }

                    // Next button — user decides when to proceed
                    var btnP = new StackPanel { Margin = new Thickness(0, 20, 0, 20) };
                    var next = UIHelper.CreateDarkNextButton("Next");
                    next.Click += (s, e) => ShowFlashPrompt();
                    btnP.Children.Add(next);
                    mainContent.Children.Add(btnP);
                });
            }
            catch (OperationCanceledException) { isDownloading = false; Dispatcher.Invoke(() => { if (al != null) al.Text = "Cancelled"; }); AddRetry(); }
            catch (Exception ex) { isDownloading = false; Dispatcher.Invoke(() => { if (al != null) al.Text = "Download failed"; if (et != null) et.Text = ex.Message; }); AddRetry(); }
        }

        private void AddRetry()
        {
            Dispatcher.Invoke(() =>
            {
                var p = new StackPanel { Margin = new Thickness(0, 14, 0, 15) };
                var b = UIHelper.CreateDarkNextButton("Try Again");
                b.Click += (s, e) => ShowPmxPage();
                p.Children.Add(b);
                mainContent.Children.Add(p);
            });
        }

        // =============================================================
        //  PAGE 5: FLASH PROMPT
        // =============================================================
        private void ShowFlashPrompt()
        {
            allowThemeToggle = true;
            renderCurrentPage = ShowFlashPrompt;
            SetupPage();

            var c = new StackPanel { Margin = new Thickness(0, 80, 0, 0) };

            var fb = UIHelper.CreateGreenActionButton("Flash to USB Drive");
            fb.Margin = new Thickness(30, 6, 30, 6); fb.Height = 54; fb.FontSize = 20;
            fb.Click += (s, e) => ShowUsbSelect();
            c.Children.Add(fb);

            c.Children.Add(new TextBlock
            {
                Text = "⚠ This will erase everything on the USB drive",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60)),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0)
            });

            mainContent.Children.Add(c);
        }

        // =============================================================
        //  PAGE 6: USB SELECT — live-polls for drives every 2 seconds
        // =============================================================
        private StackPanel usbListPanel;
        private StackPanel usbPageContent;
        private TextBlock usbEmptyText;
        private Button usbNextButton;
        private List<UsbDrive> lastKnownDrives = new List<UsbDrive>();

        private void ShowUsbSelect()
        {
            allowThemeToggle = true;
            renderCurrentPage = ShowUsbSelect;
            StopUsbPolling();
            SetupPage();
            mainContent.Children.Add(UIHelper.CreateSectionHeader("Choose USB Drive"));

            usbListPanel = new StackPanel();
            usbEmptyText = new TextBlock
            {
                Text = "Plug in a USB drive…",
                FontSize = 14, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 125, 140)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20)
            };

            usbPageContent = new StackPanel();
            usbPageContent.Children.Add(usbListPanel);
            usbPageContent.Children.Add(usbEmptyText);
            mainContent.Children.Add(UIHelper.CreateGroupedSection(usbPageContent));

            var bp = new StackPanel { Margin = new Thickness(0, 20, 0, 20) };
            usbNextButton = UIHelper.CreateDarkNextButton("Next");
            usbNextButton.IsEnabled = false;
            usbNextButton.Click += (s, e) =>
            {
                if (selectedUsbDrive == null) return;
                StopUsbPolling();
                _ = DoFlashAsync();
            };
            bp.Children.Add(usbNextButton);
            mainContent.Children.Add(bp);

            // Initial scan
            selectedUsbDrive = null;
            lastKnownDrives.Clear();
            RefreshUsbList();

            // Start polling every 2 seconds
            usbPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            usbPollTimer.Tick += (s, e) => RefreshUsbList();
            usbPollTimer.Start();
        }

        private bool usbRefreshing = false;
        private void RefreshUsbList()
        {
            if (usbRefreshing) return; // Skip if previous refresh still running
            usbRefreshing = true;

            Task.Run(() =>
            {
                try
                {
                    var drives = hardwareScanner.GetUsbDrives();
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // Check if list changed (compare by disk number)
                            var currentIds = drives.Select(d => d.DiskNumber).OrderBy(x => x).ToList();
                            var lastIds = lastKnownDrives.Select(d => d.DiskNumber).OrderBy(x => x).ToList();
                            if (currentIds.SequenceEqual(lastIds)) return; // No change

                            lastKnownDrives = drives;

                            // Remember current selection
                            int selectedDisk = selectedUsbDrive?.DiskNumber ?? -1;
                            selectedUsbDrive = null;

                            usbListPanel.Children.Clear();

                            if (drives.Count == 0)
                            {
                                usbEmptyText.Visibility = Visibility.Visible;
                                usbNextButton.IsEnabled = false;
                                return;
                            }

                            usbEmptyText.Visibility = Visibility.Collapsed;

                            foreach (var d in drives)
                            {
                                var r = UIHelper.CreateRadioRow($"{d.Name} ({d.SizeGB} GB)");
                                r.GroupName = "Usb";
                                var drive = d;
                                r.Checked += (s, e) => { selectedUsbDrive = drive; usbNextButton.IsEnabled = true; };

                                if (d.DiskNumber == selectedDisk)
                                {
                                    r.IsChecked = true;
                                    selectedUsbDrive = d;
                                }
                                else if (selectedUsbDrive == null)
                                {
                                    r.IsChecked = true;
                                    selectedUsbDrive = d;
                                }
                                usbListPanel.Children.Add(r);
                            }

                            usbNextButton.IsEnabled = selectedUsbDrive != null;
                        }
                        catch { }
                    });
                }
                catch { }
                finally { usbRefreshing = false; }
            });
        }

        private void StopUsbPolling()
        {
            if (usbPollTimer != null)
            {
                usbPollTimer.Stop();
                usbPollTimer = null;
            }
        }

        // =============================================================
        //  PAGE 7: FLASHING — just progress bar + "Flashing to USB…"
        // =============================================================
        private async Task DoFlashAsync()
        {
            allowThemeToggle = false;
            renderCurrentPage = null;
            SetupPage();
            isFlashing = true;

            var al = new TextBlock
            {
                Text = "Flashing to USB…", FontSize = 18, FontWeight = FontWeights.Medium,
                Foreground = HeadingBrush(),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 20)
            };
            mainContent.Children.Add(al);

            var ps = new StackPanel();
            var pb = UIHelper.CreateProgressBar(); ps.Children.Add(pb);
            var et = UIHelper.CreateStatusText(""); et.Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60)); et.FontWeight = FontWeights.SemiBold; ps.Children.Add(et);
            mainContent.Children.Add(UIHelper.CreateGroupedSection(ps));

            var fp = new Progress<string>(msg => Dispatcher.Invoke(() =>
            {
                if (msg.StartsWith("Flashing..."))
                {
                    var p = msg.Replace("Flashing... ", "").Replace("%", "").Trim();
                    if (int.TryParse(p, out int v)) pb.Value = v;
                }
                else if (msg.Contains("ERROR")) { al.Text = "Flash failed"; et.Text = msg.Replace("ERROR: ", ""); }
            }));

            bool ok = await usbFlasher.FlashIsoToUsbAsync(
                downloadedIsoPath, selectedUsbDrive.DiskNumber,
                currentHardware?.BootMode == "UEFI", fp);

            if (ok)
            {
                isFlashing = false;
                // Flashing is done — no need to keep a couple of GB on disk.
                DeleteDownloadedIso();
                await Task.Delay(300);
                ShowDonePage();
            }
            else
            {
                isFlashing = false;
                Dispatcher.Invoke(() =>
                {
                    al.Text = "Flash failed";
                    var p = new StackPanel { Margin = new Thickness(0, 14, 0, 15) };
                    var b = UIHelper.CreateDarkNextButton("Try Again");
                    b.Click += (s2, e2) => ShowFlashPrompt();
                    p.Children.Add(b);
                    mainContent.Children.Add(p);
                });
            }
        }

        // =============================================================
        //  PAGE 8: DONE — calm, centered, clean
        // =============================================================
        private void ShowDonePage()
        {
            allowThemeToggle = true;
            renderCurrentPage = ShowDonePage;

            rootPanel.Children.Clear();

            var bg = new Grid { Background = UIHelper.CreateDarkGradientBackground() };

            var center = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var brand = BrandHeadline("PMX OS", 36, "PANMOX", UIHelper.PmxPink);
            brand.Margin = new Thickness(0, 0, 0, 20);
            center.Children.Add(brand);

            center.Children.Add(new TextBlock
            {
                Text = "Ready to boot from USB", FontSize = 16,
                FontFamily = UIHelper.Theme == AppTheme.Pmx ? UIHelper.PmxMono : null,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 205, 215)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14)
            });

            // Link to the project's home — panmox.org
            var linkBlock = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 36)
            };
            var link = new Hyperlink(new Run("panmox.org"))
            {
                NavigateUri = new Uri(PmxSiteUrl),
                Foreground = new SolidColorBrush(UIHelper.Theme == AppTheme.Pmx ? UIHelper.PmxCyan : Color.FromRgb(140, 200, 255)),
                FontSize = 15
            };
            if (UIHelper.Theme == AppTheme.Pmx) linkBlock.FontFamily = UIHelper.PmxMono;
            link.RequestNavigate += OnRequestNavigate;
            linkBlock.Inlines.Add(link);
            center.Children.Add(linkBlock);

            var doneBtn = UIHelper.CreateDarkNextButton("DONE");
            doneBtn.Width = 300; doneBtn.Height = 52; doneBtn.FontSize = 22;
            doneBtn.Click += (s, e) => Application.Current.Shutdown();
            center.Children.Add(doneBtn);

            bg.Children.Add(center);

            // Credits at the bottom — visible but not loud
            var credits = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 18)
            };
            credits.Children.Add(new TextBlock
            {
                Text = "@actuallypanmauk on X/Twitter",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 165, 178)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            credits.Children.Add(new TextBlock
            {
                Text = "Debian underneath.",
                FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 145, 158)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            credits.Children.Add(new TextBlock
            {
                Text = "© 2025 PMX OS — GNU GPL v3",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(130, 135, 148)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            bg.Children.Add(credits);
            bg.Children.Add(BuildCornerToggle());

            rootPanel.Children.Add(bg);
        }

        private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { }
            e.Handled = true;
        }

        /// <summary>
        /// Deletes the downloaded ISO after successful flash.
        /// No reason to keep a couple of GB sitting around.
        /// </summary>
        private void DeleteDownloadedIso()
        {
            try
            {
                if (!string.IsNullOrEmpty(downloadedIsoPath) && File.Exists(downloadedIsoPath))
                    File.Delete(downloadedIsoPath);
            }
            catch { }

            // Also clean any other ISOs that might be lingering
            CleanOldIsos();
        }

        /// <summary>
        /// Deletes any .iso files in the ISO folder by moving them to the recycle bin.
        /// Silent — if anything fails, it just skips.
        /// </summary>
        private void CleanOldIsos()
        {
            try
            {
                if (!Directory.Exists(isoFolderPath)) return;
                var isoFiles = Directory.GetFiles(isoFolderPath, "*.iso");
                foreach (var f in isoFiles)
                {
                    try { MoveToRecycleBin(f); }
                    catch
                    {
                        // If recycle bin fails, just delete
                        try { File.Delete(f); } catch { }
                    }
                }
                // Also clean partial downloads
                var partials = Directory.GetFiles(isoFolderPath, "*.iso.tmp");
                foreach (var f in partials)
                {
                    try { File.Delete(f); } catch { }
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_ALLOWUNDO = 0x0040;      // Send to recycle bin
        private const ushort FOF_NOCONFIRMATION = 0x0010;  // Don't ask
        private const ushort FOF_NOERRORUI = 0x0400;       // No error dialog
        private const ushort FOF_SILENT = 0x0004;          // No progress dialog

        private static void MoveToRecycleBin(string filePath)
        {
            var fs = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = filePath + '\0' + '\0',  // Double-null terminated
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
            };
            SHFileOperation(ref fs);
        }
    }
}
