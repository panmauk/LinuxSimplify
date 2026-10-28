using System;
using System.Linq;
using System.Text.RegularExpressions;
using LinuxSimplify.Models;

namespace LinuxSimplify.Services
{
    /// <summary>
    /// Platform-neutral classification of scanned hardware (GPU vendor/tier, overall system profile).
    /// Shared by the Windows (WMI) and Linux (/proc, /sys, lsblk) scanners.
    /// </summary>
    public static class HardwareClassifier
    {
        public static string DetectVendor(string name)
        {
            string n = (name ?? "").ToUpperInvariant();
            if (n.Contains("NVIDIA")) return "NVIDIA";
            if (n.Contains("AMD") || n.Contains("RADEON") || n.Contains("ATI ") || n.Contains("ADVANCED MICRO DEVICES")) return "AMD";
            if (n.Contains("INTEL")) return "Intel";
            return "Unknown";
        }

        /// <summary>Intel Arc discrete cards are "Arc A770", "Arc B580", "Arc Pro A40". "Arc(TM) Graphics" (Meteor Lake and later) is integrated.</summary>
        private static bool IsIntelArcDiscrete(string upperModel)
        {
            return Regex.IsMatch(upperModel, @"\bARC(\(TM\))?\s+(PRO\s+)?[AB]\d{2,3}[MX]?\b");
        }

        public static GpuInfo BuildGpu(string name, double vramGB)
        {
            string vendor = DetectVendor(name);
            if (vramGB < 0.5) vramGB = InferVramFromModel(name);
            bool isDiscrete = ClassifyDiscrete(vendor, name);
            return new GpuInfo
            {
                Vendor = vendor,
                Model = name,
                VramGB = Math.Round(vramGB, 1),
                IsDiscrete = isDiscrete,
                IsGaming = ClassifyGaming(vendor, name),
                IsWorkstation = ClassifyWorkstation(vendor, name),
                Tier = ClassifyGpuTier(vendor, name, vramGB, isDiscrete)
            };
        }

        public static bool ClassifyDiscrete(string vendor, string model)
        {
            string m = (model ?? "").ToUpperInvariant();

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

            // Intel: Arc A/B-series cards are discrete, everything else is integrated
            if (vendor == "Intel")
                return IsIntelArcDiscrete(m);

            return false;
        }

        public static bool ClassifyGaming(string vendor, string model)
        {
            string m = (model ?? "").ToUpperInvariant();
            if (vendor == "NVIDIA") return m.Contains("GEFORCE") || m.Contains("GTX") || m.Contains("RTX");
            if (vendor == "AMD") return m.Contains("RADEON RX") || m.Contains("RADEON R9") || m.Contains("RADEON R7");
            if (vendor == "Intel") return IsIntelArcDiscrete(m);
            return false;
        }

        public static bool ClassifyWorkstation(string vendor, string model)
        {
            string m = (model ?? "").ToUpperInvariant();
            if (vendor == "NVIDIA") return m.Contains("QUADRO") || m.Contains("RTX A") || m.Contains("TESLA") || m.Contains("RTX 4000") || m.Contains("RTX 5000") || m.Contains("RTX 6000");
            if (vendor == "AMD") return m.Contains("FIREPRO") || m.Contains("RADEON PRO");
            return false;
        }

        public static GpuTier ClassifyGpuTier(string vendor, string model, double vramGB, bool isDiscrete)
        {
            if (!isDiscrete) return GpuTier.Integrated;

            string m = (model ?? "").ToUpperInvariant();

            if (vendor == "NVIDIA")
            {
                // Workstation
                if (ClassifyWorkstation(vendor, model)) return GpuTier.Workstation;

                // Enthusiast: x090 cards (the old pattern "(RTX\s*\d0)?90" matched ANY name containing "90")
                if (Regex.IsMatch(m, @"\b(RTX|GTX)\s*\d{2}90\b")) return GpuTier.Enthusiast;
                if (m.Contains("TITAN")) return GpuTier.Enthusiast;

                // High end: x070, x080
                if (Regex.IsMatch(m, @"\b(RTX|GTX)\s*\d{2}[78]0\b")) return GpuTier.HighEnd;

                // Mid range: x060 (GTX 1660 / 16xx handled below)
                if (Regex.IsMatch(m, @"\b(RTX|GTX)\s*\d{2}60\b")) return GpuTier.MidRange;

                // Entry: x050, x030, GT-series
                if (Regex.IsMatch(m, @"\b(RTX|GTX)\s*\d{2}[35]0\b") || Regex.IsMatch(m, @"\bGT\s*\d{3,4}\b"))
                    return GpuTier.EntryLevel;
                if (m.Contains("GTX 16") || m.Contains("GTX 10"))
                    return vramGB >= 6 ? GpuTier.MidRange : GpuTier.EntryLevel;

                return vramGB >= 8 ? GpuTier.HighEnd : vramGB >= 4 ? GpuTier.MidRange : GpuTier.EntryLevel;
            }

            if (vendor == "AMD")
            {
                if (m.Contains("FIREPRO") || m.Contains("RADEON PRO")) return GpuTier.Workstation;
                if (m.Contains("7900") || m.Contains("6900") || m.Contains("6950")) return GpuTier.Enthusiast;
                if (m.Contains("9070") || m.Contains("7800") || m.Contains("6800") || m.Contains("6700 XT")) return GpuTier.HighEnd;
                if (m.Contains("9060") || m.Contains("7700") || m.Contains("7600") || m.Contains("6600") || m.Contains("6700")) return GpuTier.MidRange;
                if (m.Contains("6500") || m.Contains("6400")) return GpuTier.EntryLevel;
                return vramGB >= 8 ? GpuTier.HighEnd : vramGB >= 4 ? GpuTier.MidRange : GpuTier.EntryLevel;
            }

            if (vendor == "Intel" && IsIntelArcDiscrete(m))
            {
                if (m.Contains("A770") || m.Contains("A750") || m.Contains("B580") || m.Contains("B570")) return GpuTier.MidRange;
                return GpuTier.EntryLevel;
            }

            return GpuTier.Unknown;
        }

        public static double InferVramFromModel(string model)
        {
            string m = (model ?? "").ToUpperInvariant();
            // Common known VRAM amounts by model
            if (m.Contains("5090")) return 32;
            if (m.Contains("5080")) return 16;
            if (m.Contains("5070 TI")) return 16;
            if (m.Contains("5070")) return 12;
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
        //  SYSTEM PROFILE — what kind of machine is this?
        // =============================================================
        public static void ClassifySystem(HardwareInfo info)
        {
            bool hasDiscreteGaming = info.Gpus.Any(g => g.IsDiscrete && g.IsGaming);
            bool hasWorkstationGpu = info.Gpus.Any(g => g.IsWorkstation);
            bool hasSsd = info.StorageDevices.Any(s => s.Type == "NVMe" || s.Type == "SSD");

            if (hasWorkstationGpu || (info.CpuCores >= 12 && info.RamGB >= 32))
            {
                info.Profile = SystemProfile.Workstation;
            }
            else if (hasDiscreteGaming && info.CpuCores >= 6 && info.RamGB >= 16)
            {
                info.Profile = SystemProfile.GamingDesktop;
            }
            else if (info.RamGB > 0 && info.RamGB <= 4) // 0 = unknown (scan failed), not "no RAM"
            {
                info.Profile = info.CpuCores <= 2 && !hasSsd ? SystemProfile.Legacy : SystemProfile.Lightweight;
            }
            else
            {
                info.Profile = SystemProfile.Standard;
            }
        }
    }
}
