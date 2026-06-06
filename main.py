#!/usr/bin/env python3
# =============================================================================
# main.py - Point d'entrée de GenSpeed
# =============================================================================
import os
import sys
import ctypes
import json
import tempfile
import shutil

from gui import GenSpeedGUI
from core import detect_targets, patch_target
from config import ConfigManager, detect_zh_install


def is_admin():
    """Vérifie si le script est exécuté avec des droits administrateur"""
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except:
        return False


def elevate_and_restart():
    """Relance le script avec des droits administrateur (en préservant les args)"""
    # On reconstruit la ligne d'arguments d'origine (sans le nom du script)
    extra = " ".join('"%s"' % a for a in sys.argv[1:])
    params = '"%s" %s' % (os.path.abspath(__file__), extra)
    ctypes.windll.shell32.ShellExecuteW(None, "runas", sys.executable, params, os.getcwd(), 1)
    sys.exit(0)


def _resolve_params_file(mode):
    """Retourne le chemin du fichier de params passé après --<mode>."""
    flag = '--' + mode
    if flag in sys.argv:
        idx = sys.argv.index(flag)
        if len(sys.argv) > idx + 1 and sys.argv[idx + 1].lower().endswith('.json'):
            return sys.argv[idx + 1]
    return os.path.join(tempfile.gettempdir(), "genspeed_%s_params.json" % mode)


def run_restore_mode():
    """Mode dépatch élevé : exécute UNIQUEMENT les opérations fichier dans le
    dossier du jeu (la GUI a déjà géré détection + état)."""
    params_file = _resolve_params_file('restore')
    if not os.path.exists(params_file):
        print("Erreur: fichier de paramètres introuvable")
        return

    with open(params_file, 'r', encoding='utf-8') as f:
        params = json.load(f)
    try:
        os.remove(params_file)
    except OSError:
        pass

    n_restored = 0
    for fp in params.get('restore', []):
        bak = fp + ".speedbak"
        if os.path.exists(bak):
            shutil.copy2(bak, fp)
            try:
                os.remove(bak)
            except OSError:
                pass
            n_restored += 1

    n_del = 0
    for fp in params.get('delbak', []):
        bak = fp + ".speedbak"
        if os.path.exists(bak):
            try:
                os.remove(bak)
                n_del += 1
            except OSError:
                pass

    print(f"Dépatch : {n_restored} restauré(s), {n_del} backup(s) périmé(s) supprimé(s)")


def run_patch_mode():
    """Mode patch : exécute le patch avec les paramètres du fichier temporaire"""
    params_file = _resolve_params_file('patch')

    if not os.path.exists(params_file):
        print("Erreur: fichier de paramètres introuvable")
        return

    with open(params_file, 'r', encoding='utf-8') as f:
        params = json.load(f)

    game_dir = params['game_dir']
    factors = params['factors']
    cam = params['cam']
    target_labels = params['targets']
    config_path = params.get('config_path')  # même config que la GUI

    # Nettoyer le fichier temporaire
    try:
        os.remove(params_file)
    except OSError as e:
        print(f"Avertissement: impossible de supprimer {params_file}: {e}")

    # Détecter les cibles
    all_targets = detect_targets(game_dir)
    targets = [t for t in all_targets if t['label'] in target_labels]

    if not targets:
        print("Erreur: aucune cible trouvée")
        return

    # Exécuter le patch
    print(f"Patching de {len(targets)} mod(s)...")

    config_mgr = ConfigManager(game_dir, config_path=config_path)
    state = config_mgr.load_state()
    patched_ok = []

    for target in targets:
        print(f"  {target['label']}...")

        # Empreintes du dernier patch pour ce mod (pour détecter les MAJ externes)
        prev_hashes = state.get(target['label'], {}).get('patched_files', {})

        try:
            patched_files, skipped = patch_target(target, factors, cam,
                                                  prev_hashes=prev_hashes, log=print)

            # --- Persister l'état RÉEL + empreintes des fichiers patchés ---
            state[target['label']] = {
                'factors': factors,
                'cam': cam,
                'patched_files': patched_files,
            }
            patched_ok.append(target['label'])
            print(f"  {target['label']} : OK ({len(patched_files)} patché(s), {skipped} ignoré(s))")
        except Exception as e:
            print(f"  {target['label']} : ERREUR - {e}")

    # Sauvegarde de l'état (un seul write atomique pour tous les mods OK)
    if patched_ok:
        config_mgr.save_state(state)
        print(f"État sauvegardé pour : {', '.join(patched_ok)}")

    print("Patch terminé avec succès")


