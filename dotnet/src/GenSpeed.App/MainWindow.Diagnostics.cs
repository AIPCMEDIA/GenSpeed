using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using GenSpeed.Core;

namespace GenSpeed.App;

public partial class MainWindow
{
    // ===== Aperçu =====
    private async void RunPreview(string mode, Target? target = null)
    {
        var t = target ?? CheckedTargets().FirstOrDefault();
        if (t == null) { Dialogs.Info(this, "GenSpeed", Loc.T("preview.nosel")); return; }
        bool onlyChanged = mode == "mod";
        if (onlyChanged && !t.Files.Any(fp => File.Exists(fp + ".speedbak")))
        { Dialogs.Info(this, "GenSpeed", Loc.T("preview.notpatched")); return; }

        ISet<string>? wanted = mode == "key" ? Preview.KeyVars : null;
        var (rows, patched, changed) = await Task.Run(() => Preview.Gather(t.InstallDir, t, wanted, onlyChanged));
        if (rows.Count == 0) { Dialogs.Info(this, "GenSpeed", Loc.T("preview.none")); return; }

        string changedStr = patched ? string.Format(Loc.T("preview.changed"), changed) : "";
        string header = $"{t.Label}\n" + string.Format(Loc.T("preview.summary"), rows.Count, changedStr);
        PreviewWindow.Show(this, string.Format(Loc.T("preview.title"), t.Label), header, rows);
    }

    // ===== Dernier replay =====
    private void OnReplay()
    {
        var path = Replay.FindLatest();
        var fp = path != null ? Replay.Read(path) : null;
        if (fp == null) { Dialogs.Info(this, "GenSpeed", Loc.T("replay.none")); return; }
        string players = fp.Players.Count > 0 ? string.Join(", ", fp.Players) : "—";
        Dialogs.Info(this, Loc.T("replay.title"), string.Format(Loc.T("replay.body"),
            string.IsNullOrEmpty(fp.Version) ? "—" : fp.Version,
            string.IsNullOrEmpty(fp.Map) ? "—" : fp.Map,
            string.IsNullOrEmpty(fp.MapCrc) ? "—" : fp.MapCrc, players));
    }

    // ===== Code LAN =====
    private async void OnComputeLanCode(object sender, RoutedEventArgs e)
    {
        var targets = CheckedTargets();
        string? dir = targets.FirstOrDefault()?.InstallDir ?? _installs.FirstOrDefault();
        if (dir == null) { Log(Loc.T("log.nogame")); return; }
        // Le code LAN est PAR install : base + mods cochés de cette install.
        var inDir = targets.Where(t => string.Equals(t.InstallDir, dir, StringComparison.OrdinalIgnoreCase)).ToList();
        Log(Loc.T("lan.computing"));
        var r = await Task.Run(() =>
        {
            var files = ModDetection.BaseInstallFiles(dir).ToList();
            foreach (var t in inDir) files.AddRange(t.Files);
            return Hashing.InstallHash(dir, files);
        });
        LanCodeLabel.Text = r.Hash;
        Log(string.Format(Loc.T("lan.done"), r.Hash, r.FileCount, r.TotalBytes / 1048576));
    }

    /// <summary>L'empreinte mismatch est PAR install (c'est l'install qu'on joue qui compte) :
    /// une seule → directe ; plusieurs → on demande laquelle.</summary>
    private string? PickInstall()
    {
        if (_installs.Count == 0) { Log(Loc.T("log.nogame")); return null; }
        if (_installs.Count == 1) return _installs[0];
        var options = _installs.Select(d => $"{InstallLabel(d)}   ·   {InstallType(d)}").ToList();
        string? pick = Dialogs.Choose(this, Loc.T("diag.pick.title"), Loc.T("diag.pick.msg"), options);
        if (pick == null) return null;
        int idx = options.IndexOf(pick);
        return idx >= 0 ? _installs[idx] : null;
    }

    // ===== Diagnostic mismatch =====
    private async void OnDiagExport()
    {
        string? dir = PickInstall();
        if (dir == null) return;
        var modTargets = _targets.Where(t => t.Type == TargetType.Gib
                                          && string.Equals(t.InstallDir, dir, StringComparison.OrdinalIgnoreCase)).ToList();
        var fp = await Task.Run(() => Diagnostics.Build(dir, modTargets));
        var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = "GenSpeed-diagnostic.json" };
        if (dlg.ShowDialog() == true)
        {
            File.WriteAllText(dlg.FileName, Diagnostics.ExportJson(fp));
            Log(string.Format(Loc.T("diag.exported"), dlg.FileName));
        }
    }

    private async void OnDiagCompare()
    {
        string? dir = PickInstall();
        if (dir == null) return;
        var dlg = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dlg.ShowDialog() != true) return;
        string json = File.ReadAllText(dlg.FileName);
        if (!Diagnostics.IsSyncFingerprint(json)) { Dialogs.Info(this, "GenSpeed", Loc.T("diag.badfile")); return; }
        var other = Diagnostics.Parse(json);
        var modTargets = _targets.Where(t => t.Type == TargetType.Gib
                                          && string.Equals(t.InstallDir, dir, StringComparison.OrdinalIgnoreCase)).ToList();
        var mine = await Task.Run(() => Diagnostics.Build(dir, modTargets));
        DiagnosticWindow.Show(this, Diagnostics.Diff(mine, other));
    }
}
