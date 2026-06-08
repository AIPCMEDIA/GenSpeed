using System.Buffers.Binary;
using System.Text;

namespace GenSpeed.Core;

/// <summary>Erreur générique de lecture/écriture d'archive BIG.</summary>
public class BigFileException : Exception
{
    public BigFileException(string message) : base(message) { }
}

/// <summary>Archive corrompue / signature invalide.</summary>
public sealed class BigFileCorruptedException : BigFileException
{
    public BigFileCorruptedException(string message) : base(message) { }
}

/// <summary>Une entrée (fichier interne) d'une archive BIG.</summary>
public sealed class BigEntry
{
    public required string Name { get; set; }
    public required byte[] Data { get; set; }
}

/// <summary>
/// Lecture/écriture des archives BIG (format SAGE, big-endian).
/// Port fidèle de core.read_big / core.write_big (Python) — l'écriture
/// repacke les données de façon CONTIGUË (pas d'alignement), exactement
/// comme la version Python, pour garantir l'égalité octet-pour-octet.
/// </summary>
public static class BigArchive
{
    // latin-1 = ISO-8859-1 : 1 octet ↔ 1 code point (comme Python 'latin-1').
    private static readonly Encoding Latin1 = Encoding.Latin1;

    /// <summary>Lit une archive BIG et retourne ses entrées (nom + données).</summary>
    public static List<BigEntry> Read(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Fichier BIG introuvable: {path}");

        byte[] raw;
        try { raw = File.ReadAllBytes(path); }
        catch (IOException e) { throw new BigFileException($"Erreur lecture fichier {path}: {e.Message}"); }

        if (raw.Length < 16)
            throw new BigFileCorruptedException($"Header BIG trop court: {path}");
        if (!(raw[0] == (byte)'B' && raw[1] == (byte)'I' && raw[2] == (byte)'G' && raw[3] == (byte)'F'))
            throw new BigFileCorruptedException($"Signature BIG invalide: {path}");

        // Octets 4-7 = champ "taille" : sémantique variable selon les variantes
        // BIG, on l'ignore (comme la version Python).
        uint numFiles = BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(8, 4));
        uint headerSize = BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(12, 4));

        if (numFiles > 100000)
            throw new BigFileCorruptedException($"Nombre de fichiers suspect: {numFiles}");
        if (headerSize > raw.Length)
            throw new BigFileCorruptedException($"Taille header invalide: {headerSize}");

        int pos = 16;
        var entries = new List<BigEntry>((int)numFiles);
        for (uint i = 0; i < numFiles; i++)
        {
            if (pos + 8 > raw.Length)
                throw new BigFileCorruptedException($"Entrée {i}: header tronqué");

            uint offset = BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(pos, 4));
            uint size = BinaryPrimitives.ReadUInt32BigEndian(raw.AsSpan(pos + 4, 4));
            pos += 8;

            int end = Array.IndexOf(raw, (byte)0, pos);
            if (end < 0)
                throw new BigFileCorruptedException($"Entrée {i}: nom de fichier non terminé");
            if (end - pos > 255)
                throw new BigFileCorruptedException($"Entrée {i}: nom de fichier trop long");

            string name = Latin1.GetString(raw, pos, end - pos);
            pos = end + 1;

            if (offset > raw.Length)
                throw new BigFileCorruptedException($"Fichier {name}: offset hors limite");
            if ((long)offset + size > raw.Length)
                throw new BigFileCorruptedException($"Fichier {name}: taille hors limite");

            var data = new byte[size];
            Array.Copy(raw, offset, data, 0, size);
            entries.Add(new BigEntry { Name = name, Data = data });
        }
        return entries;
    }

    /// <summary>Écrit une archive BIG (repack contigu, ordre des entrées conservé).</summary>
    public static void Write(string path, IReadOnlyList<BigEntry> items)
    {
        int headerSize = 16;
        foreach (var it in items)
            headerSize += 8 + Latin1.GetByteCount(it.Name) + 1;

        long currentOffset = headerSize;
        var offsets = new uint[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            offsets[i] = (uint)currentOffset;
            currentOffset += items[i].Data.Length;
        }

        try
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            Span<byte> u32 = stackalloc byte[4];

            fs.Write("BIGF"u8);
            BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)currentOffset); fs.Write(u32);
            BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)items.Count);   fs.Write(u32);
            BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)headerSize);    fs.Write(u32);

            for (int i = 0; i < items.Count; i++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(u32, offsets[i]);            fs.Write(u32);
                BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)items[i].Data.Length); fs.Write(u32);
                fs.Write(Latin1.GetBytes(items[i].Name));
                fs.WriteByte(0);
            }
            foreach (var it in items)
                fs.Write(it.Data);
        }
        catch (IOException e)
        {
            throw new BigFileException($"Erreur écriture fichier {path}: {e.Message}");
        }
    }
}
