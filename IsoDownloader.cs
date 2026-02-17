using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LinuxSimplify.Models;

namespace LinuxSimplify.Services
{
    public class IsoDownloader
    {
        private readonly HttpClient httpClient;

        public IsoDownloader()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromHours(4) };
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
            httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
        }

        public async Task<bool> DownloadIsoAsync(string url, string destinationPath, IProgress<DownloadState> progressReporter, CancellationToken cancellationToken)
        {
            var state = new DownloadState { CurrentAction = "Checking disk space..." };
            progressReporter?.Report(state);

            try
            {
                var fileInfo = new FileInfo(destinationPath);
                var drive = new DriveInfo(Path.GetPathRoot(fileInfo.FullName));

                if (drive.AvailableFreeSpace < 6L * 1024 * 1024 * 1024)
                {
                    state.ErrorMessage = $"Need about 6 GB free, but {drive.Name} only has {FormatBytes(drive.AvailableFreeSpace)}";
                    state.CurrentAction = "Insufficient disk space";
                    progressReporter?.Report(state);
                    return false;
                }
                state.LastSuccessfulAction = $"{FormatBytes(drive.AvailableFreeSpace)} available";
            }
            catch { }

            for (int retry = 0; retry < 3; retry++)
            {
                try
                {
                    state.CurrentAction = retry == 0 ? "Connecting..." : $"Connecting... (retry {retry + 1})";
                    progressReporter?.Report(state);

                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            state.ErrorMessage = $"Server returned {(int)response.StatusCode}";
                            state.CurrentAction = "Server error";
                            progressReporter?.Report(state);
                            if (retry < 2) { await Task.Delay(3000, cancellationToken); continue; }
                            return false;
                        }

                        state.TotalBytes = response.Content.Headers.ContentLength ?? -1;
                        state.CurrentAction = "Downloading...";
                        state.LastSuccessfulAction = "Connected";
                        progressReporter?.Report(state);

                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, true))
                        {
                            var buffer = new byte[1048576];
                            long totalRead = 0;
                            int bytesRead;

                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                                totalRead += bytesRead;
                                state.BytesDownloaded = totalRead;
                                state.Percentage = state.TotalBytes > 0 ? (int)((totalRead * 100) / state.TotalBytes) : 0;
                                state.CurrentAction = state.TotalBytes > 0
                                    ? $"Downloading... {FormatBytes(totalRead)} / {FormatBytes(state.TotalBytes)}"
                                    : $"Downloading... {FormatBytes(totalRead)}";
                                progressReporter?.Report(state);
                            }
                            await fileStream.FlushAsync(cancellationToken);
                        }
                    }

                    var fi = new FileInfo(destinationPath);
                    if (fi.Exists && fi.Length > 0)
                    {
                        state.LastSuccessfulAction = $"Downloaded {FormatBytes(fi.Length)}";
                        return true;
                    }
                    if (retry < 2) continue;
                    return false;
                }
                catch (OperationCanceledException) { state.ErrorMessage = "Download was cancelled"; return false; }
                catch (IOException ex) { state.ErrorMessage = $"Couldn't write to disk: {ex.Message}"; return false; }
                catch (Exception ex) { state.ErrorMessage = ex.Message; if (retry < 2) { await Task.Delay(3000, cancellationToken); continue; } return false; }
            }
            return false;
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        public async Task<string> DownloadChecksumFileAsync(string url)
        {
            try { return await httpClient.GetStringAsync(url); } catch { return null; }
        }

        public async Task<string> DownloadSha256FileAsync(string url)
        {
            return await DownloadChecksumFileAsync(url);
        }

        public async Task<string> ComputeChecksumAsync(string filePath, string algorithm, IProgress<string> statusReporter)
        {
            return await Task.Run(() =>
            {
                try
                {
                    statusReporter?.Report("Verifying file integrity...");
                    System.Security.Cryptography.HashAlgorithm hasher;
                    if (algorithm == "sha512")
                        hasher = System.Security.Cryptography.SHA512.Create();
                    else
                        hasher = SHA256.Create();

                    using (hasher)
                    using (var stream = File.OpenRead(filePath))
                        return BitConverter.ToString(hasher.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }
                catch { return null; }
            });
        }

        public async Task<string> ComputeSha256Async(string filePath, IProgress<string> statusReporter)
        {
            return await ComputeChecksumAsync(filePath, "sha256", statusReporter);
        }

        public bool VerifySha256(string computedHash, string checksumFileContent, string isoFileName)
        {
            if (string.IsNullOrWhiteSpace(checksumFileContent) || string.IsNullOrWhiteSpace(computedHash)) return false;
            computedHash = computedHash.ToLowerInvariant();
            isoFileName = Path.GetFileName(isoFileName).ToLowerInvariant();

            var lines = checksumFileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("#"))
                .ToList();

            foreach (var line in lines)
            {
                var parts = line.Trim().Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var hash = parts[0].ToLowerInvariant();
                    var filename = parts[parts.Length - 1].ToLowerInvariant();
                    // Match by filename if possible
                    if (filename.Contains(isoFileName) || isoFileName.Contains(filename))
                        return hash == computedHash;
                }
            }

            // If only one hash line and no filename matched, just compare the hash directly
            // (common with single-file sha512sum files like EndeavourOS)
            if (lines.Count == 1)
            {
                var parts = lines[0].Trim().Split(new[] { ' ', '\t', '*' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1)
                    return parts[0].ToLowerInvariant() == computedHash;
            }

            // Last resort: does the checksum file contain our hash anywhere?
            return checksumFileContent.Contains(computedHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}
