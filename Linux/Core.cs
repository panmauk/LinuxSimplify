using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PmxInstaller
{
    // ===================================================================
    //  Models
    // ===================================================================
    public class IsoPart { public string Name = ""; public string Url = ""; }

    public class PmxRelease
    {
        public string Version = "";
        public string DisplayName = "PMX OS";
        public List<IsoPart> Parts = new();
        public string IsoFileName = "PMX-OS-amd64.iso";
        public string ChecksumUrl = "";
        public string ChecksumType = "sha256";
    }

    public class DownloadState
    {
        public string Action = "";
        public long Downloaded;
        public long Total = -1;
        public int Percent;
    }

    public class HardwareInfo
    {
        public string Cpu = "Unknown";
        public int Cores, Threads;
        public double RamGb;
        public List<string> Gpus = new();
        public double StorageGb;
        public string StorageType = "HDD";
        public string Boot = "BIOS";
    }

    public class UsbDrive
    {
        public string Path = "";
        public string Name = "";
        public double SizeGb;
        public override string ToString() => $"{Name}  ({SizeGb} GB)  —  {Path}";
    }

    // ===================================================================
    //  GitHub release resolver  (panmox.org / github.com/panmauk/PMX-OS)
    // ===================================================================
    public static class ReleaseResolver
    {
        public const string SiteUrl = "https://panmox.org";
        private const string Api = "https://api.github.com/repos/panmauk/PMX-OS/releases/latest";

        public static async Task<PmxRelease> ResolveLatestAsync()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LinuxSimplify-PMX/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string json = await http.GetStringAsync(Api);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var rel = new PmxRelease();
            if (root.TryGetProperty("tag_name", out var tag)) rel.Version = tag.GetString() ?? "";
            if (root.TryGetProperty("name", out var nm) && !string.IsNullOrWhiteSpace(nm.GetString()))
                rel.DisplayName = nm.GetString();

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                throw new Exception("The latest release has no downloadable files");

            var all = new List<IsoPart>();
            foreach (var a in assets.EnumerateArray())
            {
                string name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                string url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                    all.Add(new IsoPart { Name = name, Url = url });
            }

            var checksum = all.FirstOrDefault(x => IsChecksum(x.Name));
            if (checksum != null)
            {
                rel.ChecksumUrl = checksum.Url;
                rel.ChecksumType = checksum.Name.ToLowerInvariant().Contains("sha512") ? "sha512" : "sha256";
            }

            var iso = all.Where(x => x.Name.IndexOf(".iso", StringComparison.OrdinalIgnoreCase) >= 0
                                     && !IsChecksum(x.Name)).ToList();
            var whole = iso.Where(x => x.Name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)).ToList();
            var parts = whole.Count == 1 ? whole
                : iso.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

            if (parts.Count == 0) throw new Exception("Couldn't find an ISO in the latest release");

            rel.Parts = parts;
            rel.IsoFileName = DeriveIsoName(parts[0].Name);
            return rel;
        }

        private static bool IsChecksum(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("sha256sums") || n.Contains("sha512sums")
                || n.EndsWith(".sha256") || n.EndsWith(".sha256sum")
                || n.EndsWith(".sha512") || n.EndsWith(".sha512sum")
                || n.EndsWith(".sum") || (n.Contains("checksum") && n.EndsWith(".txt"));
        }

        private static string DeriveIsoName(string part)
        {
            int i = part.IndexOf(".iso", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? part.Substring(0, i + 4) : part;
        }
    }

    // ===================================================================
    //  Download + join + verify
    // ===================================================================
    public class IsoDownloader
    {
        private readonly HttpClient _http;

        public IsoDownloader()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromHours(4) };
            _http.DefaultRequestHeaders.Add("User-Agent", "LinuxSimplify-PMX/1.0");
        }

        public async Task<bool> DownloadPartsAsync(IReadOnlyList<string> urls, string dest,
            IProgress<DownloadState> progress, CancellationToken ct)
        {
            long grandTotal = 0; bool haveSizes = true;
            foreach (var u in urls)
            {
                try
                {
                    using var head = new HttpRequestMessage(HttpMethod.Head, u);
                    using var r = await _http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (r.IsSuccessStatusCode && r.Content.Headers.ContentLength.HasValue)
                        grandTotal += r.Content.Headers.ContentLength.Value;
                    else { haveSizes = false; break; }
                }
                catch (OperationCanceledException) { return false; }
                catch { haveSizes = false; break; }
            }

            var st = new DownloadState();
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            long cumulative = 0;
            using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
            {
                for (int i = 0; i < urls.Count; i++)
                {
                    string label = urls.Count > 1 ? $"Downloading part {i + 1} of {urls.Count}…" : "Downloading…";
                    using var req = new HttpRequestMessage(HttpMethod.Get, urls[i]);
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!resp.IsSuccessStatusCode) return false;
                    long partTotal = resp.Content.Headers.ContentLength ?? -1;

                    using var stream = await resp.Content.ReadAsStreamAsync(ct);
                    var buf = new byte[1 << 20];
                    long partRead = 0; int read;
                    while ((read = await stream.ReadAsync(buf, ct)) > 0)
                    {
                        await fs.WriteAsync(buf.AsMemory(0, read), ct);
                        partRead += read; cumulative += read;
                        st.Action = label; st.Downloaded = cumulative;
                        st.Total = haveSizes ? grandTotal : -1;
                        if (haveSizes && grandTotal > 0) st.Percent = (int)(cumulative * 100 / grandTotal);
                        else if (partTotal > 0) st.Percent = (int)(((i * 100.0) + partRead * 100.0 / partTotal) / urls.Count);
                        progress?.Report(st);
                    }
                    await fs.FlushAsync(ct);
                }
            }
            return new FileInfo(dest).Length > 0;
        }

        public async Task<string> FetchTextAsync(string url)
        {
            try { return await _http.GetStringAsync(url); } catch { return null; }
        }

        public async Task<string> ComputeHashAsync(string path, string algo, IProgress<int> progress)
        {
            return await Task.Run(() =>
            {
                using HashAlgorithm h = algo == "sha512" ? SHA512.Create() : SHA256.Create();
                long total = new FileInfo(path).Length, done = 0;
                using var fs = File.OpenRead(path);
                var buf = new byte[1 << 20]; int read;
                while ((read = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    h.TransformBlock(buf, 0, read, null, 0);
                    done += read;
                    if (total > 0) progress?.Report((int)(done * 100 / total));
                }
                h.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return Convert.ToHexString(h.Hash).ToLowerInvariant();
            });
        }

        public bool Verify(string checksumText, string isoName, string computed)
        {
            if (string.IsNullOrWhiteSpace(checksumText) || string.IsNullOrWhiteSpace(computed)) return false;
            computed = computed.ToLowerInvariant();
            isoName = Path.GetFileName(isoName).ToLowerInvariant();
            var lines = checksumText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !l.TrimStart().StartsWith("#")).ToList();
            foreach (var line in lines)
            {
                var p = line.Trim().Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 2)
                {
                    var fname = p[^1].ToLowerInvariant();
                    if (fname.Contains(isoName) || isoName.Contains(fname))
                        return p[0].ToLowerInvariant() == computed;
                }
            }
            if (lines.Count == 1)
            {
                var p = lines[0].Trim().Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 1) return p[0].ToLowerInvariant() == computed;
            }
            return checksumText.ToLowerInvariant().Contains(computed);
        }
    }

    // ===================================================================
    //  Linux system: hardware, USB drives, flashing
    // ===================================================================
    public static class LinuxSystem
    {
        private static (int code, string output) Run(string file, params string[] args)
        {
            try
            {
                var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                foreach (var a in args) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit(10000);
                return (p.HasExited ? p.ExitCode : -1, outp);
            }
            catch { return (-1, ""); }
        }

        public static HardwareInfo ScanHardware()
        {
            var hw = new HardwareInfo();

            // CPU
            var (lc, lscpu) = Run("lscpu", "-J");
            if (lc == 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(lscpu);
                    var map = new Dictionary<string, string>();
                    foreach (var e in doc.RootElement.GetProperty("lscpu").EnumerateArray())
                        map[e.GetProperty("field").GetString().TrimEnd(':')] = e.GetProperty("data").GetString();
                    if (map.TryGetValue("Model name", out var mn)) hw.Cpu = mn;
                    if (map.TryGetValue("CPU(s)", out var cs) && int.TryParse(cs, out var t)) hw.Threads = t;
                    int perCore = map.TryGetValue("Thread(s) per core", out var tpc) && int.TryParse(tpc, out var pc) ? pc : 1;
                    hw.Cores = perCore > 0 ? hw.Threads / perCore : hw.Threads;
                }
                catch { }
            }

            // RAM
            try
            {
                var m = Regex.Match(File.ReadAllText("/proc/meminfo"), @"MemTotal:\s+(\d+)\s+kB");
                if (m.Success) hw.RamGb = Math.Round(long.Parse(m.Groups[1].Value) / (1024.0 * 1024.0), 1);
            }
            catch { }

            // GPU via lspci -mm (slot, class, vendor, device, ...)
            var (gc, lspci) = Run("lspci", "-mm");
            if (gc == 0)
            {
                foreach (var line in lspci.Split('\n'))
                {
                    var f = SplitQuoted(line);
                    if (f.Count < 4) continue;
                    if (!Regex.IsMatch(f[1], "VGA|3D|Display", RegexOptions.IgnoreCase)) continue;
                    string dev = f[3];
                    hw.Gpus.Add(string.IsNullOrWhiteSpace(dev) ? f[2] : dev);
                }
            }

            // Storage via lsblk
            var (sc, lsblk) = Run("lsblk", "-b", "-d", "-J", "-o", "NAME,SIZE,ROTA,TYPE");
            if (sc == 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(lsblk);
                    long total = 0; bool nvme = false, ssd = false;
                    foreach (var d in doc.RootElement.GetProperty("blockdevices").EnumerateArray())
                    {
                        if (d.GetProperty("type").GetString() != "disk") continue;
                        total += d.GetProperty("size").GetInt64();
                        string name = d.GetProperty("name").GetString() ?? "";
                        if (name.StartsWith("nvme")) nvme = true;
                        else if (d.TryGetProperty("rota", out var r) && r.ValueKind == JsonValueKind.False) ssd = true;
                    }
                    hw.StorageGb = Math.Round(total / (1024.0 * 1024.0 * 1024.0), 0);
                    hw.StorageType = nvme ? "NVMe" : ssd ? "SSD" : "HDD";
                }
                catch { }
            }

            hw.Boot = Directory.Exists("/sys/firmware/efi") ? "UEFI" : "BIOS";
            return hw;
        }

        public static List<UsbDrive> ListUsb()
        {
            var list = new List<UsbDrive>();
            var (c, outp) = Run("lsblk", "-b", "-d", "-J", "-o", "NAME,SIZE,TYPE,TRAN,RM,MODEL,PATH");
            if (c != 0) return list;
            try
            {
                using var doc = JsonDocument.Parse(outp);
                foreach (var d in doc.RootElement.GetProperty("blockdevices").EnumerateArray())
                {
                    if (d.GetProperty("type").GetString() != "disk") continue;
                    bool usb = d.TryGetProperty("tran", out var tr) && tr.GetString() == "usb";
                    bool rm = d.TryGetProperty("rm", out var r) &&
                              (r.ValueKind == JsonValueKind.True ||
                               (r.ValueKind == JsonValueKind.String && r.GetString() == "1"));
                    if (!usb && !rm) continue;
                    string path = d.TryGetProperty("path", out var pp) ? pp.GetString() : $"/dev/{d.GetProperty("name").GetString()}";
                    string model = d.TryGetProperty("model", out var mm) && mm.ValueKind == JsonValueKind.String ? mm.GetString() : null;
                    list.Add(new UsbDrive
                    {
                        Path = path,
                        Name = string.IsNullOrWhiteSpace(model) ? "USB drive" : model.Trim(),
                        SizeGb = Math.Round(d.GetProperty("size").GetInt64() / (1024.0 * 1024.0 * 1024.0), 1)
                    });
                }
            }
            catch { }
            return list;
        }

        public static async Task<(bool ok, string error)> FlashAsync(string isoPath, string device,
            IProgress<int> progress, CancellationToken ct)
        {
            if (!File.Exists(isoPath)) return (false, "ISO file not found");
            long isoSize = new FileInfo(isoPath).Length;
            if (isoSize < 1 << 20) return (false, "ISO file looks too small");

            string script =
                "set -e; " +
                $"for p in $(lsblk -ln -o PATH '{device}' | tail -n +2); do umount \"$p\" 2>/dev/null || true; done; " +
                $"dd if='{isoPath}' of='{device}' bs=4M conv=fsync status=progress; " +
                "sync; " +
                $"partprobe '{device}' 2>/dev/null || true";

            bool root = Environment.UserName == "root" || (Run("id", "-u").output.Trim() == "0");
            string file = root ? "sh" : "pkexec";
            var psi = new ProcessStartInfo(file) { RedirectStandardError = true, UseShellExecute = false };
            if (root) { psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(script); }
            else { psi.ArgumentList.Add("sh"); psi.ArgumentList.Add("-c"); psi.ArgumentList.Add(script); }

            Process proc;
            try { proc = Process.Start(psi); }
            catch (Exception e) { return (false, e.Message); }

            string lastErr = "";
            var sb = new StringBuilder();
            var buf = new char[1];
            while (true)
            {
                if (ct.IsCancellationRequested) { try { proc.Kill(true); } catch { } return (false, "Cancelled"); }
                int n = await proc.StandardError.ReadAsync(buf, 0, 1);
                if (n == 0) break;
                char ch = buf[0];
                if (ch == '\r' || ch == '\n')
                {
                    string line = sb.ToString().Trim(); sb.Clear();
                    if (line.Length > 0)
                    {
                        lastErr = line;
                        var m = Regex.Match(line, @"^(\d+)\s+bytes");
                        if (m.Success) progress?.Report((int)Math.Min(100, long.Parse(m.Groups[1].Value) * 100 / isoSize));
                    }
                }
                else sb.Append(ch);
            }
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0)
            {
                if (proc.ExitCode is 126 or 127) return (false, "Authorization was cancelled");
                return (false, lastErr.Length > 0 ? lastErr : $"dd exited with code {proc.ExitCode}");
            }
            progress?.Report(100);
            return (true, null);
        }

        // Minimal quoted-field splitter for `lspci -mm` lines.
        private static List<string> SplitQuoted(string line)
        {
            var res = new List<string>();
            var sb = new StringBuilder(); bool q = false;
            foreach (char c in line)
            {
                if (c == '"') q = !q;
                else if (c == ' ' && !q) { if (sb.Length > 0) { res.Add(sb.ToString()); sb.Clear(); } }
                else sb.Append(c);
            }
            if (sb.Length > 0) res.Add(sb.ToString());
            return res;
        }
    }
}
