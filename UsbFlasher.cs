using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace LinuxSimplify.Services
{
    public class UsbFlasher
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushFileBuffers(SafeFileHandle hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode,
            IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize,
            out uint lpBytesReturned, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFilePointerEx(SafeFileHandle hFile, long liDistanceToMove,
            out long lpNewFilePointer, uint dwMoveMethod);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FSCTL_LOCK_VOLUME = 0x00090018;
        private const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
        private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
        private const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140;
        private const uint FILE_BEGIN = 0;

        public static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public async Task<bool> FlashIsoToUsbAsync(string isoPath, int diskNumber, bool useUefi, IProgress<string> statusReporter)
        {
            return await Task.Run(() =>
            {
                bool automountDisabled = false;

                try
                {
                    if (!IsAdministrator())
                    {
                        statusReporter?.Report("ERROR: Run as administrator to flash USB drives");
                        return false;
                    }

                    if (!File.Exists(isoPath))
                    {
                        statusReporter?.Report("ERROR: Can't find the ISO file");
                        return false;
                    }

                    long isoSize = new FileInfo(isoPath).Length;
                    if (isoSize < 1024 * 1024)
                    {
                        statusReporter?.Report("ERROR: ISO file seems too small");
                        return false;
                    }

                    // ==========================================================
                    //  STEP 1: Disable automount.
                    //  This is critical. Without this, Windows detects the
                    //  ISO9660 header mid-write and tries to mount the disk,
                    //  causing error 5 (ACCESS_DENIED) partway through.
                    //  This is the same approach Rufus uses.
                    // ==========================================================
                    statusReporter?.Report("Preparing USB drive...");
                    RunDiskpartSilent("automount disable");
                    automountDisabled = true;

                    // ==========================================================
                    //  STEP 2: diskpart clean — destroy all partitions.
                    //  Forces Windows to release all volume handles.
                    // ==========================================================
                    string dpResult = RunDiskpartSilent(
                        $"select disk {diskNumber}\n" +
                        $"attributes disk clear readonly\n" +
                        $"clean"
                    );

                    if (dpResult != null && dpResult.ToLower().Contains("cannot"))
                    {
                        statusReporter?.Report("ERROR: Couldn't prepare the USB drive");
                        return false;
                    }

                    Thread.Sleep(2000);

                    // ==========================================================
                    //  STEP 3: Open physical drive with exclusive access.
                    //  dwShareMode = 0 means no sharing — we want exclusive.
                    // ==========================================================
                    statusReporter?.Report("Flashing... 0%");
                    string physPath = $"\\\\.\\PhysicalDrive{diskNumber}";

                    SafeFileHandle driveHandle = null;
                    int openErr = 0;

                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        // Try exclusive first (no sharing)
                        driveHandle = CreateFile(physPath,
                            GENERIC_READ | GENERIC_WRITE,
                            0, // exclusive — no sharing
                            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

                        if (!driveHandle.IsInvalid) break;

                        // Fallback to shared if exclusive fails
                        driveHandle = CreateFile(physPath,
                            GENERIC_READ | GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE,
                            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

                        if (!driveHandle.IsInvalid) break;

                        openErr = Marshal.GetLastWin32Error();
                        Thread.Sleep(1000);
                    }

                    if (driveHandle == null || driveHandle.IsInvalid)
                    {
                        statusReporter?.Report($"ERROR: Can't open USB drive (error {openErr}). Close any Explorer windows showing the drive.");
                        return false;
                    }

                    using (driveHandle)
                    {
                        // Dismount any lingering volume references
                        uint br;
                        DeviceIoControl(driveHandle, FSCTL_DISMOUNT_VOLUME,
                            IntPtr.Zero, 0, IntPtr.Zero, 0, out br, IntPtr.Zero);

                        // Lock with retries
                        bool locked = false;
                        for (int i = 0; i < 15; i++)
                        {
                            if (DeviceIoControl(driveHandle, FSCTL_LOCK_VOLUME,
                                IntPtr.Zero, 0, IntPtr.Zero, 0, out br, IntPtr.Zero))
                            { locked = true; break; }
                            Thread.Sleep(500);
                        }

                        // Dismount again after lock
                        DeviceIoControl(driveHandle, FSCTL_DISMOUNT_VOLUME,
                            IntPtr.Zero, 0, IntPtr.Zero, 0, out br, IntPtr.Zero);

                        // Seek to start
                        long newPos;
                        SetFilePointerEx(driveHandle, 0, out newPos, FILE_BEGIN);

                        // ==========================================================
                        //  STEP 4: Write ISO byte-for-byte (raw dd-style)
                        //  4MB buffer = fewer syscalls = faster throughput
                        // ==========================================================
                        const int BUF = 1024 * 1024; // 1MB chunks — safer for USB
                        byte[] buffer = new byte[BUF];
                        long totalWritten = 0;
                        int lastPct = -1;

                        using (var iso = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, BUF))
                        {
                            int bytesRead;
                            while ((bytesRead = iso.Read(buffer, 0, BUF)) > 0)
                            {
                                // Pad last chunk to 512-byte sector boundary
                                int writeSize = bytesRead;
                                if (writeSize % 512 != 0)
                                {
                                    writeSize = ((writeSize / 512) + 1) * 512;
                                    for (int p = bytesRead; p < writeSize; p++)
                                        buffer[p] = 0;
                                }

                                uint written = 0;
                                bool ok = false;

                                for (int retry = 0; retry < 3; retry++)
                                {
                                    ok = WriteFile(driveHandle, buffer, (uint)writeSize, out written, IntPtr.Zero);
                                    int err = Marshal.GetLastWin32Error();

                                    // Success: wrote something
                                    if (ok && written > 0) break;

                                    // ok=true but written=0, or ok=false with error 0 — transient, just retry
                                    if (err == 0)
                                    {
                                        Thread.Sleep(300 * (retry + 1));
                                        SetFilePointerEx(driveHandle, totalWritten, out newPos, FILE_BEGIN);
                                        continue;
                                    }

                                    // Real error — dismount and retry
                                    DeviceIoControl(driveHandle, FSCTL_DISMOUNT_VOLUME,
                                        IntPtr.Zero, 0, IntPtr.Zero, 0, out br, IntPtr.Zero);
                                    Thread.Sleep(500 * (retry + 1));
                                    SetFilePointerEx(driveHandle, totalWritten, out newPos, FILE_BEGIN);
                                }

                                if (!ok || written == 0)
                                {
                                    int err = Marshal.GetLastWin32Error();
                                    statusReporter?.Report($"ERROR: Write failed at {totalWritten / (1024 * 1024)} MB (error {err})");
                                    DeviceIoControl(driveHandle, FSCTL_UNLOCK_VOLUME,
                                        IntPtr.Zero, 0, IntPtr.Zero, 0, out br, IntPtr.Zero);
                                    return false;
                                }

                                totalWritten += bytesRead;
                                int pct = (int)((totalWritten * 100) / isoSize);
                                if (pct != lastPct)
                                {
                                    statusReporter?.Report($"Flashing... {pct}%");
                                    lastPct = pct;
                                }
                            }
                        }

                        // Flush + unlock
                        statusReporter?.Report("Finishing up...");
                        FlushFileBuffers(driveHandle);
                        DeviceIoControl(driveHandle, FSCTL_UNLOCK_VOLUME,
                            IntPtr.Zero, 0, IntPtr.Zero, 0, out br, IntPtr.Zero);

                        // Force disk geometry update
                        DeviceIoControl(driveHandle, IOCTL_DISK_UPDATE_PROPERTIES,
                            IntPtr.Zero, 0, IntPtr.Zero, 0, out br, IntPtr.Zero);
                    }

                    // ==========================================================
                    //  STEP 5: Re-enable automount + post-flash cleanup
                    // ==========================================================
                    statusReporter?.Report("Cleaning up...");
                    RunDiskpartSilent("automount enable");
                    automountDisabled = false;

                    Thread.Sleep(1000);
                    CleanupPartitionVisibility(diskNumber);

                    statusReporter?.Report("USB is ready!");
                    return true;
                }
                catch (Exception ex)
                {
                    statusReporter?.Report($"ERROR: {ex.Message}");
                    return false;
                }
                finally
                {
                    // Always re-enable automount
                    if (automountDisabled)
                    {
                        try { RunDiskpartSilent("automount enable"); } catch { }
                    }
                }
            });
        }

        private void CleanupPartitionVisibility(int diskNumber)
        {
            try
            {
                RunDiskpartSilent($"select disk {diskNumber}\noffline disk\nonline disk");
                Thread.Sleep(1500);

                var letters = GetDriveLettersForDisk(diskNumber);
                if (letters.Count > 1)
                {
                    for (int i = 1; i < letters.Count; i++)
                        RunDiskpartSilent($"select volume {letters[i]}\nremove letter={letters[i]}");
                }
            }
            catch { }
        }

        private string RunDiskpartSilent(string script)
        {
            try
            {
                string path = Path.Combine(Path.GetTempPath(), $"ls_{Guid.NewGuid():N}.txt");
                File.WriteAllText(path, script);
                var psi = new ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = $"/s \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                string output = null;
                using (var p = Process.Start(psi))
                {
                    output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(15000);
                    if (!p.HasExited) p.Kill();
                }
                try { File.Delete(path); } catch { }
                return output;
            }
            catch { return null; }
        }

        private List<char> GetDriveLettersForDisk(int diskNumber)
        {
            var letters = new List<char>();
            try
            {
                string query = $@"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='\\.\PhysicalDrive{diskNumber}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition";
                using (var parts = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject part in parts.Get())
                    {
                        string pq = $"ASSOCIATORS OF {{{part.Path.RelativePath}}} WHERE AssocClass=Win32_LogicalDiskToPartition";
                        using (var logs = new ManagementObjectSearcher(pq))
                        {
                            foreach (ManagementObject log in logs.Get())
                            {
                                string id = log["DeviceID"]?.ToString();
                                if (!string.IsNullOrEmpty(id) && id.Length >= 1)
                                    letters.Add(id[0]);
                            }
                        }
                    }
                }
            }
            catch { }
            return letters;
        }
    }
}
