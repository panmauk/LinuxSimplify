using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;
using LinuxSimplify.Models;

namespace LinuxSimplify.Services
{
    public class HardwareScanner
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetFirmwareEnvironmentVariable(
            string lpName, string lpGuid, IntPtr pBuffer, uint nSize);

        public async Task<HardwareInfo> ScanHardwareAsync()
        {
            return await Task.Run(() => ScanHardware());
        }

        private HardwareInfo ScanHardware()
        {
            var info = new HardwareInfo();
            try
            {
                ScanCpu(info);
                ScanRam(info);
                ScanGpu(info);
                ScanStorage(info);
                DetectBootMode(info);
                ClassifySystem(info);
            }
            catch { }
            return info;
        }

        // =============================================================
        //  CPU — brand, model, cores, threads, base clock
        // =============================================================
        private void ScanCpu(HardwareInfo info)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                    if (manufacturer.Contains("Intel", StringComparison.OrdinalIgnoreCase))
                        info.CpuBrand = "Intel";
                    else if (manufacturer.Contains("AMD", StringComparison.OrdinalIgnoreCase))
                        info.CpuBrand = "AMD";
                    else
                        info.CpuBrand = manufacturer;

                    info.CpuModel = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                    info.CpuCores = Convert.ToInt32(obj["NumberOfCores"]);

                    try { info.CpuThreads = Convert.ToInt32(obj["NumberOfLogicalProcessors"]); }
                    catch { info.CpuThreads = info.CpuCores; }

                    try { info.CpuBaseClockMHz = Convert.ToInt32(obj["MaxClockSpeed"]); }
                    catch { info.CpuBaseClockMHz = 0; }

