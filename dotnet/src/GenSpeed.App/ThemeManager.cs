using System.Windows;

namespace GenSpeed.App;

/// <summary>Bascule de thème à chaud (swap d'un ResourceDictionary fusionné).</summary>
public static class ThemeManager
{
    public static readonly (string Key, string Name)[] Themes =
    {
        ("Eva",   "Terminal EVA"),
        ("Usa",   "USA — High-Tech"),
        ("China", "Chine — Armée Rouge"),
    };

    public static string Current { get; private set; } = "Eva";

    private static ResourceDictionary? _themeDict;

    public static void Apply(string key)
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri($"/GenSpeed.App;component/Themes/{key}.xaml", UriKind.Relative)
        };
        var merged = Application.Current.Resources.MergedDictionaries;

        if (_themeDict != null)
            merged.Remove(_themeDict);
        else if (merged.Count > 0)
            merged.RemoveAt(0);   // retire le thème par défaut mergé dans App.xaml

        merged.Insert(0, dict);
        _themeDict = dict;
        Current = key;
    }
}
