using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LinuxSimplify.Services
{
    /// <summary>Small Linux helpers: run external tools safely, find programs, well-known directories.</summary>
    public static class Sys
    {
        /// <summary>
        /// Runs a program without a shell and returns (exit code, stdout). Returns exit code -1 (and empty output)
        /// if it is missing, fails to start, or exceeds <paramref name="timeoutMs"/> (then it is killed).
        /// </summary>
        public static (int ExitCode, string Output) Run(string file, string[] args, int timeoutMs = 8000)
        {
            try
            {
                var psi = new ProcessStartInfo(file)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                foreach (var a in args) psi.ArgumentList.Add(a);
                // stable, parseable output regardless of the user's locale
                psi.Environment["LC_ALL"] = "C";

                using (var p = Process.Start(psi))
                {
                    var stdout = p.StandardOutput.ReadToEndAsync();
                    var stderr = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(true); } catch { }
                        return (-1, "");
                    }
                    p.WaitForExit();
                    return (p.ExitCode, stdout.Result);
                }
            }
            catch { return (-1, ""); }
        }

        /// <summary>Absolute path of an executable found on PATH (plus the sbin dirs, which are often missing from a user's PATH), or null.</summary>
        public static string FindInPath(string name)
        {
            var dirs = new List<string>((Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries));
            foreach (var extra in new[] { "/usr/local/sbin", "/usr/sbin", "/sbin", "/usr/local/bin", "/usr/bin", "/bin" })
                if (!dirs.Contains(extra)) dirs.Add(extra);

            foreach (var d in dirs)
            {
                try
                {
                    string p = Path.Combine(d, name);
                    if (File.Exists(p)) return p;
                }
                catch { }
            }
            return null;
        }

        public static string HomeDir =>
            Environment.GetEnvironmentVariable("HOME") is string h && h.Length > 0 ? h : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>~/.cache/linuxsimplify (or $XDG_CACHE_HOME/linuxsimplify). Created on demand.</summary>
        public static string CacheDir
        {
            get
            {
                string baseDir = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrEmpty(baseDir) || !Path.IsPathRooted(baseDir)) baseDir = Path.Combine(HomeDir, ".cache");
                string dir = Path.Combine(baseDir, "linuxsimplify");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        /// <summary>$XDG_DOWNLOAD_DIR, else XDG_DOWNLOAD_DIR from ~/.config/user-dirs.dirs, else ~/Downloads.</summary>
        public static string DownloadsDir
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
                if (!string.IsNullOrEmpty(env) && Path.IsPathRooted(env)) return env;

                try
                {
                    string cfgHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                    if (string.IsNullOrEmpty(cfgHome)) cfgHome = Path.Combine(HomeDir, ".config");
                    string file = Path.Combine(cfgHome, "user-dirs.dirs");
                    if (File.Exists(file))
                    {
                        foreach (var raw in File.ReadAllLines(file))
                        {
                            string line = raw.Trim();
                            if (!line.StartsWith("XDG_DOWNLOAD_DIR=")) continue;
                            string v = line.Substring("XDG_DOWNLOAD_DIR=".Length).Trim().Trim('"');
                            v = v.Replace("$HOME", HomeDir);
                            if (Path.IsPathRooted(v)) return v;
                        }
                    }
                }
                catch { }
                return Path.Combine(HomeDir, "Downloads");
            }
        }
    }
}
