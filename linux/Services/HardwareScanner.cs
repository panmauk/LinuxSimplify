using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LinuxSimplify.Models;

namespace LinuxSimplify.Services
{
    /// <summary>
    /// Linux implementation of the hardware scanner (same shape as the Windows/WMI one):
    /// /proc/cpuinfo, /proc/meminfo, /sys/class/drm + lspci, lsblk, /sys/firmware/efi, uname.
    /// Nothing here needs root.
    /// </summary>
    public class HardwareScanner
    {
        public async Task<HardwareInfo> ScanHardwareAsync()
        {
            return await Task.Run(() => ScanHardware());
        }

        public HardwareInfo ScanHardware()
        {
            var info = new HardwareInfo();

            // Each step is isolated so one odd /proc or /sys layout can't blank the whole scan
            Step(() => ScanCpu(info));
            Step(() => ScanRam(info));
            Step(() => ScanGpu(info));
            Step(() => ScanStorage(info));
            Step(() => DetectBootMode(info));
            Step(() => info.Architecture = DetectArchitecture());
            Step(() => HardwareClassifier.ClassifySystem(info));
            return info;
        }

        private static void Step(Action a)
        {
            try { a(); } catch { }
        }

        private static string ReadFile(string path)
        {
            try { return File.ReadAllText(path); } catch { return null; }
        }

        // =============================================================
        //  ARCHITECTURE
        // =============================================================
        private static string DetectArchitecture()
        {
            // uname -m is the kernel's machine type; RuntimeInformation covers it when uname is missing
            var (rc, output) = Sys.Run("uname", new[] { "-m" }, 3000);
            string m = rc == 0 ? output.Trim().ToLowerInvariant() : "";
            switch (m)
            {
                case "x86_64": case "amd64": return "x86_64";
                case "aarch64": case "arm64": case "armv8l": return "aarch64";
                case "i386": case "i486": case "i586": case "i686": return "x86";
                case "": break;
                default: return m.StartsWith("arm") ? "arm" : m;
            }
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64: return "x86_64";
                case Architecture.X86: return "x86";
                case Architecture.Arm64: return "aarch64";
                case Architecture.Arm: return "arm";
                default: return "";
            }
        }

        // =============================================================
        //  CPU — brand, model, cores, threads, base clock
        // =============================================================
        private void ScanCpu(HardwareInfo info)
        {
            ParseCpuInfo(ReadFile("/proc/cpuinfo") ?? "", info);

            if (string.IsNullOrEmpty(info.CpuModel) || info.CpuModel == "Unknown")
            {
                // aarch64 /proc/cpuinfo has no model name; lscpu knows it
                var (rc, output) = Sys.Run("lscpu", Array.Empty<string>(), 4000);
                if (rc == 0)
                {
                    string model = LscpuField(output, "Model name");
                    string vendor = LscpuField(output, "Vendor ID");
                    if (!string.IsNullOrEmpty(model)) info.CpuModel = model;
                    if (string.IsNullOrEmpty(info.CpuBrand) && !string.IsNullOrEmpty(vendor)) info.CpuBrand = vendor;
                }
            }
            if (string.IsNullOrEmpty(info.CpuModel))
            {
                string board = (ReadFile("/sys/firmware/devicetree/base/model") ?? "").Trim('\0', ' ', '\n');
                info.CpuModel = board.Length > 0 ? board : "Unknown";
            }
            if (info.CpuThreads <= 0) info.CpuThreads = Math.Max(1, Environment.ProcessorCount);
            if (info.CpuCores <= 0) info.CpuCores = info.CpuThreads;

            // Base clock: cpuinfo's "cpu MHz" is the *current* frequency; the cpufreq max is closer to what Windows reports
            string max = ReadFile("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq");
            if (max != null && long.TryParse(max.Trim(), out long khz) && khz > 0)
                info.CpuBaseClockMHz = (int)(khz / 1000);
        }

