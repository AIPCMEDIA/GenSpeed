using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Blocs VITESSE + CAMÉRA réutilisables (curseur + facteur + réglage fin + CRUD), construits en code.
/// Une instance dans la fenêtre principale (le tableau) ET une dans l'assistant (« Vitesse/caméra → Tout »).
/// Expose ReadFactors/ReadCam (ce que RunPatch consomme) + les libellés (pour l'étiquetage du patch).
/// Porté à l'identique depuis l'ancien MainWindow.Presets.cs.</summary>
public sealed class SpeedCameraPanel : UserControl
{
    private Brush B(string key) => (Brush)FindResource(key);
    private Brush Fg => B("fg");
    private Brush Dim => B("dim");
    private static Style? St(string key) => Application.Current.TryFindResource(key) as Style;

    private GenConfig _config = null!;
    private Window _owner = null!;
    private List<SpeedPreset> Speeds => _config.SpeedPresets;

    // (clé cat, symbole, couleur pastille)
    private static readonly (string Key, string Sym, string Dot)[] Cats =
    {
        ("deplacement","×","#2ECC71"), ("projectiles","×","#2ECC71"), ("visee","×","#2ECC71"),
        ("construction","÷","#2ECC71"), ("tir","÷","#2ECC71"), ("pouvoirs","÷","#2ECC71"),
        ("deploiement","÷","#2ECC71"), ("economie_collecte","÷","#2ECC71"), ("economie_gain","×","#E67E22"),
        ("detection","×","#E74C3C"), ("soin","×","#2ECC71"), ("merite","×","#E67E22"),
    };
    private static readonly (string Var, string HintKey)[] CamVars =
    {
        ("CameraPitch", "cam.hint.pitch"), ("CameraHeight", "cam.hint.h"),
        ("MaxCameraHeight", "cam.hint.max"), ("MinCameraHeight", "cam.hint.min"),
    };
    private static readonly string[] CamAllVars =
        { "CameraPitch", "CameraYaw", "CameraHeight", "MaxCameraHeight", "MinCameraHeight", "DrawEntireTerrain" };
    private static readonly string[] CamOrderDefault =
        { "Cam haute", "Cam max", "Cam eloignee", "Vue satellite" };

    private readonly List<(string Key, TextBlock Tb)> _catLabels = new();
    private readonly List<(string Key, TextBlock Tb)> _camHints = new();
    private readonly Dictionary<string, TextBox> _catBoxes = new();
    private readonly Dictionary<string, Control> _camControls = new();
    private List<string> _camNames = new();
    private int _camIdx;
    private bool _suppressFactor;

