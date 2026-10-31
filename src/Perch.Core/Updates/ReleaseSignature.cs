using System.Security.Cryptography;

namespace Perch.Updates;

/// <summary>
/// Each release carries checksums.txt (sha256sum's output for every file) and
/// checksums.txt.sig, an ECDSA P-256 signature of it made by the release workflow with a
/// key that lives only in the repository's secrets. Perch carries the public half, so a
/// file is trusted only when its hash appears in a list signed by that key: replacing a
/// release's files and hashes on GitHub is not enough to get something installed.
/// </summary>
public static class ReleaseSignature
{
    public const string ChecksumsName = "checksums.txt";
    public const string SignatureName = ChecksumsName + ".sig";

    /// <summary>packaging/release-signing-key.pub, built into Perch.Core.</summary>
    static readonly Lazy<string> PublicKey = new(() =>
    {
        using var stream = typeof(ReleaseSignature).Assembly.GetManifestResourceStream("release-signing-key.pub")
                           ?? throw new InvalidOperationException("The release signing key is missing from this build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>Whether <paramref name="signature"/> (DER, as openssl writes it) is the release key's signature of <paramref name="data"/>.</summary>
    public static bool Verify(byte[] data, byte[] signature) => Verify(data, signature, PublicKey.Value);

    internal static bool Verify(byte[] data, byte[] signature, string publicKeyPem)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// The hash checksums.txt lists for <paramref name="fileName"/>. Lines are sha256sum's
    /// "&lt;hex&gt;  &lt;name&gt;", or "&lt;hex&gt; *&lt;name&gt;" in its binary mode.
    /// </summary>
    public static string? FindChecksum(string checksums, string fileName)
    {
        foreach (var line in checksums.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;

            var (hash, name) = (parts[0], parts[1].TrimStart('*'));
            if (string.Equals(name, fileName, StringComparison.Ordinal) && hash.Length == 64 && hash.All(Uri.IsHexDigit))
                return hash.ToLowerInvariant();
        }

        return null;
    }
}