        private static string LscpuField(string text, string name)
        {
            var m = Regex.Match(text, @"^" + Regex.Escape(name) + @":\s*(.+)$", RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        /// <summary>Parses the text of /proc/cpuinfo into <paramref name="info"/>. Public/static so it can be unit-tested.</summary>
        public static void ParseCpuInfo(string text, HardwareInfo info)
        {
            var blocks = Regex.Split(text.Replace("\r", ""), @"\n\s*\n").Where(b => b.Contains(":")).ToList();
            var cores = new HashSet<string>();
            var coresPerPackage = new Dictionary<string, int>();
            int logical = 0;
            double maxMhz = 0;
            string vendorId = "", model = "";

            foreach (var block in blocks)
            {
                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in block.Split('\n'))
                {
                    int i = line.IndexOf(':');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim();
                    if (!kv.ContainsKey(k)) kv[k] = line.Substring(i + 1).Trim();
                }
                if (!kv.ContainsKey("processor")) continue;
                logical++;

                if (vendorId.Length == 0 && kv.TryGetValue("vendor_id", out var v)) vendorId = v;
                if (model.Length == 0 && kv.TryGetValue("model name", out var mn)) model = mn;
                if (model.Length == 0 && kv.TryGetValue("Hardware", out var hw)) model = hw;
                if (kv.TryGetValue("cpu MHz", out var mhz) && double.TryParse(mhz, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double f))
                    maxMhz = Math.Max(maxMhz, f);

                kv.TryGetValue("physical id", out string pkg);
                kv.TryGetValue("core id", out string core);
                if (pkg != null && core != null) cores.Add(pkg + "/" + core);
                if (pkg != null && kv.TryGetValue("cpu cores", out var cc) && int.TryParse(cc, out int n)) coresPerPackage[pkg] = n;
            }

            info.CpuThreads = logical;
            if (cores.Count > 0) info.CpuCores = cores.Count;
            else if (coresPerPackage.Count > 0) info.CpuCores = coresPerPackage.Values.Sum();
            else info.CpuCores = logical; // no SMT info (ARM, some VMs): treat every logical CPU as a core

            string combined = (vendorId + " " + model).ToUpperInvariant();
            if (vendorId == "GenuineIntel" || combined.Contains("INTEL")) info.CpuBrand = "Intel";
            else if (vendorId == "AuthenticAMD" || combined.Contains("AMD")) info.CpuBrand = "AMD";
            else info.CpuBrand = vendorId;

            info.CpuModel = model.Length > 0 ? Regex.Replace(model, @"\s+", " ").Trim() : "";
            info.CpuBaseClockMHz = (int)maxMhz;
        }

        // =============================================================
        //  RAM
        // =============================================================
        private void ScanRam(HardwareInfo info)
        {
            string text = ReadFile("/proc/meminfo") ?? "";
            var m = Regex.Match(text, @"^MemTotal:\s+(\d+)\s*kB", RegexOptions.Multiline);
            if (!m.Success) return;
            info.RamGB = EstimateInstalledRamGB(long.Parse(m.Groups[1].Value));
        }

        private static readonly double[] CommonRamSizes =
            { 0.5, 1, 1.5, 2, 3, 4, 6, 8, 10, 12, 16, 20, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024 };

        /// <summary>
        /// MemTotal is what the kernel can use — always below what is installed (firmware, integrated-GPU carve-out,
        /// kernel structures take several %). The Windows scanner reports installed capacity, and the analyzer's
        /// thresholds (e.g. "4 GB minimum") are written against that, so round up to the next common module size;
        /// otherwise a 4 GB laptop (3.7 GB usable) would be rejected by every distro that asks for 4 GB.
        /// </summary>
        public static double EstimateInstalledRamGB(long memTotalKb)
        {
            double usable = memTotalKb / 1048576.0;
            foreach (var size in CommonRamSizes)
                if (size >= usable) return size;
            return Math.Round(usable, 0);
        }

        // =============================================================
        //  GPU — model, vendor, VRAM, discrete vs integrated, tier
        // =============================================================
        private sealed class PciGpu
        {
            public string Slot = "";      // 0000:01:00.0
            public string VendorId = "";  // 0x10de
            public string Name = "";
            public string Driver = "";
            public double VramGB;
        }

        private void ScanGpu(HardwareInfo info)
        {
            var found = new List<PciGpu>();

            // 1) /sys/class/drm/cardN -> PCI device (skips virtual/non-PCI GPUs, connectors "card0-HDMI-A-1" and render nodes)
            try
            {
                foreach (var card in Directory.GetDirectories("/sys/class/drm", "card*").OrderBy(d => d, StringComparer.Ordinal))
                {
                    if (!Regex.IsMatch(Path.GetFileName(card), @"^card\d+$")) continue;
                    string dev = Path.Combine(card, "device");
                    string vendor = (ReadFile(Path.Combine(dev, "vendor")) ?? "").Trim();
                    if (vendor.Length == 0) continue;

                    string slot = "";
                    try { slot = Path.GetFileName(new DirectoryInfo(dev).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(dev)); } catch { }
                    var g = new PciGpu { Slot = slot, VendorId = vendor.ToLowerInvariant() };

                    // driver name from uevent: DRIVER=amdgpu
                    var um = Regex.Match(ReadFile(Path.Combine(dev, "uevent")) ?? "", @"^DRIVER=(\S+)", RegexOptions.Multiline);
                    if (um.Success) g.Driver = um.Groups[1].Value;

                    // amdgpu (and xe/i915 dGPUs on newer kernels) expose the VRAM size directly
                    string vram = ReadFile(Path.Combine(dev, "mem_info_vram_total"));
                    if (vram != null && double.TryParse(vram.Trim(), out double bytes) && bytes > 0)
                        g.VramGB = bytes / (1024.0 * 1024.0 * 1024.0);

                    found.Add(g);
                }
            }
            catch { }

            // 2) lspci for human-readable names (and as the only source on systems without /sys/class/drm entries)
            var lspci = ReadLspciDisplayDevices();
            foreach (var g in found)
            {
                if (lspci.TryGetValue(NormSlot(g.Slot), out string n)) g.Name = n;
            }
            if (found.Count == 0)
            {
                foreach (var kv in lspci)
                    found.Add(new PciGpu { Slot = kv.Key, Name = kv.Value });
            }

            // 3) NVIDIA proprietary driver: exact model names without lspci, and VRAM via nvidia-smi
            var nvidiaNames = ReadNvidiaProcInfo();
            var nvidiaVram = ReadNvidiaSmiMemory();
            int nvIndex = 0;

            foreach (var g in found)
            {
                bool isNvidia = g.VendorId == "0x10de" || g.Name.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase);
                if (isNvidia)
                {
                    if (nvidiaNames.TryGetValue(NormSlot(g.Slot), out string exact)) g.Name = exact;
                    if (g.VramGB <= 0 && nvIndex < nvidiaVram.Count) g.VramGB = nvidiaVram[nvIndex] / 1024.0;
                    nvIndex++;
                }

                if (string.IsNullOrWhiteSpace(g.Name)) g.Name = GenericGpuName(g);

                info.Gpus.Add(HardwareClassifier.BuildGpu(g.Name, g.VramGB));
            }
        }

