# Fenêtres modales (confirmation patch, hash, aperçus, sélection mod).
import tkinter as tk
from tkinter import ttk

from core import CATS, CAT_NAMES

from gui.constants import DIV_ONLY, cat_danger


def _center_on_parent(dlg, parent):
    dlg.update_idletasks()
    x = parent.winfo_rootx() + (parent.winfo_width() - dlg.winfo_width()) // 2
    y = parent.winfo_rooty() + (parent.winfo_height() - dlg.winfo_height()) // 2
    dlg.geometry(f"+{max(0, x)}+{max(0, y)}")


class DialogsMixin:
    """Dialogues modaux de l'application."""

    def _confirm_patch_dialog(self, mods_list, factors, cam):
        """Fenêtre de confirmation sous forme de TABLEAU. Retourne True/False."""
        dlg = tk.Toplevel(self.root)
        dlg.title("Confirmer le patch")
        dlg.configure(bg=self.c_bg)
        dlg.transient(self.root)
        dlg.grab_set()
        dlg.resizable(False, False)
        self._confirm_result = False

        frm = ttk.Frame(dlg, padding=12)
        frm.pack(fill='both', expand=True)

        ttk.Label(frm, text="Mod(s) : " + ", ".join(mods_list),
                  font=('Segoe UI', 10, 'bold')).pack(anchor='w')
        ttk.Label(frm, text="Résumé des effets à appliquer :",
                  font=('Segoe UI', 9)).pack(anchor='w', pady=(8, 4))

        cols = ('cat', 'eff', 'det')
        tv = ttk.Treeview(frm, columns=cols, show='headings', height=12)
        for c, txt, w in (('cat', 'Catégorie', 160), ('eff', 'Effet', 230), ('det', 'Exemple', 220)):
            tv.heading(c, text=txt)
            tv.column(c, width=w, anchor='w')
        tv.tag_configure('safe', foreground='#0a7d2c')
        tv.tag_configure('warn', foreground='#b9770e')
        tv.tag_configure('danger', foreground='#c0392b')
        tag_by_color = {'#2ecc71': 'safe', '#e67e22': 'warn', '#e74c3c': 'danger'}

        for cat in CATS:
            f = factors.get(cat, 1.0)
            if f == 1.0:
                continue
            tag = tag_by_color.get(cat_danger(cat)[0], 'safe')
            if cat in DIV_ONLY:
                pct = round((1 - 1.0 / f) * 100)
                eff = f"durées −{pct}%  (≈ ×{f:g} plus rapide)"
                det = ''
                if cat == 'construction':
                    det = f"30 s → ~{30.0 / f:.0f} s"
                elif cat == 'pouvoirs':
                    det = f"5 min → ~{5.0 / f:g} min"
            else:
                pct = round((f - 1.0) * 100)
                eff = f"+{pct}%  (×{f:g})"
                det = ''
            tv.insert('', 'end', values=(CAT_NAMES.get(cat, cat), eff, det), tags=(tag,))

        if cam:
            for k, v in cam.items():
                val = v if k == 'DrawEntireTerrain' else (f"{float(v):g}" if str(v).replace('.', '').isdigit() else v)
                tv.insert('', 'end', values=("📷 " + k, val, ''))

        tv.pack(fill='both', expand=True)

        unchanged = [CAT_NAMES[c] for c in CATS if factors.get(c, 1.0) == 1.0]
        if unchanged:
            ttk.Label(frm, text="Inchangé : " + ", ".join(unchanged),
                      font=('Segoe UI', 8), foreground='#777', wraplength=600).pack(anchor='w', pady=(6, 0))
        ttk.Label(frm, text="Le mod original sera sauvegardé automatiquement (dépatchable).",
                  font=('Segoe UI', 8), foreground='#777').pack(anchor='w', pady=(2, 10))

        bar = ttk.Frame(frm)
        bar.pack(fill='x')

        def apply():
            self._confirm_result = True
            dlg.destroy()

        big = tk.Button(bar, text="🚀  Appliquer", command=apply,
                        font=('Segoe UI', 10, 'bold'), bg=self.c_primary, fg=self.c_primary_fg,
                        activebackground=self.c_primary_h, activeforeground=self.c_primary_fg,
                        relief='raised', bd=2, cursor='hand2', padx=12, pady=3)
        big.pack(side='right', padx=4)
        ttk.Button(bar, text="Annuler", command=dlg.destroy).pack(side='right', padx=4)

        _center_on_parent(dlg, self.root)
        dlg.wait_window()
        return self._confirm_result

    def _show_post_patch_dialog(self, mods_done, h, n):
        """Fenêtre de fin de patch avec un gros bouton 'Lancer GenLauncher'."""
        dlg = tk.Toplevel(self.root)
        dlg.title("Patch appliqué")
        dlg.configure(bg=self.c_bg)
        dlg.transient(self.root)
        dlg.grab_set()
        dlg.resizable(False, False)

        frm = ttk.Frame(dlg, padding=18)
        frm.pack(fill='both', expand=True)

        ttk.Label(frm, text="✅  Patch appliqué avec succès",
                  font=('Segoe UI', 13, 'bold'), foreground='#0a7d2c').pack(anchor='w')
        ttk.Label(frm, text="Mod(s) : " + ", ".join(mods_done),
                  font=('Segoe UI', 9)).pack(anchor='w', pady=(8, 0))
        ttk.Label(frm, text=f"Hash LAN : {h or '—'}  ({n} fichiers)",
                  font=('Consolas', 10, 'bold'), foreground=self.c_hashlbl).pack(anchor='w', pady=(4, 0))
        ttk.Label(frm, text="⚠ En LAN, tous les joueurs doivent avoir ce même hash.",
                  font=('Segoe UI', 8), foreground='#a00').pack(anchor='w', pady=(2, 12))

        def launch_and_close():
            dlg.destroy()
            self._launch_genlauncher()

        big = tk.Button(frm, text="🚀  Lancer GenLauncher", command=launch_and_close,
                        font=('Segoe UI', 12, 'bold'), bg=self.c_go, fg=self.c_go_fg,
                        activebackground=self.c_go_h, activeforeground=self.c_go_fg,
                        relief='raised', bd=2, height=2, cursor='hand2')
        big.pack(fill='x', pady=(0, 6))
        ttk.Button(frm, text="Fermer", command=dlg.destroy).pack(fill='x')

        _center_on_parent(dlg, self.root)

    def _show_hash_table(self, rows, had_mods):
        """Fenêtre tableau des hashes d'installation (à comparer entre joueurs)."""
        win = tk.Toplevel(self.root)
        win.title("Hash d'installation — vérification LAN")
        win.geometry('620x360')
        win.configure(bg=self.c_bg)
        win.transient(self.root)
        win.grab_set()

        ttk.Label(win, text="Compare ces valeurs avec l'autre joueur.",
                  font=('Segoe UI', 10, 'bold'), padding=(12, 10)).pack(anchor='w')
        ttk.Label(win, text="Elles doivent être IDENTIQUES pour garantir exactement les mêmes fichiers.",
                  font=('Segoe UI', 9), foreground='#555').pack(anchor='w', padx=12)

        body = ttk.Frame(win)
        body.pack(fill='both', expand=True, padx=12, pady=8)
        cols = ('elem', 'hash', 'arch', 'taille')
        tv = ttk.Treeview(body, columns=cols, show='headings', height=8)
        for c, txt, w, anchor in (('elem', 'Élément', 230, 'w'), ('hash', 'Hash', 110, 'center'),
                                  ('arch', 'Fichiers', 90, 'center'), ('taille', 'Taille', 90, 'e')):
            tv.heading(c, text=txt)
            tv.column(c, width=w, anchor=anchor)
        tv.tag_configure('base', foreground=self.c_base_fg)
        for elem, hh, arch, taille in rows:
            tag = 'base' if elem.startswith('🎮') else ''
            tv.insert('', 'end', values=(elem, hh, arch, taille), tags=(tag,) if tag else ())
        tv.pack(fill='both', expand=True)

        note = ("⚠ La base Steam couvre les .big racine (version du jeu) + l'exe.\n"
                "Si tout est identique mais ça désync quand même → c'est le déterminisme "
                "cross-machine (CPU/FP), pas les fichiers.")
        if not had_mods:
            note = "ℹ Aucun mod coché : seule la base Steam est vérifiée.\n" + note
        ttk.Label(win, text=note, font=('Segoe UI', 8), foreground='#777',
                  justify='left', wraplength=580).pack(anchor='w', padx=12, pady=(0, 6))
        ttk.Button(win, text="Fermer", command=win.destroy).pack(pady=(0, 8))

    def _show_table_window(self, title, header, rows):
        """Affiche un tableau (Variable | Original | Actuel | Emplacement)."""
        win = tk.Toplevel(self.root)
        win.title(title)
        win.geometry('780x540')
        win.configure(bg=self.c_bg)
        win.transient(self.root)

        ttk.Label(win, text=header, font=('Segoe UI', 9), justify='left',
                  padding=(10, 8)).pack(anchor='w')

        body = ttk.Frame(win)
        body.pack(fill='both', expand=True, padx=8, pady=(0, 6))

        cols = ('var', 'orig', 'cur', 'loc')
        tv = ttk.Treeview(body, columns=cols, show='headings')
        for c, txt, w in (('var', 'Variable', 230), ('orig', 'Original', 90),
                          ('cur', 'Actuel', 110), ('loc', 'Emplacement (fichier:ligne)', 330)):
            tv.heading(c, text=txt)
            tv.column(c, width=w, anchor='w')
        tv.tag_configure('modified', **self.tag_mod)

        for var, ov, cv, loc, modified in rows:
            tv.insert('', 'end', values=(var, ov, cv, loc),
                      tags=('modified',) if modified else ())

        vs = ttk.Scrollbar(body, orient='vertical', command=tv.yview)
        tv.configure(yscrollcommand=vs.set)
        tv.pack(side='left', fill='both', expand=True)
        vs.pack(side='right', fill='y')

        ttk.Button(win, text='Fermer', command=win.destroy).pack(pady=6)

    def _select_target_dialog(self, targets, callback):
        """Dialogue pour sélectionner une cible parmi plusieurs."""
        dialog = tk.Toplevel(self.root)
        dialog.title("Sélectionner un mod")
        dialog.geometry("300x250")
        dialog.configure(bg=self.c_bg)
        dialog.transient(self.root)
        dialog.grab_set()

        ttk.Label(dialog, text="Plusieurs mods sélectionnés :\nChoisis celui à analyser :",
                 padding=10, justify='center').pack()

        listbox = tk.Listbox(dialog, height=10, bg=self.c_inputbg, fg=self.c_fg,
                             selectbackground=self.c_primary, relief='flat', borderwidth=0)
        listbox.pack(fill='both', expand=True, padx=10, pady=5)
        for t in targets:
            listbox.insert('end', t['label'])

        def on_select():
            selection = listbox.curselection()
            if selection:
                idx = selection[0]
                tgt = targets[idx]
                dialog.destroy()
                callback(tgt)

        def on_double_click(event):
            selection = listbox.curselection()
            if selection:
                idx = selection[0]
                tgt = targets[idx]
                dialog.destroy()
                callback(tgt)

        listbox.bind('<Double-Button-1>', on_double_click)

        btn_frame = ttk.Frame(dialog)
        btn_frame.pack(fill='x', pady=10)
        ttk.Button(btn_frame, text="Analyser", command=on_select).pack(side='left', expand=True, padx=10)
        ttk.Button(btn_frame, text="Annuler", command=dialog.destroy).pack(side='right', expand=True, padx=10)
