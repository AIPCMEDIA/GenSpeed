using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;

namespace GenSpeed.App;

/// <summary>Langue courante + table de chaînes FR/EN, avec bascule à chaud.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc I { get; } = new();
    private Loc() { }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>0 = FR, 1 = EN.</summary>
    public int Lang { get; private set; } = 0;

    public void SetLanguage(int lang)
    {
        if (lang == Lang) return;
        Lang = lang;
        // Rafraîchit toutes les liaisons d'indexeur (Loc.I["clé"]).
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
        LanguageChanged?.Invoke();
    }

    /// <summary>Notifié après changement de langue (pour reconstruire le contenu généré en code).</summary>
    public static event Action? LanguageChanged;

    public string this[string key] =>
        Strings.TryGetValue(key, out var pair) ? pair[Lang] : $"[{key}]";

    /// <summary>Accès direct (code-behind).</summary>
    public static string T(string key) => I[key];

    // [0] = FR, [1] = EN
    private static readonly Dictionary<string, string[]> Strings = new()
    {
        ["subtitle"]    = ["Configurateur de vitesse — Generals Zero Hour", "Speed configurator — Generals Zero Hour"],
        ["help"]        = ["①  Mod(s)   →   ②  Vitesse + Caméra   →   ③  Appliquer            🌐 Vérification multijoueur : optionnelle",
                           "①  Mod(s)   →   ②  Speed + Camera   →   ③  Apply            🌐 Multiplayer check: optional"],
        ["tb.reset"]    = ["↺ Réinit. presets", "↺ Reset presets"],
        ["tb.log"]      = ["◀ Journal", "◀ Log"],
        ["tb.logshow"]  = ["▶ Journal", "▶ Log"],
        ["tb.launch"]   = ["▶  Lancer GenLauncher", "▶  Launch GenLauncher"],
        ["tb.lan"]      = ["🛡 Calculer mon code LAN", "🛡 Compute my LAN code"],
        ["tb.mp"]       = ["🌐 Multijoueur ▾", "🌐 Multiplayer ▾"],
        ["tb.preview"]  = ["🔎 Aperçu ▾", "🔎 Preview ▾"],
        ["tb.config"]   = ["⚙ Config ▾", "⚙ Config ▾"],
        ["tb.diag"]     = ["🩺 Diagnostic mismatch ▾", "🩺 Mismatch diagnostic ▾"],
        ["tb.theme"]    = ["Thème :", "Theme:"],

        ["card.mods"]   = ["①  Choisis un ou plusieurs mods", "①  Choose one or more mods"],
        ["card.speed"]  = ["②  Configuration vitesse", "②  Speed configuration"],
        ["card.camera"] = ["③  Configuration caméra", "③  Camera configuration"],
        ["card.apply"]  = ["④  Applique les modifications", "④  Apply the changes"],
        ["card.log"]    = ["Journal d'activité", "Activity log"],

        ["col.mod"]     = ["Jeu / Mod", "Game / Mod"],
        ["col.speed"]   = ["Vitesse", "Speed"],
        ["col.camera"]  = ["Caméra", "Camera"],
        ["col.archives"]= ["Archives", "Archives"],
        ["col.ini"]     = [".ini", ".ini"],
        ["col.patched"] = ["Patchés", "Patched"],
        ["col.code"]    = ["Code", "Code"],

        ["speed.global"]= ["Facteur global :", "Global factor:"],
        ["speed.factor"]= ["Facteur :", "Factor:"],
        ["crud.save"]   = ["💾 Sauvegarder", "💾 Save"],
        ["crud.new"]    = ["＋ Nouveau", "＋ New"],
        ["crud.rename"] = ["✏ Renommer", "✏ Rename"],
        ["crud.delete"] = ["🗑 Supprimer", "🗑 Delete"],

        ["dlg.newspeed"]= ["Nouveau preset vitesse", "New speed preset"],
        ["dlg.newcam"]  = ["Nouveau preset caméra", "New camera preset"],
        ["dlg.name"]    = ["Nom du preset :", "Preset name:"],
        ["dlg.rename"]  = ["Renommer", "Rename"],
        ["dlg.newname"] = ["Nouveau nom :", "New name:"],
        ["msg.locked"]  = ["« {0} » est verrouillé.", "“{0}” is locked."],
        ["msg.camlocked"]=["« Vue par défaut » n'est pas modifiable.", "“Default view” cannot be edited."],
        ["msg.exists"]  = ["Un preset « {0} » existe déjà.", "A preset “{0}” already exists."],
        ["msg.delconfirm"]=["Supprimer « {0} » ?", "Delete “{0}”?"],
        ["msg.minone"]  = ["Il faut au moins un preset.", "At least one preset is required."],
        ["log.psaved"]  = ["💾 Preset « {0} » sauvegardé.", "💾 Preset “{0}” saved."],
        ["log.pnew"]    = ["＋ Preset « {0} » créé.", "＋ Preset “{0}” created."],
        ["log.prenamed"]= ["✏ Preset « {0} » → « {1} ».", "✏ Preset “{0}” → “{1}”."],
        ["log.pdeleted"]= ["🗑 Preset « {0} » supprimé.", "🗑 Preset “{0}” deleted."],
        ["speed.fine"]  = ["Réglage fin par catégorie  (× multiplie,  ÷ divise)",
                           "Fine tuning per category  (× multiply,  ÷ divide)"],
        ["cam.preset"]  = ["Preset caméra :", "Camera preset:"],
        ["cam.default"] = ["Vue par défaut", "Default view"],
        ["campreset.high"] = ["Cam haute", "High cam"],
        ["campreset.max"]  = ["Cam max", "Max cam"],
        ["campreset.far"]  = ["Cam éloignée", "Far cam"],
        ["campreset.sat"]  = ["Vue satellite", "Satellite view"],
        ["cam.fine"]    = ["Réglage fin de la caméra  (vide = défaut du jeu)",
                           "Camera fine tuning  (empty = game default)"],

        ["apply.btn"]   = ["🚀  Appliquer la config (vitesse + caméra) au(x) mod(s) coché(s)",
                           "🚀  Apply config (speed + camera) to checked mod(s)"],
        ["apply.cancel"]= ["↩  Annuler / Revenir à l'original", "↩  Cancel / Revert to original"],

        // Confirmation avant patch
        ["confirm.title"]   = ["Confirmer l'application", "Confirm apply"],
        ["confirm.intro"]   = ["Mods concernés :", "Affected mods:"],
        ["confirm.changes"] = ["Ce que ça va changer en jeu :", "What it changes in-game:"],
        ["confirm.note"]    = ["💾 Une sauvegarde « .speedbak » est créée à côté de CHAQUE fichier modifié (dans {0} et dans les dossiers des mods). « Annuler » les restaure. Une élévation Windows (UAC) sera demandée.",
                               "💾 A “.speedbak” backup is created next to EACH modified file (in {0} and in the mod folders). “Cancel” restores them. A Windows elevation (UAC) prompt will appear."],
        // Effets joueur par catégorie ({0} = facteur)
        ["fx.none"]              = ["Aucun changement (tout est à ×1).", "No change (everything at ×1)."],
        ["fx.deplacement"]      = ["🏃 Déplacement : unités {0}× plus rapides", "🏃 Movement: units {0}× faster"],
        ["fx.projectiles"]      = ["💥 Projectiles {0}× plus rapides", "💥 Projectiles {0}× faster"],
        ["fx.visee"]            = ["🎯 Tourelles : rotation {0}× plus rapide", "🎯 Turrets: {0}× faster rotation"],
        ["fx.construction"]     = ["🏗️ Construction {0}× plus rapide", "🏗️ Construction {0}× faster"],
        ["fx.tir"]              = ["🔫 Cadence de tir {0}× plus rapide", "🔫 Rate of fire {0}× faster"],
        ["fx.pouvoirs"]         = ["⚡ Pouvoirs : recharge {0}× plus rapide", "⚡ Powers: {0}× faster cooldown"],
        ["fx.deploiement"]      = ["📦 Déploiement {0}× plus rapide", "📦 Deployment {0}× faster"],
        ["fx.economie_collecte"]= ["💰 Collecte des ressources {0}× plus rapide", "💰 Resource gathering {0}× faster"],
        ["fx.economie_gain"]    = ["💵 Caisses : valeur ×{0}", "💵 Crates: value ×{0}"],
        ["fx.detection"]        = ["👁️ Vision : portée ×{0}", "👁️ Vision range ×{0}"],
        ["fx.soin"]             = ["➕ Soin {0}× plus efficace", "➕ Healing {0}× more effective"],
        ["fx.merite"]           = ["⭐ XP/mérite ×{0} (promotions plus rapides)", "⭐ XP/merit ×{0} (faster promotions)"],
        ["fx.camera"]           = ["📷 Caméra : {0}", "📷 Camera: {0}"],
        ["cam.custom"]          = ["réglages personnalisés", "custom settings"],
        // Sélection manuelle du dossier de jeu
        ["pick.title"]          = ["Sélectionne le dossier de Generals Zero Hour", "Select the Generals Zero Hour folder"],
        ["pick.invalid"]        = ["Ce dossier ne ressemble pas à une installation de Zero Hour. Réessaie.",
                                   "This folder doesn't look like a Zero Hour install. Try again."],
        ["confirm.ok"]      = ["✅ Valider", "✅ Confirm"],
        ["confirm.cancel"]  = ["Annuler", "Cancel"],
        // Résultat après application
        ["result.title"]    = ["✅ Appliqué", "✅ Applied"],
        ["result.body"]     = ["Configuration appliquée aux mods cochés.\n\nTon code LAN :", "Config applied to checked mods.\n\nYour LAN code:"],
        ["result.launch"]   = ["▶ Lancer GenLauncher", "▶ Launch GenLauncher"],
        ["result.close"]    = ["Fermer", "Close"],
        ["genl.notfound"]   = ["GenLauncher.exe introuvable dans le dossier du jeu.", "GenLauncher.exe not found in the game folder."],
        ["genl.launched"]   = ["▶ GenLauncher lancé.", "▶ GenLauncher launched."],
        ["genl.cancel"]     = ["Lancement de GenLauncher annulé/refusé.", "GenLauncher launch cancelled/denied."],
        // Menu contextuel mods
        ["ctx.open"]        = ["📁 Ouvrir le dossier du mod", "📁 Open mod folder"],
        ["ctx.copy"]        = ["Copier", "Copy"],
        ["apply.note"]  = ["« Appliquer » modifie le mod (sauvegarde auto). « Annuler » restaure l'original.",
                           "“Apply” modifies the mod (auto-backup). “Cancel” restores the original."],

        // Catégories
        ["cat.deplacement"]      = ["Déplacement", "Movement"],
        ["cat.projectiles"]      = ["Projectiles", "Projectiles"],
        ["cat.visee"]            = ["Visée (tourelles)", "Aiming (turrets)"],
        ["cat.construction"]     = ["Construction", "Construction"],
        ["cat.tir"]              = ["Tir", "Firing"],
        ["cat.pouvoirs"]         = ["Pouvoirs (recharge)", "Powers (cooldown)"],
        ["cat.deploiement"]      = ["Déploiement", "Deployment"],
        ["cat.economie_collecte"]= ["Collecte", "Gathering"],
        ["cat.economie_gain"]    = ["Gain caisse", "Crate value"],
        ["cat.detection"]        = ["Vision", "Vision"],
        ["cat.soin"]             = ["Soin", "Healing"],
        ["cat.merite"]           = ["Mérite (XP)", "Merit (XP)"],

        // Presets vitesse
        ["preset.0"] = ["Original", "Original"],
        ["preset.1"] = ["Cool", "Cool"],
        ["preset.2"] = ["Énervé", "Angry"],
        ["preset.3"] = ["Déchaîné", "Unleashed"],

        // Hints caméra
        ["cam.hint.pitch"] = ["37.5 (inclinaison)", "37.5 (pitch)"],
        ["cam.hint.h"]     = ["232 (hauteur de départ)", "232 (start height)"],
        ["cam.hint.max"]   = ["310 (zoom max, ~800 conseillé)", "310 (max zoom, ~800 advised)"],
        ["cam.hint.min"]   = ["120 (zoom min)", "120 (min zoom)"],
        ["cam.hint.terrain"]= ["Yes/No (afficher tout le terrain)", "Yes/No (draw entire terrain)"],

        ["orig"] = ["Original", "Original"],
        ["vanilla.name"] = ["🎮 Jeu de base (sans mod)", "🎮 Base game (no mod)"],
        ["vanilla.ini"]  = ["🎮 Jeu de base — fichiers INI", "🎮 Base game — INI files"],

        // Aperçu
        ["preview.key"]  = ["Valeurs clés", "Key values"],
        ["preview.full"] = ["🔍 Exhaustif", "🔍 Exhaustive"],
        ["preview.mod"]  = ["🟢 Modifs seulement", "🟢 Changes only"],
        ["preview.title"]= ["Aperçu — {0}", "Preview — {0}"],
        ["preview.nosel"]= ["Coche d'abord un mod dans la liste.", "Check a mod in the list first."],
        ["preview.none"] = ["Aucune variable trouvée.", "No variable found."],
        ["preview.notpatched"]=["Ce mod n'est pas patché : aucune modification à afficher.",
                                "This mod isn't patched: nothing to show."],
        ["preview.summary"]=["{0} variable(s){1}", "{0} variable(s){1}"],
        ["preview.changed"]=[", {0} modifiée(s)", ", {0} changed"],
        ["preview.col.var"]= ["Variable", "Variable"],
        ["preview.col.orig"]=["Original", "Original"],
        ["preview.col.cur"]= ["Actuel", "Current"],
        ["preview.col.loc"]= ["Localisation", "Location"],

        // Dernier replay
        ["mp.replay"]    = ["📜 Dernier replay (version + map)", "📜 Last replay (version + map)"],
        ["replay.none"]  = ["Aucun replay trouvé. Joue une partie d'abord.", "No replay found. Play a game first."],
        ["replay.title"] = ["📜 Dernière partie", "📜 Last game"],
        ["replay.body"]  = ["VERSION (à comparer entre joueurs) :\n→  {0}\n\nMap : {1}\nCRC map : {2}\nJoueurs : {3}\n\nPour jouer en LAN sans mismatch, la ligne VERSION et le CRC map doivent être IDENTIQUES chez tous.",
                            "VERSION (compare between players):\n→  {0}\n\nMap: {1}\nMap CRC: {2}\nPlayers: {3}\n\nTo play LAN without mismatch, the VERSION line and map CRC must be IDENTICAL for everyone."],

        // Diagnostic mismatch
        ["diag.export"]  = ["🩺 Exporter mon diagnostic…", "🩺 Export my diagnostic…"],
        ["diag.compare"] = ["🔍 Comparer avec un ami…", "🔍 Compare with a friend…"],
        ["diag.title"]   = ["🩺 Diagnostic mismatch", "🩺 Mismatch diagnostic"],
        ["diag.badfile"] = ["Ce fichier n'est pas un diagnostic de synchro GenSpeed.\nDemande à l'autre joueur de l'exporter depuis « 🩺 Diagnostic ».",
                            "This file is not a GenSpeed sync diagnostic.\nAsk the other player to export it from “🩺 Diagnostic”."],
        ["diag.exported"]= ["🩺 Diagnostic exporté : {0}", "🩺 Diagnostic exported: {0}"],
        ["diag.ndiff"]   = ["{0} différence(s) détectée(s) :", "{0} difference(s) found:"],
        ["diag.allok"]   = ["✅ Tout le contenu déterminant est identique. Un désync viendrait alors du CPU/réseau, pas des fichiers.",
                            "✅ All deciding content is identical. A desync would then come from CPU/network, not files."],
        ["diag.crit"]    = ["❌ Mismatch quasi certain — {0} élément(s) déterminant(s) diffèrent (jeu / INI / mod).",
                            "❌ Mismatch almost certain — {0} deciding item(s) differ (game / INI / mod)."],
        ["diag.warn"]    = ["⚠️ Jeu identique, mais {0} map(s)/addon(s) diffèrent — mismatch possible si concernés.",
                            "⚠️ Same game, but {0} map(s)/add-on(s) differ — possible mismatch if used."],
        ["diag.legend"]  = ["❌ critique = empêche la synchro   ·   ⚠ attention = map/addon   ·   ℹ info = contexte",
                            "❌ critical = blocks sync   ·   ⚠ warning = map/add-on   ·   ℹ info = context"],
        ["diag.col.sev"] = ["Gravité", "Severity"],
        ["diag.col.item"]= ["Élément", "Item"],
        ["diag.col.detail"]=["Détail (toi ↔ ami)", "Detail (you ↔ friend)"],
        ["sev.crit"]     = ["❌ CRITIQUE", "❌ CRITICAL"],
        ["sev.warn"]     = ["⚠ ATTENTION", "⚠ WARNING"],
        ["sev.info"]     = ["ℹ INFO", "ℹ INFO"],
        ["st.diff"]      = ["DIFFÉRENT", "DIFFERENT"],
        ["st.absme"]     = ["absent chez toi", "missing on your side"],
        ["st.absother"]  = ["absent chez l'ami", "missing on friend's side"],
        ["sec.base"]     = ["Données de base", "Base game data"],
        ["sec.ini"]      = ["INI lâches", "Loose INI"],
        ["sec.maps"]     = ["Maps", "Maps"],
        ["sec.gentool"]  = ["GenTool / overlay", "GenTool / overlay"],
        ["sec.components"]= ["Composants", "Components"],
        ["sec.mod"]      = ["Mod", "Mod"],

        // Code LAN
        ["lan.computing"]= ["🛡 Calcul du code LAN…", "🛡 Computing LAN code…"],
        ["lan.done"]     = ["🛡 Ton code LAN : {0}   ({1} fichiers, {2} Mo)", "🛡 Your LAN code: {0}   ({1} files, {2} MB)"],

        // Menu config
        ["cfg.reset"]    = ["↺ Réinitialiser les champs", "↺ Reset fields"],
        ["cfg.export"]   = ["📤 Exporter la config…", "📤 Export config…"],
        ["cfg.import"]   = ["📥 Importer une config…", "📥 Import config…"],
        ["cfg.reset.done"]=["↺ Champs réinitialisés.", "↺ Fields reset."],
        ["cfg.exported"] = ["📤 Config exportée : {0}", "📤 Config exported: {0}"],
        ["cfg.imported"] = ["📥 Config importée : {0}", "📥 Config imported: {0}"],

        // Journal
        ["log.start"]    = ["GenSpeed (.NET 8 / WPF) démarré.", "GenSpeed (.NET 8 / WPF) started."],
        ["log.detected"] = ["{0} cible(s) détectée(s).", "{0} target(s) detected."],
        ["log.gamedir"]  = ["Dossier jeu : {0}", "Game folder: {0}"],
        ["log.nogame"]   = ["Dossier du jeu introuvable (sélection manuelle à venir).",
                            "Game folder not found (manual selection coming soon)."],
        ["log.nosel"]    = ["Aucun mod coché.", "No mod checked."],
        ["log.applying"] = ["⏳ Application en cours (autorise l'élévation UAC)…",
                            "⏳ Applying (allow the UAC prompt)…"],
        ["log.restoring"]= ["⏳ Restauration en cours (autorise l'élévation UAC)…",
                            "⏳ Restoring (allow the UAC prompt)…"],
        ["log.uaccancel"]= ["Élévation refusée/annulée — rien n'a été modifié.",
                            "Elevation declined/cancelled — nothing changed."],
        ["log.noresult"] = ["Aucun résultat renvoyé par l'opération.", "No result returned by the operation."],
        ["log.applied"]  = ["✅ Configuration appliquée aux mods cochés.", "✅ Configuration applied to checked mods."],
        ["log.restored"] = ["↩ Mods restaurés à l'original.", "↩ Mods restored to original."],
        ["log.filespatched"] = ["fichier(s) patché(s)", "file(s) patched"],
        ["log.restoredmod"]  = ["restauré", "restored"],
    };
}

/// <summary>Extension de balisage : {l:Tr clé} → liaison vers Loc.I["clé"] (mise à jour à chaud).</summary>
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
