using System.Security.Cryptography;
using System.Text;

namespace GenSpeed.Core;

/// <summary>Empreintes SHA-256 — port fidèle de core.file_sha256 / install_hash.</summary>
public static class Hashing
{
    /// <summary>SHA-256 d'un fichier en hex minuscules, ou null si illisible.</summary>
    public static string? FileSha256(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>SHA-256 d'un buffer en hex minuscules.</summary>
    public static string Sha256Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public readonly record struct InstallHashResult(string Hash, int FileCount, long TotalBytes);

    /// <summary>
    /// Empreinte d'installation : tri ordinal des chemins, puis pour chaque fichier
    /// hache le chemin relatif (séparateurs '/') en UTF-8 + le contenu. Retourne
    /// les 8 premiers hex en MAJUSCULES. Équivalent exact de core.install_hash.
    /// </summary>
    public static InstallHashResult InstallHash(string gameDir, IEnumerable<string> files)
    {
        var sorted = files.OrderBy(p => p, StringComparer.Ordinal).ToList();
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        var buffer = new byte[1 << 20];

        foreach (var fp in sorted)
        {
            string rel = Path.GetRelativePath(gameDir, fp).Replace('\\', '/');
            sha.AppendData(Encoding.UTF8.GetBytes(rel));
            try
            {
                using var fs = File.OpenRead(fp);
                int read;
                while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    total += read;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        string hex = Convert.ToHexString(sha.GetHashAndReset()); // MAJUSCULES
        return new InstallHashResult(hex[..8], sorted.Count, total);
    }
}
