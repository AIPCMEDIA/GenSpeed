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
    // ===== Grilles =====
    private void BuildCategoryGrid()
    {
        foreach (var c in Cats)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 14, 3) };
            sp.Children.Add(new TextBlock
            {
                Text = "●", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(c.Dot))
            });
            var lbl = new TextBlock
            {
                Width = 100, VerticalAlignment = VerticalAlignment.Center,
                Foreground = Fg, TextTrimming = TextTrimming.CharacterEllipsis
            };
            _catLabels.Add((c.Key, lbl));
            sp.Children.Add(lbl);
            sp.Children.Add(new TextBlock
            {
                Text = c.Sym, Margin = new Thickness(0, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center,
                Foreground = Dim
            });
            var box = new TextBox { Text = c.Key == "detection" ? "1" : "2.0", Width = 46,
                                    VerticalContentAlignment = VerticalAlignment.Center };
            _catBoxes[c.Key] = box;
            sp.Children.Add(box);
            CatGrid.Children.Add(sp);
        }
    }

    private void BuildCameraGrid()
    {
        CamGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        CamGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        CamGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int r = 0;
        foreach (var (var, hintKey) in CamVars)
        {
            CamGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lbl = new TextBlock { Text = var, Foreground = Dim, FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(lbl, r); Grid.SetColumn(lbl, 0); CamGrid.Children.Add(lbl);

            var tb = new TextBox { Width = 70, Margin = new Thickness(0, 3, 0, 3) };
            _camControls[var] = tb;
            Grid.SetRow(tb, r); Grid.SetColumn(tb, 1); CamGrid.Children.Add(tb);

            var hint = new TextBlock { Foreground = Dim, FontSize = 11, Margin = new Thickness(6, 3, 0, 3),
                VerticalAlignment = VerticalAlignment.Center };
            _camHints.Add((hintKey, hint));
            Grid.SetRow(hint, r); Grid.SetColumn(hint, 2); CamGrid.Children.Add(hint);
            r++;
        }

        CamGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var dl = new TextBlock { Text = "DrawEntireTerrain", Foreground = Dim, FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 3, 6, 3), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(dl, r); Grid.SetColumn(dl, 0); CamGrid.Children.Add(dl);
        var cb = new ComboBox { Width = 70, Margin = new Thickness(0, 3, 0, 3) };
        cb.Items.Add(""); cb.Items.Add("Yes"); cb.Items.Add("No"); cb.SelectedIndex = 0;
        _camControls["DrawEntireTerrain"] = cb;
        Grid.SetRow(cb, r); Grid.SetColumn(cb, 1); CamGrid.Children.Add(cb);
        var dh = new TextBlock { Foreground = Dim, FontSize = 11, Margin = new Thickness(6, 3, 0, 3),
            VerticalAlignment = VerticalAlignment.Center };
        _camHints.Add(("cam.hint.terrain", dh));
        Grid.SetRow(dh, r); Grid.SetColumn(dh, 2); CamGrid.Children.Add(dh);
    }

    // ===== Slider vitesse =====
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
        UpdateSpeedLabel();
        ApplySpeedPreset(i);
    }

    private void UpdateSpeedLabel()
    {
        if (SpeedLabel == null || _config == null) return;
        int i = Math.Clamp((int)Math.Round(SpeedSlider.Value), 0, Speeds.Count - 1);
        var p = Speeds[i];
        SpeedLabel.Text = $"{p.Name}  (×{p.Factor:g})";
    }

    private void ApplySpeedPreset(int i)
    {
        if (_catBoxes.Count == 0) return;
        var p = Speeds[i];
        _suppressFactor = true;
        FactorBox.Text = Fmt(p.Factor);
        _suppressFactor = false;
        foreach (var (key, _, _) in Cats)
        {
            double v = p.Cats != null && p.Cats.TryGetValue(key, out var cv) ? cv
                     : key == "detection" ? 1.0 : p.Factor;
            _catBoxes[key].Text = Fmt(v);
        }
    }

    private void FactorBox_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressFactor || _catBoxes.Count == 0) return;
        if (!double.TryParse(FactorBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            return;
        foreach (var (key, box) in _catBoxes)
            if (key != "detection") box.Text = Fmt(f);
    }

    private int SpeedIdx => Math.Clamp((int)Math.Round(SpeedSlider.Value), 0, Speeds.Count - 1);

    private void OnSpeedSave(object sender, RoutedEventArgs e)
    {
        var p = Speeds[SpeedIdx];
        if (p.Locked) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.locked"), p.Name)); return; }
        if (!double.TryParse(FactorBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return;
        var cats = new Dictionary<string, double>();
        foreach (var (key, _, _) in Cats)
        {
            if (key == "detection") continue;
            if (double.TryParse(_catBoxes[key].Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && Math.Abs(v - f) > 0.001)
                cats[key] = Math.Round(v, 2);
        }
        p.Factor = Math.Round(f, 2);
        p.Cats = cats.Count > 0 ? cats : null;
        ConfigStore.Save(_config);
        UpdateSpeedLabel();
        Log(string.Format(Loc.T("log.psaved"), p.Name));
    }

    private void OnSpeedNew(object sender, RoutedEventArgs e)
    {
        string? name = Dialogs.Prompt(this, Loc.T("dlg.newspeed"), Loc.T("dlg.name"));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Speeds.Any(s => s.Name == name)) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        double.TryParse(FactorBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f);
        var cats = new Dictionary<string, double>();
        foreach (var (key, _, _) in Cats)
        {
            if (key == "detection") continue;
            if (double.TryParse(_catBoxes[key].Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && Math.Abs(v - f) > 0.001) cats[key] = Math.Round(v, 2);
        }
        Speeds.Add(new SpeedPreset { Name = name, Locked = false, Factor = Math.Round(f, 2), Cats = cats.Count > 0 ? cats : null });
        ConfigStore.Save(_config);
        SpeedSlider.Maximum = Speeds.Count - 1;
        SpeedSlider.Value = Speeds.Count - 1;
        Log(string.Format(Loc.T("log.pnew"), name));
    }

    private void OnSpeedRename(object sender, RoutedEventArgs e)
    {
        var p = Speeds[SpeedIdx];
        if (p.Locked) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.locked"), p.Name)); return; }
        string? name = Dialogs.Prompt(this, Loc.T("dlg.rename"), Loc.T("dlg.newname"), p.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (Speeds.Any(s => s.Name == name && s != p)) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        string old = p.Name; p.Name = name;
        ConfigStore.Save(_config);
        UpdateSpeedLabel();
        Log(string.Format(Loc.T("log.prenamed"), old, name));
    }

    private void OnSpeedDelete(object sender, RoutedEventArgs e)
    {
        int i = SpeedIdx; var p = Speeds[i];
        if (p.Locked) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.locked"), p.Name)); return; }
        if (Speeds.Count <= 1) { Dialogs.Info(this, "GenSpeed", Loc.T("msg.minone")); return; }
        if (!Dialogs.Confirm(this, Loc.T("crud.delete"), string.Format(Loc.T("msg.delconfirm"), p.Name))) return;
        string name = p.Name;
        Speeds.RemoveAt(i);
        ConfigStore.Save(_config);
        SpeedSlider.Maximum = Speeds.Count - 1;
        int ni = Math.Min(i, Speeds.Count - 1);
        if ((int)Math.Round(SpeedSlider.Value) == ni) { UpdateSpeedLabel(); ApplySpeedPreset(ni); }
        else SpeedSlider.Value = ni;
        Log(string.Format(Loc.T("log.pdeleted"), name));
    }

    // ===== Slider caméra =====
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
        // Défaut au démarrage : « Cam haute » (1er preset), comme la vitesse sur Énervé.
        int init = _camNames.IndexOf("Cam haute");
        if (init < 0) init = _camNames.Count > 1 ? 1 : 0;
        _camIdx = init;
        if ((int)Math.Round(CamSlider.Value) == init)
        {
            if (CamLabel != null) CamLabel.Text = init == 0 ? Loc.T("cam.default") : _camNames[init];
            ApplyCamPreset(init);
        }
        else CamSlider.Value = init;
    }

    private void CamSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_config == null || _camControls.Count == 0 || CamLabel == null) return;
        _camIdx = Math.Clamp((int)Math.Round(CamSlider.Value), 0, _camNames.Count - 1);
        CamLabel.Text = _camIdx == 0 ? Loc.T("cam.default") : _camNames[_camIdx];
        ApplyCamPreset(_camIdx);
    }

    private void ApplyCamPreset(int i)
    {
        if (_camControls.Count == 0) return;
        Dictionary<string, string>? vals = (i > 0 && _config.CameraPresets.TryGetValue(_camNames[i], out var v)) ? v : null;
        foreach (var (var, _) in CamVars)
            if (_camControls[var] is TextBox tb)
                tb.Text = vals != null && vals.TryGetValue(var, out var vv) ? vv : "";
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
        if (_camIdx == 0) { Dialogs.Info(this, "GenSpeed", Loc.T("msg.camlocked")); return; }
        string name = _camNames[_camIdx];
        _config.CameraPresets[name] = ReadCamRaw();
        ConfigStore.Save(_config);
        Log(string.Format(Loc.T("log.psaved"), name));
    }

    private void OnCamNew(object sender, RoutedEventArgs e)
    {
        string? name = Dialogs.Prompt(this, Loc.T("dlg.newcam"), Loc.T("dlg.name"));
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_config.CameraPresets.ContainsKey(name)) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        _config.CameraPresets[name] = ReadCamRaw();
        ConfigStore.Save(_config);
        SetupCamSlider();
        int ni = _camNames.IndexOf(name);
        if (ni > 0) CamSlider.Value = ni;
        Log(string.Format(Loc.T("log.pnew"), name));
    }

    private void OnCamRename(object sender, RoutedEventArgs e)
    {
        if (_camIdx == 0) { Dialogs.Info(this, "GenSpeed", Loc.T("msg.camlocked")); return; }
        string old = _camNames[_camIdx];
        string? name = Dialogs.Prompt(this, Loc.T("dlg.rename"), Loc.T("dlg.newname"), old);
        if (string.IsNullOrWhiteSpace(name) || name == old) return;
        if (_config.CameraPresets.ContainsKey(name)) { Dialogs.Info(this, "GenSpeed", string.Format(Loc.T("msg.exists"), name)); return; }
        _config.CameraPresets[name] = _config.CameraPresets[old];
        _config.CameraPresets.Remove(old);
        ConfigStore.Save(_config);
        SetupCamSlider();
        int ni = _camNames.IndexOf(name);
        if (ni > 0) CamSlider.Value = ni;
        Log(string.Format(Loc.T("log.prenamed"), old, name));
    }

    private void OnCamDelete(object sender, RoutedEventArgs e)
    {
        if (_camIdx == 0) { Dialogs.Info(this, "GenSpeed", Loc.T("msg.camlocked")); return; }
        string name = _camNames[_camIdx];
        if (!Dialogs.Confirm(this, Loc.T("crud.delete"), string.Format(Loc.T("msg.delconfirm"), name))) return;
        _config.CameraPresets.Remove(name);
        ConfigStore.Save(_config);
        SetupCamSlider();
        CamSlider.Value = 0;
        Log(string.Format(Loc.T("log.pdeleted"), name));
    }
}
