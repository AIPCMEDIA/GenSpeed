using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Volet « Vérification des fichiers » : pour chaque binaire tiers d'une install, un statut NEUTRE
/// (référence connue / non répertoriée) et un bouton VirusTotal (70 antivirus à jour). Aucune alarme locale —
/// la décision sécurité se prend sur VirusTotal, pas sur une liste figée.</summary>
public static class SecurityWindow
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    // Couleurs des pastilles (partagées légende + lignes). Jamais de rouge : pas d'alarme locale.
    private static readonly Color CGreen  = Color.FromRgb(0x4C, 0xAF, 0x50);   // connu, sans danger
    private static readonly Color COrange = Color.FromRgb(0xFF, 0xB3, 0x00);   // non répertorié
    private static readonly Color CGray   = Color.FromRgb(0x9E, 0x9E, 0x9E);   // non suivi

    // Binaires tiers d'intérêt sécurité (présents = affichés).
    private static readonly string[] Binaries =
        { "d3d8.dll", "GenLauncher.exe", "GenToolUpdater.exe", "modded.exe", "EdgeScroller.exe", "Game.dat" };

    public static void Show(Window owner, IEnumerable<string> installs)
    {
        var win = new Window
        {
            Title = Loc.T("sec.title"), Owner = owner, Width = 760, Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("bgRoot"), Foreground = B("fg"),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 13,
        };
        var root = new DockPanel();
        win.Content = root;

        var close = new Button
        {
            Content = "OK", MinWidth = 90, Margin = new Thickness(14, 8, 14, 12),
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)Application.Current.FindResource("PrimaryButton"),
        };
        close.Click += (_, _) => win.Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var panel = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("sec.title"), Foreground = B("accent"),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 18,
            Margin = new Thickness(0, 0, 0, 6),
        });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("sec.intro"), Foreground = B("dim"), TextWrapping = TextWrapping.Wrap,
            LineHeight = 18, Margin = new Thickness(0, 0, 0, 8),
        });

        // Légende des pastilles.
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        void AddLegend(Color c, string key)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(c), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
            sp.Children.Add(new TextBlock { Text = Loc.T(key), Foreground = B("dim"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            legend.Children.Add(sp);
        }
        AddLegend(CGreen, "sec.legend.known");
        AddLegend(COrange, "sec.legend.unlisted");
        AddLegend(CGray, "sec.legend.untracked");
        panel.Children.Add(legend);

        bool any = false;
        foreach (var dir in installs)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
            var present = new List<string>();
            foreach (var n in Binaries)
                if (File.Exists(Path.Combine(dir, n))) present.Add(n);
            if (present.Count == 0) continue;
            any = true;

            panel.Children.Add(new TextBlock
            {
                Text = "🖥 " + Path.GetFileName(dir.TrimEnd('\\', '/')), Foreground = B("accent"),
                FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 12, 0, 2),
            });
            panel.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 0, 0, 4) });

            foreach (var n in present)
            {
                string path = Path.Combine(dir, n);
                var (status, label) = KnownBinaries.Identify(path);
                // Verdict EN CLAIR (pas la liste technique de VirusTotal).
                string verdict = status switch
                {
                    KnownBinaries.Status.Known    => string.Format(Loc.T("sec.verdict.known"), label),
                    KnownBinaries.Status.Unlisted => Loc.T("sec.verdict.unlisted"),
                    _                             => Loc.T("sec.verdict.untracked"),
                };

                var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };

                var vt = new Button
                {
                    Content = Loc.T("sec.vt"), Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top,
                };
                string? url = KnownBinaries.VirusTotalUrl(path);
                vt.IsEnabled = url != null;
                vt.Click += (_, _) => { try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { } };
                DockPanel.SetDock(vt, Dock.Right);
                row.Children.Add(vt);

                // Pastille ronde colorée (rendu WPF garanti, contrairement aux emojis) :
                // vert = connu/sans danger, orange = non répertorié, gris = non suivi. Jamais rouge (pas d'alarme).
                var dotColor = status switch
                {
                    KnownBinaries.Status.Known    => CGreen,
                    KnownBinaries.Status.Unlisted => COrange,
                    _                             => CGray,
                };
                var dot = new Border
                {
                    Width = 11, Height = 11, CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(dotColor),
                    VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 3, 9, 0),
                };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);

                // Nom du fichier (gras) + verdict en clair dessous.
                var col = new StackPanel();
                col.Children.Add(new TextBlock { Text = n, Foreground = B("fg"), FontWeight = FontWeights.SemiBold });
                col.Children.Add(new TextBlock
                {
                    Text = verdict, Foreground = B("dim"), TextWrapping = TextWrapping.Wrap, LineHeight = 17,
                    Margin = new Thickness(0, 1, 0, 0),
                });
                row.Children.Add(col);
                panel.Children.Add(row);
            }
        }

        if (!any)
            panel.Children.Add(new TextBlock { Text = Loc.T("sec.none"), Foreground = B("dim"),
                Margin = new Thickness(0, 10, 0, 0) });

        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel });
        win.ShowDialog();
    }
}