    // Contrôles (ex-nommés du XAML).
    private readonly Slider SpeedSlider = new() { Minimum = 0, Maximum = 3, IsSnapToTickEnabled = true, TickFrequency = 1, Value = 2, Width = 180, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock SpeedLabel = new() { FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly TextBox FactorBox = new() { Width = 52, Text = "2.0", VerticalContentAlignment = VerticalAlignment.Center };
    private readonly UniformGrid CatGrid = new() { Columns = 2 };
    private readonly Slider CamSlider = new() { Minimum = 0, Maximum = 4, IsSnapToTickEnabled = true, TickFrequency = 1, Value = 0, Width = 180, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock CamLabel = new() { FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Grid CamGrid = new();

    public string SpeedLabelText => SpeedLabel.Text;
    public string CamLabelText => CamLabel.Text;
    public int CamIdx => _camIdx;

    public SpeedCameraPanel()
    {
        // Cartes EMPILÉES verticalement (vitesse au-dessus de caméra) : pensé pour être posé à droite, le tableau
        // occupant la gauche — même disposition en mode avancé et dans l'assistant.
        var stack = new StackPanel();
        var speed = BuildSpeedCard(); speed.Margin = new Thickness(0, 0, 0, 6); stack.Children.Add(speed);
        var cam = BuildCameraCard(); cam.Margin = new Thickness(0); stack.Children.Add(cam);
        Content = stack;
    }

    /// <summary>À appeler une fois la config disponible : remplit les grilles + cale les curseurs.</summary>
    public void Init(GenConfig config, Window owner)
    {
        _config = config; _owner = owner;
        BuildCategoryGrid();
        BuildCameraGrid();
        SetupSpeedSlider();
        SetupCamSlider();
        RefreshTexts();
    }

    // ===== Construction des cartes =====
    private Border Card(string headKey, FrameworkElement content)
    {
        var dp = new DockPanel();
        var head = new Border { Background = B("bgFrame2"), Padding = new Thickness(8, 4, 8, 4) };
        head.Child = new TextBlock { Text = Loc.T(headKey), Foreground = B("accent"), FontWeight = FontWeights.Bold, FontFamily = new FontFamily("Consolas") };
        DockPanel.SetDock(head, Dock.Top); dp.Children.Add(head);
        dp.Children.Add(content);
        var b = new Border { Child = dp };
        if (St("Card") is { } s) b.Style = s; else { b.Background = B("bgFrame"); b.BorderBrush = B("border"); b.BorderThickness = new Thickness(1); b.CornerRadius = new CornerRadius(4); }
        return b;
    }

    private Border BuildSpeedCard()
    {
        var sp = new StackPanel { Margin = new Thickness(10, 6, 10, 6) };
        var row1 = new StackPanel { Orientation = Orientation.Horizontal };
        row1.Children.Add(new TextBlock { Text = Loc.T("speed.global"), Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        SpeedSlider.ValueChanged += SpeedSlider_Changed;
        SpeedLabel.Foreground = B("accent");
        row1.Children.Add(SpeedSlider); row1.Children.Add(SpeedLabel);
        sp.Children.Add(row1);
        sp.Children.Add(new Border { Height = 1, Background = B("border"), Margin = new Thickness(0, 6, 0, 6) });
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row2.Children.Add(new TextBlock { Text = Loc.T("speed.factor"), Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        FactorBox.TextChanged += FactorBox_Changed;
        row2.Children.Add(FactorBox);
        row2.Children.Add(CrudBtn("💾", "crud.save", OnSpeedSave, 8));
        row2.Children.Add(CrudBtn("＋", "crud.new", OnSpeedNew));
        row2.Children.Add(CrudBtn("✏", "crud.rename", OnSpeedRename));
        row2.Children.Add(CrudBtn("🗑", "crud.delete", OnSpeedDelete));
        sp.Children.Add(row2);
        sp.Children.Add(new TextBlock { Text = Loc.T("speed.fine"), Foreground = Dim, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
        sp.Children.Add(CatGrid);
        return Card("card.speed", sp);
    }

    private Border BuildCameraCard()
    {
        var sp = new StackPanel { Margin = new Thickness(10, 6, 10, 6) };
        var row1 = new StackPanel { Orientation = Orientation.Horizontal };
        row1.Children.Add(new TextBlock { Text = Loc.T("cam.preset"), Foreground = Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        CamSlider.ValueChanged += CamSlider_Changed;
        CamLabel.Foreground = B("accent"); CamLabel.Text = Loc.T("cam.default");
        row1.Children.Add(CamSlider); row1.Children.Add(CamLabel);
        sp.Children.Add(row1);
        sp.Children.Add(new Border { Height = 1, Background = B("border"), Margin = new Thickness(0, 6, 0, 6) });
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row2.Children.Add(CrudBtn("💾", "crud.save", OnCamSave));
        row2.Children.Add(CrudBtn("＋", "crud.new", OnCamNew));
        row2.Children.Add(CrudBtn("✏", "crud.rename", OnCamRename));
        row2.Children.Add(CrudBtn("🗑", "crud.delete", OnCamDelete));
        sp.Children.Add(row2);
        sp.Children.Add(new TextBlock { Text = Loc.T("cam.fine"), Foreground = Dim, FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
        sp.Children.Add(CamGrid);
        return Card("card.camera", sp);
    }

    private Button CrudBtn(string glyph, string tipKey, RoutedEventHandler onClick, double left = 2)
    {
        var b = new Button { Content = glyph, ToolTip = Loc.T(tipKey), Margin = new Thickness(left, 0, 2, 0), Padding = new Thickness(8, 4, 8, 4) };
        b.Click += onClick;
        return b;
    }

    // ===== Grilles (port de BuildCategoryGrid / BuildCameraGrid) =====
    private void BuildCategoryGrid()
    {
        CatGrid.Children.Clear(); _catBoxes.Clear(); _catLabels.Clear();
        foreach (var c in Cats)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 14, 3) };
            sp.Children.Add(new TextBlock { Text = "●", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Dot)) });
            var lbl = new TextBlock { Width = 100, VerticalAlignment = VerticalAlignment.Center, Foreground = Fg, TextTrimming = TextTrimming.CharacterEllipsis };
            _catLabels.Add((c.Key, lbl)); sp.Children.Add(lbl);
            sp.Children.Add(new TextBlock { Text = c.Sym, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = Dim });
            var box = new TextBox { Text = c.Key == "detection" ? "1" : "2.0", Width = 46, VerticalContentAlignment = VerticalAlignment.Center };
            _catBoxes[c.Key] = box; sp.Children.Add(box);
            CatGrid.Children.Add(sp);
        }
    }

    private void BuildCameraGrid()
    {
        CamGrid.Children.Clear(); CamGrid.ColumnDefinitions.Clear(); CamGrid.RowDefinitions.Clear(); _camControls.Clear(); _camHints.Clear();
        CamGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        CamGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        CamGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int r = 0;
        foreach (var (var, hintKey) in CamVars)
        {
            CamGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = new TextBlock { Text = var, Foreground = Dim, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lbl, r); Grid.SetColumn(lbl, 0); CamGrid.Children.Add(lbl);
            var tb = new TextBox { Width = 70, Margin = new Thickness(0, 3, 0, 3) };
            _camControls[var] = tb; Grid.SetRow(tb, r); Grid.SetColumn(tb, 1); CamGrid.Children.Add(tb);
            var hint = new TextBlock { Foreground = Dim, FontSize = 11, Margin = new Thickness(6, 3, 0, 3), VerticalAlignment = VerticalAlignment.Center };
            _camHints.Add((hintKey, hint)); Grid.SetRow(hint, r); Grid.SetColumn(hint, 2); CamGrid.Children.Add(hint);
            r++;
        }
        CamGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var dl = new TextBlock { Text = "DrawEntireTerrain", Foreground = Dim, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(dl, r); Grid.SetColumn(dl, 0); CamGrid.Children.Add(dl);
        var cb = new ComboBox { Width = 70, Margin = new Thickness(0, 3, 0, 3) };
        cb.Items.Add(""); cb.Items.Add("Yes"); cb.Items.Add("No"); cb.SelectedIndex = 0;
        _camControls["DrawEntireTerrain"] = cb; Grid.SetRow(cb, r); Grid.SetColumn(cb, 1); CamGrid.Children.Add(cb);
        var dh = new TextBlock { Foreground = Dim, FontSize = 11, Margin = new Thickness(6, 3, 0, 3), VerticalAlignment = VerticalAlignment.Center };
        _camHints.Add(("cam.hint.terrain", dh)); Grid.SetRow(dh, r); Grid.SetColumn(dh, 2); CamGrid.Children.Add(dh);
    }

    /// <summary>Met à jour les libellés/tooltips dépendant de la langue (catégories + caméra).</summary>
    public void RefreshTexts()
    {
        foreach (var (key, tb) in _catLabels) { tb.Text = Loc.T("cat." + key); tb.ToolTip = Loc.T("tip.cat." + key); }
        foreach (var (key, box) in _catBoxes) box.ToolTip = Loc.T("tip.cat." + key);
        foreach (var (key, tb) in _camHints) tb.Text = Loc.T(key);
        foreach (var kv in _camControls) kv.Value.ToolTip = Loc.T("tip.cam." + kv.Key);
        if (_config != null)
        {
            UpdateSpeedLabel();
            CamLabel.Text = _camIdx == 0 ? Loc.T("cam.default") : (_camIdx < _camNames.Count ? _camNames[_camIdx] : "");
        }
    }

    /// <summary>Recale les curseurs après un changement de presets (ex. import de config).</summary>
    public void ReloadPresets() { SetupSpeedSlider(); SetupCamSlider(); }

    /// <summary>Réinitialise vitesse (Énervé par défaut) + caméra (Cam haute) — utilisé par « Réinitialiser ».</summary>
    public void ResetToDefaults()
    {
        SpeedSlider.Value = Math.Min(2, Speeds.Count - 1);
        ApplySpeedPreset(SpeedIdx);
        int camInit = _camNames.IndexOf("Cam haute"); if (camInit < 0) camInit = _camNames.Count > 1 ? 1 : 0;
        _camIdx = camInit; CamLabel.Text = camInit == 0 ? Loc.T("cam.default") : _camNames[camInit];
        CamSlider.Value = camInit; ApplyCamPreset(camInit);
    }

    // ===== Vitesse (port de Presets.cs) =====
    private static string Fmt(double v) => Math.Round(v, 2).ToString(CultureInfo.InvariantCulture);

    private void SetupSpeedSlider()
    {
        SpeedSlider.Maximum = Math.Max(1, Speeds.Count - 1);
        int init = Math.Min(2, Speeds.Count - 1);
        if ((int)Math.Round(SpeedSlider.Value) == init) { UpdateSpeedLabel(); ApplySpeedPreset(init); }
        else SpeedSlider.Value = init;
    }

    private void SpeedSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_config == null) return;
        int i = Math.Clamp((int)Math.Round(SpeedSlider.Value), 0, Speeds.Count - 1);
        UpdateSpeedLabel(); ApplySpeedPreset(i);
    }

    private void UpdateSpeedLabel()
    {
        if (_config == null) return;
        int i = Math.Clamp((int)Math.Round(SpeedSlider.Value), 0, Speeds.Count - 1);
        var p = Speeds[i]; SpeedLabel.Text = $"{p.Name}  (×{p.Factor:g})";
    }

    private void ApplySpeedPreset(int i)
    {
        if (_catBoxes.Count == 0) return;
        var p = Speeds[i];
        _suppressFactor = true; FactorBox.Text = Fmt(p.Factor); _suppressFactor = false;
        foreach (var (key, _, _) in Cats)
        {
            double v = p.Cats != null && p.Cats.TryGetValue(key, out var cv) ? cv : key == "detection" ? 1.0 : p.Factor;
            _catBoxes[key].Text = Fmt(v);
        }
    }

    private void FactorBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressFactor || _catBoxes.Count == 0) return;
        if (!double.TryParse(FactorBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return;
        foreach (var (key, box) in _catBoxes) if (key != "detection") box.Text = Fmt(f);
    }

    private int SpeedIdx => Math.Clamp((int)Math.Round(SpeedSlider.Value), 0, Speeds.Count - 1);

    private void OnSpeedSave(object sender, RoutedEventArgs e)
    {
        var p = Speeds[SpeedIdx];
        if (p.Locked) { Dialogs.Info(_owner, "GenSpeed", string.Format(Loc.T("msg.locked"), p.Name)); return; }
        if (!double.TryParse(FactorBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return;
        var cats = ReadCatOverrides(f);
        p.Factor = Math.Round(f, 2); p.Cats = cats.Count > 0 ? cats : null;
        ConfigStore.Save(_config); UpdateSpeedLabel();
    }

    private Dictionary<string, double> ReadCatOverrides(double f)
    {
        var cats = new Dictionary<string, double>();
        foreach (var (key, _, _) in Cats)
        {
            if (key == "detection") continue;
            if (double.TryParse(_catBoxes[key].Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && Math.Abs(v - f) > 0.001)
                cats[key] = Math.Round(v, 2);
        }
        return cats;
    }

    private void OnSpeedNew(object sender, RoutedEventArgs e)
    {
        string? name = Dialogs.Prompt(_owner, Loc.T("dlg.newspeed"), Loc.T("dlg.name"));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Speeds.Any(s => s.Name == name)) { Dialogs.Info(_owner, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        double.TryParse(FactorBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f);
        var cats = ReadCatOverrides(f);
        Speeds.Add(new SpeedPreset { Name = name, Locked = false, Factor = Math.Round(f, 2), Cats = cats.Count > 0 ? cats : null });
        ConfigStore.Save(_config); SpeedSlider.Maximum = Speeds.Count - 1; SpeedSlider.Value = Speeds.Count - 1;
    }

    private void OnSpeedRename(object sender, RoutedEventArgs e)
    {
        var p = Speeds[SpeedIdx];
        if (p.Locked) { Dialogs.Info(_owner, "GenSpeed", string.Format(Loc.T("msg.locked"), p.Name)); return; }
        string? name = Dialogs.Prompt(_owner, Loc.T("dlg.rename"), Loc.T("dlg.newname"), p.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Speeds.Any(s => s.Name == name && s != p)) { Dialogs.Info(_owner, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        p.Name = name; ConfigStore.Save(_config); UpdateSpeedLabel();
    }

    private void OnSpeedDelete(object sender, RoutedEventArgs e)
    {
        int i = SpeedIdx; var p = Speeds[i];
        if (p.Locked) { Dialogs.Info(_owner, "GenSpeed", string.Format(Loc.T("msg.locked"), p.Name)); return; }
        if (Speeds.Count <= 1) { Dialogs.Info(_owner, "GenSpeed", Loc.T("msg.minone")); return; }
        if (!Dialogs.Confirm(_owner, Loc.T("crud.delete"), string.Format(Loc.T("msg.delconfirm"), p.Name))) return;
        Speeds.RemoveAt(i); ConfigStore.Save(_config); SpeedSlider.Maximum = Speeds.Count - 1;
        int ni = Math.Min(i, Speeds.Count - 1);
        if ((int)Math.Round(SpeedSlider.Value) == ni) { UpdateSpeedLabel(); ApplySpeedPreset(ni); } else SpeedSlider.Value = ni;
    }

    // ===== Caméra (port de Presets.cs) =====
    private List<string> CamOrder()
    {
        var order = CamOrderDefault.Where(n => _config.CameraPresets.ContainsKey(n)).ToList();
        var extras = _config.CameraPresets.Keys.Where(n => !order.Contains(n) && n != "Reset camera");
        return new List<string> { "__default__" }.Concat(order).Concat(extras).ToList();
    }

    private void SetupCamSlider()
    {
        _camNames = CamOrder();
        CamSlider.Maximum = Math.Max(1, _camNames.Count - 1);
        int init = _camNames.IndexOf("Cam haute"); if (init < 0) init = _camNames.Count > 1 ? 1 : 0;
        _camIdx = init;
        if ((int)Math.Round(CamSlider.Value) == init) { CamLabel.Text = init == 0 ? Loc.T("cam.default") : _camNames[init]; ApplyCamPreset(init); }
        else CamSlider.Value = init;
    }

    private void CamSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_config == null || _camControls.Count == 0) return;
        _camIdx = Math.Clamp((int)Math.Round(CamSlider.Value), 0, _camNames.Count - 1);
        CamLabel.Text = _camIdx == 0 ? Loc.T("cam.default") : _camNames[_camIdx];
        ApplyCamPreset(_camIdx);
    }

    private void ApplyCamPreset(int i)
    {
        if (_camControls.Count == 0) return;
        Dictionary<string, string>? vals = (i > 0 && _config.CameraPresets.TryGetValue(_camNames[i], out var v)) ? v : null;
        foreach (var (var, _) in CamVars)
            if (_camControls[var] is TextBox tb) tb.Text = vals != null && vals.TryGetValue(var, out var vv) ? vv : "";
        if (_camControls["DrawEntireTerrain"] is ComboBox cb)
            cb.SelectedItem = vals != null && vals.TryGetValue("DrawEntireTerrain", out var dv) ? dv : "";
    }

    private Dictionary<string, string> ReadCamRaw()
    {
        var d = new Dictionary<string, string>();
        foreach (var var in CamAllVars)
        {
            if (var == "CameraYaw") { d[var] = ""; continue; }
            if (_camControls.TryGetValue(var, out var c))
                d[var] = c is ComboBox cb ? cb.SelectedItem as string ?? "" : ((TextBox)c).Text.Trim();
        }
        return d;
    }

    private void OnCamSave(object sender, RoutedEventArgs e)
    {
        if (_camIdx == 0) { Dialogs.Info(_owner, "GenSpeed", Loc.T("msg.camlocked")); return; }
        _config.CameraPresets[_camNames[_camIdx]] = ReadCamRaw(); ConfigStore.Save(_config);
    }

    private void OnCamNew(object sender, RoutedEventArgs e)
    {
        string? name = Dialogs.Prompt(_owner, Loc.T("dlg.newcam"), Loc.T("dlg.name"));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_config.CameraPresets.ContainsKey(name)) { Dialogs.Info(_owner, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        _config.CameraPresets[name] = ReadCamRaw(); ConfigStore.Save(_config); SetupCamSlider();
        int ni = _camNames.IndexOf(name); if (ni > 0) CamSlider.Value = ni;
    }

    private void OnCamRename(object sender, RoutedEventArgs e)
    {
        if (_camIdx == 0) { Dialogs.Info(_owner, "GenSpeed", Loc.T("msg.camlocked")); return; }
        string old = _camNames[_camIdx];
        string? name = Dialogs.Prompt(_owner, Loc.T("dlg.rename"), Loc.T("dlg.newname"), old);
        if (string.IsNullOrWhiteSpace(name) || name == old) return;
        if (_config.CameraPresets.ContainsKey(name)) { Dialogs.Info(_owner, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        _config.CameraPresets[name] = _config.CameraPresets[old]; _config.CameraPresets.Remove(old); ConfigStore.Save(_config);
        SetupCamSlider(); int ni = _camNames.IndexOf(name); if (ni > 0) CamSlider.Value = ni;
    }

    private void OnCamDelete(object sender, RoutedEventArgs e)
    {
        if (_camIdx == 0) { Dialogs.Info(_owner, "GenSpeed", Loc.T("msg.camlocked")); return; }
        string name = _camNames[_camIdx];
        if (!Dialogs.Confirm(_owner, Loc.T("crud.delete"), string.Format(Loc.T("msg.delconfirm"), name))) return;
        _config.CameraPresets.Remove(name); ConfigStore.Save(_config); SetupCamSlider(); CamSlider.Value = 0;
    }

    // ===== Lecture / écriture des valeurs (ce que RunPatch consomme) =====
    public Dictionary<string, double> ReadFactors()
    {
        var d = new Dictionary<string, double>();
        foreach (var (key, _, _) in Cats)
            if (_catBoxes.TryGetValue(key, out var box) &&
                double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                d[key] = v;
        return d;
    }

    public Dictionary<string, string?> ReadCam()
    {
        var d = new Dictionary<string, string?> { ["CameraYaw"] = "" };
        foreach (var (var, _) in CamVars)
            if (_camControls.TryGetValue(var, out var c) && c is TextBox tb) d[var] = tb.Text.Trim();
        if (_camControls.TryGetValue("DrawEntireTerrain", out var cc) && cc is ComboBox cb)
            d["DrawEntireTerrain"] = cb.SelectedItem as string ?? "";
        return d;
    }

    /// <summary>Résumé « joueur » des changements (par catégorie + caméra) — pour la confirmation du patch.</summary>
    public List<string> BuildChangeSummary()
    {
        var lines = new List<string>();
        foreach (var (key, _, _) in Cats)
            if (_catBoxes.TryGetValue(key, out var box) &&
                double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) &&
                Math.Abs(f - 1) > 0.001)
                lines.Add(string.Format(Loc.T("fx." + key), Fmt(f)));
        var cam = ReadCam();
        if (cam.Any(kv => kv.Key != "CameraYaw" && !string.IsNullOrEmpty(kv.Value)))
            lines.Add(string.Format(Loc.T("fx.camera"), _camIdx > 0 ? CamLabel.Text : Loc.T("cam.custom")));
        if (lines.Count == 0) lines.Add(Loc.T("fx.none"));
        return lines;
    }

    /// <summary>Écrit des facteurs BRUTS (par catégorie) dans les champs — pour appliquer la config d'une autre
    /// instance du composant (assistant) sur celle-ci.</summary>
    public void SetFactors(IReadOnlyDictionary<string, double> factors)
    {
        _suppressFactor = true;
        if (factors.TryGetValue("deplacement", out var g)) FactorBox.Text = Fmt(g);
        _suppressFactor = false;
        foreach (var (key, _, _) in Cats)
            if (_catBoxes.TryGetValue(key, out var box) && factors.TryGetValue(key, out var v)) box.Text = Fmt(v);
    }

    /// <summary>Écrit des valeurs caméra BRUTES dans les champs.</summary>
    public void SetCam(IReadOnlyDictionary<string, string?> cam)
    {
        foreach (var (var, _) in CamVars)
            if (_camControls.TryGetValue(var, out var c) && c is TextBox tb && cam.TryGetValue(var, out var v)) tb.Text = v ?? "";
        if (_camControls.TryGetValue("DrawEntireTerrain", out var cc) && cc is ComboBox cb && cam.TryGetValue("DrawEntireTerrain", out var dv))
            cb.SelectedItem = dv ?? "";
    }

    /// <summary>Sélectionne un preset de vitesse par NOM (curseur). Inconnu = ignoré.</summary>
    public void SelectSpeedByName(string name)
    {
        int i = Speeds.FindIndex(p => p.Name == name);
        if (i >= 0) { CamGuardless(() => SpeedSlider.Value = i); UpdateSpeedLabel(); ApplySpeedPreset(i); }
    }

    /// <summary>Sélectionne un preset caméra par NOM. "" / inconnu = ne change pas.</summary>
    public void SelectCamByName(string name)
    {
        int i = _camNames.IndexOf(name);
        if (i >= 0) { CamSlider.Value = i; _camIdx = i; CamLabel.Text = i == 0 ? Loc.T("cam.default") : _camNames[i]; ApplyCamPreset(i); }
    }

    private static void CamGuardless(System.Action a) => a();
}
