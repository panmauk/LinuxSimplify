using System;
using System.Collections.Generic;
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

        /// <summary>
        /// Downloads one or more ISO parts and joins them, in order, into a
        /// single ISO file at <paramref name="destinationPath"/>. A single-part
        /// list is just a normal download. Parts are streamed straight into the
        /// final file (no temporary duplicates), so only one ISO's worth of disk
        /// space is needed.
        /// </summary>
        public async Task<bool> DownloadPartsAsync(IReadOnlyList<string> partUrls, string destinationPath, IProgress<DownloadState> progressReporter, CancellationToken cancellationToken)
        {
            var state = new DownloadState { CurrentAction = "Checking disk space..." };
            progressReporter?.Report(state);

            if (partUrls == null || partUrls.Count == 0)
            {
                state.ErrorMessage = "Nothing to download";
                progressReporter?.Report(state);
                return false;
            }

            // ---- Pre-flight: work out the total download size ----
            long grandTotal = 0;
            bool haveSizes = true;
            foreach (var url in partUrls)
            {
                try
                {
                    using var head = new HttpRequestMessage(HttpMethod.Head, url);
                    using var hr = await httpClient.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (hr.IsSuccessStatusCode && hr.Content.Headers.ContentLength.HasValue)
                        grandTotal += hr.Content.Headers.ContentLength.Value;
                    else { haveSizes = false; break; }
                }
                catch (OperationCanceledException) { state.ErrorMessage = "Download was cancelled"; return false; }
                catch { haveSizes = false; break; }
            }

            // ---- Disk space ----
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(new FileInfo(destinationPath).FullName));
                long needed = (haveSizes ? grandTotal : 3L * 1024 * 1024 * 1024) + 512L * 1024 * 1024;
                if (drive.AvailableFreeSpace < needed)
                {
                    state.ErrorMessage = $"Need about {FormatBytes(needed)} free, but {drive.Name} only has {FormatBytes(drive.AvailableFreeSpace)}";
                    state.CurrentAction = "Insufficient disk space";
                    progressReporter?.Report(state);
                    return false;
                }
                state.LastSuccessfulAction = $"{FormatBytes(drive.AvailableFreeSpace)} available";
            }
            catch { }

            try
            {
                using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1048576, true))
                {
                    long cumulative = 0;
                    for (int i = 0; i < partUrls.Count; i++)
                    {
                        // Where this part begins, so a retry can rewind cleanly
                        // instead of appending duplicate bytes.
                        long partStartPos = fileStream.Position;
                        long cumulativeAtStart = cumulative;
                        bool partOk = false;

                        for (int retry = 0; retry < 3 && !partOk; retry++)
                        {
                            // Reset to the start of this part before each attempt.
                            fileStream.SetLength(partStartPos);
                            fileStream.Position = partStartPos;
                            cumulative = cumulativeAtStart;

                            try
                            {
                                string label = partUrls.Count > 1
                                    ? $"Downloading part {i + 1} of {partUrls.Count}…"
                                    : "Downloading…";
                                state.CurrentAction = retry == 0 ? label : $"{label} (retry {retry + 1})";
                                progressReporter?.Report(state);

                                using var request = new HttpRequestMessage(HttpMethod.Get, partUrls[i]);
                                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                                if (!response.IsSuccessStatusCode)
                                {
                                    state.ErrorMessage = $"Server returned {(int)response.StatusCode}";
                                    progressReporter?.Report(state);
                                    if (retry < 2) { await Task.Delay(3000, cancellationToken); continue; }
                                    return false;
                                }

                                long partTotal = response.Content.Headers.ContentLength ?? -1;

                                using (var contentStream = await response.Content.ReadAsStreamAsync())
                                {
                                    var buffer = new byte[1048576];
                                    long partRead = 0;
                                    int bytesRead;
                                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                                    {
                                        await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                                        partRead += bytesRead;
                                        cumulative += bytesRead;

                                        if (haveSizes && grandTotal > 0)
                                            state.Percentage = (int)((cumulative * 100) / grandTotal);
                                        else if (partTotal > 0)
                                            state.Percentage = (int)(((i * 100.0) + (partRead * 100.0 / partTotal)) / partUrls.Count);

                                        state.BytesDownloaded = cumulative;
                                        state.TotalBytes = haveSizes ? grandTotal : -1;
                                        string sizeText = haveSizes
                                            ? $"{FormatBytes(cumulative)} / {FormatBytes(grandTotal)}"
                                            : FormatBytes(cumulative);
                                        state.CurrentAction = $"{label} {sizeText}";
                                        progressReporter?.Report(state);
                                    }
                                    await fileStream.FlushAsync(cancellationToken);
                                }
                                partOk = true;
                            }
                            catch (OperationCanceledException) { state.ErrorMessage = "Download was cancelled"; return false; }
                            catch (IOException ex) { state.ErrorMessage = $"Couldn't write to disk: {ex.Message}"; return false; }
                            catch (Exception ex)
                            {
                                state.ErrorMessage = ex.Message;
                                progressReporter?.Report(state);
                                if (retry < 2) { await Task.Delay(3000, cancellationToken); continue; }
                                return false;
                            }
                        }

                        if (!partOk) return false;
                    }
                }

                var fi = new FileInfo(destinationPath);
                if (fi.Exists && fi.Length > 0)
                {
                    state.LastSuccessfulAction = $"Downloaded {FormatBytes(fi.Length)}";
                    return true;
                }
                return false;
            }
            catch (OperationCanceledException) { state.ErrorMessage = "Download was cancelled"; return false; }
            catch (IOException ex) { state.ErrorMessage = $"Couldn't write to disk: {ex.Message}"; return false; }
            catch (Exception ex) { state.ErrorMessage = ex.Message; return false; }
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
