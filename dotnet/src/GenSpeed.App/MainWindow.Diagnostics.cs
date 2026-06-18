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
    // Bouton : recalcule + journalise. (Le calcul auto à l'ouverture / à chaque modif passe par RefreshLanCode.)
    private async void OnComputeLanCode(object sender, RoutedEventArgs e) => await RefreshLanCode(log: true);

    /// <summary>Met à jour le code LAN affiché (base + mods cochés de la 1re install cochée). Appelé AUTOMATIQUEMENT
    /// à l'ouverture et à chaque modif (sélection, patch). Mis en CACHE par signature de fichiers → instantané si
    /// rien n'a changé. Affiche « — » si aucun mod coché, « … » pendant un calcul à froid.</summary>
    private async Task RefreshLanCode(bool log = false)
    {
        var targets = CheckedTargets();
        if (targets.Count == 0) { LanCodeLabel.Text = "—"; return; }
        string? dir = targets[0].InstallDir ?? _installs.FirstOrDefault();
        if (dir == null) { LanCodeLabel.Text = "—"; if (log) Log(Loc.T("log.nogame")); return; }
        var inDir = targets.Where(t => string.Equals(t.InstallDir, dir, StringComparison.OrdinalIgnoreCase)).ToList();

        var files = ModDetection.BaseInstallFiles(dir).ToList();
        foreach (var t in inDir) files.AddRange(LanFilesFor(t));
        // Clé de cache combinée (base + mods cochés), insensible à la casse via Normalize.
        string key = dir + "::LAN::" + string.Join("+", inDir.Select(t => t.Label).OrderBy(x => x, StringComparer.Ordinal));
        var sig = BuildSig(files);
        if (_config.HashCache.TryGetValue(key, out var ce) && SigEqual(ce.Sig, sig))
        {
            LanCodeLabel.Text = ce.Hash;                          // cache valide → instantané
            if (log) Log(string.Format(Loc.T("lan.done"), ce.Hash, ce.Sig.Count, 0));
            return;
        }
        LanCodeLabel.Text = "…";
        if (log) Log(Loc.T("lan.computing"));
        var r = await Task.Run(() => Hashing.InstallHash(dir, files));
        _config.HashCache[key] = new HashCacheEntry { Hash = r.Hash, Sig = sig };
        _hashCacheDirty = true;
        LanCodeLabel.Text = r.Hash;
        if (log) Log(string.Format(Loc.T("lan.done"), r.Hash, r.FileCount, r.TotalBytes / 1048576));
    }

    /// <summary>Vérification des fichiers (statut known-good neutre + lien VirusTotal) sur toutes les installs.</summary>
    private void OnDiagVerify() => SecurityWindow.Show(this, _installs);

    /// <summary>L'empreinte mismatch est PAR install (c'est l'install qu'on joue qui compte) :
    /// une seule → directe ; plusieurs → on demande laquelle.</summary>
    private string? PickInstall(System.Windows.Window? owner = null)
    {
        owner ??= this;
        if (_installs.Count == 0) { Log(Loc.T("log.nogame")); return null; }
        if (_installs.Count == 1) return _installs[0];
        var options = _installs.Select(d => $"{InstallLabel(d)}   ·   {InstallType(d)}").ToList();
        string? pick = Dialogs.Choose(owner, Loc.T("diag.pick.title"), Loc.T("diag.pick.msg"), options);
        if (pick == null) return null;
        int idx = options.IndexOf(pick);
        return idx >= 0 ? _installs[idx] : null;
    }

    // ===== Diagnostic mismatch =====
    private void OnDiagExport() { _ = DiagExportFrom(this); }

    /// <summary>Export de l'empreinte mismatch. owner = fenêtre des pop-ups (l'assistant peut la posséder).</summary>
    private async System.Threading.Tasks.Task DiagExportFrom(System.Windows.Window owner)
    {
        string? dir = PickInstall(owner);
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
