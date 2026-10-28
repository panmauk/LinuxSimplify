using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LinuxSimplify.Models;

namespace LinuxSimplify.Services
{
    /// <summary>
    /// Dynamically resolves the latest ISO download URL for each distro
    /// by checking official mirrors and directory listings.
    /// Falls back to hardcoded URL if network fails.
    /// </summary>
    public class DistroUrlResolver
    {
        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        private static readonly TimeSpan OverallTimeout = TimeSpan.FromSeconds(50);

        static DistroUrlResolver()
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LinuxSimplify/1.0");
        }

        public class ResolvedUrl
        {
            public string DownloadUrl { get; set; }
            public string Sha256Url { get; set; }
            public string ExpectedHash { get; set; }
            public string NvidiaDownloadUrl { get; set; }
            public string NvidiaSha256Url { get; set; }
            public string NvidiaExpectedHash { get; set; }
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

            // The UI waits for this before letting the user continue, so one hung mirror must not hold everything up:
            // after OverallTimeout, whatever has not finished is reported as unresolved (= hardcoded fallback is used).
            var all = Task.WhenAll(tasks.Values.Select(t => t.ContinueWith(_ => { })));
            await Task.WhenAny(all, Task.Delay(OverallTimeout));

            var results = new Dictionary<string, ResolvedUrl>();
            foreach (var kvp in tasks)
            {
                results[kvp.Key] = kvp.Value.IsCompletedSuccessfully && kvp.Value.Result != null
                    ? kvp.Value.Result
                    : new ResolvedUrl { Resolved = false };
            }
            return results;
        }

        /// <summary>
        /// Overwrites the hardcoded fallback URL/checksum info with freshly resolved values.
        /// Checksum info is always replaced together with the URL: keeping the old checksum URL
        /// next to a new ISO would point at the wrong version's hash.
        /// </summary>
        public static void ApplyResolved(IEnumerable<DistroCompatibility> distros, IDictionary<string, ResolvedUrl> resolved, bool hasNvidia)
        {
            foreach (var d in distros)
            {
                if (!resolved.TryGetValue(d.Name, out var r) || r == null || !r.Resolved) continue;

                d.DownloadUrl = r.DownloadUrl;
                d.Sha256Url = r.Sha256Url ?? "";
                d.ExpectedHash = r.ExpectedHash ?? "";
                d.ChecksumType = string.IsNullOrEmpty(r.ChecksumType) ? "sha256" : r.ChecksumType;

                if (hasNvidia && !string.IsNullOrEmpty(r.NvidiaDownloadUrl))
                {
                    d.DownloadUrl = r.NvidiaDownloadUrl;
                    d.Sha256Url = r.NvidiaSha256Url ?? "";
                    d.ExpectedHash = r.NvidiaExpectedHash ?? "";
                }
            }
        }

        // =============================================================
        //  Helpers
        // =============================================================

        /// <summary>GET with retries — mirrors (kernel.org in particular) return sporadic 5xx / time out.</summary>
        private static async Task<string> GetStringAsync(string url, int attempts = 3)
        {
            Exception last = null;
            for (int i = 0; i < attempts; i++)
            {
                try
                {
                    using (var resp = await http.GetAsync(url))
                    {
                        if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsStringAsync();
                        // 4xx (other than 408/429) won't get better by retrying
                        int code = (int)resp.StatusCode;
                        last = new HttpRequestException($"HTTP {code} for {url}");
                        if (code >= 400 && code < 500 && code != 408 && code != 429) throw last;
                    }
                }
                catch (HttpRequestException ex) when (last != null && ReferenceEquals(ex, last)) { throw; }
                catch (Exception ex) { last = ex; }

                if (i < attempts - 1) await Task.Delay(600 * (i + 1));
            }
            throw last ?? new HttpRequestException("request failed");
        }

        /// <summary>
        /// Sort key for "newest first": the first dotted number in the string ("zorin-os-18.1-core" -> 18.1),
        /// padded to a fixed width, followed by an optional "-rN" respin number as the lowest-priority tie-breaker
        /// (so 18.1 beats 18-r3, and 18-r3 beats 18). Later numbers such as the "64" in "amd64" are ignored.
        /// </summary>
        private static int[] VersionParts(string s)
        {
            const int Width = 8;
            var key = new int[Width + 1];
            var m = Regex.Match(s ?? "", @"\d+(?:\.\d+)*");
            if (m.Success)
            {
                var parts = m.Value.Split('.');
                for (int i = 0; i < parts.Length && i < Width; i++)
                    key[i] = int.TryParse(parts[i], out int v) ? v : 0;
            }
            var rev = Regex.Match(s ?? "", @"-r(\d+)(?=[.\-_]|$)");
            if (rev.Success && int.TryParse(rev.Groups[1].Value, out int r)) key[Width] = r;
            return key;
        }

