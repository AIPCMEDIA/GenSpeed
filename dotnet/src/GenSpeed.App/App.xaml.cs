using System.IO;
using System.Windows;

namespace GenSpeed.App;

public partial class App : Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "genspeed-crash.txt"), ev.ExceptionObject?.ToString()); } catch { }
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Journalise toute exception non gérée dans un fichier (diagnostic).
        DispatcherUnhandledException += (_, ev) =>
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "genspeed-crash.txt"), ev.Exception.ToString()); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "genspeed-crash.txt"), ev.ExceptionObject?.ToString()); } catch { }
        };

        // Mode élevé (relancé en admin pour écrire dans le dossier du jeu) : headless.
        if (e.Args.Length >= 2 && (e.Args[0] == "--apply" || e.Args[0] == "--restore"))
        {
            int code;
            try { code = ElevatedRunner.Run(e.Args[0] == "--apply" ? "apply" : "restore", e.Args[1]); }
            catch { code = 2; }
            Shutdown(code);
            return;
        }

        // Mode élevé désinstalleur propre (sauvegarde + suppression).
        if (e.Args.Length >= 2 && e.Args[0] == "--cleanup")
        {
            int code;
            try { code = CleanupRunner.Run(e.Args[1]); }
            catch { code = 2; }
            Shutdown(code);
            return;
        }

        new MainWindow().Show();
    }
}
