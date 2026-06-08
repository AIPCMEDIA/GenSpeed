using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Fenêtre de verdict du diagnostic mismatch (thémée, triée par gravité).</summary>
public static class DiagnosticWindow
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xE2, 0x55, 0x38));

    private sealed class DRow
    {
        public string Sev { get; init; } = "";
        public string Item { get; init; } = "";
        public string Detail { get; init; } = "";
        public Brush Color { get; init; } = Brushes.White;
    }

    public static void Show(Window owner, List<DiffEntry> diffs)
    {
        int crit = diffs.Count(d => d.Severity == Severity.Critique);
        int warn = diffs.Count(d => d.Severity == Severity.Attention);

        var win = new Window
        {
            Title = Loc.T("diag.title"), Owner = owner, Width = 860, Height = 580,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("bgRoot"), Foreground = B("fg"),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
        };
        var root = new DockPanel { Margin = new Thickness(0) };
        win.Content = root;

        // ── Bannière verdict ──
        Brush banner = crit > 0 ? Red : warn > 0 ? B("orange") : B("accent");
        string head = crit > 0 ? string.Format(Loc.T("diag.crit"), crit)
                    : warn > 0 ? string.Format(Loc.T("diag.warn"), warn)
                    : Loc.T("diag.allok");
        var headPanel = new StackPanel { Background = B("bgFrame2") };
        DockPanel.SetDock(headPanel, Dock.Top);
        headPanel.Children.Add(new TextBlock
        {
            Text = head, Foreground = banner, FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.Bold, FontSize = 14, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(14, 10, 14, 4),
        });
        headPanel.Children.Add(new Border { Height = 2, Background = banner, Margin = new Thickness(0, 6, 0, 0) });
        root.Children.Add(headPanel);

        if (diffs.Count == 0)
        {
            var okb = new Button { Content = "OK", MinWidth = 90, Margin = new Thickness(14),
                                   HorizontalAlignment = HorizontalAlignment.Center,
                                   Style = (Style)Application.Current.FindResource("PrimaryButton") };
            okb.Click += (_, _) => win.Close();
            DockPanel.SetDock(okb, Dock.Bottom);
            root.Children.Add(okb);
            win.ShowDialog();
            return;
        }

        // ── Pied : légende + fermer ──
        var foot = new StackPanel { Margin = new Thickness(14, 6, 14, 10) };
        DockPanel.SetDock(foot, Dock.Bottom);
        foot.Children.Add(new TextBlock { Text = Loc.T("diag.legend"), Foreground = B("dim"),
            FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var close = new Button { Content = "OK", MinWidth = 90, HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)Application.Current.FindResource("PrimaryButton") };
        close.Click += (_, _) => win.Close();
        foot.Children.Add(close);
        root.Children.Add(foot);

        // ── Tableau ──
        var count = new TextBlock { Text = string.Format(Loc.T("diag.ndiff"), diffs.Count),
            Foreground = B("dim"), FontSize = 11, Margin = new Thickness(14, 8, 14, 2) };
        DockPanel.SetDock(count, Dock.Top);
        root.Children.Add(count);

        var grid = new DataGrid
        {
            Margin = new Thickness(12, 0, 12, 6),
            Style = (Style)Application.Current.FindResource(typeof(DataGrid)),
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
        };
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("diag.col.sev"), Binding = new Binding("Sev"), Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("diag.col.item"), Binding = new Binding("Item"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("diag.col.detail"), Binding = new Binding("Detail"), Width = 320 });

        // Couleur de ligne selon la gravité.
        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, new Binding("Color")));
        grid.RowStyle = rowStyle;

        grid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
        var cm = new ContextMenu();
        cm.Items.Add(new MenuItem { Header = Loc.T("ctx.copy"), Command = ApplicationCommands.Copy, CommandTarget = grid });
        grid.ContextMenu = cm;

        grid.ItemsSource = diffs
            .OrderBy(d => d.Severity).ThenBy(d => d.SectionKey, StringComparer.Ordinal).ThenBy(d => d.Item, StringComparer.Ordinal)
            .Select(ToRow).ToList();
        root.Children.Add(grid);

        win.ShowDialog();
    }

    private static DRow ToRow(DiffEntry d)
    {
        string sev = d.Severity switch
        {
            Severity.Critique => Loc.T("sev.crit"),
            Severity.Attention => Loc.T("sev.warn"),
            _ => Loc.T("sev.info"),
        };
        Brush color = d.Severity switch
        {
            Severity.Critique => Red,
            Severity.Attention => B("orange"),
            _ => B("dim"),
        };

        string section = d.SectionKey switch
        {
            "sec.components" => "",
            "sec.base" => Loc.T("sec.base"),
            "sec.ini" => Loc.T("sec.ini"),
            "sec.maps" => Loc.T("sec.maps"),
            "sec.gentool" => Loc.T("sec.gentool"),
            var s when s.StartsWith("mod:", StringComparison.Ordinal) => $"{Loc.T("sec.mod")} {s[4..]}",
            _ => d.SectionKey,
        };
        string item = section.Length > 0 ? $"{section} » {d.Item}" : d.Item;

        string detail;
        if (d.Mine != null || d.Other != null)
            detail = $"{d.Mine ?? "—"}   ↔   {d.Other ?? "—"}";
        else
            detail = d.Status switch
            {
                DiffStatus.Different => Loc.T("st.diff"),
                DiffStatus.AbsentMine => Loc.T("st.absme"),
                _ => Loc.T("st.absother"),
            };

        return new DRow { Sev = sev, Item = item, Detail = detail, Color = color };
    }
}