        private static int CompareVersions(int[] a, int[] b)
        {
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }

        /// <summary>Distinct regex matches (group 1 if present, else the whole match), newest version first.</summary>
        private static List<string> MatchesNewestFirst(string text, string pattern, Func<string, int[]> key = null)
        {
            key = key ?? VersionParts;
            var list = Regex.Matches(text, pattern).Cast<Match>()
                .Select(m => m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value)
                .Distinct().ToList();
            list.Sort((a, b) =>
            {
                int c = CompareVersions(key(b), key(a));
                return c != 0 ? c : string.CompareOrdinal(b, a);
            });
            return list;
        }

        // ===== Ubuntu: releases.ubuntu.com — walks version dirs to find the latest LTS =====
        private async Task<ResolvedUrl> ResolveUbuntu()
        {
            return await ResolveLatestUbuntuFamilyRelease(
                "https://releases.ubuntu.com/",
                v => "",
                ver => IsLtsVersion(ver),
                @"(?<![\w.-])ubuntu-[\d.]+-desktop-amd64\.iso(?![\w.])",
                @"(?<![\w.-])SHA256SUMS(?![\w.])"
            );
        }

        /// <summary>LTS releases ship every even year in April: 22.04, 24.04, 26.04 (and their point releases).</summary>
        private static bool IsLtsVersion(string ver)
        {
            var p = VersionParts(ver);
            return p.Length >= 2 && p[1] == 4 && p[0] % 2 == 0;
        }

        // ===== Lubuntu: cdimage.ubuntu.com — walks version dirs to find the latest release =====
        private async Task<ResolvedUrl> ResolveLubuntu()
        {
            return await ResolveLatestUbuntuFamilyRelease(
                "https://cdimage.ubuntu.com/lubuntu/releases/",
                v => "release/",
                ver => true, // Lubuntu ships both LTS and interim releases
                @"(?<![\w.-])lubuntu-[\d.]+-desktop-amd64\.iso(?![\w.])",
                @"(?<![\w.-])SHA256SUMS(?![\w.])"
            );
        }

        // ===== Shared: find the newest version directory that actually has a published ISO =====
        // Directories for in-development releases (e.g. daily snapshot-only builds) exist before
        // the final ISO is published, so this walks versions newest-first and skips any that 404.
        private async Task<ResolvedUrl> ResolveLatestUbuntuFamilyRelease(string baseUrl, Func<string, string> toIsoDir, Func<string, bool> versionFilter, string isoRegex, string checksumFile)
        {
            try
            {
                string listing = await GetStringAsync(baseUrl);
                var versions = MatchesNewestFirst(listing, @"href=""(\d{2}\.\d{2}(?:\.\d+)?)/""")
                    .Where(versionFilter)
                    .ToList();

                foreach (var ver in versions)
                {
                    try
                    {
                        string isoDir = baseUrl + ver + "/" + toIsoDir(ver);
                        var resolved = await ResolveFromDirListing(isoDir, isoRegex, checksumFile);
                        if (resolved.Resolved) return resolved;
                    }
                    catch { continue; }
                }
            }
            catch { }

            return new ResolvedUrl { Resolved = false };
        }

        // ===== Linux Mint: kernel.org mirror, with fallbacks =====
        private static readonly string[] MintMirrors =
        {
            "https://mirrors.edge.kernel.org/linuxmint/stable/",
            "https://ftp.fau.de/mint/iso/stable/",
            "https://mirror.csclub.uwaterloo.ca/linuxmint/stable/"
        };

        private async Task<ResolvedUrl> ResolveMint()
        {
            foreach (var baseUrl in MintMirrors)
            {
                try
                {
                    // Parse version directories (e.g., "22/", "22.1/", "22.3/")
                    string listing = await GetStringAsync(baseUrl, 2);
                    var versions = MatchesNewestFirst(listing, @"href=""(\d+(?:\.\d+)?)/""");

                    foreach (var ver in versions.Take(3))
                    {
                        string verUrl = baseUrl + ver + "/";
                        string verListing;
                        try { verListing = await GetStringAsync(verUrl, 2); } catch { continue; }

                        // Find cinnamon 64-bit ISO
                        var isos = MatchesNewestFirst(verListing, @"(?<![\w.-])(linuxmint-[\d.]+-cinnamon-64bit\.iso)(?![\w.])");
                        if (isos.Count == 0) continue;

                        string isoFile = isos[0];
                        string sha256Url = Regex.IsMatch(verListing, @"(?<![\w.-])sha256sum\.txt(?![\w.])") ? verUrl + "sha256sum.txt" : "";

                        return new ResolvedUrl
                        {
                            DownloadUrl = verUrl + isoFile,
                            Sha256Url = sha256Url,
                            Resolved = true
                        };
                    }
                }
                catch { /* try next mirror */ }
            }
            return new ResolvedUrl { Resolved = false };
        }

