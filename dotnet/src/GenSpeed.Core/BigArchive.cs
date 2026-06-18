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

    /// <summary>Lit UNIQUEMENT les entrées .ini d'une archive BIG, en streaming (table + seek sur chaque .ini) —
    /// sans charger toute l'archive en mémoire. Indispensable pour les .pak de fork (centaines de Mo) dont on ne
    /// veut que les INI. Lève BigFileException si l'entête est invalide.</summary>
    public static List<BigEntry> ReadIniEntries(string path)
    {
        var result = new List<BigEntry>();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

        var hdr = new byte[16];
        if (ReadFull(fs, hdr, 16) < 16) throw new BigFileCorruptedException($"Header BIG trop court: {path}");
        if (!(hdr[0] == (byte)'B' && hdr[1] == (byte)'I' && hdr[2] == (byte)'G' && hdr[3] == (byte)'F'))
            throw new BigFileCorruptedException($"Signature BIG invalide: {path}");
        uint numFiles = BinaryPrimitives.ReadUInt32BigEndian(hdr.AsSpan(8, 4));
        if (numFiles > 200000) throw new BigFileCorruptedException($"Nombre de fichiers suspect: {numFiles}");

        // 1) Lire la table (offset, size, name) — séquentiel, léger.
        var table = new List<(uint Off, uint Size, string Name)>();
        var ent = new byte[8];
        var nameBuf = new List<byte>(64);
        for (uint i = 0; i < numFiles; i++)
        {
            if (ReadFull(fs, ent, 8) < 8) break;
            uint off = BinaryPrimitives.ReadUInt32BigEndian(ent.AsSpan(0, 4));
            uint size = BinaryPrimitives.ReadUInt32BigEndian(ent.AsSpan(4, 4));
            nameBuf.Clear();
            int b;
            while ((b = fs.ReadByte()) > 0) nameBuf.Add((byte)b);
            if (b < 0) break;
            string name = Latin1.GetString(nameBuf.ToArray());
            if (name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) table.Add((off, size, name));
        }

        // 2) Lire les données de chaque .ini par seek (seuls quelques Mo au total).
        long len = fs.Length;
        foreach (var (off, size, name) in table)
        {
            if (off > len || (long)off + size > len) continue;
            fs.Seek(off, SeekOrigin.Begin);
            var data = new byte[size];
            if (ReadFull(fs, data, (int)size) < size) continue;
            result.Add(new BigEntry { Name = name, Data = data });
        }
        return result;
    }

    private static int ReadFull(Stream s, byte[] buf, int count)
    {
        int r = 0;
        while (r < count) { int n = s.Read(buf, r, count - r); if (n <= 0) break; r += n; }
        return r;
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
