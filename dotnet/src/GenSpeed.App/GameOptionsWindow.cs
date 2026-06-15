using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Sélecteur « Options de jeu » en 2 étapes : Étape 1 = 🟢 réglages libres (PC + goût, zéro risque LAN),
/// Étape 2 = 🔴 à aligner avec les amis (+ barre de synchro par code + ⚡ Vulkan). Chaque option a un libellé +
/// une explication brève (bilingue), pré-cochée selon le PC, MODIFIABLE.</summary>
public sealed class GameOptionsWindow : Window
{
    private static readonly string[] ResChoices = { "native", "1920 1080", "1600 900", "1366 768", "1280 720" };

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? St(string key) => Application.Current.TryFindResource(key) as Style;
    private static Brush Amber => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));

    private readonly GenConfig _config;
    private readonly Action _onApply;
    private readonly StackPanel _list = new() { Margin = new Thickness(16, 8, 16, 8) };
    private readonly StackPanel _footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 12) };
    private readonly Dictionary<string, Func<string>> _readers = new();
    private readonly TextBlock _stepLabel = new() { Foreground = B("dim"), FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };

    private int _page;                 // 0 = libre, 1 = à aligner
    private TextBox? _codeBox;         // champ « mon code » (page 1), mis à jour en direct quand on coche

    public static void Show(Window owner, GenConfig config, Action onApply)
        => new GameOptionsWindow(owner, config, onApply).ShowDialog();

    private GameOptionsWindow(Window owner, GenConfig config, Action onApply)
    {
        _config = config; _onApply = onApply;
        Title = Loc.T("go.title"); Owner = owner; Width = 740; Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = B("bgRoot"); Foreground = B("fg");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel();
        var head = new StackPanel { Margin = new Thickness(16, 14, 16, 6) };
        head.Children.Add(new TextBlock { Text = "🎛  " + Loc.T("go.title"), Foreground = B("accent"),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 18 });
        head.Children.Add(new TextBlock { Text = Loc.T("go.scope"), Foreground = B("dim"), FontSize = 11,
            FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, LineHeight = 15, Margin = new Thickness(0, 2, 0, 0) });
        head.Children.Add(_stepLabel);
        head.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

        DockPanel.SetDock(_footer, Dock.Bottom); root.Children.Add(_footer);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
        Content = root;
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        _readers.Clear();
        _codeBox = null;

        if (_page == 0)
        {
            _stepLabel.Text = Loc.T("go.step1");
            _list.Children.Add(new TextBlock {
                Text = "🖥  " + string.Format(Loc.T("go.pc.detected"), PcInfo.Summary(), Loc.T("go.lvl." + PcInfo.RecommendedGraphics())),
                Foreground = B("accent"), FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 16,
                Margin = new Thickness(0, 4, 0, 6), ToolTip = Loc.T("go.pc.tip") });
            AddGroup("free", "go.grp.free", B("fg"));
        }
        else
        {
            _stepLabel.Text = Loc.T("go.step2");
            AddGroup("match", "go.grp.match", Amber);   // inclut désormais Vulkan (plus de bloc « Avancé » séparé)
            // Barre de synchro en TÊTE (visible sans scroller), insérée après les groupes pour que les _readers
            // existent et que le code initial reflète l'écran.
            _list.Children.Insert(0, MatchSyncBar());
        }
        RenderFooter();
    }

    private void RenderFooter()
    {
        _footer.Children.Clear();
        var reco = new Button { Content = Loc.T("go.reco"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6), ToolTip = Loc.T("go.reco.tip") };
        reco.Click += (_, _) => { _config.GameOptions.Clear(); Render(); };

        if (_page == 0)
        {
            var next = new Button { Content = Loc.T("go.next"), MinWidth = 150, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
            if (St("PrimaryButton") is { } sp) next.Style = sp;
            next.Click += (_, _) => { CaptureReaders(); _page = 1; Render(); };
            _footer.Children.Add(reco); _footer.Children.Add(next);
        }
        else
        {
            var back = new Button { Content = Loc.T("go.back"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
            back.Click += (_, _) => { CaptureReaders(); _page = 0; Render(); };
            var save = new Button { Content = Loc.T("go.save"), MinWidth = 130, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
            if (St("PrimaryButton") is { } sp) save.Style = sp;
            save.Click += (_, _) => SaveAndClose();
            _footer.Children.Add(back); _footer.Children.Add(reco); _footer.Children.Add(save);
        }
        var close = new Button { Content = Loc.T("go.close"), MinWidth = 90, Padding = new Thickness(12, 6, 12, 6) };
        close.Click += (_, _) => Close();
        _footer.Children.Add(close);
    }

    private void AddGroup(string group, string headerKey, Brush color)
    {
        _list.Children.Add(new TextBlock { Text = Loc.T(headerKey), Foreground = color, FontWeight = FontWeights.Bold,
            FontSize = 14, Margin = new Thickness(0, 14, 0, 4) });
        foreach (var o in GameOptions.Defs.Where(x => x.Group == group))
            _list.Children.Add(Row(o));
    }

    /// <summary>Capture les valeurs affichées dans Config.GameOptions (sans écrire sur disque) : permet de
    /// préserver l'étape courante en changeant de page, et de refléter l'écran dans le code/à l'enregistrement.</summary>
    private void CaptureReaders()
    {
        foreach (var o in GameOptions.Defs)
            if (_readers.TryGetValue(o.Key, out var read)) _config.GameOptions[o.Key] = read();
    }

    /// <summary>Recalcule « mon code » en direct (appelé à chaque changement d'une option anti-désync).</summary>
    private void LiveCode()
    {
        if (_codeBox is null) return;
        CaptureReaders();
        _codeBox.Text = GameOptions.ExportMatchCode(_config);
    }

    /// <summary>Barre « synchroniser avec les amis » : mon code (copier) + coller le code reçu (appliquer),
    /// avec le conseil « caler sur le PC le moins puissant ».</summary>
    private UIElement MatchSyncBar()
    {
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = Loc.T("go.sync.title"), Foreground = B("accent"), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        box.Children.Add(new TextBlock { Text = Loc.T("go.sync.note"), Foreground = B("dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, LineHeight = 15, Margin = new Thickness(0, 0, 0, 6) });
        var status = new TextBlock { Foreground = B("accent"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };

        box.Children.Add(new TextBlock { Text = Loc.T("go.sync.mycode"), Foreground = B("fg"), FontSize = 11, Margin = new Thickness(0, 2, 0, 1) });
        CaptureReaders();
        _codeBox = new TextBox { Text = GameOptions.ExportMatchCode(_config), IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, FontWeight = FontWeights.Bold };
        var copy = new Button { Content = Loc.T("go.sync.copy"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        copy.Click += (_, _) => { try { LiveCode(); Clipboard.SetText(_codeBox!.Text); status.Foreground = B("accent"); status.Text = Loc.T("go.sync.copied"); } catch { } };
        box.Children.Add(FillRow(_codeBox, copy));

        box.Children.Add(new TextBlock { Text = Loc.T("go.sync.paste"), Foreground = B("fg"), FontSize = 11, Margin = new Thickness(0, 6, 0, 1) });
        var recv = new TextBox { FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        var apply = new Button { Content = Loc.T("go.sync.apply"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        apply.Click += (_, _) =>
        {
            CaptureReaders();
            int n = GameOptions.ImportMatchCode(_config, recv.Text);
            if (n > 0) { MessageBox.Show(this, string.Format(Loc.T("go.sync.applied"), n), Loc.T("go.title"), MessageBoxButton.OK, MessageBoxImage.Information); Render(); }
            else { status.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)); status.Text = Loc.T("go.sync.bad"); }
        };
        box.Children.Add(FillRow(recv, apply));
        box.Children.Add(status);

        return new Border { BorderBrush = B("accent"), BorderThickness = new Thickness(2), Background = B("bgFrame"), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 2, 0, 10), Child = box };
    }

    private static UIElement FillRow(TextBox tb, Button btn)
    {
        var dp = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(btn, Dock.Right);
        dp.Children.Add(btn);
        dp.Children.Add(tb);
        return dp;
    }

    private UIElement Row(GOpt o)
    {
        bool sync = o.Group == "match" || o.Yaml;   // option anti-désync → met à jour le code en direct
        string cur = GameOptions.Value(_config, o.Key);
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        col.Children.Add(new TextBlock { Text = Loc.T($"go.{o.Key}.l"), Foreground = B("fg"), FontWeight = FontWeights.SemiBold });
        string help = Loc.T($"go.{o.Key}.h");
        if (o.Key == "Resolution" && ScreenInfo.NativeResolution() is { } nat)
            help += " " + string.Format(Loc.T("go.res.native"), nat.Replace(" ", "×"));
        col.Children.Add(new TextBlock { Text = help, Foreground = B("dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, LineHeight = 15 });

        FrameworkElement ctrl;
        if (o.Kind == "toggle")
        {
            var cb = new CheckBox { IsChecked = cur.Equals("yes", StringComparison.OrdinalIgnoreCase), VerticalAlignment = VerticalAlignment.Center };
            _readers[o.Key] = () => cb.IsChecked == true ? "yes" : "no";
            if (sync) { cb.Checked += (_, _) => LiveCode(); cb.Unchecked += (_, _) => LiveCode(); }
            ctrl = cb;
        }
        else if (o.Kind == "particles")
        {
            int n = int.TryParse(cur, out var p) ? Math.Clamp(p, 100, 50000) : 1000;
            var valLbl = new TextBlock { Text = n.ToString(), MinWidth = 48, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Foreground = B("fg"), FontWeight = FontWeights.SemiBold };
            var sl = new Slider { Minimum = 100, Maximum = 50000, Value = n, Width = 210, VerticalAlignment = VerticalAlignment.Center,
                TickFrequency = 100, IsSnapToTickEnabled = true, SmallChange = 100, LargeChange = 1000, Margin = new Thickness(8, 0, 0, 0) };
            sl.ValueChanged += (_, _) => { valLbl.Text = ((int)Math.Round(sl.Value)).ToString(); if (sync) LiveCode(); };
            _readers[o.Key] = () => ((int)Math.Round(sl.Value)).ToString();
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(valLbl); sp.Children.Add(sl);
            ctrl = sp;
        }
        else
        {
            var choices = o.Kind == "res" ? ResChoices : (o.Choices ?? Array.Empty<string>());
            var combo = new ComboBox { Width = 130, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in choices) combo.Items.Add(c);
            if (!choices.Contains(cur, StringComparer.OrdinalIgnoreCase)) combo.Items.Add(cur);
            combo.SelectedItem = choices.FirstOrDefault(c => c.Equals(cur, StringComparison.OrdinalIgnoreCase)) ?? cur;
            _readers[o.Key] = () => (combo.SelectedItem as string) ?? cur;
            if (sync) combo.SelectionChanged += (_, _) => LiveCode();
            ctrl = combo;
        }

        var line = new DockPanel { LastChildFill = true };
        ctrl.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(ctrl, Dock.Right);
        line.Children.Add(ctrl);
        line.Children.Add(col);
        return new Border
        {
            BorderBrush = B("border"), BorderThickness = new Thickness(1), Background = B("bgFrame"),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 3, 0, 3),
            Child = line,
        };
    }

    private void SaveAndClose()
    {
        CaptureReaders();                 // étape courante ; l'autre étape est déjà dans _config (capturée à la navigation)
        ConfigStore.Save(_config);
        GameOptions.ApplyIni(_config);
        _onApply();                       // applique Vulkan aux installs GenLauncher + rafraîchit
        Close();
    }
}
