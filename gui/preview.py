# Aperçus des variables INI (clés, exhaustif, modifications).
import os
import re

from tkinter import messagebox

from core import read_big, BigFileError, PREVIEW_VARS


class PreviewMixin:
    """Lecture et affichage des aperçus de variables mod."""

    def _iter_inis(self, fp, is_gib):
        """Itère (label, texte) sur les .ini d'un fichier."""
        base = os.path.basename(fp)
        if is_gib:
            raw, files = read_big(fp)
            for fo in files:
                if fo['name'].lower().endswith('.ini'):
                    txt = raw[fo['off']:fo['off'] + fo['sz']].decode('latin-1', errors='ignore')
                    yield f"{base} › {fo['name']}", txt
        else:
            with open(fp, 'r', encoding='latin-1', errors='ignore') as f:
                yield base, f.read()

    def _located_vars(self, paths, is_gib, wanted=None):
        """Première occurrence de chaque variable AVEC sa localisation."""
        out = {}
        pattern = re.compile(r'^[ \t]*([A-Za-z][A-Za-z0-9_]*)\s*=\s*([^\s;]+)', re.MULTILINE)
        for fp in paths:
            try:
                for label, text in self._iter_inis(fp, is_gib):
                    for m in pattern.finditer(text):
                        n = m.group(1)
                        if wanted is not None and n not in wanted:
                            continue
                        if n in out:
                            continue
                        lineno = text.count('\n', 0, m.start()) + 1
                        out[n] = (m.group(2), label, lineno)
            except (BigFileError, IOError) as e:
                self._log(f"  ⚠️ Erreur lecture de {os.path.basename(fp)}: {e}")
        return out

    def _orig_cur_located(self, target, wanted=None):
        """Retourne (orig, cur) en {var:(valeur,source,ligne)}."""
        is_gib = target['type'] == 'gib'
        orig_paths = [(fp + ".speedbak") if os.path.exists(fp + ".speedbak") else fp
                      for fp in target['files']]
        cur_paths = list(target['files'])
        orig = self._located_vars(orig_paths, is_gib, wanted)
        cur = self._located_vars(cur_paths, is_gib, wanted)
        return orig, cur

    def _gather_rows(self, target, wanted=None, only_changed=False):
        """Construit les lignes (var, orig, actuel, loc, modifié) pour un tableau."""
        patched = any(os.path.exists(fp + ".speedbak") for fp in target['files'])
        orig, cur = self._orig_cur_located(target, wanted=wanted)
        names = sorted(set(orig) | set(cur))
        rows = []
        n_changed = 0
        for n in names:
            ov = orig.get(n, (None,))[0]
            cur_e = cur.get(n)
            cv = cur_e[0] if cur_e else None
            ref = orig.get(n) or cur.get(n)
            loc = f"{ref[1]}:{ref[2]}" if ref else ""
            modified = patched and cv is not None and cv != ov
            if modified:
                n_changed += 1
            if only_changed and not modified:
                continue
            rows.append((n, ov if ov is not None else '',
                         (cv if cv is not None else '') if patched else '',
                         loc, modified))
        return rows, patched, n_changed

    def _preview_original_values(self, target):
        """Tableau des variables clés (original → actuel)."""
        rows, patched, n_changed = self._gather_rows(target, wanted=PREVIEW_VARS)
        if not rows:
            messagebox.showinfo("GenSpeed", "Aucune variable standard trouvée.")
            return
        status = self.config_manager.get_target_status(target['label'])
        header = (f"{target['label']}   —   {status}\n"
                  f"Valeurs clés : {len(rows)} variable(s)"
                  + (f", {n_changed} modifiée(s)" if patched else ""))
        self._show_table_window(f"Aperçu (valeurs clés) — {target['label']}", header, rows)

    def _show_original_preview(self):
        """Affiche un aperçu des valeurs originales."""
        targets = self._selected_targets()
        if not targets:
            messagebox.showwarning("GenSpeed", "Sélectionne d'abord au moins un mod dans la liste.")
            return

        if len(targets) > 1:
            self._select_target_dialog(targets, self._preview_original_values)
        else:
            self._preview_original_values(targets[0])

    def _preview_exhaustive_values(self, target):
        """Tableau de TOUTES les variables (modifiées surlignées)."""
        rows, patched, n_changed = self._gather_rows(target)
        if not rows:
            messagebox.showinfo("GenSpeed", "Aucune variable trouvée.")
            return
        status = self.config_manager.get_target_status(target['label'])
        header = (f"{target['label']}   —   {status}\n"
                  f"{len(rows)} variables uniques"
                  + (f", {n_changed} modifiée(s) par le patch (surlignées)" if patched else ""))
        self._show_table_window(f"Aperçu exhaustif — {target['label']}", header, rows)

    def _show_exhaustive_preview(self):
        """Affiche un aperçu exhaustif."""
        targets = self._selected_targets()
        if not targets:
            messagebox.showwarning("GenSpeed", "Sélectionne d'abord au moins un mod dans la liste.")
            return

        if len(targets) > 1:
            self._select_target_dialog(targets, self._preview_exhaustive_values)
        else:
            self._preview_exhaustive_values(targets[0])

    def _preview_modified_values(self, target):
        """Tableau des variables UNIQUEMENT modifiées par le patch."""
        patched = any(os.path.exists(fp + ".speedbak") for fp in target['files'])
        if not patched:
            messagebox.showinfo("GenSpeed", "Ce mod n'est pas patché : aucune modification à afficher.")
            return
        rows, _patched, n_changed = self._gather_rows(target, only_changed=True)
        if not rows:
            messagebox.showinfo("GenSpeed", "Aucune valeur modifiée détectée.")
            return
        status = self.config_manager.get_target_status(target['label'])
        header = f"{target['label']}   —   {status}\n{len(rows)} variable(s) modifiée(s) (original → actuel)"
        self._show_table_window(f"Modifications — {target['label']}", header, rows)

    def _show_modified_preview(self):
        """Affiche uniquement les modifications."""
        targets = self._selected_targets()
        if not targets:
            messagebox.showwarning("GenSpeed", "Sélectionne d'abord au moins un mod dans la liste.")
            return

        if len(targets) > 1:
            self._select_target_dialog(targets, self._preview_modified_values)
        else:
            self._preview_modified_values(targets[0])
