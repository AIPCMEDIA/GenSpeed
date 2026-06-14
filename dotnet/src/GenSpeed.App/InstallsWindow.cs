using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Panneau « Mes installs » : vue éditable des emplacements M0/M1/Mx stockés dans la config (JSON =
/// source de vérité). Corriger (re-pointer), Retirer (oublier, sans supprimer les fichiers), Ajouter. Toute
/// modif sauve la config et rafraîchit le tableau. L'auto-découverte (Steam/raccourci) re-complète ensuite.</summary>
public sealed class InstallsWindow : Window
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static Style? St(string key) => Application.Current.TryFindResource(key) as Style;

    private readonly GenConfig _config;
    private readonly Func<List<string>, Dictionary<string, string>> _labeler;
    private readonly Action _onChanged;
    private readonly StackPanel _list = new() { Margin = new Thickness(16, 8, 16, 8) };

    public static void Show(Window owner, GenConfig config,
                            Func<List<string>, Dictionary<string, string>> labeler, Action onChanged)
        => new InstallsWindow(owner, config, labeler, onChanged).ShowDialog();

    private InstallsWindow(Window owner, GenConfig config,
                           Func<List<string>, Dictionary<string, string>> labeler, Action onChanged)
    {
        _config = config; _labeler = labeler; _onChanged = onChanged;

        Title = Loc.T("installs.title"); Owner = owner; Width = 700; Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = B("bgRoot"); Foreground = B("fg");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;

        var root = new DockPanel();

        var head = new StackPanel { Margin = new Thickness(16, 14, 16, 6) };
        head.Children.Add(new TextBlock
        {
            Text = "📍  " + Loc.T("installs.title"), Foreground = B("accent"),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 18,
        });
        head.Children.Add(new TextBlock
        {
            Text = Loc.T("installs.intro"), Foreground = B("dim"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, LineHeight = 17, Margin = new Thickness(0, 3, 0, 0),
        });
        head.Children.Add(new Border { Height = 1, Background = B("bgFrame2"), Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 8, 16, 12),
        };
        var add = new Button { Content = Loc.T("installs.add"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        add.Click += (_, _) => AddInstall();
        var close = new Button { Content = Loc.T("installs.close"), MinWidth = 110, Padding = new Thickness(12, 6, 12, 6) };
        if (St("PrimaryButton") is { } s) close.Style = s;
        close.Click += (_, _) => Close();
        footer.Children.Add(add); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list });
        Content = root;
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        var paths = _config.KnownInstalls.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                                         .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0)
        {
            _list.Children.Add(new TextBlock { Text = Loc.T("installs.empty"), Foreground = B("dim"),
                TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 10, 0, 0) });
            return;
        }
        var labels = _labeler(paths);
        foreach (var dir in paths.OrderBy(d => labels.TryGetValue(d, out var l) ? l : "Z", StringComparer.Ordinal))
            _list.Children.Add(Row(dir, labels.TryGetValue(dir, out var lab) ? lab : "—"));
    }

    private UIElement Row(string dir, string label)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(new Border
        {
            Background = B("bgFrame2"), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), MinWidth = 42,
            Child = new TextBlock { Text = label, Foreground = B("accent"), FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("Consolas"), HorizontalAlignment = HorizontalAlignment.Center },
        });
        var col = new StackPanel { Width = 360, VerticalAlignment = VerticalAlignment.Center };
        col.Children.Add(new TextBlock { Text = Path.GetFileName(dir.TrimEnd('\\', '/')), Foreground = B("fg"), FontWeight = FontWeights.SemiBold });
        col.Children.Add(new TextBlock { Text = dir, Foreground = B("dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap });
        line.Children.Add(col);

        var fix = new Button { Content = Loc.T("installs.fix"), Margin = new Thickness(6, 0, 4, 0), Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        fix.Click += (_, _) => Repoint(dir);
        var rem = new Button { Content = Loc.T("installs.remove"), Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        rem.Click += (_, _) => Remove(dir);
        line.Children.Add(fix); line.Children.Add(rem);

        return new Border
        {
            BorderBrush = B("border"), BorderThickness = new Thickness(1), Background = B("bgFrame"),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 3, 0, 3),
            Child = line,
        };
    }

    private void AddInstall()
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("installs.add") };
        if (dlg.ShowDialog() != true) return;
        if (!GameLocator.IsZhFolder(dlg.FolderName)) { Dialogs.Info(this, Loc.T("installs.title"), Loc.T("wiz.s1.invalid")); return; }
        if (!_config.KnownInstalls.Any(p => Same(p, dlg.FolderName))) _config.KnownInstalls.Add(dlg.FolderName);
        Save();
    }

    private void Repoint(string dir)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("installs.fix") };
        try { dlg.InitialDirectory = Path.GetDirectoryName(dir); } catch { }
        if (dlg.ShowDialog() != true) return;
        if (!GameLocator.IsZhFolder(dlg.FolderName)) { Dialogs.Info(this, Loc.T("installs.title"), Loc.T("wiz.s1.invalid")); return; }
        _config.KnownInstalls.RemoveAll(p => Same(p, dir));
        if (!_config.KnownInstalls.Any(p => Same(p, dlg.FolderName))) _config.KnownInstalls.Add(dlg.FolderName);
        Save();
    }

    private void Remove(string dir)
    {
        if (!Dialogs.Confirm(this, Loc.T("installs.title"), string.Format(Loc.T("installs.remove.confirm"), dir))) return;
        _config.KnownInstalls.RemoveAll(p => Same(p, dir));
        Save();
    }

    private void Save() { ConfigStore.Save(_config); _onChanged(); Render(); }

    private static bool Same(string a, string b)
        => string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}
