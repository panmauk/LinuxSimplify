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
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using LinuxSimplify.Models;
using LinuxSimplify.Services;
using LinuxSimplify.UI;

namespace LinuxSimplify
{
    public partial class MainWindow : Window
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
        private bool scanComplete = false;
        private bool isDownloading = false;
        private bool isFlashing = false;
        private DispatcherTimer usbPollTimer;

        public MainWindow()
        {
            hardwareScanner = new HardwareScanner();
            compatibilityAnalyzer = new CompatibilityAnalyzer();
            isoDownloader = new IsoDownloader();
            usbFlasher = new UsbFlasher();
            urlResolver = new DistroUrlResolver();
            distroResults = new List<DistroCompatibility>();
            currentDownloadState = new DownloadState();

            string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            isoFolderPath = Path.Combine(exeDir, "ISO");

            InitializeComponent();
            Closing += OnWindowClosing;
            _ = ScanInBackground();
            ShowLockScreen();
        }

        private void InitializeComponent()
        {
            Title = "LinuxSimplify";
            Width = 550; Height = 680;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = UIHelper.CreateLinenBackground();
            rootPanel = new DockPanel();
            Content = rootPanel;
        }

        private async Task ScanInBackground()
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
                foreach (var d in distroResults)
                {
                    if (resolved.TryGetValue(d.Name, out var r) && r.Resolved)
                    {
                        d.DownloadUrl = r.DownloadUrl;
                        if (!string.IsNullOrEmpty(r.Sha256Url))
                            d.Sha256Url = r.Sha256Url;
                        if (!string.IsNullOrEmpty(r.ChecksumType))
                            d.ChecksumType = r.ChecksumType;
                        if (!string.IsNullOrEmpty(r.NvidiaDownloadUrl))
                        {
                            bool hasNv = currentHardware.Gpus.Any(g => g.Vendor == "NVIDIA");
                            if (hasNv) d.DownloadUrl = r.NvidiaDownloadUrl;
                        }
                    }
                }
            }
            catch { /* URL resolution failed, use hardcoded fallbacks */ }

            selectedDistro = distroResults.FirstOrDefault(d => d.IsRecommended)
                          ?? distroResults.FirstOrDefault();
            if (selectedDistro != null) selectedDistro.IsSelected = true;
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

        private async Task<bool> CheckInternetAsync()
        {
            try
            {
                using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                {
                    var resp = await http.GetAsync("https://www.google.com/generate_204");
                    return resp.IsSuccessStatusCode;
                }
            }
            catch { }
            try
            {
                using (var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) })
                {
                    var resp = await http.GetAsync("https://captive.apple.com");
                    return resp.IsSuccessStatusCode;
                }
            }
            catch { return false; }
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
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
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
                Text = "LinuxSimplify", FontSize = 42, FontWeight = FontWeights.Light,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8),
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.6, BlurRadius = 4, ShadowDepth = 2 }
            });
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
        private void ShowDistroPage()
        {
            SetupPage();

            if (selectedDistro != null && selectedDistro.IsRecommended)
            {
                mainContent.Children.Add(new TextBlock
                {
                    Text = $"\u2605  Best pick: {selectedDistro.Name}",
                    FontSize = 13, FontWeight = FontWeights.SemiBold,
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

            var notes = new TextBlock
            {
                Text = selectedDistro?.Notes ?? "", FontSize = 12, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 110, 130)),
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20, 6, 20, 4), Tag = "DistroNotes"
            };
            mainContent.Children.Add(notes);

            var btnP = new StackPanel { Margin = new Thickness(0, 14, 0, 20) };
            var dl = UIHelper.CreateGreenActionButton("Download ISO");
            dl.Click += (s, e) =>
            {
                if (selectedDistro == null) return;
                _ = DoDownloadAsync();
            };
            btnP.Children.Add(dl);
            mainContent.Children.Add(btnP);
        }

        private void UpdateNotes()
        {
            foreach (var c in mainContent.Children)
                if (c is TextBlock tb && tb.Tag as string == "DistroNotes")
                { tb.Text = selectedDistro?.Notes ?? ""; break; }
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

            try { Directory.CreateDirectory(isoFolderPath); } catch { }

            // Clean old ISOs — move any existing .iso files to recycle bin
            CleanOldIsos();

            string fn = $"{selectedDistro.Name.Replace(" ", "_")}.iso";
            downloadedIsoPath = Path.Combine(isoFolderPath, fn);

            var al = new TextBlock
            {
                Text = "Downloading\u2026", FontSize = 18, FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(50, 60, 80)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 20)
            };
            mainContent.Children.Add(al);

            var ps = new StackPanel();
            var pb = UIHelper.CreateProgressBar(); ps.Children.Add(pb);
            var et = UIHelper.CreateStatusText(""); et.Foreground = new SolidColorBrush(Color.FromRgb(200, 60, 60)); et.FontWeight = FontWeights.SemiBold; ps.Children.Add(et);
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
                var prog = new Progress<DownloadState>(s => Dispatcher.Invoke(() =>
                {
                    currentDownloadState = s; pb.Value = s.Percentage;
                    if (!string.IsNullOrEmpty(s.ErrorMessage)) et.Text = s.ErrorMessage;
                }));

                bool ok = await isoDownloader.DownloadIsoAsync(selectedDistro.DownloadUrl, downloadedIsoPath, prog, downloadCts.Token);
                if (!ok) { Dispatcher.Invoke(() => { al.Text = "Download failed"; }); isDownloading = false; AddRetry(); return; }

                Dispatcher.Invoke(() => { al.Text = "Verifying\u2026"; pb.IsIndeterminate = true; });

                bool verified = false;
                bool checksumAvailable = false;

                if (!string.IsNullOrEmpty(selectedDistro.Sha256Url))
                {
                    checksumAvailable = true;
                    var cf = await isoDownloader.DownloadChecksumFileAsync(selectedDistro.Sha256Url);
                    var sp = new Progress<string>(_ => { });
                    string algo = selectedDistro.ChecksumType ?? "sha256";
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
                            verifyText.Text = "\u2713 Download is safe";
                            verifyText.Foreground = new SolidColorBrush(Color.FromRgb(80, 150, 50));
                            verifyText.FontWeight = FontWeights.SemiBold;
                        }
                        else
                        {
                            verifyText.Text = "\u26A0 Download might not be safe";
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
            catch (OperationCanceledException) { isDownloading = false; Dispatcher.Invoke(() => al.Text = "Cancelled"); AddRetry(); }
            catch (Exception ex) { isDownloading = false; Dispatcher.Invoke(() => { al.Text = "Download failed"; et.Text = ex.Message; }); AddRetry(); }
        }

        private void AddRetry()
        {
            Dispatcher.Invoke(() =>
            {
                var p = new StackPanel { Margin = new Thickness(0, 14, 0, 15) };
                var b = UIHelper.CreateDarkNextButton("Try Again");
                b.Click += (s, e) => ShowDistroPage();
                p.Children.Add(b);
                mainContent.Children.Add(p);
            });
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
                Text = "\u26A0 This will erase everything on the USB drive",
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
            StopUsbPolling();
            SetupPage();
            mainContent.Children.Add(UIHelper.CreateSectionHeader("Choose USB Drive"));

            usbListPanel = new StackPanel();
            usbEmptyText = new TextBlock
            {
                Text = "Plug in a USB drive\u2026",
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
            SetupPage();
            isFlashing = true;

            var al = new TextBlock
            {
                Text = "Flashing to USB\u2026", FontSize = 18, FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromRgb(50, 60, 80)),
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
                Text = "LinuxSimplify", FontSize = 36, FontWeight = FontWeights.Light,
                Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20),
                Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.5, BlurRadius = 4, ShadowDepth = 2 }
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
                Text = "helping people switch to GNU/Linux",
                FontSize = 11, FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 145, 158)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            credits.Children.Add(new TextBlock
            {
                Text = "\u00A9 2025 LinuxSimplify \u2014 GNU GPL v3",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(130, 135, 148)),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            bg.Children.Add(credits);

            rootPanel.Children.Add(bg);
        }

        private string Fmt(long b)
        {
            if (b < 1024) return $"{b} B";
            if (b < 1048576) return $"{b / 1024.0:F1} KB";
            if (b < 1073741824) return $"{b / 1048576.0:F1} MB";
            return $"{b / 1073741824.0:F2} GB";
        }

        /// <summary>
        /// Deletes the downloaded ISO after successful flash.
        /// No reason to keep 2-5 GB sitting around.
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
