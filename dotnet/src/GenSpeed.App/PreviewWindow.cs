using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using GenSpeed.Core;

namespace GenSpeed.App;

/// <summary>Fenêtre d'aperçu des variables (thémée). Lignes modifiées surlignées.</summary>
public static class PreviewWindow
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    private sealed class PRow
    {
        public string Var { get; init; } = "";
        public string Orig { get; init; } = "";
        public string Cur { get; init; } = "";
        public string Loc { get; init; } = "";
        public Brush Color { get; init; } = Brushes.White;
    }

    public static void Show(Window owner, string title, string header, List<PreviewRow> rows)
    {
        var win = new Window
        {
            Title = title, Owner = owner, Width = 760, Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = B("bgRoot"), Foreground = B("fg"),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
        };
        var root = new DockPanel();
        win.Content = root;

        var head = new TextBlock
        {
            Text = header, Foreground = B("accent"), FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 10, 14, 6),
        };
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var close = new Button { Content = "OK", MinWidth = 90, Margin = new Thickness(14, 6, 14, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)Application.Current.FindResource("PrimaryButton") };
        close.Click += (_, _) => win.Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var grid = new DataGrid
        {
            Margin = new Thickness(12, 0, 12, 6),
            Style = (Style)Application.Current.FindResource(typeof(DataGrid)),
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
        };
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("preview.col.var"), Binding = new Binding("Var"), Width = 220 });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("preview.col.orig"), Binding = new Binding("Orig"), Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("preview.col.cur"), Binding = new Binding("Cur"), Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = Loc.T("preview.col.loc"), Binding = new Binding("Loc"),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, new Binding("Color")));
        grid.RowStyle = rowStyle;

        // Copiable : Ctrl+C + clic droit « Copier ».
        grid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
        var cm = new ContextMenu();
        cm.Items.Add(new MenuItem { Header = Loc.T("ctx.copy"), Command = ApplicationCommands.Copy, CommandTarget = grid });
        grid.ContextMenu = cm;

        Brush normal = B("fg"), changed = B("orange");
        grid.ItemsSource = rows.Select(r => new PRow
        {
            Var = r.Var, Orig = r.Orig, Cur = r.Current, Loc = r.Location,
            Color = r.Modified ? changed : normal,
        }).ToList();
        root.Children.Add(grid);

        win.ShowDialog();
    }
}
