using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Sélecteur « Options de jeu » : chaque option avec un libellé + une explication brève (bilingue),
/// pré-cochée intelligemment (analyse PC), MODIFIABLE. Groupes : 🟢 libres, 🔴 à aligner avec l'ami, ⚡ Vulkan.</summary>
public sealed class GameOptionsWindow : Window
{
    private static readonly string[] ResChoices = { "native", "1920 1080", "1600 900", "1366 768", "1280 720" };

    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? St(string key) => Application.Current.TryFindResource(key) as Style;

    private readonly GenConfig _config;
    private readonly Action _onApply;
    private readonly StackPanel _list = new() { Margin = new Thickness(16, 8, 16, 8) };
    private readonly Dictionary<string, Func<string>> _readers = new();

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
        head.Children.Add(new TextBlock { Text = Loc.T("go.intro"), Foreground = B("dim"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 3, 0, 0) });
        head.Children.Add(new TextBlock {
            Text = "🖥  " + string.Format(Loc.T("go.pc.detected"), PcInfo.Summary(), Loc.T("go.lvl." + PcInfo.RecommendedGraphics())),
            Foreground = B("accent"), FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 16,
            Margin = new Thickness(0, 6, 0, 0), ToolTip = Loc.T("go.pc.tip") });
        head.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 12) };
        var reco = new Button { Content = Loc.T("go.reco"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6), ToolTip = Loc.T("go.reco.tip") };
        reco.Click += (_, _) => { _config.GameOptions.Clear(); Render(); };
        var save = new Button { Content = Loc.T("go.save"), MinWidth = 130, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        if (St("PrimaryButton") is { } s) save.Style = s;
        save.Click += (_, _) => SaveAndClose();
        var close = new Button { Content = Loc.T("go.close"), MinWidth = 100, Padding = new Thickness(12, 6, 12, 6) };
        close.Click += (_, _) => Close();
        footer.Children.Add(reco); footer.Children.Add(save); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
        Content = root;
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        _readers.Clear();
        AddGroup("free",  "go.grp.free",  B("fg"));
        AddGroup("match", "go.grp.match", new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)));
        AddGroup("adv",   "go.grp.adv",   new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00)));
    }

    private void AddGroup(string group, string headerKey, Brush color)
    {
        _list.Children.Add(new TextBlock { Text = Loc.T(headerKey), Foreground = color, FontWeight = FontWeights.Bold,
            FontSize = 14, Margin = new Thickness(0, 14, 0, 4) });
        foreach (var o in GameOptions.Defs.Where(x => x.Group == group))
            _list.Children.Add(Row(o));
    }

    private UIElement Row(GOpt o)
    {
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
            ctrl = cb;
        }
        else
        {
            var choices = o.Kind == "res" ? ResChoices : (o.Choices ?? Array.Empty<string>());
            var combo = new ComboBox { Width = 130, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in choices) combo.Items.Add(c);
            if (!choices.Contains(cur, StringComparer.OrdinalIgnoreCase)) combo.Items.Add(cur);
            combo.SelectedItem = choices.FirstOrDefault(c => c.Equals(cur, StringComparison.OrdinalIgnoreCase)) ?? cur;
            _readers[o.Key] = () => (combo.SelectedItem as string) ?? cur;
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
        foreach (var o in GameOptions.Defs)
            if (_readers.TryGetValue(o.Key, out var read)) _config.GameOptions[o.Key] = read();
        ConfigStore.Save(_config);
        GameOptions.ApplyIni(_config);
        _onApply();   // applique Vulkan aux installs GenLauncher + rafraîchit
        Close();
    }
}
