using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LinuxSimplify.Models;
using LinuxSimplify.Services;
using LinuxSimplify.UI;

namespace LinuxSimplify
{
    /// <summary>Avalonia port of the WPF MainWindow: same pages, same flow.</summary>
    public class MainWindow : Window
    {
        private readonly HardwareScanner hardwareScanner;
        private readonly CompatibilityAnalyzer compatibilityAnalyzer;
        private readonly IsoDownloader isoDownloader;
        private readonly UsbFlasher usbFlasher;

        private readonly DistroUrlResolver urlResolver;

        private HardwareInfo currentHardware;
        private List<DistroCompatibility> distroResults;
        private DockPanel rootPanel;
        private StackPanel mainContent;
        private ScrollViewer scrollViewer;
        private CancellationTokenSource downloadCts;
        private DistroCompatibility selectedDistro;
        private UsbDrive selectedUsbDrive;
        private DownloadState currentDownloadState;
        private string isoFolderPath;
        private string downloadedIsoPath;
        private volatile bool scanComplete = false;
        private bool isDownloading = false;
        private bool isFlashing = false;
        private bool allowClose = false;
        private DispatcherTimer usbPollTimer;

        public MainWindow()
        {
            // not "switch from Windows": people running this are already on Linux
            hardwareScanner = new HardwareScanner();
            compatibilityAnalyzer = new CompatibilityAnalyzer(windowsMigration: false);
            isoDownloader = new IsoDownloader();
            usbFlasher = new UsbFlasher();
            urlResolver = new DistroUrlResolver();
            distroResults = new List<DistroCompatibility>();
            currentDownloadState = new DownloadState();

            // ISOs go to <Downloads>/LinuxSimplify so cleaning up afterwards can never touch the user's own ISOs
            isoFolderPath = Path.Combine(Sys.DownloadsDir, "LinuxSimplify");

            InitializeComponent();
            Closing += OnWindowClosing;
            _ = ScanInBackground();
            ShowLockScreen();
        }

        private void InitializeComponent()
        {
            Title = "LinuxSimplify";
            Width = 550; Height = 680;
            MinWidth = 420; MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = UIHelper.CreateLinenBackground();
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.png"))
                    if (s != null) Icon = new WindowIcon(s);
            }
            catch { }
            rootPanel = new DockPanel();
            Content = rootPanel;
        }

        private async Task ScanInBackground()
        {
            // scanComplete MUST end up true whatever happens: the lock screen's slider waits on it
            try
            {
                // Run hardware scan and URL resolution in parallel
                var hwTask = hardwareScanner.ScanHardwareAsync();
                var urlTask = urlResolver.ResolveAllAsync();

                currentHardware = await hwTask;
                distroResults = compatibilityAnalyzer.AnalyzeCompatibility(currentHardware);

                // Apply resolved URLs (replace hardcoded with live ones)
                try
                {
                    var resolved = await urlTask;
                    DistroUrlResolver.ApplyResolved(distroResults, resolved, currentHardware.Gpus.Any(g => g.Vendor == "NVIDIA"));
                }
                catch { /* URL resolution failed, use hardcoded fallbacks */ }

                selectedDistro = distroResults.FirstOrDefault(d => d.IsRecommended)
                              ?? distroResults.FirstOrDefault();
                if (selectedDistro != null) selectedDistro.IsSelected = true;
            }
            catch { /* pages cope with null/empty results */ }
            finally { scanComplete = true; }
        }

        private void OnWindowClosing(object sender, WindowClosingEventArgs e)
        {
            if (allowClose) { StopUsbPolling(); return; }

            string question = null;
            if (isFlashing)
                question = "Flashing is still in progress. Quitting now could corrupt the USB drive. Quit anyway?";
            else if (isDownloading && downloadCts != null && !downloadCts.IsCancellationRequested)
                question = "A download is still going. Quit anyway?";

            if (question == null) { StopUsbPolling(); return; }

            // Avalonia dialogs are async: veto this close, ask, then close again if the answer is yes
            e.Cancel = true;
            _ = ConfirmQuitAsync(question);
        }

        private async Task ConfirmQuitAsync(string question)
        {
            if (await Dialogs.ConfirmAsync(this, question))
            {
                if (isDownloading) downloadCts?.Cancel();
                allowClose = true;
                Close();
            }
        }

