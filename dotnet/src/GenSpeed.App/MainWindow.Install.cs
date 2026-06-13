namespace GenSpeed.App;

public partial class MainWindow
{
    // ===== Assistant d'installation propre (wizard) =====
    /// <summary>Ouvre le wizard d'installation. À la fin, l'install créée/choisie est enregistrée
    /// (KnownInstalls) et le tableau principal est rafraîchi.</summary>
    private void OnInstallWizard()
        => InstallWizardWindow.Show(this, _config, Log, dir => { EnsureInstallListed(dir); LoadMods(); });
}
