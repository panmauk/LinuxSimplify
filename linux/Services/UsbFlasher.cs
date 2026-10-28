using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LinuxSimplify.Models;

namespace LinuxSimplify.Services
{
    /// <summary>
    /// Linux flasher. The GUI never touches a block device itself: it starts the small shell helper
    /// "linuxsimplify-flash" through pkexec (polkit shows the password prompt), feeds it the ISO on stdin and
    /// turns the helper's output into the same status messages the Windows flasher reports:
    /// "Preparing USB drive...", "Flashing... N%", "Finishing up...", "Verifying... N%", "USB is ready!", "ERROR: ...".
    ///
    /// Two ways to reach the helper:
    ///  * installed (.deb): pkexec /opt/linuxsimplify/linuxsimplify-flash ...   -> polkit action with a proper message.
    ///  * not installed (AppImage, dev build): root can't read inside an AppImage's FUSE mount and a copy in a
    ///    user-writable temp file could be swapped before root runs it, so the script text is passed as an
    ///    argument instead:  pkexec /bin/sh -c "&lt;script&gt;" ...   (the dialog then shows the generic message).
    /// </summary>
    public class UsbFlasher
    {
        public const string TrustedHelperPath = "/opt/linuxsimplify/linuxsimplify-flash";
        private const string HelperName = "linuxsimplify-flash";

        public static string PkexecPath => Sys.FindInPath("pkexec");

