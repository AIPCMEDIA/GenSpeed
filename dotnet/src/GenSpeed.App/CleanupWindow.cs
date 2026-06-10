using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GenSpeed.Core;

namespace GenSpeed.App;

public enum CleanupAction { Cancel, Simulate, Execute }

/// <summary>Fenêtre du désinstalleur propre : sélection granulaire + méthode par élément (thémée).</summary>
public static class CleanupWindow
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xE2, 0x55, 0x38));

    private static Brush RiskBrush(CleanupRisk r) => r switch
    {
        CleanupRisk.Danger => Red,
        CleanupRisk.Attention => B("orange"),
        _ => B("accent"),
    };

    private static string FmtSize(long bytes)
    {
        if (bytes <= 0) return "";
        string[] u = { "o", "Ko", "Mo", "Go" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }

    /// <summary>Affiche la fenêtre. Mute Selected/ChosenMethod des items. Retourne l'action choisie.</summary>
    public static (CleanupAction action, List<CleanupItem> items) Show(Window owner, List<CleanupItem> items, string backupDir, string gameDir)
    {
        bool ownerOk = owner.IsLoaded;   // l'owner peut avoir été fermé pendant le scan asynchrone
        var win = new Window
        {
            Title = Loc.T("clean.title"), Width = 900, Height = 640,
            WindowStartupLocation = ownerOk ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Background = B("bgRoot"), Foreground = B("fg"),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
        };
        if (ownerOk) win.Owner = owner;
        var root = new DockPanel();
        win.Content = root;

        // ── Bannière ──
        var headPanel = new StackPanel { Background = B("bgFrame2") };
        DockPanel.SetDock(headPanel, Dock.Top);
        headPanel.Children.Add(new TextBlock
        {
            Text = Loc.T("clean.banner"), Foreground = B("orange"), FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold, FontSize = 14, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(14, 10, 14, 2),
        });
        headPanel.Children.Add(new TextBlock
        {
            Text = Loc.T("clean.step0"), Foreground = B("orange"),
            FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 14, 4),
        });
        headPanel.Children.Add(new TextBlock
        {
            Text = string.Format(Loc.T("clean.backup.at"), backupDir), Foreground = B("dim"),
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 14, 8),
        });
        // Ligne case « tout cocher » (gauche) + légende des pastilles de risque (droite).
        var topRow = new DockPanel { Margin = new Thickness(14, 0, 14, 8), LastChildFill = false };
        var checkAll = new CheckBox
        {
            Content = Loc.T("clean.checkall"), Foreground = B("fg"), FontWeight = FontWeights.SemiBold,
        };
        DockPanel.SetDock(checkAll, Dock.Left);
        topRow.Children.Add(checkAll);

        var legend = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        void Dot(Brush c, string key)
        {
            legend.Inlines.Add(new System.Windows.Documents.Run("●  ") { Foreground = c });
            legend.Inlines.Add(new System.Windows.Documents.Run(Loc.T(key) + "    ") { Foreground = B("dim") });
        }
        Dot(Red, "clean.risk.danger"); Dot(B("orange"), "clean.risk.attention"); Dot(B("accent"), "clean.risk.sur");
        DockPanel.SetDock(legend, Dock.Right);
        topRow.Children.Add(legend);

        headPanel.Children.Add(topRow);
        headPanel.Children.Add(new Border { Height = 2, Background = B("orange") });
        root.Children.Add(headPanel);

        // ── Pied (total + boutons) ──
        var foot = new DockPanel { Margin = new Thickness(14, 8, 14, 10), LastChildFill = false };
        DockPanel.SetDock(foot, Dock.Bottom);
        var totalLbl = new TextBlock { Foreground = B("fg"), VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas") };
        DockPanel.SetDock(totalLbl, Dock.Left);
        foot.Children.Add(totalLbl);

        var action = CleanupAction.Cancel;
        var btnClose = new Button { Content = Loc.T("clean.btn.close"), MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };
        var btnExec = new Button { Content = Loc.T("clean.btn.exec"), MinWidth = 150, Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var btnSim = new Button { Content = Loc.T("clean.btn.simulate"), MinWidth = 150 };
        btnClose.Click += (_, _) => { action = CleanupAction.Cancel; win.Close(); };
        btnSim.Click += (_, _) => { action = CleanupAction.Simulate; win.Close(); };
        btnExec.Click += (_, _) => { action = CleanupAction.Execute; win.Close(); };
        DockPanel.SetDock(btnClose, Dock.Right); DockPanel.SetDock(btnExec, Dock.Right); DockPanel.SetDock(btnSim, Dock.Right);
        foot.Children.Add(btnClose); foot.Children.Add(btnExec); foot.Children.Add(btnSim);
        root.Children.Add(foot);

        // ── Corps : liste groupée par catégorie ──
        var list = new StackPanel { Margin = new Thickness(14, 8, 14, 8) };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        root.Children.Add(scroll);

        var checks = new List<(CheckBox cb, CleanupItem it)>();
        void RecomputeTotal()
        {
            long total = checks.Where(c => c.cb.IsChecked == true).Sum(c => c.it.SizeBytes);
            int n = checks.Count(c => c.cb.IsChecked == true);
            totalLbl.Text = string.Format(Loc.T("clean.total"), n, FmtSize(total));
            btnExec.IsEnabled = n > 0;
        }

        int step = 0;
        foreach (var grp in items.GroupBy(i => i.Category).OrderBy(g => Cleanup.CategoryRank(g.Key)))
        {
            step++;
            var groupItems = grp.ToList();
            var removable = groupItems.Where(i => i.Removable && i.AllowedMethods.Count > 0).ToList();

            // En-tête catégorie numéroté (étape) + case maîtresse.
            var catHeader = new DockPanel { Margin = new Thickness(0, 12, 0, 4) };
            var master = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
                ToolTip = Loc.T("clean.checkcat"),
                Visibility = removable.Count > 0 ? Visibility.Visible : Visibility.Hidden };
            DockPanel.SetDock(master, Dock.Left);
            catHeader.Children.Add(master);
            catHeader.Children.Add(new TextBlock
            {
                Text = string.Format(Loc.T("clean.step"), step, Loc.T($"clean.cat.{grp.Key}")),
                Foreground = B("accent"), FontWeight = FontWeights.Bold,
                FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
            });
            list.Children.Add(catHeader);
            list.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 0, 0, 4) });

            // Mods : recommander le désinstalleur natif de GenLauncher (le plus propre pour un retrait sélectif).
            if (grp.Key == CleanupCategory.Mods)
            {
                string glExe = Path.Combine(gameDir, "GenLauncher.exe");
                var note = new DockPanel { Margin = new Thickness(20, 0, 0, 8), LastChildFill = true };
                if (File.Exists(glExe))
                {
                    var openBtn = new Button { Content = Loc.T("clean.gl.open"), Padding = new Thickness(8, 2, 8, 2),
                        Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                    openBtn.Click += (_, _) => { try { Process.Start(new ProcessStartInfo { FileName = glExe, WorkingDirectory = gameDir, UseShellExecute = true }); } catch { } };
                    DockPanel.SetDock(openBtn, Dock.Right);
                    note.Children.Add(openBtn);
                }
                note.Children.Add(new TextBlock { Text = Loc.T("clean.gl.note"), Foreground = B("dim"),
                    FontSize = 11, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                list.Children.Add(note);
            }

            var groupChecks = new List<CheckBox>();
            foreach (var it in groupItems)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };

                // Pastille de risque.
                var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
                    Background = RiskBrush(it.Risk), VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(2, 0, 8, 0) };
                DockPanel.SetDock(dot, Dock.Left);
                row.Children.Add(dot);

                if (it.Removable && it.AllowedMethods.Count > 0)
                {
                    var cb = new CheckBox { IsChecked = it.DefaultChecked, VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0) };
                    DockPanel.SetDock(cb, Dock.Left);
                    row.Children.Add(cb);
                    checks.Add((cb, it));
                    groupChecks.Add(cb);
                    cb.Checked += (_, _) => { it.Selected = true; RecomputeTotal(); };
                    cb.Unchecked += (_, _) => { it.Selected = false; RecomputeTotal(); };
                    it.Selected = it.DefaultChecked;

                    // Menu de méthode (à droite).
                    var combo = new ComboBox { MinWidth = 170, VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0) };
                    foreach (var m in it.AllowedMethods)
                        combo.Items.Add(new ComboBoxItem { Content = Loc.T($"clean.method.{m}"), Tag = m });
                    combo.SelectedIndex = Math.Max(0, it.AllowedMethods.IndexOf(it.ChosenMethod));
                    combo.SelectionChanged += (_, _) =>
                    {
                        if (combo.SelectedItem is ComboBoxItem ci && ci.Tag is CleanupMethod m) it.ChosenMethod = m;
                    };
                    DockPanel.SetDock(combo, Dock.Right);
                    row.Children.Add(combo);
                }
                else
                {
                    // Item info : pas de case, badge "info".
                    var info = new TextBlock { Text = Loc.T("clean.infoonly"), Foreground = B("dim"), FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                    DockPanel.SetDock(info, Dock.Right);
                    row.Children.Add(info);
                }

                // Texte (nom + taille + explication).
                var texts = new StackPanel();
                var title = new TextBlock { TextWrapping = TextWrapping.Wrap };
                title.Inlines.Add(new System.Windows.Documents.Run(it.Display) { Foreground = B("fg") });
                if (it.SizeBytes > 0)
                    title.Inlines.Add(new System.Windows.Documents.Run($"   {FmtSize(it.SizeBytes)}")
                    { Foreground = B("dim"), FontStyle = FontStyles.Italic });
                if (!string.IsNullOrEmpty(it.Extra))
                    title.Inlines.Add(new System.Windows.Documents.Run($"   ({it.Extra})") { Foreground = B("dim") });
                texts.Children.Add(title);
                string explain = Loc.T(it.ExplainKey);
                if (explain != it.ExplainKey)
                    // Explication en gris lisible ; rouge seulement pour les éléments Danger (alerte).
                    texts.Children.Add(new TextBlock { Text = explain,
                        Foreground = it.Risk == CleanupRisk.Danger ? Red : B("dim"),
                        FontSize = 11, TextWrapping = TextWrapping.Wrap });
                row.Children.Add(texts);   // remplit le reste

                list.Children.Add(row);
            }

            // Câblage case maîtresse.
            master.Checked += (_, _) => { foreach (var c in groupChecks) c.IsChecked = true; };
            master.Unchecked += (_, _) => { foreach (var c in groupChecks) c.IsChecked = false; };
        }

        if (checks.Count == 0)
            list.Children.Add(new TextBlock { Text = Loc.T("clean.nothing"), Foreground = B("dim"),
                Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });

        // Case globale « tout cocher / décocher » (toutes catégories).
        checkAll.Checked += (_, _) => { foreach (var (cb, _) in checks) cb.IsChecked = true; };
        checkAll.Unchecked += (_, _) => { foreach (var (cb, _) in checks) cb.IsChecked = false; };

        RecomputeTotal();
        win.ShowDialog();
        return (action, items);
    }
}