        /// <summary>PCI slot as "0000:01:00.0" (lspci omits the domain when it is 0000), lower-case, for dictionary lookups.</summary>
        private static string NormSlot(string slot)
        {
            slot = (slot ?? "").Trim().ToLowerInvariant();
            return slot.Count(c => c == ':') == 1 ? "0000:" + slot : slot;
        }

        private static string GenericGpuName(PciGpu g)
        {
            switch (g.VendorId)
            {
                case "0x10de": return "NVIDIA Graphics" + (g.Driver.Length > 0 ? $" ({g.Driver})" : "");
                case "0x1002": return "AMD Radeon Graphics" + (g.Driver.Length > 0 ? $" ({g.Driver})" : "");
                case "0x8086": return "Intel Graphics" + (g.Driver.Length > 0 ? $" ({g.Driver})" : "");
                default: return "Unknown Graphics" + (g.Driver.Length > 0 ? $" ({g.Driver})" : "");
            }
        }

        /// <summary>slot ("01:00.0") -> "NVIDIA GeForce RTX 4070", for PCI class 03xx display controllers.</summary>
        private static Dictionary<string, string> ReadLspciDisplayDevices()
        {
            var result = new Dictionary<string, string>();
            string lspci = Sys.FindInPath("lspci");
            if (lspci == null) return result;
            var (rc, output) = Sys.Run(lspci, new[] { "-mm" }, 5000);
            if (rc != 0) return result;
            foreach (var line in output.Split('\n'))
            {
                var parsed = ParseLspciLine(line);
                if (parsed != null) result[NormSlot(parsed.Value.slot)] = parsed.Value.name;
            }
            return result;
        }