        private static async Task Quit(Window w)
        {
            await Task.Yield();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
            else w.Close();
        }

        // One shared client — a client per check leaks sockets
        private static readonly System.Net.Http.HttpClient connectivityClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private async Task<bool> CheckInternetAsync()
        {
            foreach (var url in new[] { "https://www.google.com/generate_204", "https://captive.apple.com" })
            {
                try
                {
                    using (var resp = await connectivityClient.GetAsync(url))
                        if (resp.IsSuccessStatusCode) return true;
                }
                catch { }
            }
            return false;
        }

        /// <summary>Awaits a fire-and-forget page task and shows any exception instead of silently losing it.</summary>
        private async Task RunGuarded(Task task)
        {
            try { await task; }
            catch (Exception ex)
            {
                isDownloading = false; isFlashing = false;
                await Dialogs.MessageAsync(this, ex.Message);
            }
        }

        private void SetupPage(string title = "LinuxSimplify")
        {
            StopUsbPolling();
            rootPanel.Children.Clear();
            Background = UIHelper.CreateLinenBackground();
            var nav = UIHelper.CreateNavigationBar(title);
            DockPanel.SetDock(nav, Dock.Top);
            rootPanel.Children.Add(nav);
            scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };
            mainContent = new StackPanel();
            scrollViewer.Content = mainContent;
            rootPanel.Children.Add(scrollViewer);
        }

        // =============================================================
        //  PAGE 1: LOCK SCREEN
        // =============================================================
        private void ShowLockScreen()
        {
            rootPanel.Children.Clear();
            var lockScreen = new Grid { Background = UIHelper.CreateDarkGradientBackground() };

            var titleStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            titleStack.Children.Add(new TextBlock
            {
                Text = "LinuxSimplify", FontSize = 42, FontWeight = FontWeight.Light,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.6, BlurRadius = 4, OffsetX = 2, OffsetY = 2 }
            });
            lockScreen.Children.Add(titleStack);

            var sliderContainer = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 30)
            };
            var slider = UIHelper.CreateSlideToUnlock("slide to scan", async () =>
            {
                // this is an Action, i.e. async void: an escaping exception would crash the app
                try
                {
                    SoundHelper.PlayUnlock();
                    while (!scanComplete) await Task.Delay(50);
                    ShowSpecsPage();
                }
                catch (Exception ex) { await Dialogs.MessageAsync(this, ex.Message); }
            });
            sliderContainer.Children.Add(slider);
            lockScreen.Children.Add(sliderContainer);

            rootPanel.Children.Add(lockScreen);
        }

        // =============================================================
        //  PAGE 2: SPECS — factual only, no labels
        // =============================================================
        private void ShowSpecsPage()
        {
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
            next.Click += (s, e) => ShowDistroPage();
            btnP.Children.Add(next);
            mainContent.Children.Add(btnP);
        }

        // =============================================================
        //  PAGE 3: DISTRO PICKER — recommended on top, no incompatible
        // =============================================================
        private TextBlock distroNotes;

        private void ShowDistroPage()
        {
            SetupPage();

            if (selectedDistro != null && selectedDistro.IsRecommended)
            {
                mainContent.Children.Add(new TextBlock
                {
                    Text = $"★  Best pick: {selectedDistro.Name}",
                    FontSize = 13, FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(80, 150, 50)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(10, 14, 10, 0)
                });
            }

            mainContent.Children.Add(UIHelper.CreateSectionHeader("Select Distribution"));

            int idx = 0;
            if (selectedDistro != null)
            {
                int fi = distroResults.FindIndex(d => d.Name == selectedDistro.Name);
                if (fi >= 0) idx = fi;
            }

            var picker = UIHelper.CreateDistroPickerWidget(distroResults, (sel) =>
            {
                foreach (var d in distroResults) d.IsSelected = false;
                sel.IsSelected = true;
                selectedDistro = sel;
                UpdateNotes();
            }, idx);
            mainContent.Children.Add(picker);

            distroNotes = new TextBlock
            {
                Text = selectedDistro?.Notes ?? "", FontSize = 12, FontStyle = FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 110, 130)),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 6, 20, 4)
            };
            mainContent.Children.Add(distroNotes);

            var btnP = new StackPanel { Margin = new Thickness(0, 14, 0, 20) };
            var dl = UIHelper.CreateGreenActionButton("Download ISO");
            dl.Click += (s, e) =>
            {
                if (selectedDistro == null || isDownloading) return;
                _ = RunGuarded(DoDownloadAsync());
            };
            btnP.Children.Add(dl);
            mainContent.Children.Add(btnP);
        }

        private void UpdateNotes()
        {
            if (distroNotes != null) distroNotes.Text = selectedDistro?.Notes ?? "";
        }

        // =============================================================
        //  PAGE 4: DOWNLOADING — just progress bar + "Downloading…"
        // =============================================================
        private async Task DoDownloadAsync()
        {
            SetupPage();
            isDownloading = true;

            // Check internet first
            if (!await CheckInternetAsync())
            {
                isDownloading = false;
                mainContent.Children.Add(new TextBlock
                {
                    Text = "No internet connection", FontSize = 18, FontWeight = FontWeight.Medium,
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

            try { Directory.CreateDirectory(isoFolderPath); } catch { }

            // Remove leftovers of earlier runs (only files this app creates)
            CleanOldIsos();

            var distro = selectedDistro; // keep a stable reference for the whole download
            string fn = $"{distro.Name.Replace(" ", "_")}.iso";
            downloadedIsoPath = Path.Combine(isoFolderPath, fn);

            var al = new TextBlock
            {
                Text = "Downloading…", FontSize = 18, FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(50, 60, 80)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 20)
            };
            mainContent.Children.Add(al);

            var ps = new StackPanel();
            var pb = UIHelper.CreateProgressBar(); ps.Children.Add(pb);
            var et = UIHelper.CreateStatusText(""); et.Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60)); et.FontWeight = FontWeight.SemiBold; ps.Children.Add(et);
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

            try
            {
                // Progress<T> invokes this on the UI thread (it captures the SynchronizationContext)
                var prog = new Progress<DownloadState>(s =>
                {
                    currentDownloadState = s; pb.Value = s.Percentage;
                    et.Text = s.ErrorMessage ?? ""; // cleared again once a retry recovers
                });

                bool ok = await isoDownloader.DownloadIsoAsync(distro.DownloadUrl, downloadedIsoPath, prog, downloadCts.Token);
                if (!ok) { al.Text = "Download failed"; isDownloading = false; AddRetry(); return; }

                al.Text = "Verifying…"; pb.IsIndeterminate = true;

                // Mismatch = the file is corrupt or tampered with. Unavailable = nothing to compare against
                // (no checksum published / couldn't be fetched): reported as such, not as a failure.
                // (also checks the distro's OpenPGP signature where one is published)
                var verification = await isoDownloader.VerifyIsoAsync(downloadedIsoPath, distro.Name, distro.DownloadUrl,
                    distro.Sha256Url, distro.ChecksumType, distro.ExpectedHash);
                var status = verification.Status;

                isDownloading = false;
                pb.IsIndeterminate = false;
                pb.Value = 100;

                if (status == ChecksumStatus.Mismatch)
                {
                    // Never offer a file that failed its checksum or signature for flashing
                    try { File.Delete(downloadedIsoPath); } catch { }
                    al.Text = "Download is corrupted";
                    verifyText.Text = verification.Message;
                    verifyText.Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60));
                    verifyText.FontWeight = FontWeight.SemiBold;
                    AddRetry();
                    return;
                }

                al.Text = "Download complete";
                verifyText.Text = verification.Message;
                if (status == ChecksumStatus.Verified)
                {
                    verifyText.Foreground = new SolidColorBrush(Color.FromRgb(80, 150, 50));
                    verifyText.FontWeight = FontWeight.SemiBold;
                }
                else
                {
                    verifyText.Foreground = new SolidColorBrush(Color.FromRgb(140, 145, 155));
                }

                // Next button — user decides when to proceed
                var btnP = new StackPanel { Margin = new Thickness(0, 20, 0, 20) };
                var next = UIHelper.CreateDarkNextButton("Next");
                next.Click += (s, e) => ShowFlashPrompt();
                btnP.Children.Add(next);
                mainContent.Children.Add(btnP);
            }
            catch (OperationCanceledException) { isDownloading = false; al.Text = "Cancelled"; AddRetry(); }
            catch (Exception ex) { isDownloading = false; al.Text = "Download failed"; et.Text = ex.Message; AddRetry(); }
        }

        private void AddRetry()
        {
            var p = new StackPanel { Margin = new Thickness(0, 14, 0, 15) };
            var b = UIHelper.CreateDarkNextButton("Try Again");
            b.Click += (s, e) => ShowDistroPage();
            p.Children.Add(b);
            mainContent.Children.Add(p);
        }

        // =============================================================
        //  PAGE 5: FLASH PROMPT
        // =============================================================
        private void ShowFlashPrompt()
        {
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
                FontWeight = FontWeight.SemiBold,
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
        private UIHelper.GlossButton usbNextButton;
        private List<UsbDrive> lastKnownDrives = new List<UsbDrive>();

        private void ShowUsbSelect()
        {
            StopUsbPolling();
            SetupPage();
            mainContent.Children.Add(UIHelper.CreateSectionHeader("Choose USB Drive"));

            usbListPanel = new StackPanel();
            usbEmptyText = new TextBlock
            {
                Text = "Plug in a USB drive…",
                FontSize = 14, FontStyle = FontStyle.Italic,
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
                _ = RunGuarded(DoFlashAsync());
            };
            bp.Children.Add(usbNextButton);
            mainContent.Children.Add(bp);

            // Initial scan
            selectedUsbDrive = null;
            lastKnownDrives.Clear();

            // Start polling every 2 seconds
            usbPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            usbPollTimer.Tick += (s, e) => RefreshUsbList();
            usbPollTimer.Start();
            RefreshUsbList();
        }

        private volatile bool usbRefreshing = false;
        private void RefreshUsbList()
        {
            if (usbRefreshing) return; // Skip if previous refresh still running
            usbRefreshing = true;

            Task.Run(() =>
            {
                try
                {
                    var drives = hardwareScanner.GetUsbDrives();
                    Dispatcher.UIThread.Post(() =>
                    {
                        try
                        {
                            // The user may have moved on while the scan ran; touching state then would null out
                            // selectedUsbDrive right before DoFlashAsync reads it
                            if (usbPollTimer == null || usbListPanel == null) return;

                            // Identity = device path + size + name, so swapping one stick for another is noticed
                            Func<UsbDrive, string> key = d => $"{d.DeviceId}|{d.SizeBytes}|{d.Name}";
                            var currentIds = drives.Select(key).OrderBy(x => x, StringComparer.Ordinal).ToList();
                            var lastIds = lastKnownDrives.Select(key).OrderBy(x => x, StringComparer.Ordinal).ToList();
                            if (currentIds.SequenceEqual(lastIds)) return; // No change

                            lastKnownDrives = drives;

                            // Remember current selection
                            string selectedKey = selectedUsbDrive != null ? key(selectedUsbDrive) : null;
                            selectedUsbDrive = null;

                            usbListPanel.Children.Clear();

                            if (drives.Count == 0)
                            {
                                usbEmptyText.IsVisible = true;
                                usbNextButton.IsEnabled = false;
                                return;
                            }

                            usbEmptyText.IsVisible = false;

                            foreach (var d in drives)
                            {
                                var r = UIHelper.CreateRadioRow($"{d.Name} ({d.SizeGB} GB)");
                                r.GroupName = "Usb";
                                var drive = d;
                                r.IsCheckedChanged += (s, e) =>
                                {
                                    if (r.IsChecked == true) { selectedUsbDrive = drive; usbNextButton.IsEnabled = true; }
                                };

                                if (selectedKey != null && key(d) == selectedKey)
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
            SetupPage();
            isFlashing = true;

            var al = new TextBlock
            {
                Text = "Flashing to USB…", FontSize = 18, FontWeight = FontWeight.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(50, 60, 80)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 20)
            };
            mainContent.Children.Add(al);

            var ps = new StackPanel();
            var pb = UIHelper.CreateProgressBar(); ps.Children.Add(pb);
            var et = UIHelper.CreateStatusText(""); et.Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60)); et.FontWeight = FontWeight.SemiBold; ps.Children.Add(et);
            mainContent.Children.Add(UIHelper.CreateGroupedSection(ps));

            var fp = new Progress<string>(msg =>
            {
                if (msg.StartsWith("Flashing..."))
                {
                    var p = msg.Replace("Flashing... ", "").Replace("%", "").Trim();
                    if (int.TryParse(p, out int v)) { al.Text = "Flashing to USB…"; pb.IsIndeterminate = false; pb.Value = v; }
                }
                else if (msg.StartsWith("Verifying..."))
                {
                    var p = msg.Replace("Verifying... ", "").Replace("%", "").Trim();
                    al.Text = "Verifying USB…";
                    if (int.TryParse(p, out int v)) { pb.IsIndeterminate = false; pb.Value = v; }
                }
                else if (msg.StartsWith("Preparing")) { al.Text = "Preparing…"; pb.IsIndeterminate = true; }
                else if (msg.StartsWith("Finishing")) { al.Text = "Finishing up…"; pb.IsIndeterminate = true; }
                else if (msg.StartsWith("ERROR")) { pb.IsIndeterminate = false; al.Text = "Flash failed"; et.Text = msg.Replace("ERROR: ", ""); }
            });

            var target = selectedUsbDrive;
            bool ok = target != null && await usbFlasher.FlashIsoToUsbAsync(downloadedIsoPath, target, currentHardware?.BootMode == "UEFI", fp);
            if (target == null) { al.Text = "Flash failed"; et.Text = "No USB drive selected"; }

            isFlashing = false;
            if (ok)
            {
                await Task.Delay(300);
                ShowDonePage();
            }
            else
            {
                pb.IsIndeterminate = false;
                al.Text = "Flash failed";
                var p = new StackPanel { Margin = new Thickness(0, 14, 0, 15) };
                var b = UIHelper.CreateDarkNextButton("Try Again");
                b.Click += (s2, e2) => ShowFlashPrompt();
                p.Children.Add(b);
                mainContent.Children.Add(p);
            }
        }

        // =============================================================
        //  PAGE 8: DONE — calm, centered, clean
        // =============================================================
        private void ShowDonePage()
        {
            // Delete the ISO — flashing is done, no need to keep 5GB on disk
            DeleteDownloadedIso();

            rootPanel.Children.Clear();

            var bg = new Grid { Background = UIHelper.CreateDarkGradientBackground() };

            var center = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            center.Children.Add(new TextBlock
            {
                Text = "LinuxSimplify", FontSize = 36, FontWeight = FontWeight.Light,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.5, BlurRadius = 4, OffsetX = 2, OffsetY = 2 }
            });

            center.Children.Add(new TextBlock
            {
                Text = "Ready to boot from USB", FontSize = 16,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 205, 215)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 40)
            });

            var doneBtn = UIHelper.CreateDarkNextButton("DONE");
            doneBtn.Width = 300; doneBtn.Height = 52; doneBtn.FontSize = 22;
            doneBtn.Click += (s, e) => { _ = Quit(this); };
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
                Text = "helping people switch to GNU/Linux",
                FontSize = 11, FontStyle = FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 145, 158)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            credits.Children.Add(new TextBlock
            {
                Text = "© 2025 LinuxSimplify — GNU GPL v3",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(130, 135, 148)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            bg.Children.Add(credits);

            rootPanel.Children.Add(bg);
        }

        /// <summary>Deletes the downloaded ISO after a successful flash. No reason to keep 2-5 GB sitting around.</summary>
        private void DeleteDownloadedIso()
        {
            try
            {
                if (!string.IsNullOrEmpty(downloadedIsoPath) && File.Exists(downloadedIsoPath))
                    File.Delete(downloadedIsoPath);
            }
            catch { }

            CleanOldIsos();
        }

        /// <summary>
        /// Removes ISOs and partial downloads this app created earlier. Unlike the Windows build (which owns an "ISO" folder
        /// next to the exe), this works in the user's Downloads, so it only deletes the exact file names it generates
        /// ("Ubuntu.iso", "Linux_Mint.iso.part", ...) — never "*.iso".
        /// </summary>
        private void CleanOldIsos()
        {
            try
            {
                if (!Directory.Exists(isoFolderPath)) return;
                // all known distro names, not just the compatible ones in distroResults
                foreach (var d in compatibilityAnalyzer.GetAllDistrosWithoutHardware())
                {
                    string baseName = Path.Combine(isoFolderPath, d.Name.Replace(" ", "_") + ".iso");
                    foreach (var f in new[] { baseName, baseName + ".part" })
                    {
                        try { if (File.Exists(f)) File.Delete(f); } catch { }
                    }
                }
            }
            catch { }
        }
    }
}
