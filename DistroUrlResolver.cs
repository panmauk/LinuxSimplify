using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LinuxSimplify.Services
{
    /// <summary>
    /// Dynamically resolves the latest ISO download URL for each distro
    /// by checking official mirrors and directory listings.
    /// Falls back to hardcoded URL if network fails.
    /// </summary>
    public class DistroUrlResolver
    {
        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        static DistroUrlResolver()
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LinuxSimplify/1.0");
        }

        public class ResolvedUrl
        {
            public string DownloadUrl { get; set; }
            public string Sha256Url { get; set; }
            public string NvidiaDownloadUrl { get; set; }
            public string ChecksumType { get; set; } = "sha256";
            public bool Resolved { get; set; }
        }

        /// <summary>
        /// Resolve latest URLs for all distros in parallel.
        /// Returns dictionary keyed by distro name.
        /// </summary>
        public async Task<Dictionary<string, ResolvedUrl>> ResolveAllAsync()
        {
            var tasks = new Dictionary<string, Task<ResolvedUrl>>
            {
                ["Ubuntu"] = ResolveUbuntu(),
                ["Linux Mint"] = ResolveMint(),
                ["Fedora"] = ResolveFedora(),
                ["Pop!_OS"] = ResolvePopOs(),
                ["Zorin OS"] = ResolveZorin(),
                ["Arch Linux"] = ResolveArch(),
                ["EndeavourOS"] = ResolveEndeavourOS(),
                ["Lubuntu"] = ResolveLubuntu(),
                ["Debian"] = ResolveDebian(),
                ["Trisquel"] = ResolveTrisquel()
            };

            var results = new Dictionary<string, ResolvedUrl>();
            foreach (var kvp in tasks)
            {
                try { results[kvp.Key] = await kvp.Value; }
                catch { results[kvp.Key] = new ResolvedUrl { Resolved = false }; }
            }
            return results;
        }

        // ===== Ubuntu: releases.ubuntu.com/noble/ =====
        private async Task<ResolvedUrl> ResolveUbuntu()
        {
            return await ResolveFromDirListing(
                "https://releases.ubuntu.com/noble/",
                @"ubuntu-[\d.]+-desktop-amd64\.iso(?!\.)",
                @"SHA256SUMS"
            );
        }

        // ===== Lubuntu: cdimage.ubuntu.com =====
        private async Task<ResolvedUrl> ResolveLubuntu()
        {
            return await ResolveFromDirListing(
                "https://cdimage.ubuntu.com/lubuntu/releases/noble/release/",
                @"lubuntu-[\d.]+-desktop-amd64\.iso(?!\.)",
                @"SHA256SUMS"
            );
        }

        // ===== Linux Mint: kernel.org mirror =====
        private async Task<ResolvedUrl> ResolveMint()
        {
            try
            {
                // Find latest version directory
                string baseUrl = "https://mirrors.edge.kernel.org/linuxmint/stable/";
                string listing = await http.GetStringAsync(baseUrl);

                // Parse version directories (e.g., "22/", "22.1/")
                var versionPattern = new Regex(@"href=""(2[2-9](?:\.\d+)?)/""");
                var versions = versionPattern.Matches(listing)
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .OrderByDescending(v => v, StringComparer.Ordinal)
                    .ToList();

                if (versions.Count == 0) return new ResolvedUrl { Resolved = false };

                string latestVer = versions[0];
                string verUrl = baseUrl + latestVer + "/";
                string verListing = await http.GetStringAsync(verUrl);

                // Find cinnamon 64-bit ISO
                var isoPattern = new Regex(@"(linuxmint-[\d.]+-cinnamon-64bit\.iso)(?!"")");
                var match = isoPattern.Match(verListing);
                if (!match.Success) return new ResolvedUrl { Resolved = false };

                string isoFile = match.Groups[1].Value;
                string sha256File = isoFile + ".sha256";

                // Check if sha256 exists
                string sha256Url = "";
                if (verListing.Contains(sha256File) || verListing.Contains("sha256sum"))
                {
                    sha256Url = verUrl + "sha256sum.txt";
                    if (!verListing.Contains("sha256sum.txt"))
                        sha256Url = verUrl + sha256File;
                }

                return new ResolvedUrl
                {
                    DownloadUrl = verUrl + isoFile,
                    Sha256Url = sha256Url,
                    Resolved = true
                };
            }
            catch { return new ResolvedUrl { Resolved = false }; }
        }

        // ===== Fedora: Arizona mirror (reliable direct download) =====
        private async Task<ResolvedUrl> ResolveFedora()
        {
            try
            {
                string listBase = "https://dl.fedoraproject.org/pub/fedora/linux/releases/";
                string downloadBase = "https://mirror.arizona.edu/fedora/linux/releases/";
                string listing = await http.GetStringAsync(listBase);

                var verPattern = new Regex(@"(\d{2,})/");
                var versions = verPattern.Matches(listing)
                    .Cast<Match>()
                    .Select(m => int.Parse(m.Groups[1].Value))
                    .Distinct()
                    .OrderByDescending(v => v)
                    .ToList();

                foreach (int ver in versions)
                {
                    try
                    {
                        string isoDir = $"{listBase}{ver}/Workstation/x86_64/iso/";
                        string isoListing = await http.GetStringAsync(isoDir);

                        var isoPattern = new Regex(@"(Fedora-Workstation-Live-[\w._-]+\.iso)");
                        var isoMatches = isoPattern.Matches(isoListing)
                            .Cast<Match>()
                            .Select(m => m.Groups[1].Value)
                            .Where(f => !f.Contains("CHECKSUM"))
                            .Distinct()
                            .ToList();

                        if (isoMatches.Count == 0) continue;

                        string isoFile = isoMatches[0];
                        string dlDir = $"{downloadBase}{ver}/Workstation/x86_64/iso/";
                        var csMatch = Regex.Match(isoListing, @"(Fedora-Workstation-[\w._-]*CHECKSUM)");

                        return new ResolvedUrl
                        {
                            DownloadUrl = dlDir + isoFile,
                            Sha256Url = csMatch.Success ? dlDir + csMatch.Groups[1].Value : "",
                            Resolved = true
                        };
                    }
                    catch { continue; }
                }
            }
            catch { }

            return new ResolvedUrl { Resolved = false };
        }

        // ===== Pop!_OS: iso.pop-os.org =====
        private async Task<ResolvedUrl> ResolvePopOs()
        {
            try
            {
                // Pop!_OS has a known URL pattern
                string baseUrl = "https://iso.pop-os.org/22.04/amd64/intel/";
                string listing = await http.GetStringAsync(baseUrl);

                // Find latest build number
                var buildPattern = new Regex(@"href=""(\d+)/""");
                var builds = buildPattern.Matches(listing)
                    .Cast<Match>()
                    .Select(m => int.Parse(m.Groups[1].Value))
                    .OrderByDescending(v => v)
                    .ToList();

                if (builds.Count == 0) return new ResolvedUrl { Resolved = false };

                int latestBuild = builds[0];
                string intelIso = $"{baseUrl}{latestBuild}/pop-os_22.04_amd64_intel_{latestBuild}.iso";
                string nvidiaIso = $"https://iso.pop-os.org/22.04/amd64/nvidia/{latestBuild}/pop-os_22.04_amd64_nvidia_{latestBuild}.iso";

                // Checksum
                string sha256Url = $"{baseUrl}{latestBuild}/pop-os_22.04_amd64_intel_{latestBuild}.iso.sha256sum";

                return new ResolvedUrl
                {
                    DownloadUrl = intelIso,
                    NvidiaDownloadUrl = nvidiaIso,
                    Sha256Url = sha256Url,
                    Resolved = true
                };
            }
            catch { return new ResolvedUrl { Resolved = false }; }
        }

        // ===== Zorin: kernel.org mirror (stable) =====
        private async Task<ResolvedUrl> ResolveZorin()
        {
            try
            {
                string baseUrl = "https://mirrors.edge.kernel.org/zorinos-isos/";
                string listing = await http.GetStringAsync(baseUrl);

                // Find latest major version dir
                var verPattern = new Regex(@"href=""(\d+)/""");
                var versions = verPattern.Matches(listing)
                    .Cast<Match>()
                    .Select(m => int.Parse(m.Groups[1].Value))
                    .OrderByDescending(v => v)
                    .ToList();

                if (versions.Count == 0) return new ResolvedUrl { Resolved = false };

                int latestVer = versions[0];
                string verUrl = $"{baseUrl}{latestVer}/";
                string verListing = await http.GetStringAsync(verUrl);

                // Find Core 64-bit ISO (latest revision)
                var isoPattern = new Regex(@"(Zorin-OS-[\d.]+-Core-64-bit(?:-r\d+)?\.iso)(?!"")");
                var matches = isoPattern.Matches(verListing)
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Distinct()
                    .OrderByDescending(f => f)
                    .ToList();

                if (matches.Count == 0) return new ResolvedUrl { Resolved = false };

                return new ResolvedUrl
                {
                    DownloadUrl = verUrl + matches[0],
                    Sha256Url = "", // Zorin doesn't publish checksums on mirror
                    Resolved = true
                };
            }
            catch { return new ResolvedUrl { Resolved = false }; }
        }

        // ===== Arch: always latest =====
        private async Task<ResolvedUrl> ResolveArch()
        {
            try
            {
                string baseUrl = "https://geo.mirror.pkgbuild.com/iso/latest/";
                string listing = await http.GetStringAsync(baseUrl);

                var isoPattern = new Regex(@"(archlinux-\d{4}\.\d{2}\.\d{2}-x86_64\.iso)(?!"")");
                var match = isoPattern.Match(listing);
                if (!match.Success) return new ResolvedUrl { Resolved = false };

                string isoFile = match.Groups[1].Value;
                return new ResolvedUrl
                {
                    DownloadUrl = baseUrl + isoFile,
                    Sha256Url = baseUrl + "sha256sums.txt",
                    Resolved = true
                };
            }
            catch { return new ResolvedUrl { Resolved = false }; }
        }

        // ===== EndeavourOS: alpix mirror (GitHub repo is archived) =====
        private async Task<ResolvedUrl> ResolveEndeavourOS()
        {
            try
            {
                string mirrorUrl = "https://mirror.alpix.eu/endeavouros/iso/";
                string listing = await http.GetStringAsync(mirrorUrl);

                // Match ISO files — naming changed over time:
                // Old: endeavouros-2021.08.27-x86_64.iso
                // New: EndeavourOS_Ganymede-Neo-2026.01.12.iso
                var isoPattern = new Regex(@"(EndeavourOS[\w_-]+\.iso|endeavouros-[\w.-]+\.iso)(?!\.)");
                var matches = isoPattern.Matches(listing)
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Where(f => !f.EndsWith(".sig") && !f.EndsWith(".sha512sum") && !f.EndsWith(".torrent") && !f.EndsWith(".log"))
                    .Distinct()
                    .OrderByDescending(f => f)
                    .ToList();

                if (matches.Count == 0) return new ResolvedUrl { Resolved = false };

                string latestIso = matches[0];

                return new ResolvedUrl
                {
                    DownloadUrl = mirrorUrl + latestIso,
                    Sha256Url = mirrorUrl + latestIso + ".sha512sum",
                    ChecksumType = "sha512",
                    Resolved = true
                };
            }
            catch { return new ResolvedUrl { Resolved = false }; }
        }

        // ===== Debian: cdimage.debian.org =====
        private async Task<ResolvedUrl> ResolveDebian()
        {
            return await ResolveFromDirListing(
                "https://cdimage.debian.org/debian-cd/current/amd64/iso-cd/",
                @"debian-[\d.]+-amd64-netinst\.iso(?!\.)",
                @"SHA256SUMS"
            );
        }

        // ===== Trisquel: mirror.fsf.org =====
        private async Task<ResolvedUrl> ResolveTrisquel()
        {
            try
            {
                string baseUrl = "https://mirror.fsf.org/trisquel-images/";
                string listing = await http.GetStringAsync(baseUrl);

                // Find latest amd64 ISO
                var isoPattern = new Regex(@"(trisquel_[\d.]+_amd64\.iso)(?!"")");
                var matches = isoPattern.Matches(listing)
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Distinct()
                    .OrderByDescending(f => f)
                    .ToList();

                if (matches.Count == 0) return new ResolvedUrl { Resolved = false };

                string iso = matches[0];
                return new ResolvedUrl
                {
                    DownloadUrl = baseUrl + iso,
                    Sha256Url = baseUrl + iso + ".sha256",
                    Resolved = true
                };
            }
            catch { return new ResolvedUrl { Resolved = false }; }
        }

        // ===== Generic directory listing resolver =====
        private async Task<ResolvedUrl> ResolveFromDirListing(string dirUrl, string isoRegex, string checksumFile)
        {
            try
            {
                string listing = await http.GetStringAsync(dirUrl);
                var match = Regex.Match(listing, isoRegex);
                if (!match.Success) return new ResolvedUrl { Resolved = false };

                string isoFile = match.Value;
                string sha = "";
                if (checksumFile != null)
                {
                    var csMatch = Regex.Match(listing, checksumFile);
                    if (csMatch.Success) sha = dirUrl + csMatch.Value;
                }

                return new ResolvedUrl
                {
                    DownloadUrl = dirUrl + isoFile,
                    Sha256Url = sha,
                    Resolved = true
                };
            }
            catch { return new ResolvedUrl { Resolved = false }; }
        }
    }
}