        /// <summary>
        /// Parses one `lspci -mm` line, e.g.
        /// 01:00.0 "VGA compatible controller" "NVIDIA Corporation" "AD104 [GeForce RTX 4070]" -ra1 "ASUSTeK" "Device 8913"
        /// Returns null for non-display devices.
        /// </summary>
        public static (string slot, string name)? ParseLspciLine(string line)
        {
            var m = Regex.Match(line ?? "", @"^(\S+)\s+""([^""]*)""\s+""([^""]*)""\s+""([^""]*)""");
            if (!m.Success) return null;
            string cls = m.Groups[2].Value;
            if (!(cls.StartsWith("VGA compatible", StringComparison.OrdinalIgnoreCase) ||
                  cls.StartsWith("3D controller", StringComparison.OrdinalIgnoreCase) ||
                  cls.StartsWith("Display controller", StringComparison.OrdinalIgnoreCase))) return null;

            string vendor = m.Groups[3].Value;
            string device = m.Groups[4].Value;

            string v = vendor;
            if (vendor.StartsWith("NVIDIA", StringComparison.OrdinalIgnoreCase)) v = "NVIDIA";
            else if (vendor.Contains("AMD") || vendor.Contains("ATI")) v = "AMD";
            else if (vendor.StartsWith("Intel", StringComparison.OrdinalIgnoreCase)) v = "Intel";

            // "AD104 [GeForce RTX 4070]" -> "GeForce RTX 4070"; "Navi 32 [Radeon RX 7700 XT / 7800 XT]" -> "Radeon RX 7700 XT / 7800 XT"
            var br = Regex.Match(device, @"\[([^\]]+)\]");
            string model = br.Success ? br.Groups[1].Value : device;
            return (m.Groups[1].Value, $"{v} {model}".Trim());
        }

        /// <summary>/proc/driver/nvidia/gpus/*/information: "Model: NVIDIA GeForce RTX 4070", "Bus Location: 0000:01:00.0".</summary>
        private static Dictionary<string, string> ReadNvidiaProcInfo()
        {
            var result = new Dictionary<string, string>();
            try
            {
                foreach (var dir in Directory.GetDirectories("/proc/driver/nvidia/gpus"))
                {
                    string text = ReadFile(Path.Combine(dir, "information")) ?? "";
                    var model = Regex.Match(text, @"^Model:\s*(.+)$", RegexOptions.Multiline);
                    if (model.Success) result[NormSlot(Path.GetFileName(dir))] = model.Groups[1].Value.Trim();
                }
            }
            catch { }
            return result;
        }

        /// <summary>Total memory in MiB per GPU from nvidia-smi (if the proprietary driver is installed).</summary>
        private static List<double> ReadNvidiaSmiMemory()
        {
            var list = new List<double>();
            string smi = Sys.FindInPath("nvidia-smi");
            if (smi == null) return list;
            var (rc, output) = Sys.Run(smi, new[] { "--query-gpu=memory.total", "--format=csv,noheader,nounits" }, 4000);
            if (rc != 0) return list;
            foreach (var line in output.Split('\n'))
                if (double.TryParse(line.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double mib))
                    list.Add(mib);
            return list;
        }

