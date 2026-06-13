namespace GenSpeed.App;

public partial class MainWindow
{
    // ===== Assistant d'installation propre (wizard) =====
    /// <summary>Ouvre le wizard d'installation. À la fin, l'install créée/choisie est enregistrée
    /// (KnownInstalls) et le tableau principal est rafraîchi.</summary>
    private void OnInstallWizard()
        => InstallWizardWindow.Show(this, _config, Log, dir => { EnsureInstallListed(dir); LoadMods(); });

    /// <summary>Au démarrage sans aucune install détectée : proposer 2 choix clairs (Installer le jeu
    /// via Steam → ouvre l'assistant ; ou indiquer un dossier existant) au lieu d'un sélecteur Windows brut.</summary>
    /// <returns>Vrai si une action a été menée (l'appelant doit re-scanner les installs).</returns>
    private bool PromptNoInstall()
    {
        string steam = Loc.T("noinst.steam");
        string folder = Loc.T("noinst.folder");
        string? pick = Dialogs.Choose(this, Loc.T("wiz.title"), Loc.T("noinst.msg"), new[] { steam, folder });
        if (pick == steam) { OnInstallWizard(); return true; }   // l'assistant gère Steam + la suite
        if (pick == folder)
        {
            var dir = AskGameDir();
            if (dir == null) return false;
            EnsureInstallListed(dir);
            return true;
        }
        return false;   // annulé
    }
}
