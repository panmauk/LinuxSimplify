using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LinuxSimplify.Models;

namespace LinuxSimplify.Services
{
    /// <summary>
    /// LinuxSimplify runs as a normal user (asInvoker). Only writing the USB drive needs administrator rights, so
    /// that one step runs in a second, elevated copy of the app ("--flash-helper", one UAC prompt):
    ///  * The helper does all the work through <see cref="UsbFlasher"/>, which re-validates the target disk itself
    ///    (USB only, not the system disk, same device id and size as selected): it does not trust its caller.
    ///  * Progress comes back over a named pipe created by this (unelevated) process. The elevated helper writes
    ///    no files, and the pipe also works when an administrator approves the prompt for a standard user
    ///    (the helper then runs as a different account with a different %TEMP%).
    /// </summary>
    public static class ElevatedFlash
    {
        public const string HelperArg = "--flash-helper";
        private const string ResultPrefix = "RESULT ";
        private static readonly Regex PipeName = new Regex(@"^LinuxSimplify-flash-[0-9a-f]{32}$");

        /// <summary>Flashes the ISO, elevating just for this step if the app isn't already running as administrator.</summary>
        public static async Task<bool> FlashAsync(string isoPath, UsbDrive drive, bool useUefi, IProgress<string> status)
        {
            if (UsbFlasher.IsAdministrator())
                return await new UsbFlasher().FlashIsoToUsbAsync(isoPath, drive, useUefi, status);

            string exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe) || drive == null)
            {
                status?.Report("ERROR: Can't start the flash helper");
                return false;
            }

            string pipeName = $"LinuxSimplify-flash-{Guid.NewGuid():N}";
            using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                var psi = new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,   // required for Verb = "runas" (the UAC prompt)
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                psi.ArgumentList.Add(HelperArg);
                psi.ArgumentList.Add(isoPath);
                psi.ArgumentList.Add(drive.DiskNumber.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add(drive.DeviceId ?? "");
                psi.ArgumentList.Add(drive.SizeBytes.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add(useUefi ? "1" : "0");
                psi.ArgumentList.Add(pipeName);

                status?.Report("Preparing USB drive...");
                Process p;
                try { p = Process.Start(psi); }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: UAC prompt declined
                {
                    status?.Report("ERROR: Administrator permission is needed to write to the USB drive");
                    return false;
                }
                catch (Exception ex)
                {
                    status?.Report($"ERROR: Can't start the flash helper: {ex.Message}");
                    return false;
                }
                if (p == null)
                {
                    status?.Report("ERROR: Can't start the flash helper");
                    return false;
                }

                using (p)
                {
                    // Wait for the helper to connect, but give up if it exits first (bad arguments, crash)
                    var exited = p.WaitForExitAsync();
                    var connect = pipe.WaitForConnectionAsync();
                    if (await Task.WhenAny(connect, exited) != connect)
                    {
                        status?.Report($"ERROR: The flash helper stopped before it started (exit code {p.ExitCode})");
                        return false;
                    }
                    await connect;

                    bool ok = false, sawResult = false;
                    using (var reader = new StreamReader(pipe, Encoding.UTF8))
                    {
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            if (line.StartsWith(ResultPrefix, StringComparison.Ordinal))
                            {
                                sawResult = true;
                                ok = line == ResultPrefix + "OK";
                            }
                            else if (line.Length > 0) status?.Report(line);
                        }
                    }
                    await exited;
                    if (!sawResult)
                    {
                        status?.Report($"ERROR: The flash helper stopped unexpectedly (exit code {p.ExitCode})");
                        return false;
                    }
                    return ok;
                }
            }
        }

        /// <summary>Entry point of the elevated helper process. Returns the process exit code.</summary>
        public static int RunHelper(string[] args)
        {
            // --flash-helper <iso> <diskNumber> <deviceId> <sizeBytes> <uefi 0|1> <pipeName>
            if (args.Length != 7 || !PipeName.IsMatch(args[6])) return 2;

            using (var pipe = new NamedPipeClientStream(".", args[6], PipeDirection.Out))
            {
                try { pipe.Connect(10000); }
                catch { return 3; }

                using (var log = new PipeStatus(pipe))
                {
                    try
                    {
                        if (!int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int disk) ||
                            !long.TryParse(args[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long size))
                        {
                            log.Report("ERROR: Invalid flash request");
                            log.Line(ResultPrefix + "FAIL");
                            return 1;
                        }
                        var drive = new UsbDrive { DiskNumber = disk, DeviceId = args[3], SizeBytes = size };
                        // UsbFlasher.ValidateTarget re-checks all of this against Windows before anything is written
                        bool ok = new UsbFlasher().FlashIsoToUsbAsync(args[1], drive, args[5] == "1", log).GetAwaiter().GetResult();
                        log.Line(ResultPrefix + (ok ? "OK" : "FAIL"));
                        return ok ? 0 : 1;
                    }
                    catch (Exception ex)
                    {
                        log.Report("ERROR: " + ex.Message);
                        log.Line(ResultPrefix + "FAIL");
                        return 1;
                    }
                }
            }
        }

        /// <summary>Progress sink for the helper: one line per status message over the pipe.</summary>
        private sealed class PipeStatus : IProgress<string>, IDisposable
        {
            private readonly StreamWriter w;
            private readonly object gate = new object();

            public PipeStatus(Stream s) { w = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true }; }

            public void Report(string value) => Line((value ?? "").Replace('\r', ' ').Replace('\n', ' '));

            public void Line(string s)
            {
                lock (gate) { try { w.Write(s + "\n"); } catch { /* the app was closed: keep flashing, nobody listens */ } }
            }

            public void Dispose() { lock (gate) { try { w.Dispose(); } catch { } } }
        }
    }
}