        // =============================================================
        //  STORAGE
        // =============================================================
        private void ScanStorage(HardwareInfo info)
        {
            foreach (var d in Lsblk.ReadTree())
            {
                if (d.Type != "disk") continue;                         // loop, rom, part, md, ...
                if (d.Name.StartsWith("zram") || d.Name.StartsWith("nbd") || d.Name.StartsWith("ram")) continue; // compressed RAM / network block devices
                if (d.Tran == "usb" || d.Removable) continue;           // plugged-in sticks aren't part of this PC's storage
                if (d.SizeBytes <= 0) continue;

                string type = "HDD";
                if (d.Name.StartsWith("nvme") || d.Tran == "nvme") type = "NVMe";
                else if (!d.Rotational) type = "SSD";

                info.StorageDevices.Add(new StorageInfo
                {
                    Type = type,
                    SizeGB = Math.Round(d.SizeBytes / (1024.0 * 1024.0 * 1024.0), 2),
                    Model = d.Model
                });
            }
        }

        // =============================================================
        //  BOOT MODE
        // =============================================================
        private void DetectBootMode(HardwareInfo info)
        {
            if (Directory.Exists("/sys/firmware/efi"))
            {
                info.BootMode = "UEFI";
                info.SecureBootStatus = ReadSecureBoot();
            }
            else
            {
                info.BootMode = "Legacy BIOS";
                info.SecureBootStatus = "Not Available";
            }
        }

        private static string ReadSecureBoot()
        {
            try
            {
                // efivars file = 4 attribute bytes + 1 data byte (1 = enabled)
                foreach (var f in Directory.GetFiles("/sys/firmware/efi/efivars", "SecureBoot-*"))
                {
                    var data = File.ReadAllBytes(f);
                    if (data.Length >= 5) return data[4] == 1 ? "Enabled" : "Disabled";
                }
            }
            catch { }
            return "Unknown";
        }

