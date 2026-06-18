using System.IO;
using System.Linq;
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

        // Le SPLASH s'affiche EN PREMIER (avant toute fenêtre principale → plus de « microseconde » où le tableau
        // apparaît). La fenêtre principale n'est créée/affichée qu'à la fin du splash, et ouvre alors l'assistant.
        // ShutdownMode explicite : la fermeture du splash (dernière fenêtre à cet instant) ne doit PAS arrêter l'app ;
        // l'arrêt est déclenché explicitement (croix de l'assistant, ou fermeture de la fenêtre principale).
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Réveil ANTICIPÉ des disques des installs enregistrées : un disque secondaire endormi (ex. G:) répond
        // « dossier inexistant » au tout premier accès, le temps de se réveiller. On touche les chemins en tâche de
        // fond PENDANT le splash → ils sont prêts quand le scan (mode avancé) tourne juste après. (Le hub, lui, fait
        // déjà confiance au JSON via DiscoverForDisplay.)
        try
        {
            var known = ConfigStore.Load().KnownInstalls.ToList();
            System.Threading.Tasks.Task.Run(() =>
            {
                foreach (var p in known)
                    for (int i = 0; i < 12; i++) { try { if (Directory.Exists(p)) break; } catch { } System.Threading.Thread.Sleep(250); }
            });
        }
        catch { }

        SplashWindow.Run(() =>
        {
            // La fenêtre principale est créée mais PAS affichée : l'assistant (non-modal) est la seule fenêtre visible
            // au démarrage → aucun clignotement du tableau. MainWindow n'apparaît qu'en « Mode avancé ».
            var main = new MainWindow();
            main.OpenAssistant();
        });
    }
}
