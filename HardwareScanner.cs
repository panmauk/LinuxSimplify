using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using LinuxSimplify.Models;

namespace LinuxSimplify.Services
{
    public class HardwareScanner
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFirmwareType(ref uint firmwareType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetFirmwareEnvironmentVariable(
            string lpName, string lpGuid, IntPtr pBuffer, uint nSize);

        public async Task<HardwareInfo> ScanHardwareAsync()
        {
            return await Task.Run(() => ScanHardware());
        }

        private HardwareInfo ScanHardware()
        {
            var info = new HardwareInfo();

            // Each step is isolated: one failing WMI class (or a null property) used to abort the whole scan,
            // leaving boot mode and profile unset and the distro list empty.
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

        private static string DetectArchitecture()
        {
            // OSArchitecture is the machine's real architecture even when this process is x64 running emulated on Windows-on-ARM
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64: return "x86_64";
                case Architecture.X86: return "x86";
                case Architecture.Arm64: return "aarch64";
                case Architecture.Arm: return "arm";
                default: return "";
            }
        }

        private static int ToInt(object o, int fallback = 0)
        {
            try { return o == null ? fallback : Convert.ToInt32(o); } catch { return fallback; }
        }

        private static ulong ToULong(object o)
        {
            try { return o == null ? 0 : Convert.ToUInt64(o); } catch { return 0; }
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
                    info.CpuCores = ToInt(obj["NumberOfCores"]);
                    info.CpuThreads = ToInt(obj["NumberOfLogicalProcessors"], info.CpuCores);
                    info.CpuBaseClockMHz = ToInt(obj["MaxClockSpeed"]);
                    if (info.CpuCores <= 0) info.CpuCores = Math.Max(1, info.CpuThreads);

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
                    totalRam += ToULong(obj["Capacity"]);
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
                    if (name.Length == 0) continue;

                    // Win32_VideoController.AdapterRAM is a uint32: cards with 4 GB or more wrap/saturate.
                    // The display driver's registry key has the real 64-bit value.
                    double vramGB = 0;
                    try { vramGB = ReadVramFromRegistry(name); } catch { }
                    if (vramGB <= 0)
                    {
                        // Only trust AdapterRAM below the 4 GB wrap point
                        ulong ramBytes = ToULong(obj["AdapterRAM"]) & 0xFFFFFFFF;
                        vramGB = ramBytes / (1024.0 * 1024.0 * 1024.0);
                        if (vramGB < 0.5 || ramBytes >= 0xFFFF0000UL) vramGB = 0;
                    }

                    // VRAM <= 0 is inferred from the model name inside BuildGpu
                    info.Gpus.Add(HardwareClassifier.BuildGpu(name, vramGB));
                }
            }
        }

        /// <summary>Reads HardwareInformation.qwMemorySize of the display adapter whose DriverDesc matches <paramref name="adapterName"/>.</summary>
        private static double ReadVramFromRegistry(string adapterName)
        {
            const string classKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using (var cls = Registry.LocalMachine.OpenSubKey(classKey))
            {
                if (cls == null) return 0;
                foreach (var sub in cls.GetSubKeyNames())
                {
                    if (sub.Length != 4 || !sub.All(char.IsDigit)) continue;
                    using (var k = cls.OpenSubKey(sub))
                    {
                        if (k == null) continue;
                        string desc = k.GetValue("DriverDesc") as string;
                        if (!string.Equals(desc, adapterName, StringComparison.OrdinalIgnoreCase)) continue;

                        object v = k.GetValue("HardwareInformation.qwMemorySize");
                        ulong bytes = 0;
                        if (v is long l) bytes = (ulong)l;
                        else if (v is byte[] b && b.Length >= 8) bytes = BitConverter.ToUInt64(b, 0);
                        if (bytes > 0) return bytes / (1024.0 * 1024.0 * 1024.0);
                    }
                }
            }
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
                    // Plugged-in USB sticks / external drives aren't part of "your system" and used to inflate the storage total
                    if (IsUsbDisk(obj)) continue;

                    ulong size = ToULong(obj["Size"]); // null for empty card readers
                    if (size == 0) continue;

                    string model = obj["Model"]?.ToString() ?? "";
                    string type = "HDD";
                    if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)) type = "NVMe";
                    else if (model.Contains("SSD", StringComparison.OrdinalIgnoreCase)) type = "SSD";

                    info.StorageDevices.Add(new StorageInfo
                    {
                        Type = type,
                        SizeGB = Math.Round(size / (1024.0 * 1024.0 * 1024.0), 2),
                        Model = model
                    });
                }
            }
        }

        private static bool IsUsbDisk(ManagementBaseObject disk)
        {
            string iface = disk["InterfaceType"]?.ToString() ?? "";
            string pnp = disk["PNPDeviceID"]?.ToString() ?? "";
            return iface.Equals("USB", StringComparison.OrdinalIgnoreCase) ||
                   pnp.StartsWith("USBSTOR\\", StringComparison.OrdinalIgnoreCase);
        }

        // =============================================================
        //  BOOT MODE
        // =============================================================
        private void DetectBootMode(HardwareInfo info)
        {
            // GetFirmwareType (Windows 8+): 1 = BIOS, 2 = UEFI
            try
            {
                uint type = 0;
                if (GetFirmwareType(ref type) && (type == 1 || type == 2))
                {
                    if (type == 2) { info.BootMode = "UEFI"; DetectSecureBoot(info); }
                    else { info.BootMode = "Legacy BIOS"; info.SecureBootStatus = "Not Available"; }
                    return;
                }
            }
            catch { }

            try
            {
                // Legacy BIOS: the call fails with ERROR_INVALID_FUNCTION (1). Any other error means the machine is UEFI.
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
        //  USB DRIVES
        // =============================================================

        /// <summary>
        /// Physical disk number (PhysicalDriveN) that holds the Windows system drive, or -1 if it can't be determined.
        /// This disk must never be offered as, or accepted as, a flash target (matters for Windows To Go style USB installs).
        /// </summary>
        public static int GetSystemDiskNumber()
        {
            try
            {
                string systemDrive = Environment.GetEnvironmentVariable("SystemDrive");
                if (string.IsNullOrEmpty(systemDrive)) systemDrive = "C:";
                systemDrive = systemDrive.TrimEnd('\\');

                using (var parts = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{systemDrive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                {
                    foreach (ManagementObject part in parts.Get())
                    {
                        // DeviceID looks like "Disk #1, Partition #2"
                        string id = part["DeviceID"]?.ToString() ?? "";
                        int hash = id.IndexOf('#');
                        int comma = id.IndexOf(',');
                        if (hash >= 0 && comma > hash && int.TryParse(id.Substring(hash + 1, comma - hash - 1), out int n))
                            return n;
                    }
                }
            }
            catch { }
            return -1;
        }

        public List<UsbDrive> GetUsbDrives()
        {
            var drives = new List<UsbDrive>();
            try
            {
                int systemDisk = GetSystemDiskNumber();
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_DiskDrive WHERE InterfaceType='USB'"))
                {
                    foreach (ManagementObject disk in searcher.Get())
                    {
                        try
                        {
                            int index = ToInt(disk["Index"], -1);
                            if (index < 0 || index == systemDisk) continue;

                            // Empty card-reader slots report no size; they used to throw and hide every real drive
                            ulong size = ToULong(disk["Size"]);
                            if (size == 0) continue;

                            drives.Add(new UsbDrive
                            {
                                DeviceId = disk["DeviceID"]?.ToString() ?? "",
                                Name = disk["Model"]?.ToString() ?? "Unknown USB",
                                SizeBytes = (long)size,
                                SizeGB = Math.Round(size / (1024.0 * 1024.0 * 1024.0), 2),
                                DiskNumber = index
                            });
                        }
                        catch { /* skip this one drive, keep the rest */ }
                    }
                }
            }
            catch { }
            return drives;
        }
    }
}
