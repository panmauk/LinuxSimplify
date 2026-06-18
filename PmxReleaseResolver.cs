using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace LinuxSimplify.Services
{
    /// <summary>
    /// Resolves the latest PMX OS release straight from GitHub.
    ///
    /// PMX OS lives at panmox.org and ships its releases on GitHub
    /// (github.com/panmauk/PMX-OS). Because the ISO is larger than GitHub's
    /// 2 GB per-file limit, it is split into several parts that have to be
    /// joined back together after downloading.
    ///
    /// This resolver always asks GitHub for the *latest* release, then works
    /// out — purely from the asset names — which files are the ISO parts (in
    /// order) and which one is the checksum. That way new releases just work,
    /// even if the version number or the exact file names change.
    /// </summary>
    public class PmxReleaseResolver
    {
        // GitHub repo that hosts the releases. The project itself is panmox.org.
        private const string ReleasesApi = "https://api.github.com/repos/panmauk/PMX-OS/releases/latest";

        private static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        static PmxReleaseResolver()
        {
            // GitHub's API rejects requests without a User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("LinuxSimplify-PMX/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        public class IsoPart
        {
            public string Name { get; set; } = "";
            public string Url { get; set; } = "";
        }

        public class PmxRelease
        {
            public string Version { get; set; } = "";              // e.g. "v1"
            public string DisplayName { get; set; } = "PMX OS";    // release title
            public List<IsoPart> Parts { get; set; } = new List<IsoPart>();
            public string IsoFileName { get; set; } = "PMX-OS-amd64.iso"; // reassembled file name
            public string ChecksumUrl { get; set; } = "";
            public string ChecksumType { get; set; } = "sha256";
            public bool Resolved { get; set; }
            public string Error { get; set; } = "";
        }

        /// <summary>
        /// Fetch the latest release and figure out its ISO parts + checksum.
        /// Never throws — failures come back as Resolved = false with an Error.
        /// </summary>
        public async Task<PmxRelease> ResolveLatestAsync()
        {
            var result = new PmxRelease();
            try
            {
                string json = await http.GetStringAsync(ReleasesApi);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("tag_name", out var tag))
                    result.Version = tag.GetString() ?? "";
                if (root.TryGetProperty("name", out var nm) && !string.IsNullOrWhiteSpace(nm.GetString()))
                    result.DisplayName = nm.GetString();

                if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                {
                    result.Error = "The latest release has no downloadable files";
                    return result;
                }

                // Every uploaded file in the release.
                var all = new List<IsoPart>();
                foreach (var a in assets.EnumerateArray())
                {
                    string name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    string url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                        all.Add(new IsoPart { Name = name, Url = url });
                }

                // ---- Checksum file ----
                var checksum = all.FirstOrDefault(x => IsChecksumFile(x.Name));
                if (checksum != null)
                {
                    result.ChecksumUrl = checksum.Url;
                    string lower = checksum.Name.ToLowerInvariant();
                    result.ChecksumType = lower.Contains("sha512") ? "sha512" : "sha256";
                }

                // ---- ISO parts ----
                // Anything that mentions ".iso" and isn't the checksum is an ISO piece.
                var isoCandidates = all
                    .Where(x => x.Name.IndexOf(".iso", StringComparison.OrdinalIgnoreCase) >= 0
                                && !IsChecksumFile(x.Name))
                    .ToList();

                // If a single whole ".iso" is published, prefer it (no joining needed).
                var whole = isoCandidates
                    .Where(x => x.Name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                List<IsoPart> parts;
                if (whole.Count == 1)
                {
                    parts = whole;
                }
                else
                {
                    // Split parts — order matters. Ordinal sort by name handles
                    // every common split convention: part-aa/part-ab, .001/.002,
                    // .00/.01, etc.
                    parts = isoCandidates
                        .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                if (parts.Count == 0)
                {
                    result.Error = "Couldn't find an ISO in the latest release";
                    return result;
                }

                result.Parts = parts;
                result.IsoFileName = DeriveIsoName(parts[0].Name);
                result.Resolved = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.Resolved = false;
            }
            return result;
        }

        private static bool IsChecksumFile(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("sha256sums") || n.Contains("sha512sums")
                || n.EndsWith(".sha256") || n.EndsWith(".sha256sum")
                || n.EndsWith(".sha512") || n.EndsWith(".sha512sum")
                || n.EndsWith(".sum")
                || (n.Contains("checksum") && n.EndsWith(".txt"));
        }

        // "PMX-OS-1-amd64.iso.part-aa" -> "PMX-OS-1-amd64.iso".
        // A name that already ends in ".iso" comes back unchanged.
        private static string DeriveIsoName(string partName)
        {
            int idx = partName.IndexOf(".iso", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return partName.Substring(0, idx + 4); // keep the ".iso"
            return partName;
        }
    }
}
