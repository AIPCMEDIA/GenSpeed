using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>« 🌐 IP réseau du jeu » : choisis l'IP que le jeu utilise pour le LAN et pour le jeu en ligne — comme
/// dans Options → Réseau du jeu. Écrit « IPAddress » (LAN) et « GameSpyIPAddress » (en ligne) dans Options.ini
/// (format pointé ; 0.0.0.0 = auto). Règle proprement le souci « IP 172.x (Hyper-V) en LAN » sans toucher au
/// système : on dit juste au jeu d'utiliser ton 192.168.x. Voir [[lan-mismatch-problem]].</summary>
public sealed class NetFixWindow : Window
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? St(string key) => Application.Current.TryFindResource(key) as Style;

    private ComboBox _lan = null!, _online = null!;

    public static void Show(Window owner) => new NetFixWindow(owner).ShowDialog();

    private NetFixWindow(Window owner)
    {
        Title = Loc.T("net.title"); Owner = owner; Width = 660; Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = B("bgRoot"); Foreground = B("fg");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel();
        var body = new StackPanel { Margin = new Thickness(18, 16, 18, 12) };

        body.Children.Add(new TextBlock { Text = "🌐  " + Loc.T("net.title"), Foreground = B("accent"),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 18 });
        body.Children.Add(new TextBlock { Text = Loc.T("net.intro"), Foreground = B("dim"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 4, 0, 12) });

        string optPath = MultiplayerTuning.DefaultOptionsIniPath();
        string? curLan = MultiplayerTuning.ReadOptionValue(optPath, "IPAddress");
        string? curOnline = MultiplayerTuning.ReadOptionValue(optPath, "GameSpyIPAddress");
        string? lan = NetInfo.LanIp();

        // LAN : défaut = ton 192.168.x (sinon valeur actuelle, sinon auto).
        _lan = BuildCombo(curLan, preferred: lan ?? NetInfo.Auto);
        body.Children.Add(Field("net.lan.label", "net.lan.help", _lan));
        // En ligne : défaut = valeur actuelle, sinon auto (le jeu route tout seul vers Internet).
        _online = BuildCombo(curOnline, preferred: curOnline ?? NetInfo.Auto);
        body.Children.Add(Field("net.online.label", "net.online.help", _online));

        body.Children.Add(new TextBlock { Text = Loc.T("net.note"), Foreground = B("dim"), FontSize = 11,
            FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, LineHeight = 15, Margin = new Thickness(0, 14, 0, 0) });

        DockPanel.SetDock(body, Dock.Top); root.Children.Add(body);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 12) };
        var save = new Button { Content = Loc.T("net.save"), MinWidth = 150, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        if (St("PrimaryButton") is { } s) save.Style = s;
        save.Click += (_, _) => Save(optPath);
        var close = new Button { Content = Loc.T("net.close"), MinWidth = 90, Padding = new Thickness(12, 6, 12, 6) };
        close.Click += (_, _) => Close();
        footer.Children.Add(save); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        Content = root;
    }

    private UIElement Field(string labelKey, string helpKey, ComboBox combo)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        sp.Children.Add(new TextBlock { Text = Loc.T(labelKey), Foreground = B("fg"), FontWeight = FontWeights.SemiBold });
        sp.Children.Add(new TextBlock { Text = Loc.T(helpKey), Foreground = B("dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, LineHeight = 15, Margin = new Thickness(0, 0, 0, 4) });
        combo.HorizontalAlignment = HorizontalAlignment.Left; combo.MinWidth = 380;
        sp.Children.Add(combo);
        return sp;
    }

    /// <summary>Construit un menu : « Auto (0.0.0.0) » + chaque IP détectée (étiquetée). Présélectionne la valeur
    /// actuelle si elle existe, sinon <paramref name="preferred"/>.</summary>
    private ComboBox BuildCombo(string? current, string preferred)
    {
        var combo = new ComboBox();
        void Add(string value, string display)
        {
            var it = new ComboBoxItem { Content = display, Tag = value };
            combo.Items.Add(it);
        }
        Add(NetInfo.Auto, Loc.T("net.auto"));
        var cands = NetInfo.Candidates();
        foreach (var c in cands)
        {
            string tag = c.IsLan ? Loc.T("net.tag.lan") : c.IsParasite ? Loc.T("net.tag.virtual") : Loc.T("net.tag.other");
            Add(c.Ip, $"{c.Ip}  —  {tag}");
        }
        // Si la valeur actuelle est une IP non détectée (carte débranchée…), on l'ajoute pour ne pas la perdre.
        string want = !string.IsNullOrWhiteSpace(current) && current != NetInfo.Auto ? current! : preferred;
        if (want != NetInfo.Auto && !cands.Any(c => c.Ip == want))
            Add(want, $"{want}  —  {Loc.T("net.tag.other")}");

        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == want)
                             ?? combo.Items.Cast<ComboBoxItem>().First();   // 1er = Auto
        return combo;
    }

    private static string ValueOf(ComboBox c) => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? NetInfo.Auto;

    private void Save(string optPath)
    {
        var vals = new[] { ("IPAddress", ValueOf(_lan)), ("GameSpyIPAddress", ValueOf(_online)) };
        var r = MultiplayerTuning.ApplyOptionsValues(optPath, vals);
        Dialogs.Info(this, Loc.T("net.title"),
            r.Ok ? string.Format(Loc.T("net.saved"), ValueOf(_lan), ValueOf(_online)) : "⚠ " + r.Error);
        if (r.Ok) Close();
    }
}
