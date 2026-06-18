using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace GenSpeed.App;

/// <summary>Écran de démarrage : SEUL le logo apparaît (fondu + grossissement avec léger rebond), pause, puis fondu de
/// sortie — la fenêtre s'auto-ferme ensuite. Sans bordure ; le fond reprend EXACTEMENT la couleur de l'app (bgRoot) :
/// la transparence per-pixel rend un fond noir sur certaines machines, donc on s'aligne sur le fond de l'app pour une
/// transition continue vers l'assistant (on a l'impression que seul le logo s'affiche, puis le contenu charge).
/// <see cref="Run"/> enchaîne sur l'action suivante.</summary>
public sealed class SplashWindow : Window
{
    private readonly ScaleTransform _scale = new(0.55, 0.55);
    private readonly UIElement _visual;

    /// <summary>Affiche le splash, joue l'animation (~2,9 s) puis exécute <paramref name="onDone"/> à la fermeture.</summary>
    public static void Run(Action onDone)
    {
        var s = new SplashWindow();
        s.Closed += (_, _) => onDone();
        s.Show();
    }

    private SplashWindow()
    {
        // Fenêtre 100% transparente per-pixel : seuls les pixels opaques du logo se voient (rien d'autre — ni cadre,
        // ni fond). AllowsTransparency/WindowStyle posés AVANT l'affichage. La fenêtre principale est masquée pendant
        // ce temps (voir MainWindow), donc derrière le logo il n'y a que le bureau.
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 360; Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var accent = (Application.Current?.TryFindResource("accent") as Brush)
                     ?? new SolidColorBrush(Color.FromRgb(0xE8, 0x9A, 0x3C));

        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _scale, Opacity = 0,
        };
        var img = new Image { Width = 230, Height = 230, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        try { img.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")); } catch { }
        panel.Children.Add(img);
        panel.Children.Add(new TextBlock
        {
            Text = "GenSpeed", Foreground = accent,
            FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 26,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0),
        });

        _visual = panel;
        Content = panel;
        Loaded += OnLoadedAnim;
    }

    private void OnLoadedAnim(object sender, RoutedEventArgs e)
    {
        // Grossissement avec léger rebond (BackEase) pendant l'apparition.
        var grow = new DoubleAnimation(0.55, 1.0, TimeSpan.FromMilliseconds(800))
        { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 } };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);

        // Opacité en une seule passe : fondu d'entrée → longue pause (pour bien voir) → fondu de sortie → fermeture.
        var op = new DoubleAnimationUsingKeyFrames();
        op.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        op.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(550)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        op.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2400))));
        op.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2900)),
            new CubicEase { EasingMode = EasingMode.EaseIn }));
        op.Completed += (_, _) => Close();
        _visual.BeginAnimation(OpacityProperty, op);
    }
}