        /// <summary>Flash <paramref name="isoPath"/> to <paramref name="drive"/>. Reports status strings; returns true on success.</summary>
        public async Task<bool> FlashIsoToUsbAsync(string isoPath, UsbDrive drive, bool useUefi, IProgress<string> statusReporter)
        {
            try
            {
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

                // Re-check the target right before the destructive step: the list the user clicked on may be stale
                // (stick swapped, removed, or - in theory - the device name re-used).
                statusReporter?.Report("Preparing USB drive...");
                var current = await Task.Run(() => new HardwareScanner().GetUsbDrives());
                var match = current.FirstOrDefault(d => d.DeviceId == drive?.DeviceId && d.SizeBytes == drive.SizeBytes);
                if (match == null)
                {
                    statusReporter?.Report("ERROR: The USB drive was removed or changed - please select it again");
                    return false;
                }
                if (isoSize > match.SizeBytes)
                {
                    statusReporter?.Report($"ERROR: The ISO ({isoSize / (1024 * 1024)} MB) is larger than the USB drive ({match.SizeBytes / (1024 * 1024)} MB)");
                    return false;
                }

                string pkexec = PkexecPath;
                if (pkexec == null)
                {
                    statusReporter?.Report("ERROR: pkexec (polkit) was not found - install polkit to flash drives");
                    return false;
                }

                string sha = await Task.Run(() => Sha256Of(isoPath));
                if (sha == null)
                {
                    statusReporter?.Report("ERROR: Couldn't read the ISO file");
                    return false;
                }

                ProcessStartInfo psi = BuildPkexecStartInfo(pkexec, isoPath, match.DeviceId, isoSize, sha, statusReporter);
                if (psi == null) return false;
                return await RunHelperAsync(psi, isoSize, statusReporter, viaPkexec: true);
            }
            catch (Exception ex)
            {
                statusReporter?.Report($"ERROR: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Test hook (CLI --test-flash): runs the helper in --test-file mode against a regular file, without pkexec and
        /// without any device checks, so the whole progress/verify pipeline can be exercised safely.
        /// </summary>
        public async Task<bool> FlashIsoToFileForTestingAsync(string isoPath, string targetFile, string helperScript, IProgress<string> statusReporter)
        {
            long isoSize = new FileInfo(isoPath).Length;
            string sha = Sha256Of(isoPath);
            var psi = new ProcessStartInfo("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("exec \"$0\" --test-file \"$@\" < \"$LINUXSIMPLIFY_ISO\"");
            psi.ArgumentList.Add(helperScript);
            psi.ArgumentList.Add(targetFile);
            psi.ArgumentList.Add(isoSize.ToString());
            psi.ArgumentList.Add(sha);
            psi.Environment["LINUXSIMPLIFY_ISO"] = isoPath;
            return await RunHelperAsync(psi, isoSize, statusReporter, viaPkexec: false);
        }

        private static ProcessStartInfo BuildPkexecStartInfo(string pkexec, string isoPath, string device, long isoSize, string sha, IProgress<string> status)
        {
            var psi = new ProcessStartInfo("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // The shell opens the ISO as the *normal user* and hands the descriptor to pkexec as stdin,
            // so the privileged helper never opens a user-chosen path.
            psi.Environment["LINUXSIMPLIFY_ISO"] = isoPath;
            psi.ArgumentList.Add("-c");

            if (File.Exists(TrustedHelperPath))
            {
                psi.ArgumentList.Add("exec \"$0\" \"$HELPER\" \"$@\" < \"$LINUXSIMPLIFY_ISO\"");
                psi.ArgumentList.Add(pkexec);
                psi.Environment["HELPER"] = TrustedHelperPath;
            }
            else
            {
                string script = null;
                try { script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, HelperName)); } catch { }
                if (string.IsNullOrWhiteSpace(script))
                {
                    status?.Report("ERROR: The flash helper script is missing from this installation");
                    return null;
                }
                // $0 = pkexec, $1 = script text (shifted away), then the three helper arguments. Root runs: sh -c <text> linuxsimplify-flash <args>
                psi.ArgumentList.Add("script=\"$1\"; shift; exec \"$0\" /bin/sh -c \"$script\" " + HelperName + " \"$@\" < \"$LINUXSIMPLIFY_ISO\"");
                psi.ArgumentList.Add(pkexec);
                psi.ArgumentList.Add(script);
            }

            // Arguments for the helper: they become "$@" in the shell command (after $0 / $1 which were consumed above)
            psi.ArgumentList.Add(device);
            psi.ArgumentList.Add(isoSize.ToString());
            psi.ArgumentList.Add(sha);
            return psi;
        }

        private static string Sha256Of(string path)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024))
                    return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            }
            catch { return null; }
        }

        private static readonly Regex DdProgress = new Regex(@"^\s*(\d+)\s+bytes", RegexOptions.Compiled);

        private async Task<bool> RunHelperAsync(ProcessStartInfo psi, long isoSize, IProgress<string> status, bool viaPkexec)
        {
            Process p;
            try { p = Process.Start(psi); }
            catch (Exception ex)
            {
                status?.Report($"ERROR: Couldn't start the flash helper: {ex.Message}");
                return false;
            }

            using (p)
            {
                string stage = "";
                bool done = false;
                string errorLine = null;
                var stderrTail = new Queue<string>();
                int lastPct = -1;

                void Pct(string label, long bytes)
                {
                    int pct = (int)Math.Min(100, bytes * 100 / Math.Max(1, isoSize));
                    if (pct != lastPct) { lastPct = pct; status?.Report($"{label} {pct}%"); }
                }

                var outTask = Task.Run(() =>
                {
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                    {
                        if (line.StartsWith("STAGE "))
                        {
                            stage = line.Substring(6).Trim();
                            lastPct = -1;
                            switch (stage)
                            {
                                case "validate":
                                case "unmount": status?.Report("Preparing USB drive..."); break;
                                case "write": status?.Report("Flashing... 0%"); break;
                                case "sync": status?.Report("Finishing up..."); break;
                                case "verify": status?.Report("Verifying... 0%"); break;
                            }
                        }
                        else if (line.StartsWith("ERROR: ")) errorLine = line.Substring(7).Trim();
                        else if (line == "DONE") done = true;
                    }
                });

                // dd writes "123456 bytes (...) copied, ..." with carriage returns, so split on \r as well as \n
                var errTask = Task.Run(() =>
                {
                    var sb = new StringBuilder();
                    int ch;
                    while ((ch = p.StandardError.Read()) >= 0)
                    {
                        if (ch != '\r' && ch != '\n') { sb.Append((char)ch); continue; }
                        if (sb.Length == 0) continue;
                        string line = sb.ToString();
                        sb.Clear();

                        var m = DdProgress.Match(line);
                        if (m.Success && long.TryParse(m.Groups[1].Value, out long bytes))
                        {
                            if (stage == "write") Pct("Flashing...", bytes);
                            else if (stage == "verify") Pct("Verifying...", bytes);
                        }
                        else
                        {
                            lock (stderrTail) { stderrTail.Enqueue(line); if (stderrTail.Count > 6) stderrTail.Dequeue(); }
                        }
                    }
                });

                await Task.Run(() => p.WaitForExit());
                await Task.WhenAll(outTask, errTask);

                if (done && p.ExitCode == 0)
                {
                    status?.Report("Cleaning up...");
                    status?.Report("USB is ready!");
                    return true;
                }

                string message = errorLine;
                if (string.IsNullOrEmpty(message) && viaPkexec)
                {
                    // pkexec: 126 = dialog dismissed / not authorized, 127 = authentication failed or could not run
                    if (p.ExitCode == 126) message = "Authentication was cancelled";
                    else if (p.ExitCode == 127) message = "Couldn't get permission to write to the drive (authentication failed, or no polkit agent is running)";
                }
                if (string.IsNullOrEmpty(message))
                {
                    string tail;
                    lock (stderrTail) tail = string.Join(" ", stderrTail.Where(s => !s.Contains("records in") && !s.Contains("records out")));
                    message = tail.Length > 0 ? tail : $"The flash helper stopped unexpectedly (exit code {p.ExitCode})";
                }
                status?.Report("ERROR: " + message);
                return false;
            }
        }
    }
}
