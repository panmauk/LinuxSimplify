using System.Collections.Generic;
using System.ComponentModel;

namespace LinuxSimplify.Models
{
    public class HardwareInfo
    {
        public string CpuBrand { get; set; } = "";
        public string CpuModel { get; set; } = "";
        public int CpuCores { get; set; }
        public int CpuThreads { get; set; }
        public int CpuBaseClockMHz { get; set; }
        public double RamGB { get; set; }
        public List<GpuInfo> Gpus { get; set; } = new List<GpuInfo>();
        public List<StorageInfo> StorageDevices { get; set; } = new List<StorageInfo>();
        public string BootMode { get; set; } = "";
        public string SecureBootStatus { get; set; } = "";
        public SystemProfile Profile { get; set; } = SystemProfile.Standard;
    }

    public enum SystemProfile
    {
        GamingDesktop,    // discrete gaming GPU + 6+ cores + 16+ GB RAM
        Workstation,      // high core count or workstation GPU
        Standard,         // decent specs, typical desktop/laptop
        Lightweight,      // 4 GB RAM or less, integrated GPU
        Legacy            // old CPU, low RAM, HDD only
    }

    public class GpuInfo
    {
        public string Vendor { get; set; } = "";
        public string Model { get; set; } = "";     // full WMI name like "NVIDIA GeForce RTX 4070"
        public double VramGB { get; set; }
        public bool IsDiscrete { get; set; }
        public bool IsGaming { get; set; }           // GeForce, Radeon RX, Arc
        public bool IsWorkstation { get; set; }      // Quadro, RTX A-series, FirePro, Radeon Pro
        public GpuTier Tier { get; set; } = GpuTier.Unknown;
    }

    public enum GpuTier
    {
        Unknown,
        Integrated,   // Intel UHD/Iris, AMD Vega (APU)
        EntryLevel,   // GT 1030, GTX 1650, RX 6400
        MidRange,     // RTX 3060, RTX 4060, RX 6600/7600
        HighEnd,      // RTX 3080, RTX 4070+, RX 6800+, RX 7800+
        Enthusiast,   // RTX 4090, RX 7900 XTX
        Workstation   // Quadro, RTX A-series, FirePro
    }

    public class StorageInfo
    {
        public string Type { get; set; } = "";
        public double SizeGB { get; set; }
        public string Model { get; set; } = "";
    }

    public class DistroCompatibility : INotifyPropertyChanged
    {
        private bool _isSelected;
        public string Name { get; set; } = "";
        public string CompatibilityStatus { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool IsRecommended { get; set; }
        public string DownloadUrl { get; set; } = "";
        public string Sha256Url { get; set; } = "";
        public string ChecksumType { get; set; } = "sha256";
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class UsbDrive
    {
        public string DeviceId { get; set; } = "";
        public string Name { get; set; } = "";
        public double SizeGB { get; set; }
        public int DiskNumber { get; set; }
    }

    public class DistroRequirements
    {
        public string Name { get; set; } = "";
        public double MinRamGB { get; set; }
        public double RecommendedRamGB { get; set; }
        public double MinStorageGB { get; set; }
        public bool SupportsProprietaryDrivers { get; set; }
        public bool GoodForGaming { get; set; }
        public bool GoodForOldHardware { get; set; }
        public bool GoodForBeginners { get; set; }
        public bool GoodForAdvanced { get; set; }
        public string DownloadUrl { get; set; } = "";
        public string NvidiaDownloadUrl { get; set; } = "";
        public string Sha256Url { get; set; } = "";
        public string ChecksumType { get; set; } = "sha256";
    }

    public class DownloadState
    {
        public string CurrentAction { get; set; } = "";
        public long BytesDownloaded { get; set; } = 0;
        public long TotalBytes { get; set; } = 0;
        public int Percentage { get; set; } = 0;
        public string ErrorMessage { get; set; } = "";
        public string LastSuccessfulAction { get; set; } = "";
    }
}
