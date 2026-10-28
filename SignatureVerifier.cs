using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace LinuxSimplify.Services
{
    public enum SignatureResult
    {
        Valid,       // signature verified with one of the distro's pinned keys
        Invalid,     // signature present but does NOT match the data (tampered / corrupt) -> must block
        UnknownKey,  // signed with a key we don't have pinned (e.g. a new release key) -> not checked
        Unavailable  // no signature / could not fetch or parse it -> not checked
    }

    /// <summary>How a distro signs its downloads.</summary>
    public enum SignatureKind
    {
        None,
        DetachedOverChecksumFile, // e.g. SHA256SUMS + SHA256SUMS.gpg (Ubuntu, Debian, Mint, Pop!_OS)
        ClearSignedChecksumFile,  // the checksum file itself is PGP clear-signed (Fedora CHECKSUM)
        DetachedOverIso           // e.g. archlinux-*.iso.sig (Arch, EndeavourOS)
    }

    public sealed class SignatureSpec
    {
        public SignatureKind Kind = SignatureKind.None;
        public string SignatureUrl = "";
        public string KeyFile = "";     // embedded resource "keys.<KeyFile>.asc"
        public string Signer = "";      // shown to the user, e.g. "Ubuntu"
    }

    /// <summary>
    /// OpenPGP signature checks for distro downloads, against signing keys that are pinned in the app
    /// (keys/*.asc, embedded). Fingerprints were cross-checked with each distro's official site:
    ///   Ubuntu/Lubuntu 843938DF228D22F7B3742BC0D94AA3F0EFE21092  (ubuntu.com)
    ///   Debian         DF9B9C49EAA9298432589D76DA87E80D6294BE9B  (debian.org/CD/verify)
    ///   Linux Mint     27DEB15644C6B3CF3BD7D291300F846BA25BAE09  (Linux Mint installation guide)
    ///   Pop!_OS        63C46DF0140D738961429F4E204DD8AEC33A7AFF  (github.com/pop-os/iso)
    ///   Fedora 43-46   fedoraproject.org/fedora.gpg              (fedoraproject.org/security)
    ///   Arch Linux     3E80CA1A8B89F69CBA57D98A76A5EF9054449A5C  (archlinux.org download + WKD)
    ///   EndeavourOS    8F43FC374CD4CEEA19CEE323E3D8752ACDF595A1  (github.com/endeavouros-team/keyring)
    /// Zorin OS publishes no signatures; Trisquel's key could not be confirmed from an official source,
    /// so both stay checksum-only.
    /// </summary>
    public static class SignatureVerifier
    {
        /// <summary>Signature setup for a distro (by display name), or Kind=None.</summary>
        public static SignatureSpec For(string distroName, string downloadUrl, string checksumUrl)
        {
            bool hasSums = !string.IsNullOrEmpty(checksumUrl);
            switch (distroName)
            {
                case "Ubuntu":
                case "Lubuntu":
                    return hasSums ? Detached(checksumUrl + ".gpg", "ubuntu", distroName) : None;
                case "Debian":
                    return hasSums ? Detached(checksumUrl + ".sign", "debian", "Debian") : None;
                case "Linux Mint":
                    return hasSums ? Detached(checksumUrl + ".gpg", "linuxmint", "Linux Mint") : None;
                case "Pop!_OS":
                    return hasSums ? Detached(checksumUrl + ".gpg", "popos", "System76") : None;
                case "Fedora":
                    return hasSums ? new SignatureSpec { Kind = SignatureKind.ClearSignedChecksumFile, KeyFile = "fedora", Signer = "Fedora" } : None;
                case "Arch Linux":
                    return new SignatureSpec { Kind = SignatureKind.DetachedOverIso, SignatureUrl = downloadUrl + ".sig", KeyFile = "archlinux", Signer = "Arch Linux" };
                case "EndeavourOS":
                    return new SignatureSpec { Kind = SignatureKind.DetachedOverIso, SignatureUrl = downloadUrl + ".sig", KeyFile = "endeavouros", Signer = "EndeavourOS" };
                default:
                    return None;
            }
        }

        private static SignatureSpec None => new SignatureSpec();

        private static SignatureSpec Detached(string sigUrl, string key, string signer) =>
            new SignatureSpec { Kind = SignatureKind.DetachedOverChecksumFile, SignatureUrl = sigUrl, KeyFile = key, Signer = signer };

        // ---------------------------------------------------------------- keys

        private static readonly Dictionary<string, PgpPublicKeyRingBundle> KeyCache = new Dictionary<string, PgpPublicKeyRingBundle>();

        private static PgpPublicKeyRingBundle LoadKeys(string keyFile)
        {
            lock (KeyCache)
            {
                if (KeyCache.TryGetValue(keyFile, out var cached)) return cached;
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"keys.{keyFile}.asc"))
                {
                    if (s == null) return null;
                    var bundle = new PgpPublicKeyRingBundle(PgpUtilities.GetDecoderStream(s));
                    KeyCache[keyFile] = bundle;
                    return bundle;
                }
            }
        }

        // ---------------------------------------------------------------- detached signatures

        /// <summary>Verifies a detached signature (binary or armored) over <paramref name="data"/>.</summary>
        public static SignatureResult VerifyDetached(byte[] signature, Stream data, string keyFile)
        {
            try
            {
                var keys = LoadKeys(keyFile);
                if (keys == null || signature == null || signature.Length == 0) return SignatureResult.Unavailable;
                PgpSignatureList sigs = ReadSignatures(new MemoryStream(signature));
                if (sigs == null || sigs.Count == 0) return SignatureResult.Unavailable;

                // Usually one signature; if several, any valid one from a pinned key is enough
                var candidates = new List<(PgpSignature sig, PgpPublicKey key)>();
                for (int i = 0; i < sigs.Count; i++)
                {
                    var key = keys.GetPublicKey(sigs[i].KeyId);
                    if (key != null && !key.HasRevocation()) candidates.Add((sigs[i], key));
                }
                if (candidates.Count == 0) return SignatureResult.UnknownKey;

                foreach (var (sig, key) in candidates) sig.InitVerify(key);
                var buf = new byte[1024 * 1024];
                int n;
                while ((n = data.Read(buf, 0, buf.Length)) > 0)
                    foreach (var (sig, _) in candidates) sig.Update(buf, 0, n);

                return candidates.Any(c => c.sig.Verify() && IsSignedWhileKeyValid(c.sig, c.key))
                    ? SignatureResult.Valid : SignatureResult.Invalid;
            }
            catch (PgpException) { return SignatureResult.Unavailable; }
            catch (IOException) { return SignatureResult.Unavailable; }
        }

        private static PgpSignatureList ReadSignatures(Stream s)
        {
            var factory = new PgpObjectFactory(PgpUtilities.GetDecoderStream(s));
            object o;
            while ((o = factory.NextPgpObject()) != null)
            {
                if (o is PgpSignatureList list) return list;
                if (o is PgpCompressedData cd) { factory = new PgpObjectFactory(cd.GetDataStream()); continue; }
            }
            return null;
        }

        /// <summary>The signature must have been made while the key was valid (not after it expired).</summary>
        private static bool IsSignedWhileKeyValid(PgpSignature sig, PgpPublicKey key)
        {
            long validSeconds = key.GetValidSeconds();
            if (validSeconds <= 0) return true; // no expiry
            return sig.CreationTime <= key.CreationTime.AddSeconds(validSeconds);
        }

        // ---------------------------------------------------------------- clear-signed text

        /// <summary>
        /// Verifies a PGP clear-signed document. On success <paramref name="signedText"/> is ONLY the signed text
        /// (dash-unescaped), so nothing outside the signature can be smuggled into the checksum list.
        /// </summary>
        public static SignatureResult VerifyClearSigned(byte[] document, string keyFile, out string signedText)
        {
            signedText = null;
            try
            {
                var keys = LoadKeys(keyFile);
                if (keys == null || document == null) return SignatureResult.Unavailable;

                using (var aIn = new ArmoredInputStream(new MemoryStream(document)))
                {
                    // Cleartext part: lines exactly as signed (ArmoredInputStream removes the dash-escaping)
                    var text = new MemoryStream();
                    int ch;
                    while ((ch = aIn.ReadByte()) >= 0 && aIn.IsClearText()) text.WriteByte((byte)ch);
                    if (ch < 0) return SignatureResult.Unavailable;  // no signature block
                    byte[] clear = text.ToArray();

                    PgpSignatureList sigs = new PgpObjectFactory(aIn).NextPgpObject() as PgpSignatureList;
                    if (sigs == null || sigs.Count == 0) return SignatureResult.Unavailable;
                    PgpSignature sig = sigs[0];
                    PgpPublicKey key = keys.GetPublicKey(sig.KeyId);
                    if (key == null || key.HasRevocation()) return SignatureResult.UnknownKey;

                    sig.InitVerify(key);
                    // Canonical form (RFC 4880 7.1): trailing whitespace stripped, lines joined with CRLF,
                    // no line ending after the last line.
                    var lines = SplitLines(clear);
                    for (int i = 0; i < lines.Count; i++)
                    {
                        byte[] l = TrimTrailingWhitespace(lines[i]);
                        if (i > 0) sig.Update(new byte[] { (byte)'\r', (byte)'\n' }, 0, 2);
                        sig.Update(l, 0, l.Length);
                    }
                    if (!sig.Verify() || !IsSignedWhileKeyValid(sig, key)) return SignatureResult.Invalid;

                    signedText = Encoding.UTF8.GetString(clear);
                    return SignatureResult.Valid;
                }
            }
            catch (PgpException) { return SignatureResult.Unavailable; }
            catch (IOException) { return SignatureResult.Unavailable; }
        }

        /// <summary>Splits on \n (dropping a preceding \r); the line ending that ends the cleartext is not part of it.</summary>
        private static List<byte[]> SplitLines(byte[] data)
        {
            int len = data.Length;
            // ArmoredInputStream hands back the cleartext including the newline before "-----BEGIN PGP SIGNATURE"
            if (len > 0 && data[len - 1] == '\n') { len--; if (len > 0 && data[len - 1] == '\r') len--; }
            var lines = new List<byte[]>();
            int start = 0;
            for (int i = 0; i <= len; i++)
            {
                if (i == len || data[i] == '\n')
                {
                    int end = i;
                    if (end > start && data[end - 1] == '\r') end--;
                    lines.Add(data.Skip(start).Take(end - start).ToArray());
                    start = i + 1;
                }
            }
            return lines;
        }

        private static byte[] TrimTrailingWhitespace(byte[] line)
        {
            int end = line.Length;
            while (end > 0 && (line[end - 1] == ' ' || line[end - 1] == '\t' || line[end - 1] == '\r')) end--;
            return end == line.Length ? line : line.Take(end).ToArray();
        }
    }
}
