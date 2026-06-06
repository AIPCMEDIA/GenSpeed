# Hash LAN, compatibilité multijoueur et liste des fichiers patchés.
import os
import json
from collections import defaultdict

import tkinter as tk
from tkinter import messagebox, ttk, filedialog

from core import (lan_hash, install_hash, base_install_files, list_patched_files,
                  find_latest_replay, read_replay_fingerprint,
                  compat_report, diff_compat_reports)

from gui.widgets import ToolTip


class HashOpsMixin:
    """Empreintes LAN et vérification de compatibilité."""

    def _setup_hash_section(self):
        """Configure la section de hash LAN (mode avancé)."""
        hf = ttk.Frame(self.root, padding=(10, 5))
        self.fr_hash = hf
        self.hash_lbl = ttk.Label(hf, text="", style="Hash.TLabel")
        self.hash_lbl.pack(side='left', anchor='w')
        btn_inst = ttk.Button(hf, text="🔑 Hash installation (LAN)",
                              command=self._do_install_hash, width=24)
        btn_inst.pack(side='right')
        ttk.Button(hf, text="📂 Détails fichiers patchés",
                   command=self._show_patched_files, width=24).pack(side='right', padx=(0, 4))
        btn_cmp = ttk.Button(hf, text="🔍 Comparer rapport ami",
                             command=self._compare_compat_report, width=24)
        btn_cmp.pack(side='right', padx=(0, 4))
        btn_exp = ttk.Button(hf, text="📤 Exporter rapport",
                             command=self._export_compat_report, width=20)
        btn_exp.pack(side='right', padx=(0, 4))
        btn_rep = ttk.Button(hf, text="📜 Dernier replay (versions)",
                             command=self._show_replay_fingerprint, width=26)
        btn_rep.pack(side='right', padx=(0, 4))
        ToolTip(btn_exp, "Crée un rapport détaillé (empreinte par fichier) de ton install. "
                         "À envoyer à l'autre joueur.")
        ToolTip(btn_cmp, "Charge le rapport d'un ami et liste EXACTEMENT les fichiers qui diffèrent.")
        ToolTip(btn_inst, "Empreinte de TOUTES les archives du/des mod(s) coché(s), "
                           "patchées ou non. À comparer entre joueurs pour vérifier "
                           "que vous avez exactement les mêmes fichiers (diagnostic désync LAN).")
        ToolTip(btn_rep, "Lit la version (jeu + mod + patch) et la map de TA dernière partie. "
                         "Compare cette ligne avec l'autre joueur pour diagnostiquer un mismatch.")
        self._update_hash()

    def _show_replay_fingerprint(self):
        """Affiche l'empreinte (versions, map) de la dernière partie jouée."""
        path = find_latest_replay()
        if not path:
            messagebox.showinfo("Dernier replay",
                                "Aucun replay trouvé.\n\nJoue une partie d'abord — Zero Hour enregistre "
                                "automatiquement la dernière partie.")
            return
        fp = read_replay_fingerprint(path)
        if not fp:
            messagebox.showwarning("Dernier replay", "Replay illisible.")
            return
        players = ", ".join(fp['players']) if fp['players'] else "—"
        messagebox.showinfo(
            "📜 Empreinte de la dernière partie",
            f"VERSION (à comparer entre joueurs) :\n→  {fp['version'] or '—'}\n\n"
            f"Map : {fp['map'] or '—'}\n"
            f"CRC map : {fp['map_crc'] or '—'}\n"
            f"Joueurs : {players}\n\n"
            "Pour jouer en LAN sans mismatch, la ligne VERSION et le CRC map doivent être\n"
            "IDENTIQUES chez tous les joueurs (même jeu, même mod, même version de patch, même map)."
        )

    def _do_compat_check(self):
        """Calcule un code de compatibilité unique (base + mods cochés)."""
        targets = self._selected_targets()
        self.compat_code_lbl.config(text="Calcul…")
        self.root.update_idletasks()
        files = list(base_install_files(self.game_dir))
        for t in targets:
            files += t['files']
        code, n, total = install_hash(self.game_dir, files)
        self.compat_code_lbl.config(text=f"Votre code : {code}")
        if targets:
            scope = "base Steam (vanilla) + " + ", ".join(t['label'] for t in targets)
            jeu = "le(s) même(s) mod(s)"
        else:
            scope = "base Steam (vanilla) seule"
            jeu = "le jeu vanilla (sans mod)"
        messagebox.showinfo(
            "🌐 Vérification multijoueur (LAN)",
            f"Votre code :   {code}\n\n"
            f"Calculé sur : {scope}\n"
            f"({n} fichiers, {total / 1048576:.0f} Mo)\n\n"
            "À QUOI ÇA SERT :\n"
            "Ce code résume TES fichiers de jeu. Pour jouer en LAN ensemble, tous les\n"
            f"joueurs doivent afficher EXACTEMENT le même code (donc {jeu}, même version,\n"
            "et les mêmes réglages GenSpeed appliqués).\n\n"
            "• Vanilla : ne coche aucun mod, le code couvre la base Steam.\n"
            "• Avec mod(s) : coche le(s) mod(s) que vous jouez tous.\n\n"
            "Si le code diffère entre joueurs → fichiers différents (version, mod, patch "
            "ou langue) → risque de désync."
        )

    def _export_compat_report(self):
        """Exporte un rapport détaillé (empreinte par fichier) base + mods cochés."""
        targets = self._selected_targets()
        self._set_buttons_busy(True)
        self.root.update_idletasks()
        try:
            rep = compat_report(self.game_dir, targets)
        finally:
            self._set_buttons_busy(False)
        fn = filedialog.asksaveasfilename(
            title="Exporter mon rapport de compatibilité",
            defaultextension=".json", initialfile="GenSpeed-rapport-compat.json",
            filetypes=[("JSON", "*.json")])
        if not fn:
            return
        try:
            with open(fn, 'w', encoding='utf-8') as f:
                json.dump(rep, f, indent=1)
        except OSError as e:
            messagebox.showerror("GenSpeed", f"Échec d'écriture : {e}")
            return
        nmods = ", ".join(rep['mods']) or "(aucun mod coché)"
        messagebox.showinfo(
            "Rapport exporté",
            f"Rapport enregistré :\n{fn}\n\nContenu : base Steam + {nmods}\n\n"
            "Envoie ce fichier à l'autre joueur. Lui (ou toi) le charge ensuite via "
            "« 🔍 Comparer rapport ami » pour voir EXACTEMENT ce qui diffère.")

    def _compare_compat_report(self):
        """Charge le rapport d'un ami et liste précisément les fichiers qui diffèrent."""
        fn = filedialog.askopenfilename(
            title="Charger le rapport de l'autre joueur",
            filetypes=[("JSON", "*.json"), ("Tous", "*.*")])
        if not fn:
            return
        try:
            with open(fn, 'r', encoding='utf-8') as f:
                other = json.load(f)
        except Exception as e:
            messagebox.showerror("GenSpeed", f"Fichier illisible : {e}")
            return
        other_mods = set(other.get('mods', {}))
        targets = [t for t in self.targets if t['label'] in other_mods]
        self._set_buttons_busy(True)
        self.root.update_idletasks()
        try:
            mine = compat_report(self.game_dir, targets)
        finally:
            self._set_buttons_busy(False)
        diffs = diff_compat_reports(mine, other)
        self._show_diff_window(diffs, sorted(other_mods))

    def _show_diff_window(self, diffs, mods):
        """Affiche le résultat de la comparaison (fichiers différents)."""
        if not diffs:
            messagebox.showinfo(
                "Comparaison",
                "✅ Tout est IDENTIQUE (base Steam + " + (", ".join(mods) or "aucun mod") + ").\n\n"
                "Vos fichiers sont byte-identiques. Si ça désync quand même, c'est le "
                "déterminisme CPU/FP entre vos machines (pas les fichiers).")
            return
        win = tk.Toplevel(self.root)
        win.title("Différences de fichiers entre joueurs")
        win.geometry('720x420')
        win.configure(bg=self.c_bg)
        win.transient(self.root)
        win.grab_set()

        ttk.Label(win, text=f"⚠ {len(diffs)} différence(s) trouvée(s) — voilà la cause du mismatch :",
                  font=('Segoe UI', 10, 'bold'), padding=(12, 10)).pack(anchor='w')
        body = ttk.Frame(win)
        body.pack(fill='both', expand=True, padx=12, pady=(0, 8))
        cols = ('section', 'fichier', 'statut')
        tv = ttk.Treeview(body, columns=cols, show='headings')
        for c, txt, w in (('section', 'Section', 150), ('fichier', 'Fichier', 380), ('statut', 'Statut', 150)):
            tv.heading(c, text=txt)
            tv.column(c, width=w, anchor='w')
        tv.tag_configure('diff', foreground='#e74c3c')
        for section, rel, statut in diffs:
            tv.insert('', 'end', values=(section, rel, statut), tags=('diff',))
        vs = ttk.Scrollbar(body, orient='vertical', command=tv.yview)
        tv.configure(yscrollcommand=vs.set)
        tv.pack(side='left', fill='both', expand=True)
        vs.pack(side='right', fill='y')

        ttk.Label(win, text="« DIFFÉRENT » = même nom mais contenu différent (souvent : patch GenSpeed "
                            "d'un seul côté, ou réglages différents). Alignez ces fichiers (mêmes réglages, "
                            "ou tous les deux non patchés).",
                  font=('Segoe UI', 8), foreground=self.c_dim, justify='left', wraplength=680).pack(anchor='w', padx=12, pady=(0, 6))
        ttk.Button(win, text="Fermer", command=win.destroy).pack(pady=(0, 8))

    def _do_install_hash(self):
        """Calcule le hash d'installation (base Steam + mods cochés) pour la LAN."""
        targets = self._selected_targets()
        self._set_buttons_busy(True)
        self.root.update_idletasks()
        rows = []
        try:
            self._log("\n🔑 Hash d'installation (à comparer entre joueurs) :")
            self._log("   calcul de la base Steam ...")
            self.root.update_idletasks()
            base = base_install_files(self.game_dir)
            if base:
                hh, n, total = install_hash(self.game_dir, base)
                rows.append(("🎮 Base Steam (vanilla)", hh, f"{n} fichiers", f"{total / 1048576:.0f} Mo"))
                self._log(f"   Base Steam : {hh}")
            for t in targets:
                self._log(f"   calcul de « {t['label']} » ...")
                self.root.update_idletasks()
                hh, n, total = install_hash(self.game_dir, t['files'])
                rows.append((t['label'], hh, f"{n} archives", f"{total / 1048576:.0f} Mo"))
                self._log(f"   {t['label']} : {hh}")
        finally:
            self._set_buttons_busy(False)

        if not rows:
            messagebox.showwarning("GenSpeed", "Rien à vérifier (aucune base ni mod détecté).")
            return
        self._show_hash_table(rows, bool(targets))

    def _show_patched_files(self):
        """Liste, dans le journal, les fichiers actuellement patchés + emplacement."""
        files = list_patched_files(self.game_dir)
        if not files:
            self._log("\n📂 Aucun fichier patché actuellement.")
            return
        self._log(f"\n📂 {len(files)} fichier(s) patché(s) :")
        by_dir = defaultdict(list)
        for fp in files:
            rel = os.path.relpath(fp, self.game_dir)
            d = os.path.dirname(rel)
            by_dir[d].append(os.path.basename(fp))
        for d in sorted(by_dir):
            self._log(f"   📁 {d}")
            for name in sorted(by_dir[d]):
                self._log(f"       • {name}", tag='modified')

    def _update_hash(self):
        """Met à jour le hash du patch GenSpeed."""
        h, n = lan_hash(self.game_dir)
        if h:
            self.hash_lbl.config(text=f"🔐 Hash patch GenSpeed : {h}   ({n} fichiers patchés) — identique requis entre joueurs")
        else:
            self.hash_lbl.config(text="🔐 Hash patch GenSpeed : (aucun patch actif)")