                    break;
                }
            }
        }

        // =============================================================
        //  RAM
        // =============================================================
        private void ScanRam(HardwareInfo info)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMemory"))
            {
                ulong totalRam = 0;
                foreach (ManagementObject obj in searcher.Get())
                    totalRam += Convert.ToUInt64(obj["Capacity"]);
                info.RamGB = Math.Round(totalRam / (1024.0 * 1024.0 * 1024.0), 2);
            }
        }

        // =============================================================
        //  GPU — model, vendor, VRAM, discrete vs integrated, tier
        // =============================================================
        private void ScanGpu(HardwareInfo info)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString() ?? "";
                    string vendor = "Unknown";
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) vendor = "NVIDIA";
                    else if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                             name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) vendor = "AMD";
                    else if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) vendor = "Intel";

                    // VRAM from AdapterRAM (bytes, can overflow on 4GB+ cards due to uint)
                    double vramGB = 0;
                    try
                    {
                        var adapterRam = obj["AdapterRAM"];
                        if (adapterRam != null)
                        {
                            // WMI returns uint32 so >4GB wraps. Use unsigned.
                            ulong ramBytes = Convert.ToUInt64(adapterRam) & 0xFFFFFFFF;
                            // If it wrapped (reported as very small like 0), try to infer from name
                            vramGB = ramBytes / (1024.0 * 1024.0 * 1024.0);
                            if (vramGB < 0.5) vramGB = InferVramFromModel(name);
                        }
                    }
                    catch { vramGB = InferVramFromModel(name); }

                    bool isDiscrete = ClassifyDiscrete(vendor, name);
                    bool isGaming = ClassifyGaming(vendor, name);
                    bool isWorkstation = ClassifyWorkstation(vendor, name);
                    var tier = ClassifyGpuTier(vendor, name, vramGB, isDiscrete);

                    info.Gpus.Add(new GpuInfo
                    {
                        Vendor = vendor,
                        Model = name,
                        VramGB = Math.Round(vramGB, 1),
                        IsDiscrete = isDiscrete,
                        IsGaming = isGaming,
                        IsWorkstation = isWorkstation,
                        Tier = tier
                    });
                }
            }
        }

        private bool ClassifyDiscrete(string vendor, string model)
        {
            string m = model.ToUpperInvariant();

            // NVIDIA: anything GeForce/Quadro/RTX/Tesla is discrete
            if (vendor == "NVIDIA") return true; // NVIDIA doesn't make integrated GPUs (except Tegra/Jetson)

            // AMD: Radeon RX, Radeon Pro, FirePro are discrete. Vega integrated says "Radeon(TM) Graphics" or "Vega"
            if (vendor == "AMD")
            {
                if (m.Contains("RADEON RX") || m.Contains("RADEON PRO") || m.Contains("FIREPRO") ||
                    m.Contains("RADEON R9") || m.Contains("RADEON R7") || m.Contains("RADEON R5"))
                    return true;
                // "AMD Radeon(TM) Graphics" or "AMD Radeon Vega" are integrated
                if (m.Contains("VEGA") && !m.Contains("VEGA 56") && !m.Contains("VEGA 64"))
                    return false;
                if (m.Contains("RADEON(TM) GRAPHICS") || m.Contains("RADEON GRAPHICS"))
                    return false;
                return m.Contains("RX "); // fallback
            }

            // Intel: Arc is discrete, everything else is integrated
            if (vendor == "Intel")
                return m.Contains("ARC ");

            return false;
        }

        private bool ClassifyGaming(string vendor, string model)
        {
            string m = model.ToUpperInvariant();
            if (vendor == "NVIDIA") return m.Contains("GEFORCE") || m.Contains("GTX") || m.Contains("RTX");
            if (vendor == "AMD") return m.Contains("RADEON RX") || m.Contains("RADEON R9") || m.Contains("RADEON R7");
            if (vendor == "Intel") return m.Contains("ARC ");
            return false;
        }

        private bool ClassifyWorkstation(string vendor, string model)
        {
            string m = model.ToUpperInvariant();
            if (vendor == "NVIDIA") return m.Contains("QUADRO") || m.Contains("RTX A") || m.Contains("TESLA") || m.Contains("RTX 4000") || m.Contains("RTX 5000") || m.Contains("RTX 6000");
            if (vendor == "AMD") return m.Contains("FIREPRO") || m.Contains("RADEON PRO");
            return false;
        }

        private GpuTier ClassifyGpuTier(string vendor, string model, double vramGB, bool isDiscrete)
        {
            if (!isDiscrete) return GpuTier.Integrated;

            string m = model.ToUpperInvariant();

            if (vendor == "NVIDIA")
            {
                // Workstation
                if (m.Contains("QUADRO") || m.Contains("RTX A")) return GpuTier.Workstation;

                // Enthusiast: x90 cards
                if (Regex.IsMatch(m, @"(RTX\s*\d0)?90")) return GpuTier.Enthusiast;
                if (m.Contains("TITAN")) return GpuTier.Enthusiast;

                // High end: x80, x70 Ti
                if (Regex.IsMatch(m, @"(RTX|GTX)\s*\d0[78]0")) return GpuTier.HighEnd;
                if (m.Contains("70 TI") || m.Contains("80 SUPER")) return GpuTier.HighEnd;

                // Mid range: x60, x70
                if (Regex.IsMatch(m, @"(RTX|GTX)\s*\d0[67]0")) return GpuTier.MidRange;
                if (m.Contains("60 TI")) return GpuTier.MidRange;

                // Entry: x50, x30, GT
                if (m.Contains("GT 1") || m.Contains("GTX 16") || m.Contains("GTX 10"))
                    return vramGB >= 6 ? GpuTier.MidRange : GpuTier.EntryLevel;

                return vramGB >= 8 ? GpuTier.HighEnd : vramGB >= 4 ? GpuTier.MidRange : GpuTier.EntryLevel;
            }

            if (vendor == "AMD")
            {
                if (m.Contains("FIREPRO") || m.Contains("RADEON PRO")) return GpuTier.Workstation;
                if (m.Contains("7900") || m.Contains("6900") || m.Contains("6950")) return GpuTier.Enthusiast;
                if (m.Contains("7800") || m.Contains("6800") || m.Contains("6700 XT")) return GpuTier.HighEnd;
                if (m.Contains("7600") || m.Contains("6600") || m.Contains("6700")) return GpuTier.MidRange;
                if (m.Contains("6500") || m.Contains("6400")) return GpuTier.EntryLevel;
                return vramGB >= 8 ? GpuTier.HighEnd : vramGB >= 4 ? GpuTier.MidRange : GpuTier.EntryLevel;
            }

            if (vendor == "Intel" && m.Contains("ARC"))
            {
                if (m.Contains("A770") || m.Contains("A750")) return GpuTier.MidRange;
                return GpuTier.EntryLevel;
            }

            return GpuTier.Unknown;
        }

        private double InferVramFromModel(string model)
        {
            string m = model.ToUpperInvariant();
            // Common known VRAM amounts by model
            if (m.Contains("4090")) return 24;
            if (m.Contains("4080")) return 16;
            if (m.Contains("4070 TI")) return 12;
            if (m.Contains("4070")) return 12;
            if (m.Contains("4060 TI")) return 8;
            if (m.Contains("4060")) return 8;
            if (m.Contains("3090")) return 24;
            if (m.Contains("3080")) return 10;
            if (m.Contains("3070")) return 8;
            if (m.Contains("3060 TI")) return 8;
            if (m.Contains("3060")) return 12;
            if (m.Contains("3050")) return 8;
            if (m.Contains("7900 XTX")) return 24;
            if (m.Contains("7900 XT")) return 20;
            if (m.Contains("7800 XT")) return 16;
            if (m.Contains("7600")) return 8;
            if (m.Contains("6900")) return 16;
            if (m.Contains("6800")) return 16;
            if (m.Contains("6700 XT")) return 12;
            if (m.Contains("6600")) return 8;
            return 0;
        }

        // =============================================================
        //  STORAGE
        // =============================================================
        private void ScanStorage(HardwareInfo info)
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    string model = obj["Model"]?.ToString() ?? "";
                    string type = "HDD";
                    if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) type = "NVMe";
                    else if (model.Contains("SSD", StringComparison.OrdinalIgnoreCase)) type = "SSD";

                    info.StorageDevices.Add(new StorageInfo
                    {
                        Type = type,
                        SizeGB = Math.Round(Convert.ToUInt64(obj["Size"]) / (1024.0 * 1024.0 * 1024.0), 2),
                        Model = model
                    });
                }
            }
        }

        // =============================================================
        //  BOOT MODE
        // =============================================================
        private void DetectBootMode(HardwareInfo info)
        {
            try
            {
                GetFirmwareEnvironmentVariable("", "{00000000-0000-0000-0000-000000000000}", IntPtr.Zero, 0);
                int error = Marshal.GetLastWin32Error();
                if (error != 1) { info.BootMode = "UEFI"; DetectSecureBoot(info); return; }
            }
            catch { }

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control"))
                {
                    var pe = key?.GetValue("PEFirmwareType");
                    if (pe != null && Convert.ToInt32(pe) == 2)
                    { info.BootMode = "UEFI"; DetectSecureBoot(info); return; }
                }
            }
            catch { }

            info.BootMode = "Legacy BIOS";
            info.SecureBootStatus = "Not Available";
        }

        private void DetectSecureBoot(HardwareInfo info)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
                {
                    var enabled = key?.GetValue("UEFISecureBootEnabled");
                    info.SecureBootStatus = (enabled != null && Convert.ToInt32(enabled) == 1) ? "Enabled" : "Disabled";
                    return;
                }
            }
            catch { }
            info.SecureBootStatus = "Disabled";
        }

        // =============================================================
        //  SYSTEM PROFILE — what kind of machine is this?
        // =============================================================
        private void ClassifySystem(HardwareInfo info)
        {
            bool hasDiscreteGaming = info.Gpus.Any(g => g.IsDiscrete && g.IsGaming);
            bool hasWorkstationGpu = info.Gpus.Any(g => g.IsWorkstation);
            bool hasHighEndGpu = info.Gpus.Any(g => g.Tier >= GpuTier.HighEnd);
            bool hasSsd = info.StorageDevices.Any(s => s.Type == "NVMe" || s.Type == "SSD");

            if (hasWorkstationGpu || (info.CpuCores >= 12 && info.RamGB >= 32))
            {
                info.Profile = SystemProfile.Workstation;
            }
            else if (hasDiscreteGaming && info.CpuCores >= 6 && info.RamGB >= 16)
            {
                info.Profile = SystemProfile.GamingDesktop;
            }
            else if (info.RamGB <= 4)
            {
                info.Profile = info.CpuCores <= 2 && !hasSsd ? SystemProfile.Legacy : SystemProfile.Lightweight;
            }
            else
            {
                info.Profile = SystemProfile.Standard;
            }
        }

        // =============================================================
        //  USB DRIVES
        // =============================================================
        public List<UsbDrive> GetUsbDrives()
        {
            var drives = new List<UsbDrive>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_DiskDrive WHERE InterfaceType='USB'"))
                {
                    foreach (ManagementObject disk in searcher.Get())
                    {
                        drives.Add(new UsbDrive
                        {
                            DeviceId = disk["DeviceID"]?.ToString() ?? "",
                            Name = disk["Model"]?.ToString() ?? "Unknown USB",
                            SizeGB = Math.Round(Convert.ToUInt64(disk["Size"]) / (1024.0 * 1024.0 * 1024.0), 2),
                            DiskNumber = Convert.ToInt32(disk["Index"])
                        });
                    }
                }
            }
            catch { }
            return drives;
        }
    }
}
