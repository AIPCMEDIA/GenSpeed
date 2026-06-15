using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GenSpeed.App;

/// <summary>« 🌐 Réparer l'IP LAN » : détecte les adaptateurs parasites (Hyper-V « Default Switch » en 172.x, WSL,
/// VPN…) qui font que le jeu choisit la mauvaise IP en LAN, et propose de les désactiver (UAC). Réversible ;
/// ils peuvent revenir au redémarrage → on peut relancer ce fix. Voir [[lan-mismatch-problem]].</summary>
public sealed class NetFixWindow : Window
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? St(string key) => Application.Current.TryFindResource(key) as Style;
    private static Brush Orange => new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static Brush Green => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

    private readonly StackPanel _list = new() { Margin = new Thickness(16, 8, 16, 8) };

    public static void Show(Window owner) => new NetFixWindow(owner).ShowDialog();

    private NetFixWindow(Window owner)
    {
        Title = Loc.T("net.title"); Owner = owner; Width = 660; Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = B("bgRoot"); Foreground = B("fg");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel();
        var head = new StackPanel { Margin = new Thickness(16, 14, 16, 6) };
        head.Children.Add(new TextBlock { Text = "🌐  " + Loc.T("net.title"), Foreground = B("accent"),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 18 });
        head.Children.Add(new TextBlock { Text = Loc.T("net.intro"), Foreground = B("dim"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 3, 0, 0) });
        head.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 12) };
        var recheck = new Button { Content = Loc.T("net.recheck"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        recheck.Click += (_, _) => Render();
        var close = new Button { Content = Loc.T("net.close"), MinWidth = 90, Padding = new Thickness(12, 6, 12, 6) };
        if (St("PrimaryButton") is { } s) close.Style = s;
        close.Click += (_, _) => Close();
        footer.Children.Add(recheck); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
        Content = root;
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();

        // Ton LAN.
        string? lan = NetInfo.LanIp();
        _list.Children.Add(new TextBlock
        {
            Text = lan != null ? string.Format(Loc.T("net.lan.ok"), lan) : Loc.T("net.lan.none"),
            Foreground = lan != null ? Green : Orange, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 6, 0, 8),
        });

        var parasites = NetInfo.Parasites();
        if (parasites.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = Loc.T("net.clean"), Foreground = Green,
                TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 4, 0, 4) });
        }
        else
        {
            _list.Children.Add(new TextBlock { Text = Loc.T("net.found"), Foreground = Orange, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 2, 0, 6) });
            foreach (var a in parasites) _list.Children.Add(ParasiteRow(a));
        }

        _list.Children.Add(new TextBlock { Text = Loc.T("net.note"), Foreground = B("dim"), FontSize = 11,
            FontStyle = FontStyles.Italic, TextWrapping = TextWrapping.Wrap, LineHeight = 15, Margin = new Thickness(0, 12, 0, 0) });
    }

    private UIElement ParasiteRow(NetInfo.Adapter a)
    {
        var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        col.Children.Add(new TextBlock { Text = $"⚠ {a.Name}  ·  {a.Ipv4}", Foreground = B("fg"), FontWeight = FontWeights.SemiBold });
        col.Children.Add(new TextBlock { Text = a.Desc, Foreground = B("dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap });

        var btn = new Button { Content = Loc.T("net.disable"), Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(12, 5, 12, 5), VerticalAlignment = VerticalAlignment.Center };
        if (St("PrimaryButton") is { } s) btn.Style = s;
        btn.Click += (_, _) =>
        {
            if (!Dialogs.Confirm(this, Loc.T("net.title"), string.Format(Loc.T("net.disable.confirm"), a.Name, a.Ipv4))) return;
            bool ok = NetInfo.DisableElevated(a.Name);
            Dialogs.Info(this, Loc.T("net.title"), ok ? string.Format(Loc.T("net.disabled"), a.Name) : Loc.T("net.disable.fail"));
            Render();
        };

        var line = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(btn, Dock.Right);
        line.Children.Add(btn);
        line.Children.Add(col);
        return new Border
        {
            BorderBrush = Orange, BorderThickness = new Thickness(1), Background = B("bgFrame"),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 3, 0, 3),
            Child = line,
        };
    }
}
