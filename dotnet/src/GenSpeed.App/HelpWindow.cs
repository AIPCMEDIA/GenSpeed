using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GenSpeed.App;

/// <summary>Fenêtre d'aide globale (thémée, bilingue, défilante).</summary>
public static class HelpWindow
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    // (clé en-tête, clé corps) — le contenu vit dans Localization.
    private static readonly (string H, string Body)[] Sections =
    {
        ("help.s1.h", "help.s1.b"),
        ("help.s2.h", "help.s2.b"),
        ("help.s3.h", "help.s3.b"),
        ("help.s4.h", "help.s4.b"),
        ("help.s5.h", "help.s5.b"),
        ("help.s6.h", "help.s6.b"),
        ("help.s7.h", "help.s7.b"),
    };

    public static void Show(Window owner)
    {
        var win = new Window
        {
            Title = Loc.T("help.title"), Owner = owner, Width = 720, Height = 620,
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
            Text = Loc.T("help.title"), Foreground = B("accent"),
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 18,
            Margin = new Thickness(0, 0, 0, 8),
        });

        foreach (var (h, body) in Sections)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T(h), Foreground = B("accent"), FontWeight = FontWeights.Bold, FontSize = 14,
                Margin = new Thickness(0, 13, 0, 3), TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T(body), Foreground = B("fg"), TextWrapping = TextWrapping.Wrap, LineHeight = 19,
            });
        }

        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel,
        });

        win.ShowDialog();
    }
}
