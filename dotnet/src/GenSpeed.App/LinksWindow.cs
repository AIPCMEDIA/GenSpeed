using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Panneau « Liens de téléchargement » : édite les URL dont GenSpeed a besoin (GenLauncher, VC++,
/// DirectX) sans toucher au code ni au JSON. Vide ou = défaut → la config ne stocke rien (on reste sur le
/// défaut codé). Un lien cassé se corrige ici en quelques secondes, sans recompiler.</summary>
public sealed class LinksWindow : Window
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? St(string key) => Application.Current.TryFindResource(key) as Style;

    private readonly GenConfig _config;
    private readonly System.Collections.Generic.Dictionary<string, TextBox> _boxes = new();

    public static void Show(Window owner, GenConfig config) => new LinksWindow(owner, config).ShowDialog();

    private LinksWindow(Window owner, GenConfig config)
    {
        _config = config;
        Title = Loc.T("links.title"); Owner = owner; Width = 760; Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = B("bgRoot"); Foreground = B("fg");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel();

        var head = new StackPanel { Margin = new Thickness(16, 14, 16, 6) };
        head.Children.Add(new TextBlock { Text = "🔗  " + Loc.T("links.title"), Foreground = B("accent"),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 18 });
        head.Children.Add(new TextBlock { Text = Loc.T("links.intro"), Foreground = B("dim"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 3, 0, 0) });
        head.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8, 16, 12) };
        var save = new Button { Content = Loc.T("links.save"), MinWidth = 130, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        if (St("PrimaryButton") is { } s) save.Style = s;
        save.Click += (_, _) => SaveAndClose();
        var close = new Button { Content = Loc.T("links.close"), MinWidth = 100, Padding = new Thickness(12, 6, 12, 6) };
        close.Click += (_, _) => Close();
        footer.Children.Add(save); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        var list = new StackPanel { Margin = new Thickness(16, 8, 16, 8) };
        foreach (var e in DownloadLinks.All)
        {
            list.Children.Add(new TextBlock { Text = e.Label, Foreground = B("fg"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
            var rowp = new DockPanel { LastChildFill = true };
            var reset = new Button { Content = Loc.T("links.reset"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            string def = e.DefaultUrl;
            var tb = new TextBox { Text = _config.Link(e.Key), FontFamily = new FontFamily("Consolas"), FontSize = 12, Padding = new Thickness(6, 5, 6, 5), VerticalContentAlignment = VerticalAlignment.Center };
            reset.Click += (_, _) => tb.Text = def;
            DockPanel.SetDock(reset, Dock.Right); rowp.Children.Add(reset); rowp.Children.Add(tb);
            _boxes[e.Key] = tb;
            list.Children.Add(rowp);
        }
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
        Content = root;
    }

    private void SaveAndClose()
    {
        foreach (var e in DownloadLinks.All)
        {
            string val = (_boxes[e.Key].Text ?? "").Trim();
            // On ne stocke que les SURCHARGES (différentes du défaut) → config propre, suit les futurs défauts.
            if (string.IsNullOrWhiteSpace(val) || string.Equals(val, e.DefaultUrl, StringComparison.OrdinalIgnoreCase))
                _config.Links.Remove(e.Key);
            else
                _config.Links[e.Key] = val;
        }
        ConfigStore.Save(_config);
        Close();
    }
}
