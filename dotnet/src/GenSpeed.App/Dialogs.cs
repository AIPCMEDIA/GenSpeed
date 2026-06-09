using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GenSpeed.App;

/// <summary>Petits dialogues thémés (saisie de nom, confirmation, info).</summary>
public static class Dialogs
{
    private static Brush B(string key) => (Brush)Application.Current.FindResource(key);

    private static Window Shell(Window owner, string title, out StackPanel body)
    {
        var win = new Window
        {
            Title = title, Owner = owner, Width = 400, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow, Background = B("bgRoot"), Foreground = B("fg"),
            FontFamily = new FontFamily("Segoe UI"), FontSize = 12,
        };
        body = new StackPanel { Margin = new Thickness(16) };
        win.Content = body;
        return win;
    }

    private static StackPanel ButtonRow() =>
        new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0) };

    /// <summary>Demande un texte (nom de preset). Retourne null si annulé.</summary>
    public static string? Prompt(Window owner, string title, string label, string initial = "")
    {
        var win = Shell(owner, title, out var body);
        body.Children.Add(new TextBlock { Text = label, Foreground = B("dim"), Margin = new Thickness(0, 0, 0, 6) });
        var tb = new TextBox { Text = initial, FontSize = 13, Padding = new Thickness(5, 4, 5, 4) };
        body.Children.Add(tb);

        string? result = null;
        var ok = new Button { Content = "OK", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0),
                              Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var cancel = new Button { Content = "Annuler / Cancel", MinWidth = 80 };
        ok.Click += (_, _) => { result = tb.Text.Trim(); win.DialogResult = true; };
        cancel.Click += (_, _) => win.DialogResult = false;

        var row = ButtonRow(); row.Children.Add(ok); row.Children.Add(cancel);
        body.Children.Add(row);

        win.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { result = tb.Text.Trim(); win.DialogResult = true; }
            else if (e.Key == Key.Escape) win.DialogResult = false;
        };
        return win.ShowDialog() == true ? result : null;
    }

    /// <summary>Confirmation Oui/Non.</summary>
    public static bool Confirm(Window owner, string title, string message)
    {
        var win = Shell(owner, title, out var body);
        body.Children.Add(new TextBlock { Text = message, Foreground = B("fg"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        bool result = false;
        var yes = new Button { Content = "Oui / Yes", MinWidth = 80, Margin = new Thickness(0, 0, 8, 0),
                               Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var no = new Button { Content = "Non / No", MinWidth = 80 };
        yes.Click += (_, _) => { result = true; win.DialogResult = true; };
        no.Click += (_, _) => win.DialogResult = false;
        var row = ButtonRow(); row.Children.Add(yes); row.Children.Add(no);
        body.Children.Add(row);
        win.ShowDialog();
        return result;
    }

    /// <summary>Confirmation riche avant application : mods + effets en jeu + emplacement des sauvegardes.</summary>
    public static bool ConfirmApply(Window owner, IEnumerable<string> mods, IEnumerable<string> changes, string backupLocation)
    {
        var win = Shell(owner, Loc.T("confirm.title"), out var body);
        win.Width = 520;
        body.Children.Add(new TextBlock { Text = Loc.T("confirm.intro"), Foreground = B("dim"),
            Margin = new Thickness(0, 0, 0, 4) });
        foreach (var m in mods)
            body.Children.Add(new TextBlock { Text = "   • " + m, Foreground = B("accent"),
                FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 1, 0, 1) });

        body.Children.Add(new TextBlock { Text = Loc.T("confirm.changes"), Foreground = B("dim"),
            Margin = new Thickness(0, 10, 0, 4) });
        foreach (var c in changes)
            body.Children.Add(new TextBlock { Text = "   " + c, Foreground = B("fg"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) });

        body.Children.Add(new TextBlock { Text = string.Format(Loc.T("confirm.note"), backupLocation),
            Foreground = B("dim"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) });

        bool ok = false;
        var yes = new Button { Content = Loc.T("confirm.ok"), MinWidth = 90, Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var no = new Button { Content = Loc.T("confirm.cancel"), MinWidth = 90 };
        yes.Click += (_, _) => { ok = true; win.DialogResult = true; };
        no.Click += (_, _) => win.DialogResult = false;
        var row = ButtonRow(); row.Children.Add(yes); row.Children.Add(no);
        body.Children.Add(row);
        win.ShowDialog();
        return ok;
    }

    /// <summary>Résultat après application : message + code LAN copiable + bouton Lancer GenLauncher.</summary>
    public static void ApplyResult(Window owner, string message, string code, Action onLaunch)
    {
        var win = Shell(owner, Loc.T("result.title"), out var body);
        body.Children.Add(new TextBlock { Text = message, Foreground = B("fg"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBox
        {
            Text = code, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 18,
            Foreground = B("orange"), Background = B("bgInput"), BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(4, 2, 4, 2),
        });
        var launch = new Button { Content = Loc.T("result.launch"), MinWidth = 120, Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.FindResource("GoButton") };
        var close = new Button { Content = Loc.T("result.close"), MinWidth = 90 };
        launch.Click += (_, _) => { win.DialogResult = true; onLaunch(); };
        close.Click += (_, _) => win.DialogResult = false;
        var row = ButtonRow(); row.Children.Add(launch); row.Children.Add(close);
        body.Children.Add(row);
        win.ShowDialog();
    }

    /// <summary>Choix dans une liste (un bouton par option). Retourne l'option choisie ou null.</summary>
    public static string? Choose(Window owner, string title, string message, IList<string> options)
    {
        var win = Shell(owner, title, out var body);
        body.Children.Add(new TextBlock { Text = message, Foreground = B("fg"),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        string? result = null;
        foreach (var opt in options)
        {
            var o = opt;
            var b = new Button { Content = o, HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(10, 6, 10, 6) };
            b.Click += (_, _) => { result = o; win.DialogResult = true; };
            body.Children.Add(b);
        }
        var cancel = new Button { Content = "Annuler / Cancel", MinWidth = 80,
            Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => win.DialogResult = false;
        body.Children.Add(cancel);
        win.ShowDialog();
        return result;
    }

    /// <summary>Message d'information.</summary>
    public static void Info(Window owner, string title, string message)
    {
        var win = Shell(owner, title, out var body);
        body.Children.Add(new TextBlock { Text = message, Foreground = B("fg"),
            TextWrapping = TextWrapping.Wrap });
        var okb = new Button { Content = "OK", MinWidth = 80,
                               Style = (Style)Application.Current.FindResource("PrimaryButton") };
        okb.Click += (_, _) => win.DialogResult = true;
        var row = ButtonRow(); row.Children.Add(okb);
        body.Children.Add(row);
        win.ShowDialog();
    }
}
