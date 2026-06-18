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
    // ===== Appliquer / Annuler (élévation UAC) =====
    private async void OnApply(object sender, RoutedEventArgs e) => await RunPatch("apply");
    private async void OnRestore(object sender, RoutedEventArgs e) => await RunPatch("restore");

    private async Task RunPatch(string mode, Window? owner = null)
    {
        owner ??= this;   // fenêtre propriétaire des pop-ups (assistant si déclenché depuis le hub, sinon MainWindow)
        var rows = _rows.Where(r => r.Sel && r.Target != null).ToList();
        if (rows.Count == 0) { Log(Loc.T("log.nosel")); return; }

        // Patcher/dépatcher = action « je veux que ce soit enregistré » → on réactive la sauvegarde au cas où
        // elle aurait été coupée (Suppressed resté true après un wipe incluant la config GenSpeed). Sinon le
        // réglage vitesse/caméra ne serait pas persisté → affiché « patché (réglage inconnu) » au redémarrage.
        ConfigStore.Suppressed = false;

        // Dépatch : ne garder que les mods réellement patchés (présence d'un .speedbak).
        if (mode == "restore")
        {
            // Patché = présence d'un .speedbak (archives/INI) OU, pour un fork (.pak), des loose override encore là.
            var patchedRows = rows.Where(r => r.Target!.Files.Any(fp => File.Exists(fp + ".speedbak"))
                || (r.Target!.Type == TargetType.Pak && r.PatchedFiles.Keys.Any(File.Exists))).ToList();
            if (patchedRows.Count == 0)
            {
                Dialogs.Info(owner, "GenSpeed", Loc.T("restore.nothing"));
                Log(Loc.T("restore.nothing"));
                return;
            }
            rows = patchedRows;   // on ignore les cibles non patchées (pas d'UAC inutile, pas de faux « restauré »)
        }

        if (mode == "apply" &&
            !Dialogs.ConfirmApply(owner, rows.Select(r => $"{FriendlyLabel(r.Mod)}  ({InstallLabel(r.InstallDir)})"),
                                  SpeedCam.BuildChangeSummary(),
                                  string.Join(" · ", rows.Select(r => r.InstallDir).Distinct(StringComparer.OrdinalIgnoreCase))))
            return;

        // Multi-installs : un sous-job par install, UNE seule élévation UAC pour le tout.
        var job = new PatchJob
        {
            Mode = mode, Factors = SpeedCam.ReadFactors(), Cam = SpeedCam.ReadCam(),
            ResultPath = Path.Combine(Path.GetTempPath(), $"genspeed_result_{Guid.NewGuid():N}.json"),
        };
        foreach (var g in rows.GroupBy(r => r.InstallDir, StringComparer.OrdinalIgnoreCase))
            job.Installs.Add(new InstallPatch
            {
                GameDir = g.Key,
                ModsDir = (!string.IsNullOrEmpty(_config.ModsDir) &&
                           string.Equals(Path.GetDirectoryName(_config.ModsDir.TrimEnd('\\', '/')), g.Key, StringComparison.OrdinalIgnoreCase))
                          ? _config.ModsDir : null,
                Labels = g.Select(r => r.Mod).ToList(),
                PrevHashes = g.ToDictionary(r => r.Mod, r => r.PatchedFiles),
            });
        string jobPath = Path.Combine(Path.GetTempPath(), $"genspeed_job_{Guid.NewGuid():N}.json");
        File.WriteAllText(jobPath, JsonSerializer.Serialize(job));

        ApplyBtn.IsEnabled = RestoreBtn.IsEnabled = false;
        Log(Loc.T(mode == "apply" ? "log.applying" : "log.restoring"));
        if (mode == "apply")
            Log($"   ⚡ {SpeedCam.SpeedLabelText}  ·  📷 {SpeedCam.CamLabelText}  →  {string.Join(", ", rows.Select(r => FriendlyLabel(r.Mod)))}");
        try
        {
            int code = await RunElevated(mode == "apply" ? "--apply" : "--restore", jobPath);
            if (code < 0) { Log(Loc.T("log.uaccancel")); return; }
            PatchResult? res = File.Exists(job.ResultPath)
                ? JsonSerializer.Deserialize<PatchResult>(File.ReadAllText(job.ResultPath)) : null;
            if (res == null) { Log("⚠ " + Loc.T("log.noresult")); return; }
            foreach (var err in res.Errors) Log("⚠ " + err);
            bool camApplied = mode == "apply" &&
                SpeedCam.ReadCam().Any(kv => kv.Key != "CameraYaw" && !string.IsNullOrEmpty(kv.Value));
            foreach (var r in rows)
            {
                if (!res.Patched.TryGetValue(r.StateKey, out var pf)) continue;
                r.PatchedFiles = pf;
                if (mode == "apply")
                {
                    r.Patched = r.Target!.Type == TargetType.Pak ? pf.Count.ToString() : $"{pf.Count}/{r.Target!.ArchiveCount}";
                    r.Vitesse = SpeedCam.SpeedLabelText;
                    r.Camera = camApplied ? (SpeedCam.CamIdx > 0 ? SpeedCam.CamLabelText : Loc.T("cam.custom")) : Loc.T("orig");
                    _config.PatchedState[r.StateKey] = new PatchedInfo { Speed = r.Vitesse, Camera = r.Camera, Files = pf };
                    Log($"   • {FriendlyLabel(r.Mod)} : {pf.Count}/{r.Target!.ArchiveCount} " + Loc.T("log.filespatched"));
                }
                else
                {
                    r.Patched = "—"; r.Vitesse = Loc.T("orig"); r.Camera = Loc.T("orig");
                    _config.PatchedState.Remove(r.StateKey);
                    Log($"   • {FriendlyLabel(r.Mod)} : " + Loc.T("log.restoredmod"));
                }
            }
            ConfigStore.Save(_config);   // persiste l'état patché (vitesse/caméra) pour le prochain démarrage
            Log(Loc.T(mode == "apply" ? "log.applied" : "log.restored"));
            foreach (var r in rows)
                r.Code = await CachedLanCode(r.Target!);   // recalcule + met à jour le cache (fichiers changés par le patch)
            if (_hashCacheDirty) { ConfigStore.Save(_config); _hashCacheDirty = false; }

            if (mode == "apply")
            {
                // Code LAN affiché : celui de l'install du PREMIER mod coché (celle qu'on va lancer).
                string firstDir = rows[0].InstallDir;
                var inDir = rows.Where(r => string.Equals(r.InstallDir, firstDir, StringComparison.OrdinalIgnoreCase)).ToList();
                var lan = await Task.Run(() =>
                {
                    var files = ModDetection.BaseInstallFiles(firstDir).ToList();
                    foreach (var r in inDir) files.AddRange(LanFilesFor(r.Target!));
                    return Hashing.InstallHash(firstDir, files);
                });
                LanCodeLabel.Text = lan.Hash;
                Dialogs.ApplyResult(owner, Loc.T("result.body"), lan.Hash, LaunchGenLauncher);
            }
        }
        finally
        {
            ApplyBtn.IsEnabled = RestoreBtn.IsEnabled = true;
            try { File.Delete(jobPath); File.Delete(job.ResultPath); } catch { }
        }
    }

    private static Task<int> RunElevated(string verbArg, string jobPath) => Task.Run(() =>
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = Environment.ProcessPath!, UseShellExecute = true, Verb = "runas" };
            psi.ArgumentList.Add(verbArg);
            psi.ArgumentList.Add(jobPath);
            var p = Process.Start(psi);
            if (p == null) return -1;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    });

    /// <summary>HUB vitesse/caméra (assistant) avec des valeurs BRUTES (depuis l'instance du composant de l'assistant) :
    /// on les pousse dans le composant principal (que RunPatch consomme), puis on patche les lignes COCHÉES dans le
    /// tableau de l'assistant (l'utilisateur choisit lui-même les cibles — plus de « tout cocher » automatique).</summary>
    internal void ApplySpeedCamRawAll(Window owner, IReadOnlyDictionary<string, double> factors,
        IReadOnlyDictionary<string, string?> cam)
    {
        SpeedCam.SetFactors(factors);
        SpeedCam.SetCam(cam);
        _ = RunPatch("apply", owner);
    }

    /// <summary>Construit un tableau des jeux/mods (cases à cocher + vitesse/caméra) que l'ASSISTANT insère dans sa page
    /// vitesse/caméra. Vue distincte mais MÊMES données (_rows) que la fenêtre principale : cocher ici coche partout.
    /// MainWindow le construit car le type de ligne (ModRow) lui est privé.</summary>
    internal FrameworkElement BuildAssistantModTable()
    {
        RefreshModsIfChanged();   // _rows à jour (un fork/mod a pu être installé depuis le démarrage)
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            GridLinesVisibility = DataGridGridLinesVisibility.None,
            BorderThickness = new Thickness(0),
            Background = (Brush)FindResource("bgInput"),
            Foreground = (Brush)FindResource("fg"),
            Margin = new Thickness(0),
        };
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);

        // Colonne case à cocher : vraie CheckBox dans un template → toggle au CLIC SIMPLE (le DataGridCheckBoxColumn
        // natif exige un double-clic). En-tête = CheckBox « tout cocher / tout décocher » (comme le mode avancé).
        var selCol = new DataGridTemplateColumn { Width = 40, CanUserSort = false, CanUserResize = false };
        var cbCell = new FrameworkElementFactory(typeof(CheckBox));
        cbCell.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding(nameof(ModRow.Sel))
            { Mode = System.Windows.Data.BindingMode.TwoWay, UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged });
        cbCell.SetValue(CheckBox.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cbCell.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center);
        selCol.CellTemplate = new DataTemplate { VisualTree = cbCell };
        var headerCb = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.T("sel.all.tip") };
        headerCb.Click += (s, _) => { bool check = (s as CheckBox)?.IsChecked == true; foreach (var r in _rows) r.Sel = check; };
        selCol.Header = headerCb;
        grid.Columns.Add(selCol);
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("col.mod"), Binding = new System.Windows.Data.Binding(nameof(ModRow.Display)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("col.speed"), Binding = new System.Windows.Data.Binding(nameof(ModRow.Vitesse)),
            Width = 90, IsReadOnly = true });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("col.camera"), Binding = new System.Windows.Data.Binding(nameof(ModRow.Camera)),
            Width = 100, IsReadOnly = true });

        // Vue groupée par installation — distincte de celle de ModGrid mais sur la même ObservableCollection.
        var view = new System.Windows.Data.ListCollectionView(_rows);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription(nameof(ModRow.InstallName)));
        grid.ItemsSource = view;

        // En-tête de groupe (nom de l'installation), couleur accent.
        var header = new FrameworkElementFactory(typeof(TextBlock));
        header.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
        header.SetValue(TextBlock.ForegroundProperty, FindResource("accent"));
        header.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        header.SetValue(TextBlock.MarginProperty, new Thickness(4, 6, 0, 2));
        grid.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = header } });

        return grid;
    }
}
