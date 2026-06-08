using System.Text;
using System.Text.RegularExpressions;

namespace GenSpeed.Core;

public sealed record ReplayInfo(string Version, string Map, string MapCrc, List<string> Players, DateTime Mtime);

/// <summary>Lecture de l'empreinte de la dernière partie (.rep). Port de core.read_replay_fingerprint.</summary>
public static class Replay
{
    private static IEnumerable<string> ReplayDirs()
    {
        string up = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(up, "Documents", "Command and Conquer Generals Zero Hour Data", "Replays");
        yield return Path.Combine(up, "OneDrive", "Documents", "Command and Conquer Generals Zero Hour Data", "Replays");
    }

    public static string? FindLatest()
    {
        string? best = null;
        DateTime bestT = DateTime.MinValue;
        foreach (var dir in ReplayDirs())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.rep"))
            {
                var t = File.GetLastWriteTime(f);
                if (t > bestT) { bestT = t; best = f; }
            }
        }
        return best;
    }

    public static ReplayInfo? Read(string path)
    {
        byte[] data;
        try
        {
            using var fs = File.OpenRead(path);
            data = new byte[8192];
            int n = fs.Read(data, 0, data.Length);
            if (n < data.Length) Array.Resize(ref data, n);
        }
        catch { return null; }

        if (data.Length < 6 || data[0] != 'G' || data[1] != 'E' || data[2] != 'N'
            || data[3] != 'R' || data[4] != 'E' || data[5] != 'P')
            return null;

        string u = Encoding.Unicode.GetString(data);   // utf-16-le
        string version = Regex.Matches(u, @"[\x20-\x7e]{3,}")
            .Select(m => m.Value.Trim())
            .FirstOrDefault(s => Regex.IsMatch(s, @"V\d")) ?? "";

        string a = Encoding.Latin1.GetString(data);
        string info = Regex.Matches(a, @"[\x20-\x7e]{5,}")
            .Select(m => m.Value)
            .FirstOrDefault(s => s.Contains("M=") && s.Contains("MC=")) ?? "";

        string map = "", crc = "";
        var players = new List<string>();
        foreach (var part in info.Split(';'))
        {
            if (part.StartsWith("M=")) map = part[2..];
            else if (part.StartsWith("MC=")) crc = part[3..];
            else if (part.StartsWith("S="))
                foreach (var pl in part[2..].Split(':'))
                    if (pl.Length > 0 && pl != "X") players.Add(pl.Split(',')[0]);
        }
        return new ReplayInfo(version, map, crc, players, File.GetLastWriteTime(path));
    }
}
