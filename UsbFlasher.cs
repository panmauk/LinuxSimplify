using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using LinuxSimplify.Models;

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
        private static extern bool ReadFile(SafeFileHandle hFile, IntPtr lpBuffer, uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

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
        private const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
        private const uint FSCTL_LOCK_VOLUME = 0x00090018;
        private const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
        private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
        private const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140;
        private const uint IOCTL_DISK_GET_DRIVE_GEOMETRY = 0x00070000;
        private const uint FILE_BEGIN = 0;

        public static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        /// <summary>
        /// Re-checks, right before anything destructive happens, that the disk number the UI remembered still
        /// refers to the same removable USB disk (the user may have swapped sticks since the list was built,
        /// and PhysicalDrive numbers are reused), is not the system disk, and is big enough for the image.
        /// </summary>
        private static string ValidateTarget(UsbDrive drive, long isoSize)
        {
            if (drive == null || drive.DiskNumber < 0) return "No USB drive selected";

            int systemDisk = HardwareScanner.GetSystemDiskNumber();
            if (drive.DiskNumber == systemDisk) return "That is the disk Windows is running from — refusing to erase it";

            try
            {
                using (var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_DiskDrive WHERE Index={drive.DiskNumber}"))
                {
                    ManagementObject found = null;
                    foreach (ManagementObject d in searcher.Get()) { found = d; break; }
                    if (found == null) return "The USB drive was removed";

                    string iface = found["InterfaceType"]?.ToString() ?? "";
                    if (!iface.Equals("USB", StringComparison.OrdinalIgnoreCase))
                        return "That disk is not a USB drive — refusing to erase it";

                    string id = found["DeviceID"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(drive.DeviceId) && !id.Equals(drive.DeviceId, StringComparison.OrdinalIgnoreCase))
                        return "The USB drive changed — please select it again";

                    ulong size = 0;
                    try { size = Convert.ToUInt64(found["Size"]); } catch { }
                    if (size == 0) return "The USB drive has no media";
                    if (drive.SizeBytes > 0 && (long)size != drive.SizeBytes)
                        return "The USB drive changed — please select it again";
                    if (isoSize > (long)size)
                        return $"The ISO ({isoSize / (1024 * 1024)} MB) is larger than the USB drive ({(long)size / (1024 * 1024)} MB)";
                }
            }
            catch (Exception ex)
            {
                return $"Couldn't check the USB drive: {ex.Message}";
            }
            return null;
        }

        public async Task<bool> FlashIsoToUsbAsync(string isoPath, UsbDrive drive, bool useUefi, IProgress<string> statusReporter)
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

                    string invalid = ValidateTarget(drive, isoSize);
                    if (invalid != null)
                    {
                        statusReporter?.Report("ERROR: " + invalid);
                        return false;
                    }
                    int diskNumber = drive.DiskNumber;

                    // ==========================================================
                    //  STEP 1: Disable automount.
                    //  This is critical. Without this, Windows detects the
                    //  ISO9660 header mid-write and tries to mount the disk,
                    //  causing error 5 (ACCESS_DENIED) partway through.
                    //  This is the same approach Rufus uses.
                    // ==========================================================
                    statusReporter?.Report("Preparing USB drive...");
                    // automount is a persistent, system-wide setting: only touch (and later restore) it if it is
                    // on, so a user who switched it off on purpose doesn't find it switched back on.
                    if (IsAutomountEnabled())
                    {
                        RunDiskpart("automount disable", out _);
                        automountDisabled = true;
                    }

                    // ==========================================================
                    //  STEP 2: diskpart clean — destroy all partitions.
                    //  Forces Windows to release all volume handles.
                    // ==========================================================
                    // (the disk was validated a moment ago; diskpart returns non-zero if any command in the script fails)
                    int dpExit = RunDiskpart(
                        $"select disk {diskNumber}\n" +
                        $"attributes disk clear readonly\n" +
                        $"clean", out string dpOutput);

                    if (dpExit != 0)
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

                    string isoHash;
                    using (driveHandle)
                    {
                        // Dismount any lingering volume references
                        uint br;
                        DeviceIoControl(driveHandle, FSCTL_DISMOUNT_VOLUME,
                            IntPtr.Zero, 0, IntPtr.Zero, 0, out br, IntPtr.Zero);

                        // Lock with retries
                        for (int i = 0; i < 15; i++)
                        {
                            if (DeviceIoControl(driveHandle, FSCTL_LOCK_VOLUME,
                                IntPtr.Zero, 0, IntPtr.Zero, 0, out br, IntPtr.Zero))
                                break;
                            Thread.Sleep(500);
                        }

                        // Dismount again after lock
                        DeviceIoControl(driveHandle, FSCTL_DISMOUNT_VOLUME,
                            IntPtr.Zero, 0, IntPtr.Zero, 0, out br, IntPtr.Zero);

                        // Raw writes must be whole sectors. Most USB sticks are 512 bytes/sector, but 4Kn drives exist —
                        // padding to a hard-coded 512 makes the last write fail there.
                        int sector = GetSectorSize(driveHandle);

                        // ==========================================================
                        //  STEP 4: Write ISO byte-for-byte (raw dd-style)
                        //  1MB chunks (a multiple of any sector size)
                        // ==========================================================
                        const int BUF = 1024 * 1024;
                        byte[] buffer = new byte[BUF];
                        long totalWritten = 0;
                        int lastPct = -1;

                        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                        using (var iso = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, BUF))
                        {
                            while (true)
                            {
                                // FileStream.Read may return fewer bytes than asked for even mid-file. Fill the whole
                                // buffer, otherwise a short read in the middle would be zero-padded and corrupt the image.
                                int bytesRead = ReadFully(iso, buffer, BUF);
                                if (bytesRead == 0) break;
                                hasher.AppendData(buffer, 0, bytesRead);

                                // Pad last chunk to a sector boundary
                                int writeSize = bytesRead;
                                if (writeSize % sector != 0)
                                {
                                    writeSize = ((writeSize / sector) + 1) * sector;
                                    Array.Clear(buffer, bytesRead, writeSize - bytesRead);
                                }

                                if (!WriteChunk(driveHandle, buffer, writeSize, totalWritten, out int err))
                                {
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

                            isoHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                        }

                        if (totalWritten != isoSize)
                        {
                            statusReporter?.Report("ERROR: The ISO file changed while it was being written");
                            DeviceIoControl(driveHandle, FSCTL_UNLOCK_VOLUME,
                                IntPtr.Zero, 0, IntPtr.Zero, 0, out br, IntPtr.Zero);
                            return false;
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
                    //  STEP 4b: Read it back (through a fresh, unbuffered handle so the
                    //  data really comes off the stick) and compare with the ISO's hash.
                    // ==========================================================
                    string verifyError = VerifyWritten(physPath, isoSize, isoHash, sector: 4096, statusReporter);
                    if (verifyError != null)
                    {
                        statusReporter?.Report("ERROR: " + verifyError);
                        return false;
                    }

                    // ==========================================================
                    //  STEP 5: Re-enable automount + post-flash cleanup
                    // ==========================================================
                    statusReporter?.Report("Cleaning up...");
                    if (automountDisabled)
                    {
                        RunDiskpart("automount enable", out _);
                        automountDisabled = false;
                    }

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
                        try { RunDiskpart("automount enable", out _); } catch { }
                    }
                }
            });
        }

        /// <summary>
        /// diskpart's "automount" stores its state in mountmgr's NoAutoMount value (1 = disabled, missing/0 = enabled).
        /// Read from the registry because diskpart's own text output is localized.
        /// </summary>
        private static bool IsAutomountEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\mountmgr"))
                {
                    object v = key?.GetValue("NoAutoMount");
                    return !(v is int i && i != 0);
                }
            }
            catch { return true; } // unknown: behave as before (disable during the write, enable afterwards)
        }

        private static int ReadFully(Stream s, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buffer, total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        /// <summary>Writes the whole chunk at an absolute offset, retrying (and re-seeking) on transient errors. Partial writes count as failures.</summary>
        private static bool WriteChunk(SafeFileHandle h, byte[] buffer, int writeSize, long position, out int lastError)
        {
            lastError = 0;
            for (int retry = 0; retry < 3; retry++)
            {
                SetFilePointerEx(h, position, out _, FILE_BEGIN);

                bool ok = WriteFile(h, buffer, (uint)writeSize, out uint written, IntPtr.Zero);
                int err = ok ? 0 : Marshal.GetLastWin32Error();
                if (ok && written == (uint)writeSize) return true;

                lastError = err;
                if (err != 0)
                {
                    // Real error — dismount and give the device a moment before trying again
                    DeviceIoControl(h, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
                    Thread.Sleep(500 * (retry + 1));
                }
                else
                {
                    // ok=true but fewer bytes written — transient, just retry the whole chunk
                    Thread.Sleep(300 * (retry + 1));
                }
            }
            return false;
        }

        private static int GetSectorSize(SafeFileHandle h)
        {
            IntPtr mem = Marshal.AllocHGlobal(64);
            try
            {
                // DISK_GEOMETRY: Cylinders(8) MediaType(4) TracksPerCylinder(4) SectorsPerTrack(4) BytesPerSector(4)
                if (DeviceIoControl(h, IOCTL_DISK_GET_DRIVE_GEOMETRY, IntPtr.Zero, 0, mem, 64, out _, IntPtr.Zero))
                {
                    int bps = Marshal.ReadInt32(mem, 20);
                    if (bps >= 512 && bps <= 65536 && (bps & (bps - 1)) == 0) return bps;
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(mem); }
            return 512;
        }

        /// <summary>Returns null on success, or an error message.</summary>
        private static string VerifyWritten(string physPath, long isoSize, string expectedHash, int sector, IProgress<string> status)
        {
            // FILE_FLAG_NO_BUFFERING: bypasses the cache and requires sector-aligned buffer address/length/offset.
            // 4096 covers both 512 and 4K sectors, and 1 MiB is a multiple of it.
            SafeFileHandle h = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                h = CreateFile(physPath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                    OPEN_EXISTING, FILE_FLAG_NO_BUFFERING, IntPtr.Zero);
                if (!h.IsInvalid) break;
                Thread.Sleep(500);
            }
            if (h == null || h.IsInvalid)
                return "Couldn't re-open the USB drive to verify it";

            const int BUF = 1024 * 1024;
            IntPtr mem = IntPtr.Zero;
            try
            {
                unsafe
                {
                    mem = (IntPtr)NativeMemory.AlignedAlloc((nuint)BUF, (nuint)sector);
                }
                byte[] managed = new byte[BUF];
                long remaining = isoSize;
                long done = 0;
                int lastPct = -1;
                SetFilePointerEx(h, 0, out _, FILE_BEGIN);

                using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    while (remaining > 0)
                    {
                        // Read whole sectors; only the first `remaining` bytes belong to the ISO
                        uint toRead = (uint)Math.Min(BUF, ((remaining + sector - 1) / sector) * sector);
                        if (!ReadFile(h, mem, toRead, out uint got, IntPtr.Zero) || got == 0)
                            return $"Couldn't read the USB drive back (error {Marshal.GetLastWin32Error()})";

                        int use = (int)Math.Min((long)got, remaining);
                        Marshal.Copy(mem, managed, 0, use);
                        hasher.AppendData(managed, 0, use);
                        remaining -= use;
                        done += use;

                        int pct = (int)((done * 100) / isoSize);
                        if (pct != lastPct) { status?.Report($"Verifying... {pct}%"); lastPct = pct; }
                    }

                    string actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                    if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                        return "Verification failed: the data on the USB drive doesn't match the ISO (the drive may be faulty)";
                }
                return null;
            }
            finally
            {
                unsafe { if (mem != IntPtr.Zero) NativeMemory.AlignedFree((void*)mem); }
                h.Dispose();
            }
        }

        private void CleanupPartitionVisibility(int diskNumber)
        {
            try
            {
                RunDiskpart($"select disk {diskNumber}\noffline disk\nonline disk", out _);
                Thread.Sleep(1500);

                var letters = GetDriveLettersForDisk(diskNumber);
                if (letters.Count > 1)
                {
                    for (int i = 1; i < letters.Count; i++)
                        RunDiskpart($"select volume {letters[i]}\nremove letter={letters[i]}", out _);
                }
            }
            catch { }
        }

        /// <summary>Runs a diskpart script. Returns diskpart's exit code (0 = every command succeeded), or -1 if it couldn't run / timed out.</summary>
        private int RunDiskpart(string script, out string output)
        {
            output = null;
            string path = Path.Combine(Path.GetTempPath(), $"ls_{Guid.NewGuid():N}.txt");
            try
            {
                File.WriteAllText(path, script);
                var psi = new ProcessStartInfo
                {
                    FileName = "diskpart.exe",
                    Arguments = $"/s \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(psi))
                {
                    // Read asynchronously: ReadToEnd() before WaitForExit() blocks forever if diskpart hangs
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(120000))
                    {
                        try { p.Kill(); } catch { }
                        return -1;
                    }
                    p.WaitForExit();
                    output = outTask.Result;
                    return p.ExitCode;
                }
            }
            catch { return -1; }
            finally { try { File.Delete(path); } catch { } }
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