        // ===== Fedora: listing + CHECKSUM from the master (authoritative), download via MirrorManager redirect =====
        private async Task<ResolvedUrl> ResolveFedora()
        {
            try
            {
                string listBase = "https://dl.fedoraproject.org/pub/fedora/linux/releases/";
                string downloadBase = "https://download.fedoraproject.org/pub/fedora/linux/releases/";
                string listing = await GetStringAsync(listBase);

                var versions = Regex.Matches(listing, @"href=""(\d{2,})/""")
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
                        string isoListing = await GetStringAsync(isoDir, 2);

                        var isoMatches = MatchesNewestFirst(isoListing, @"(?<![\w.-])(Fedora-Workstation-Live-[\w._-]+?\.iso)(?![\w.])");
                        if (isoMatches.Count == 0) continue;

                        string isoFile = isoMatches[0];
                        var csMatches = MatchesNewestFirst(isoListing, @"(?<![\w.-])(Fedora-Workstation-[\w._-]*CHECKSUM)(?![\w.])");

                        return new ResolvedUrl
                        {
                            DownloadUrl = $"{downloadBase}{ver}/Workstation/x86_64/iso/{isoFile}",
                            Sha256Url = csMatches.Count > 0 ? isoDir + csMatches[0] : "",
                            Resolved = true
                        };
                    }
                    catch { continue; }
                }
            }
            catch { }

            return new ResolvedUrl { Resolved = false };
        }

        // ===== Pop!_OS: iso.pop-os.org directory listing is blocked (403) and the intel / nvidia
        // channels have *independent* build numbers, so ask System76's build API, which also publishes the SHA256 =====
        private async Task<ResolvedUrl> ResolvePopOs()
        {
            try
            {
                // Newest LTS first: 28.04 .. 22.04 (the API answers with an empty body for versions that don't exist)
                int year = DateTime.UtcNow.Year % 100;
                for (int y = year + 1; y >= 22; y--)
                {
                    string version = $"{y}.04";
                    var intel = await QueryPopBuild(version, "intel");
                    if (intel == null) continue;
                    var nvidia = await QueryPopBuild(version, "nvidia");
                    if (nvidia == null) continue;

                    string intelDir = intel.Value.url.Substring(0, intel.Value.url.LastIndexOf('/') + 1);
                    string nvidiaDir = nvidia.Value.url.Substring(0, nvidia.Value.url.LastIndexOf('/') + 1);
                    return new ResolvedUrl
                    {
                        DownloadUrl = intel.Value.url,
                        Sha256Url = intelDir + "SHA256SUMS",
                        ExpectedHash = intel.Value.sha,
                        NvidiaDownloadUrl = nvidia.Value.url,
                        NvidiaSha256Url = nvidiaDir + "SHA256SUMS",
                        NvidiaExpectedHash = nvidia.Value.sha,
                        Resolved = true
                    };
                }
            }
            catch { }
            return new ResolvedUrl { Resolved = false };
        }

        private async Task<(string url, string sha)?> QueryPopBuild(string version, string channel)
        {
            try
            {
                string json = await GetStringAsync($"https://api.pop-os.org/builds/{version}/{channel}?arch=amd64", 2);
                if (string.IsNullOrWhiteSpace(json)) return null;
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return null;
                    string url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
                    string sha = root.TryGetProperty("sha_sum", out var s) ? s.GetString() : null;
                    if (string.IsNullOrEmpty(url) || !url.StartsWith("https://iso.pop-os.org/")) return null;
                    if (string.IsNullOrEmpty(sha) || !Regex.IsMatch(sha, "^[0-9a-fA-F]{64}$")) sha = "";
                    return (url, sha.ToLowerInvariant());
                }
            }
            catch { return null; }
        }

        // ===== Zorin: kernel.org mirror (stable) =====
        private async Task<ResolvedUrl> ResolveZorin()
        {
            try
            {
                string baseUrl = "https://mirrors.edge.kernel.org/zorinos-isos/";
                string listing = await GetStringAsync(baseUrl);

                // Find latest major version dir
                var versions = Regex.Matches(listing, @"href=""(\d+)/""")
                    .Cast<Match>()
                    .Select(m => int.Parse(m.Groups[1].Value))
                    .Distinct()
                    .OrderByDescending(v => v)
                    .ToList();

                foreach (int latestVer in versions)
                {
                    string verUrl = $"{baseUrl}{latestVer}/";
                    string verListing;
                    try { verListing = await GetStringAsync(verUrl); } catch { continue; }

                    // Find Core 64-bit ISO (highest version, then highest revision: 18.1 > 18-r3 > 18-r2 > 18)
                    var matches = MatchesNewestFirst(verListing,
                        @"(?<![\w.-])(Zorin-OS-[\d.]+-Core-64-bit(?:-r\d+)?\.iso)(?![\w.])");
                    if (matches.Count == 0) continue;

                    string sha256Url = Regex.IsMatch(verListing, @"(?<![\w.-])SHA256SUMS\.txt(?![\w.])") ? verUrl + "SHA256SUMS.txt" : "";

                    return new ResolvedUrl
                    {
                        DownloadUrl = verUrl + matches[0],
                        Sha256Url = sha256Url,
                        Resolved = true
                    };
                }
            }
            catch { }
            return new ResolvedUrl { Resolved = false };
        }

        // ===== Arch: always latest =====
        private async Task<ResolvedUrl> ResolveArch()
        {
            try
            {
                string baseUrl = "https://geo.mirror.pkgbuild.com/iso/latest/";
                string listing = await GetStringAsync(baseUrl);

                var isos = MatchesNewestFirst(listing, @"(?<![\w.-])(archlinux-\d{4}\.\d{2}\.\d{2}-x86_64\.iso)(?![\w.])");
                if (isos.Count == 0) return new ResolvedUrl { Resolved = false };

                return new ResolvedUrl
                {
                    DownloadUrl = baseUrl + isos[0],
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
                string listing = await GetStringAsync(mirrorUrl);

                // Match ISO files — naming changed over time:
                // Old: endeavouros-2021.08.27-x86_64.iso
                // New: EndeavourOS_Ganymede-Neo-2026.01.12.iso
                var isoPattern = new Regex(@"(?<![\w.-])((?:EndeavourOS|endeavouros)[\w.-]*?\.iso)(?![\w.])");
                var datePattern = new Regex(@"(\d{4})\.(\d{2})\.(\d{2})");

                // Codenames aren't alphabetically chronological (e.g. "Galileo" predates "Endeavour"),
                // so sort by the embedded release date rather than the filename itself.
                var matches = isoPattern.Matches(listing)
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Distinct()
                    .Select(f => new { File = f, Date = datePattern.Match(f) })
                    .Where(x => x.Date.Success) // undated ones are the ancient 2021/2022 releases
                    .OrderByDescending(x => x.Date.Groups[1].Value + x.Date.Groups[2].Value + x.Date.Groups[3].Value)
                    .ThenByDescending(x => x.File, StringComparer.Ordinal)
                    .Select(x => x.File)
                    .ToList();

                if (matches.Count == 0) return new ResolvedUrl { Resolved = false };

                string latestIso = matches[0];

                // Checksum sidecar is ".sha512sum" for most releases but ".sha512" for some (e.g. Titan-2026.03.06)
                string checksumName = latestIso + ".sha512sum";
                if (!listing.Contains(checksumName + "\"") && listing.Contains(latestIso + ".sha512\""))
                    checksumName = latestIso + ".sha512";

                return new ResolvedUrl
                {
                    DownloadUrl = mirrorUrl + latestIso,
                    Sha256Url = mirrorUrl + checksumName,
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
                @"(?<![\w.-])debian-[\d.]+-amd64-netinst\.iso(?![\w.])",
                @"(?<![\w.-])SHA256SUMS(?![\w.])"
            );
        }

        // ===== Trisquel: mirror.fsf.org =====
        private async Task<ResolvedUrl> ResolveTrisquel()
        {
            try
            {
                string baseUrl = "https://mirror.fsf.org/trisquel-images/";
                string listing = await GetStringAsync(baseUrl);

                // Find latest amd64 ISO (numeric version sort: 9.0 < 11.0.1 < 12.0)
                var matches = MatchesNewestFirst(listing, @"(?<![\w.-])(trisquel_[\d.]+_amd64\.iso)(?![\w.])");
                if (matches.Count == 0) return new ResolvedUrl { Resolved = false };

                string iso = matches[0];
                string sha = listing.Contains(iso + ".sha256\"") ? baseUrl + iso + ".sha256" : "";
                return new ResolvedUrl
                {
                    DownloadUrl = baseUrl + iso,
                    Sha256Url = sha,
                    Resolved = true
                };
            }
            catch { return new ResolvedUrl { Resolved = false }; }
        }

        // ===== Generic directory listing resolver =====
        // Picks the *highest-versioned* match: a dir can hold several point releases at once
        // (e.g. ubuntu-26.04-desktop-amd64.iso and ubuntu-26.04.1-desktop-amd64.iso).
        private async Task<ResolvedUrl> ResolveFromDirListing(string dirUrl, string isoRegex, string checksumFile)
        {
            try
            {
                string listing = await GetStringAsync(dirUrl);
                var isos = MatchesNewestFirst(listing, isoRegex);
                if (isos.Count == 0) return new ResolvedUrl { Resolved = false };

                string isoFile = isos[0];
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