def _ask_game_dir_dialog():
    """Demande à l'utilisateur de sélectionner le dossier du jeu."""
    import tkinter as tk
    from tkinter import filedialog, messagebox

    root = tk.Tk()
    root.withdraw()
    messagebox.showinfo(
        "GenSpeed",
        "Dossier de Command & Conquer Generals - Zero Hour introuvable.\n\n"
        "Sélectionne-le manuellement (il sera mémorisé pour les prochaines fois)."
    )
    path = filedialog.askdirectory(title="Sélectionne le dossier de Zero Hour")
    root.destroy()
    return path or None


def resolve_game_dir():
    """Localise le dossier du jeu, peu importe où se trouve GenSpeed.

    Ordre : argument CLI -> chemin mémorisé -> auto-détection Steam -> dialogue.
    Le chemin retenu est mémorisé dans la config utilisateur.
    """
    cm = ConfigManager()

    # 1) Argument en ligne de commande (prioritaire)
    if len(sys.argv) > 1 and os.path.isdir(sys.argv[1]):
        cm.save_game_dir(sys.argv[1])
        return sys.argv[1]

    # 2) Chemin mémorisé
    saved = cm.load_game_dir()
    if saved and os.path.isdir(saved):
        return saved

    # 3) Auto-détection Steam
    detected = detect_zh_install()
    if detected:
        cm.save_game_dir(detected)
        return detected

    # 4) Sélection manuelle
    chosen = _ask_game_dir_dialog()
    if chosen and os.path.isdir(chosen):
        cm.save_game_dir(chosen)
        return chosen

    return None


def make_root():
    """Crée la fenêtre racine, themée en sombre via ttkbootstrap si dispo.

    Fallback automatique vers tkinter.Tk() (thème ttk classique) si
    ttkbootstrap n'est pas installé.
    """
    # Identité d'appli distincte -> la barre des tâches Windows utilise NOTRE
    # icône (et non l'icône Python générique) et regroupe les fenêtres à part.
    try:
        ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("GenSpeed.CnC.ZeroHour.1")
    except Exception:
        pass

    try:
        import ttkbootstrap as tb
        root = tb.Window(themename="darkly")  # base sombre sobre
        root._gs_dark = True
        return root
    except Exception:
        import tkinter as tk
        root = tk.Tk()
        root._gs_dark = False
        return root


def main():
    """Point d'entrée principal"""
    # Mode patch (avec élévation admin)
    if '--patch' in sys.argv:
        if not is_admin():
            elevate_and_restart()
        else:
            run_patch_mode()
        return

    # Mode dépatch (avec élévation admin)
    if '--restore' in sys.argv:
        if not is_admin():
            elevate_and_restart()
        else:
            run_restore_mode()
        return

    # Déterminer le dossier du jeu (indépendant de l'emplacement de GenSpeed)
    game_dir = resolve_game_dir()

    if not game_dir or not os.path.isdir(game_dir):
        import tkinter as tk
        from tkinter import messagebox
        r = tk.Tk(); r.withdraw()
        messagebox.showerror("GenSpeed", "Dossier du jeu introuvable. Abandon.")
        r.destroy()
        sys.exit(1)

    # Lancer l'interface graphique (sans admin par défaut)
    from tkinter import messagebox

    root = make_root()

    try:
        # Rattaché à root pour garantir sa durée de vie (images, callbacks…)
        root._gs_app = GenSpeedGUI(root, game_dir)
        root.mainloop()
    except Exception as e:
        messagebox.showerror("GenSpeed", f"Erreur fatale : {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
