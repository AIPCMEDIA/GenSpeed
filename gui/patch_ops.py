# Workflows patch / dépatch (élévation admin + attente processus).
import ctypes
import threading
import tkinter as tk

from tkinter import messagebox

from core import (
    validate_factors, InvalidFactorError, lan_hash,
    classify_restore, elevate_and_patch, elevate_and_restore,
)
from config import CAM_VARS


class PatchOpsMixin:
    """Application et annulation des patches."""

    def _get_factors(self):
        """Retourne les facteurs avec validation."""
        try:
            from core import CATS
            factors = {c: float(self.cat_vars[c].get() or 1) for c in CATS}
            return validate_factors(factors)
        except InvalidFactorError as e:
            messagebox.showerror("GenSpeed", str(e))
            return None

    def _do_apply_selected(self):
        """Patcher les mods sélectionnés."""
        targets = self._selected_targets()
        if not targets:
            messagebox.showwarning("GenSpeed", "Sélectionne d'abord au moins un mod dans la liste.")
            return

        factors = self._get_factors()
        if factors is None:
            return

        self.camera_config.from_dict({v: self.cam_vars[v].get().strip() for v in CAM_VARS})
        try:
            cam = self.camera_config.get_validated_config()
        except ValueError as e:
            messagebox.showerror("GenSpeed", f"Caméra invalide : {e}")
            return

        mods_list = [t['label'] for t in targets]

        if not self._confirm_patch_dialog(mods_list, factors, cam):
            self._log("Patch annulé")
            return

        state = self.config_manager.load_state()
        state['last_factors'] = factors
        state['last_cam'] = cam
        self.config_manager.save_state(state)

        self._log("Élévation des privilèges administrateur requise pour le patch...")
        hproc = elevate_and_patch(self.game_dir, factors, cam, targets,
                                  config_path=self.config_manager.config_file)

        if not hproc:
            self._log("Patch annulé (UAC refusé) ou échec de l'élévation.")
            return

        self._log("Patch en cours dans le process admin... (fenêtre conservée)")
        self._set_buttons_busy(True)
        mods_done = [t['label'] for t in targets]

        def _wait_and_finish():
            ctypes.windll.kernel32.WaitForSingleObject(ctypes.c_void_p(hproc), 0xFFFFFFFF)
            ctypes.windll.kernel32.CloseHandle(ctypes.c_void_p(hproc))
            self.root.after(0, lambda: self._on_patch_finished(mods_done))

        threading.Thread(target=_wait_and_finish, daemon=True).start()

    def _set_buttons_busy(self, busy):
        """Active/désactive les boutons Patcher/Dépatcher pendant l'opération."""
        state = 'disabled' if busy else 'normal'
        try:
            self.btn_patch.config(state=state)
            self.btn_restore.config(state=state)
            self.root.config(cursor="watch" if busy else "")
        except tk.TclError:
            pass

    def _on_patch_finished(self, mods_done):
        """Appelé (thread UI) quand le process admin de patch est terminé."""
        self._set_buttons_busy(False)
        self.state = self.config_manager.load_state()
        self._refresh_list()
        self._update_hash()
        self._log(f"✅ Patch terminé : {', '.join(mods_done)}")
        h, n = lan_hash(self.game_dir)
        self._show_post_patch_dialog(mods_done, h, n)

    def _do_restore_selected(self):
        """Dépatcher les mods sélectionnés."""
        targets = self._selected_targets()
        if not targets:
            messagebox.showwarning("GenSpeed", "Sélectionne d'abord au moins un mod dans la liste.")
            return

        mods_list = [t['label'] for t in targets]

        confirm = messagebox.askyesno(
            "Confirmation",
            f"Tu t'apprêtes à dépatcher {len(targets)} mod(s) :\n\n" +
            "\n".join(f"  • {m}" for m in mods_list) +
            "\n\nContinuer ?",
            icon='warning'
        )
        if not confirm:
            self._log("Dépatcher les mods sélectionnés annulé")
            return

        state = self.config_manager.load_state()
        restore_files = []
        stale = {}

        for t in targets:
            expected = state.get(t['label'], {}).get('patched_files', {})
            to_restore, st = classify_restore(t, expected=expected)
            restore_files += to_restore
            if st:
                stale[t['label']] = st

        delbak_files = []
        delete_stale = False
        if stale:
            total = sum(len(v) for v in stale.values())
            detail = "\n".join(f"  • {lbl} : {len(files)} fichier(s)" for lbl, files in stale.items())
            delete_stale = messagebox.askyesno(
                "Backups périmés détectés",
                f"{total} fichier(s) ont été MODIFIÉS en dehors de GenSpeed "
                f"(probablement une mise à jour de mod ou un addon) :\n\n{detail}\n\n"
                "Restaurer écraserait ces nouvelles versions avec d'anciens backups.\n\n"
                "Veux-tu SUPPRIMER ces backups périmés et GARDER la version actuelle ?\n"
                "(Non = on laisse ces fichiers tels quels)",
                icon='warning'
            )
            if delete_stale:
                for files in stale.values():
                    delbak_files += files

        if not restore_files and not delbak_files:
            self._log("Rien à dépatcher (aucun backup applicable).")
            return

        for t in targets:
            lbl = t['label']
            if lbl not in stale or delete_stale:
                state.pop(lbl, None)
        self.config_manager.save_state(state)

        self._log(f"Élévation admin pour le dépatch ({len(restore_files)} restauration(s), "
                  f"{len(delbak_files)} backup(s) périmé(s))...")
        hproc = elevate_and_restore(restore_files, delbak_files)
        if not hproc:
            self._log("Dépatch annulé (UAC refusé) ou échec de l'élévation.")
            return

        self._set_buttons_busy(True)

        def _wait_and_finish():
            ctypes.windll.kernel32.WaitForSingleObject(ctypes.c_void_p(hproc), 0xFFFFFFFF)
            ctypes.windll.kernel32.CloseHandle(ctypes.c_void_p(hproc))
            self.root.after(0, self._on_restore_finished)

        threading.Thread(target=_wait_and_finish, daemon=True).start()

    def _on_restore_finished(self):
        """Appelé (thread UI) quand le process admin de dépatch est terminé."""
        self._set_buttons_busy(False)
        self.state = self.config_manager.load_state()
        self._refresh_list()
        self._update_hash()
        self._log("✅ Dépatch terminé.")
        messagebox.showinfo("GenSpeed", "Dépatch terminé. Les mods concernés sont revenus à l'original.")