        // =============================================================
        //  USB DRIVES
        // =============================================================
        public List<UsbDrive> GetUsbDrives()
        {
            var drives = new List<UsbDrive>();
            try
            {
                foreach (var d in Lsblk.ReadTree())
                {
                    if (d.Type != "disk" || d.SizeBytes <= 0) continue;   // empty card-reader slots report size 0
                    if (d.Tran != "usb" && !d.Removable) continue;
                    if (d.HostsSystem()) continue;                         // hard exclusion: never offer the disk the system runs from

                    string name = $"{d.Vendor} {d.Model}".Trim();
                    name = Regex.Replace(name, @"\s+", " ");
                    if (name.Length == 0) name = "USB Drive";

                    drives.Add(new UsbDrive
                    {
                        DeviceId = d.Path,
                        Name = name,
                        SizeBytes = d.SizeBytes,
                        SizeGB = Math.Round(d.SizeBytes / (1024.0 * 1024.0 * 1024.0), 2),
                        DiskNumber = 0 // Windows-only concept; DeviceId (/dev/sdX) identifies the drive here
                    });
                }
            }
            catch { }
            return drives.OrderBy(d => d.DeviceId, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>One block device from `lsblk -J`, with its children.</summary>
    public sealed class LsblkDevice
    {
        public string Name = "", Path = "", Type = "", Model = "", Vendor = "", Tran = "";
        public long SizeBytes;
        public bool Removable, Hotplug, Rotational;
        public List<string> MountPoints = new List<string>();
        public List<LsblkDevice> Children = new List<LsblkDevice>();

        public IEnumerable<LsblkDevice> Descendants()
        {
            foreach (var c in Children)
            {
                yield return c;
                foreach (var cc in c.Descendants()) yield return cc;
            }
        }

        /// <summary>
        /// True if this disk, or anything stacked on it (partitions, LVM, LUKS), is mounted somewhere that belongs to the
        /// running system. The only mount points considered harmless are the usual removable-media places, because those
        /// are what a desktop auto-mount of a USB stick looks like; they get unmounted before writing.
        /// Swap counts as "in use".
        /// </summary>
        public bool HostsSystem()
        {
            foreach (var mp in MountPoints.Concat(Descendants().SelectMany(d => d.MountPoints)))
            {
                if (string.IsNullOrEmpty(mp)) continue;
                if (IsRemovableMediaMount(mp)) continue;
                return true;
            }
            return false;
        }

        public static bool IsRemovableMediaMount(string mp)
        {
            return mp.StartsWith("/media/") || mp.StartsWith("/run/media/") || mp.StartsWith("/mnt/");
        }
    }

    public static class Lsblk
    {
        private const string Columns = "NAME,PATH,SIZE,MODEL,VENDOR,TRAN,RM,HOTPLUG,TYPE,ROTA,MOUNTPOINTS";

        /// <summary>Runs lsblk (bytes, JSON) and returns the top-level devices. Empty list if lsblk is unavailable.</summary>
        public static List<LsblkDevice> ReadTree()
        {
            string lsblk = Sys.FindInPath("lsblk");
            if (lsblk == null) return new List<LsblkDevice>();

            var (rc, json) = Sys.Run(lsblk, new[] { "-J", "-b", "-o", Columns }, 8000);
            if (rc != 0)
            {
                // lsblk < 2.37 has MOUNTPOINT (singular) instead of MOUNTPOINTS
                (rc, json) = Sys.Run(lsblk, new[] { "-J", "-b", "-o", Columns.Replace("MOUNTPOINTS", "MOUNTPOINT") }, 8000);
                if (rc != 0) return new List<LsblkDevice>();
            }
            return Parse(json);
        }

        public static List<LsblkDevice> Parse(string json)
        {
            var list = new List<LsblkDevice>();
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("blockdevices", out var arr) && arr.ValueKind == JsonValueKind.Array)
                        foreach (var el in arr.EnumerateArray()) list.Add(ParseDevice(el));
                }
            }
            catch { }
            return list;
        }

        private static LsblkDevice ParseDevice(JsonElement el)
        {
            var d = new LsblkDevice
            {
                Name = Str(el, "name"),
                Path = Str(el, "path"),
                Type = Str(el, "type"),
                Model = Str(el, "model").Trim(),
                Vendor = Str(el, "vendor").Trim(),
                Tran = Str(el, "tran").ToLowerInvariant(),
                Removable = Bool(el, "rm"),
                Hotplug = Bool(el, "hotplug"),
                Rotational = Bool(el, "rota")
            };
            if (long.TryParse(Str(el, "size"), out long size)) d.SizeBytes = size;
            if (string.IsNullOrEmpty(d.Path) && !string.IsNullOrEmpty(d.Name)) d.Path = "/dev/" + d.Name;

            if (el.TryGetProperty("mountpoints", out var mps) && mps.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in mps.EnumerateArray())
                    if (m.ValueKind == JsonValueKind.String) d.MountPoints.Add(m.GetString());
            }
            else if (el.TryGetProperty("mountpoint", out var mp) && mp.ValueKind == JsonValueKind.String)
            {
                d.MountPoints.Add(mp.GetString());
            }

            if (el.TryGetProperty("children", out var ch) && ch.ValueKind == JsonValueKind.Array)
                foreach (var c in ch.EnumerateArray()) d.Children.Add(ParseDevice(c));
            return d;
        }

        // lsblk's JSON types vary by version: sizes may be numbers or strings, flags may be true/false, "1"/"0" or 1/0
        private static string Str(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return "";
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString() ?? "";
                case JsonValueKind.Number: return v.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return "";
            }
        }

        private static bool Bool(JsonElement el, string name)
        {
            string s = Str(el, name).ToLowerInvariant();
            return s == "true" || s == "1";
        }
    }
}
