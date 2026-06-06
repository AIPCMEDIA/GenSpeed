#!/usr/bin/env python3
# Interface graphique principale GenSpeed (orchestration des sections).
import json
import os
import re
import tkinter as tk
from datetime import datetime
from tkinter import ttk, messagebox, filedialog

from core import (
    CATS, CAT_NAMES, detect_targets, ModDetector,
)
from config import ConfigManager, CameraConfig, CAM_VARS, DEFAULT_CAM_PRESET_ORDER

from gui.constants import VERSION, DIV_ONLY, CAT_HELP, cat_danger
from gui.labels import speed_label, cam_label
from gui.widgets import ToolTip
from gui.theme import ThemeMixin
from gui.dialogs import DialogsMixin
from gui.preview import PreviewMixin
from gui.patch_ops import PatchOpsMixin
from gui.hash_ops import HashOpsMixin


class GenSpeedGUI(
    DialogsMixin,
    PreviewMixin,
    PatchOpsMixin,
    HashOpsMixin,
    ThemeMixin,
):
    """Interface graphique principale de GenSpeed."""

    def __init__(self, root, game_dir):
        self.root = root
        self.game_dir = game_dir
        self.config_manager = ConfigManager(game_dir)
        self.cam_presets = self.config_manager.load_camera_presets()
        self.camera_config = CameraConfig(game_dir)
        self.camera_config.presets = self.cam_presets

        root.title("GenSpeed - Configurateur de vitesse pour Generals")
        root.geometry("1100x1100")
        root.resizable(True, True)

        self.targets = detect_targets(game_dir)
        self.state = self.config_manager.load_state()
        self.slider_configs = self.config_manager.load_slider_configs()
        self._det = ModDetector(game_dir)
        self.ui_mode = self.config_manager.load_ui_mode()
        self.dark = getattr(root, '_gs_dark', False)
        self._init_palette()

        self._setup_ui()
        self._set_window_icon()
        self._log("GenSpeed démarré - Dossier jeu : %s" % game_dir)
        self._log("%d cible(s) détectée(s)" % len(self.targets))

    def _speed_label(self, factors):
        return speed_label(factors)

    def _cam_label(self, cam):
        return cam_label(cam, self.cam_presets)

    def _setup_ui(self):
        """Configure l'interface utilisateur (Mode Simple / Avancé)."""
        style = ttk.Style()
        style.configure("Title.TLabel", font=('Segoe UI', 11, 'bold'))
        style.configure("Hash.TLabel", font=('Consolas', 10, 'bold'), foreground=self.c_hashlbl)
        style.configure("Preset.TButton", font=('Segoe UI', 9))

        self.fr_topbar = ttk.Frame(self.root, padding=(10, 6))
        has_logo = self._place_logo(self.fr_topbar)
        title_box = ttk.Frame(self.fr_topbar)
        title_box.pack(side='left', padx=(10, 0))
        if not has_logo:
            ttk.Label(title_box, text="GenSpeed", font=('Segoe UI', 15, 'bold')).pack(side='top', anchor='w')
        ttk.Label(title_box, text=f"v{VERSION}  ·  vitesse de jeu pour Generals Zero Hour",
                  font=('Segoe UI', 8), foreground=self.c_dim).pack(side='top', anchor='w')
        self.mode_btn = ttk.Button(self.fr_topbar, text="", command=self._toggle_mode, width=18)
        self.mode_btn.pack(side='right')

        self.fr_help = ttk.Frame(self.root, padding=(10, 2))
        self.help_lbl = ttk.Label(
            self.fr_help, font=('Segoe UI', 9, 'bold'),
            text="①  Mod(s)   →   ②  Vitesse   →   ③  Caméra   →   ④  Appliquer   →   ⑤  Lancer          🌐 Vérification multijoueur : optionnelle, quand tu veux")
        self.help_lbl.pack(anchor='w')

        self._setup_mods_section()
        self._setup_config_section()
        self._setup_simple_section()
        self._setup_action_buttons()
        self._setup_hash_section()
        self._setup_log_section()

        self._set_mode(self.ui_mode)

    def _set_mode(self, mode):
        """Affiche le Mode Simple ou Avancé en (re)plaçant les sections."""
        self.ui_mode = 'advanced' if mode == 'advanced' else 'simple'
        simple = (self.ui_mode == 'simple')

        for f in (self.fr_topbar, self.fr_help, self.fr_mods, self.fr_s_speed,
                  self.fr_s_cam, self.fr_s_speed_cam_panel, self.fr_s_compat, self.fr_advconfig, self.fr_apply,
                  self.fr_launch, self.fr_advtools, self.fr_hash, self.fr_log):
            f.pack_forget()

        self.fr_topbar.pack(fill='x')
        self.fr_help.pack(fill='x')
        self.fr_mods.pack(fill='x', padx=10, pady=4)

        if simple:
            self._highlight_simple_speed()
            self._highlight_simple_cam()
            self.fr_s_speed_cam_panel.pack(fill='x', padx=10, pady=4)
            self.fr_s_speed.pack(side='left', fill='both', expand=True, padx=(0, 5), pady=0)
            self.fr_s_cam.pack(side='right', fill='both', expand=True, padx=(5, 0), pady=0)
            self.fr_apply.pack(fill='x', padx=10, pady=4)
            self.fr_s_compat.pack(fill='x', padx=10, pady=4)
            self.fr_launch.pack(fill='x', padx=10, pady=4)
            self.fr_log.pack(fill='both', expand=True, padx=10, pady=(2, 6))
            self.mode_btn.config(text="Mode avancé  ▸")
            self.root.geometry("1000x880")
            self.root.minsize(940, 700)
        else:
            self.fr_advconfig.pack(fill='x', padx=10, pady=4)
            self.fr_apply.pack(fill='x', padx=10, pady=4)
            self.fr_launch.pack(fill='x', padx=10, pady=4)
            self.fr_advtools.pack(fill='x', padx=10, pady=2)
            self.fr_hash.pack(fill='x')
            self.fr_log.pack(fill='both', expand=True, padx=10, pady=(2, 6))
            self.mode_btn.config(text="◂  Mode simple")
            self.root.geometry("1180x980")
            self.root.minsize(1000, 760)

        self.config_manager.save_ui_mode(self.ui_mode)

    def _toggle_mode(self):
        self._set_mode('advanced' if self.ui_mode == 'simple' else 'simple')

    def _setup_mods_section(self):
        """Liste des mods : colonnes Vitesse/Caméra, défilement, tout-sélectionner."""
        mods_frame = ttk.LabelFrame(self.root, text="①  Choisis un ou plusieurs mods", padding=10)
        self.fr_mods = mods_frame

        top = ttk.Frame(mods_frame)
        top.pack(fill='x')
        self.select_all_var = tk.BooleanVar(value=False)
        ttk.Checkbutton(top, text="Tout sélectionner", variable=self.select_all_var,
                        command=self._on_select_all).pack(side='left')
        ttk.Label(top, text="clic sur une ligne = cocher  ·  clic sur un en-tête = trier  ·  double-clic = aperçu",
                  font=('Segoe UI', 8), foreground=self.c_dim).pack(side='left', padx=12)

        body = ttk.Frame(mods_frame)
        body.pack(fill='both', expand=True, pady=(6, 0))
        cols = ('select', 'mod', 'vitesse', 'camera', 'archives', 'ini', 'patched')
        self.tree = ttk.Treeview(body, columns=cols, show='headings', height=8, selectmode='none')
        self._sort_state = {}
        headers = [
            ('select', '✓', 34, 'center', False),
            ('mod', 'Jeu / Mod', 250, 'w', False),
            ('vitesse', 'Vitesse', 90, 'center', False),
            ('camera', 'Caméra', 120, 'center', False),
            ('archives', 'Archives', 70, 'center', True),
            ('ini', '.ini', 60, 'center', True),
            ('patched', 'Patchés', 75, 'center', True),
        ]
        for col, text, w, anchor, numeric in headers:
            if col == 'select':
                self.tree.heading(col, text=text)
            else:
                self.tree.heading(col, text=text,
                                  command=lambda c=col, n=numeric: self._sort_by(c, n))
            self.tree.column(col, width=w, anchor=anchor, stretch=(col == 'mod'))
        vs = ttk.Scrollbar(body, orient='vertical', command=self.tree.yview)
        self.tree.configure(yscrollcommand=vs.set)
        self.tree.pack(side='left', fill='both', expand=True)
        vs.pack(side='right', fill='y')
        self.tree.bind('<Button-1>', self._on_tree_click)
        self.tree.bind('<Double-Button-1>', self._on_double_click)
        self.tree.tag_configure('patched', **self.tag_patched)
        self.tree.tag_configure('original', **self.tag_orig)

        self._refresh_list()

    def _sort_by(self, col, numeric=False):
        """Trie le tableau selon une colonne (clic sur l'en-tête)."""
        items = [(self.tree.set(k, col), k) for k in self.tree.get_children('')]
        if numeric:
            def key(t):
                m = re.search(r'-?\d+', t[0])
                return int(m.group()) if m else -1
        else:
            def key(t):
                return t[0].lower()
        reverse = self._sort_state.get(col, False)
        items.sort(key=key, reverse=reverse)
        for idx, (_, k) in enumerate(items):
            self.tree.move(k, '', idx)
        self._sort_state[col] = not reverse

    def _on_select_all(self):
        if self.select_all_var.get():
            self._check_all_mods()
        else:
            self._uncheck_all_mods()

    def _setup_simple_section(self):
        """Mode Simple : ② vitesse, ③ caméra, ⑤ compatibilité."""
        # Panneau horizontal pour vitesse et caméra
        speed_cam_panel = ttk.Frame(self.root)
        self.fr_s_speed_cam_panel = speed_cam_panel
        
        sp = ttk.LabelFrame(speed_cam_panel, text="②  Règle la vitesse de jeu", padding=12)
        self.fr_s_speed = sp
        rowb = ttk.Frame(sp)
        rowb.pack(anchor='w')
        self.simple_speed_buttons = {}
        specs = [
            (0, "Original", "×1", "Aucune accélération : vitesses d'origine du mod."),
            (1, "😎 Cool", "≈ ×1.5", "Léger boost (~1.5×). Dynamique mais proche du normal."),
            (2, "😠 Énervé", "≈ ×2", "Recommandé (~2×). Bon équilibre nervosité / contrôle."),
            (3, "🔥 Déchaîné", "≈ ×3", "Intense (~3×). Parties très rapides."),
        ]
        for pos, label, sub, tip in specs:
            b = tk.Button(rowb, text=f"{label}\n{sub}", width=11, height=2,
                          font=('Segoe UI', 10, 'bold'), relief='raised', bd=2, cursor='hand2',
                          command=lambda p=pos: self._simple_set_speed(p))
            b.pack(side='left', padx=5)
            self.simple_speed_buttons[pos] = b
            ToolTip(b, tip)

        cam = ttk.LabelFrame(speed_cam_panel, text="③  Règle la caméra (vue de jeu)", padding=12)
        self.fr_s_cam = cam
        rowc = ttk.Frame(cam)
        rowc.pack(anchor='w')
        self.simple_cam_buttons = {}
        
        # Liste des presets de caméra dans l'ordre défini
        cam_presets = [n for n in DEFAULT_CAM_PRESET_ORDER if n in self.cam_presets]
        cam_specs = [
            ("Vue par défaut", "Ne change rien", "Vue par défaut du jeu"),
        ]
        for preset in cam_presets:
            cam_specs.append((preset, "", f"Applique le preset {preset}"))
        
        for i, (label, sub, tip) in enumerate(cam_specs):
            cmd = (lambda: self._simple_apply_cam("")) if i == 0 else (lambda p=label: self._simple_apply_cam(p))
            b = tk.Button(rowc, text=f"{label}\n{sub}", width=11, height=2,
                          font=('Segoe UI', 10, 'bold'), relief='raised', bd=2, cursor='hand2',
                          bg=self.c_inactive, fg=self.c_inactive_fg, activebackground=self.c_inactive,
                          command=cmd)
            b.pack(side='left', padx=5)
            self.simple_cam_buttons[label] = b
            ToolTip(b, tip)

        comp = ttk.LabelFrame(self.root, text="🌐  Vérification multijoueur (LAN) — optionnel", padding=12)
        self.fr_s_compat = comp
        ttk.Label(comp,
                  text="À utiliser quand tu veux jouer en réseau (LAN). Génère un « code » qui résume\n"
                       "exactement tes fichiers de jeu : base Steam (vanilla) + le(s) mod(s) coché(s) + ton patch GenSpeed.",
                  font=('Segoe UI', 8), foreground=self.c_dim, justify='left').pack(anchor='w')
        ttk.Label(comp,
                  text="➡ Tous les joueurs doivent afficher le MÊME code (même jeu : vanilla ou même mod, "
                       "même version, mêmes réglages) — sinon désync.",
                  font=('Segoe UI', 8), foreground=self.c_dim, justify='left').pack(anchor='w', pady=(2, 0))
        row = ttk.Frame(comp)
        row.pack(fill='x', pady=(8, 0))
        bchk = ttk.Button(row, text="🛡 Vérifier mon code", command=self._do_compat_check, width=18)
        bchk.pack(side='left')
        ToolTip(bchk, "Calcule ton code (base Steam + mods cochés). Compare-le avec tes amis : "
                       "il doit être identique pour jouer ensemble sans désync.")
        self.compat_code_lbl = ttk.Label(row, text="Votre code : —",
                                         font=('Consolas', 13, 'bold'), foreground=self.c_primary)
        self.compat_code_lbl.pack(side='left', padx=12)

        row2 = ttk.Frame(comp)
        row2.pack(fill='x', pady=(6, 0))
        brep = ttk.Button(row2, text="📜 Versions de ma dernière partie",
                          command=self._show_replay_fingerprint, width=30)
        brep.pack(side='left')
        ToolTip(brep, "Affiche le jeu/mod/patch + la map de ta dernière partie. "
                      "Compare la ligne VERSION avec l'autre joueur pour trouver un mismatch.")
        bexp = ttk.Button(row2, text="📤 Exporter mon rapport",
                          command=self._export_compat_report, width=22)
        bexp.pack(side='left', padx=8)
        ToolTip(bexp, "Crée un fichier rapport (empreinte par fichier) à envoyer à l'autre joueur "
                      "pour trouver EXACTEMENT ce qui diffère.")

        row3 = ttk.Frame(comp)
        row3.pack(fill='x', pady=(6, 0))
        bcmp = ttk.Button(row3, text="🔍 Comparer le rapport d'un ami (trouve le fichier fautif)",
                          command=self._compare_compat_report, width=52)
        bcmp.pack(side='left')
        ToolTip(bcmp, "Charge le rapport reçu d'un ami : liste exactement les fichiers qui diffèrent "
                      "entre vos deux installations.")

        self._cam_choice = ""
        self._speed_choice = 2  # Énervé par défaut
        self._highlight_simple_speed()
        self._highlight_simple_cam()

    def _simple_set_speed(self, pos):
        # Même échelle que le slider avancé (0 Original · 1 Cool · 2 Énervé · 3 Déchaîné)
        self.global_slider_var.set(pos)
        self._on_global_slider_change(pos)

    def _highlight_simple_speed(self):
        cur = getattr(self, '_speed_choice', 1)
        for pos, b in self.simple_speed_buttons.items():
            if pos == cur:
                b.config(bg=self.c_primary, fg=self.c_primary_fg, activebackground=self.c_primary_h,
                         relief='sunken')
            else:
                b.config(bg=self.c_inactive, fg=self.c_inactive_fg, activebackground=self.c_inactive,
                         relief='raised')

    def _simple_apply_cam(self, preset_name=""):
        if preset_name == "" or preset_name.startswith('Standard'):
            self.camera_config.reset()
            for var in CAM_VARS:
                self.cam_vars[var].set('')
            self._log("Vue par défaut appliquée")
            self._cam_choice = ""
        else:
            self._apply_cam_preset(preset_name)
            self._cam_choice = preset_name
        self._highlight_simple_cam()

    def _highlight_simple_cam(self, preset_name=None):
        if preset_name is None:
            preset_name = getattr(self, '_cam_choice', "")
        for label, b in self.simple_cam_buttons.items():
            # le bouton "Vue par défaut" n'est pas un vrai preset
            is_default_btn = label not in self.cam_presets
            active = (label == preset_name) or (preset_name in ("", None) and is_default_btn)
            if active:
                b.config(bg=self.c_primary, fg=self.c_primary_fg, activebackground=self.c_primary_h,
                         relief='sunken')
            else:
                b.config(bg=self.c_inactive, fg=self.c_inactive_fg, activebackground=self.c_inactive,
                         relief='raised')

    def _setup_config_section(self):
        """Configure la section de configuration AVANCÉE (vitesse + caméra)."""
        config_container = ttk.Frame(self.root)
        self.fr_advconfig = config_container

        speed_frame = ttk.LabelFrame(config_container, text="Configuration vitesse", padding=10)
        speed_frame.pack(side='left', fill='both', expand=True, padx=5)
        self._setup_speed_controls(speed_frame)

        cam_frame = ttk.LabelFrame(config_container, text="Configuration caméra (vide = ne pas modifier)", padding=10)
        cam_frame.pack(side='right', fill='both', expand=True, padx=5)
        self._setup_camera_controls(cam_frame)

    def _setup_speed_controls(self, parent):
        row1 = ttk.Frame(parent)
        row1.pack(fill='x', pady=5)
        ttk.Label(row1, text="Facteur global :", font=('Segoe UI', 9)).pack(side='left')

        # Slider 4 niveaux : 0 Original · 1 Cool · 2 Énervé · 3 Déchaîné
        self.global_slider_var = tk.DoubleVar(value=2.0)
        self.global_slider = ttk.Scale(row1, from_=0, to=3, orient='horizontal',
                                       variable=self.global_slider_var,
                                       command=self._on_global_slider_change, length=220)
        self.global_slider.pack(side='left', padx=6)

        self.global_slider_label = ttk.Label(row1, text="😠 Énervé (≈×2)", font=('Segoe UI', 9, 'bold'),
                                             foreground='#0066cc')
        self.global_slider_label.pack(side='left', padx=4)

        self.cat_vars = {}
        cat_grid = ttk.Frame(parent)
        cat_grid.pack(fill='x', pady=10)

        for i, cat in enumerate(CATS):
            row = i // 4
            col = i % 4
            frame = ttk.Frame(cat_grid)
            frame.grid(row=row, column=col, padx=8, pady=5, sticky='w')
            dcolor, dword = cat_danger(cat)
            dot = ttk.Label(frame, text="●", foreground=dcolor)
            dot.pack(side='left', padx=(0, 2))
            ToolTip(dot, dword)
            lbl = ttk.Label(frame, text=CAT_NAMES.get(cat, cat), width=15, anchor='w')
            lbl.pack(side='left')
            symbol = "÷" if cat in DIV_ONLY else "×"
            ttk.Label(frame, text=symbol, foreground='#888',
                      font=('Segoe UI', 9, 'bold')).pack(side='left', padx=(0, 1))
            v = tk.StringVar(value="1")
            self.cat_vars[cat] = v
            ttk.Entry(frame, textvariable=v, width=4).pack(side='left')
            ToolTip(lbl, CAT_HELP.get(cat, ''))

        self._apply_slider_defaults(2)  # Énervé par défaut

    def _setup_camera_controls(self, parent):
        preset_frame = ttk.Frame(parent)
        preset_frame.pack(fill='x', pady=5)

        # Bouton Vue par défaut
        btn = ttk.Button(preset_frame, text="Vue par défaut", style="Preset.TButton",
                       command=lambda: self._reset_camera())
        btn.pack(side='left', padx=2, pady=2)

        for preset_name in DEFAULT_CAM_PRESET_ORDER:
            if preset_name in self.cam_presets:
                btn = ttk.Button(preset_frame, text=preset_name, style="Preset.TButton",
                               command=lambda n=preset_name: self._apply_cam_preset(n))
                btn.pack(side='left', padx=2, pady=2)

        self.cam_vars = {}
        cam_hint = {
            'CameraPitch': '37.5 (inclinaison)',
            'CameraYaw': '0 (orientation)',
            'CameraHeight': '232 (hauteur de départ)',
            'MaxCameraHeight': '310 (zoom max, ~800 conseillé)',
            'MinCameraHeight': '120 (zoom min)',
            'DrawEntireTerrain': 'Yes/No (afficher tout le terrain)'
        }

        cam_grid = ttk.Frame(parent)
        cam_grid.pack(fill='x')

        row_i = 0
        for var in CAM_VARS:
            v = tk.StringVar(value='')
            self.cam_vars[var] = v
            if var == 'CameraYaw':
                continue
            i = row_i
            row_i += 1
            ttk.Label(cam_grid, text=var, width=22, anchor='e').grid(row=i, column=0, padx=(8, 2), pady=1)
            if var == 'DrawEntireTerrain':
                cb = ttk.Combobox(cam_grid, textvariable=v, values=['', 'Yes', 'No'], width=10)
                cb.grid(row=i, column=1, padx=4, sticky='w')
            else:
                ttk.Entry(cam_grid, textvariable=v, width=10).grid(row=i, column=1, padx=4, sticky='w')
            ttk.Label(cam_grid, text=cam_hint[var], foreground='#666', font=('Segoe UI', 8)).grid(row=i, column=2, padx=(4, 8), sticky='w')

    def _setup_action_buttons(self):
        bf = ttk.LabelFrame(self.root, text="④  Applique les modifications", padding=10)
        self.fr_apply = bf
        row = ttk.Frame(bf)
        row.pack(fill='x')
        self.btn_patch = tk.Button(row, text="🚀  Appliquer la vitesse au(x) mod(s) coché(s)",
                                   command=self._do_apply_selected,
                                   font=('Segoe UI', 10, 'bold'), bg=self.c_primary, fg=self.c_primary_fg,
                                   activebackground=self.c_primary_h, activeforeground=self.c_primary_fg,
                                   relief='raised', bd=2, cursor='hand2', padx=10, pady=4)
        self.btn_patch.pack(side='left', padx=4)
        self.btn_restore = ttk.Button(row, text="↩  Annuler (revenir au jeu d'origine)",
                                      command=self._do_restore_selected, width=32)
        self.btn_restore.pack(side='left', padx=4)
        ttk.Label(bf, text="« Appliquer » modifie le mod (sauvegarde automatique).  "
                           "« Annuler » restaure le mod d'origine pour rejouer normalement.",
                  font=('Segoe UI', 8), foreground=self.c_dim).pack(anchor='w', padx=4, pady=(6, 0))

        lf = ttk.LabelFrame(self.root, text="⑤  Joue", padding=10)
        self.fr_launch = lf
        btn_launch = tk.Button(lf, text="▶  Lancer GenLauncher",
                               command=self._launch_genlauncher,
                               font=('Segoe UI', 11, 'bold'), bg=self.c_go, fg=self.c_go_fg,
                               activebackground=self.c_go_h, activeforeground=self.c_go_fg,
                               relief='raised', bd=2, cursor='hand2', padx=12, pady=5)
        btn_launch.pack(side='left', padx=4)
        ttk.Label(lf, text="Ouvre GenLauncher pour lancer le jeu/le mod.",
                  font=('Segoe UI', 8), foreground=self.c_dim).pack(side='left', padx=10)

        adv = ttk.Frame(self.root, padding=(10, 0))
        self.fr_advtools = adv

        cfg = ttk.Frame(adv)
        cfg.pack(fill='x', pady=2)
        ttk.Label(cfg, text="Réglages :", font=('Segoe UI', 8, 'bold'),
                  foreground='#555', width=10).pack(side='left')
        btn_reset = ttk.Button(cfg, text="Réinitialiser", command=self._reset_defaults, width=15)
        btn_reset.pack(side='left', padx=3)
        btn_last = ttk.Button(cfg, text="Derniers réglages", command=self._load_last_settings, width=15)
        btn_last.pack(side='left', padx=3)
        btn_export = ttk.Button(cfg, text="Export config", command=self._export_config, width=15)
        btn_export.pack(side='left', padx=3)
        btn_import = ttk.Button(cfg, text="Import config", command=self._import_config, width=15)
        btn_import.pack(side='left', padx=3)

        prev = ttk.LabelFrame(adv, text="🔎 Aperçus du mod sélectionné", padding=6)
        prev.pack(fill='x', pady=(6, 2))
        btn_keys = ttk.Button(prev, text="Valeurs clés", command=self._show_original_preview, width=18)
        btn_keys.pack(side='left', padx=3)
        btn_exh = ttk.Button(prev, text="🔍 Exhaustif", command=self._show_exhaustive_preview, width=16)
        btn_exh.pack(side='left', padx=3)
        btn_mod = ttk.Button(prev, text="🟢 Modifs uniquement", command=self._show_modified_preview, width=18)
        btn_mod.pack(side='left', padx=3)

        ToolTip(self.btn_patch, "Applique la vitesse choisie aux mods cochés (demande l'admin). Crée une sauvegarde restaurable.")
        ToolTip(self.btn_restore, "Restaure les mods cochés à leur état d'origine (demande l'admin).")
        ToolTip(btn_launch, "Ouvre GenLauncher pour lancer le jeu/le mod.")
        ToolTip(btn_reset, "Remet la grille de vitesse sur le preset Énervé.")
        ToolTip(btn_last, "Recharge les derniers facteurs/caméra utilisés.")
        ToolTip(btn_export, "Enregistre la configuration actuelle (facteurs + caméra).")
        ToolTip(btn_import, "Charge une configuration depuis un fichier.")
        ToolTip(btn_keys, "Affiche les variables clés du mod coché (original → actuel).")
        ToolTip(btn_exh, "Affiche TOUTES les variables du mod (modifiées surlignées).")
        ToolTip(btn_mod, "Affiche uniquement les variables changées par le patch.")

    def _setup_log_section(self):
        logf = ttk.LabelFrame(self.root, text="Journal d'activité  (clic droit = copier)", padding=5)
        self.fr_log = logf

        self.log_box = tk.Text(logf, height=8, width=80, font=('Consolas', 9), wrap='word',
                               bg=self.c_inputbg, fg=self.c_fg, insertbackground=self.c_fg,
                               relief='flat', borderwidth=0)
        scroll_log = ttk.Scrollbar(logf, orient="vertical", command=self.log_box.yview)
        self.log_box.configure(yscrollcommand=scroll_log.set)
        self.log_box.pack(side='left', fill='both', expand=True)
        scroll_log.pack(side='right', fill='y')
        self.log_box.tag_configure('modified', **self.tag_mod)
        self.log_box.tag_configure('dim', foreground=self.c_dim)

        # Lecture seule MAIS sélectionnable/copiable à la souris
        self.log_box.bind('<Key>', self._log_keyguard)

        # Menu clic droit : Copier
        self._log_menu = tk.Menu(self.log_box, tearoff=0)
        self._log_menu.add_command(label="Copier la sélection", command=self._copy_log_selection)
        self._log_menu.add_command(label="Tout copier", command=self._copy_log)
        self._log_menu.add_separator()
        self._log_menu.add_command(label="Effacer le journal", command=self._clear_log)
        self.log_box.bind('<Button-3>', self._log_context_menu)

    def _log_keyguard(self, event):
        """Bloque la frappe mais laisse la sélection/copie (Ctrl+C, Ctrl+A, navigation)."""
        if event.state & 0x4 and event.keysym.lower() in ('c', 'a'):
            return
        if event.keysym in ('Left', 'Right', 'Up', 'Down', 'Home', 'End', 'Prior', 'Next',
                             'Shift_L', 'Shift_R', 'Control_L', 'Control_R'):
            return
        return 'break'

    def _log_context_menu(self, event):
        try:
            self._log_menu.tk_popup(event.x_root, event.y_root)
        finally:
            self._log_menu.grab_release()

    def _copy_log_selection(self):
        try:
            sel = self.log_box.get('sel.first', 'sel.last')
        except tk.TclError:
            return
        self.root.clipboard_clear()
        self.root.clipboard_append(sel)

    def _copy_log(self):
        self.root.clipboard_clear()
        self.root.clipboard_append(self.log_box.get('1.0', 'end-1c'))

    def _clear_log(self):
        self.log_box.delete('1.0', 'end')

    def _log(self, msg, tag=None):
        timestamp = datetime.now().strftime("%H:%M:%S")
        line = f"[{timestamp}] {msg}\n"
        if tag:
            self.log_box.insert('end', line, tag)
        else:
            self.log_box.insert('end', line)
        self.log_box.see('end')
        self.root.update_idletasks()

    def _count_ini(self, target):
        if target['type'] != 'gib':
            return len(target['files'])
        return sum(sum(1 for nm in self._det._gib_names(fp) if nm.lower().endswith('.ini'))
                   for fp in target['files'])

    def _refresh_list(self):
        checked = {self.tree.set(i, 'mod') for i in self.tree.get_children()
                   if self.tree.set(i, 'select') == '☑'}
        for i in self.tree.get_children():
            self.tree.delete(i)
        self.state = self.config_manager.load_state()
        for t in self.targets:
            info = self.state.get(t['label']) or {}
            patched = bool(info.get('patched_files'))
            speed = self._speed_label(info.get('factors')) if patched else "Original"
            cam = self._cam_label(info.get('cam')) if patched else "Original"
            n_arch = len(t['files'])
            n_ini = self._count_ini(t)
            n_patch = len(info.get('patched_files') or {})
            patched_str = f"{n_patch}/{n_arch}" if patched else "—"
            tag = 'patched' if patched else 'original'
            mark = '☑' if t['label'] in checked else '☐'
            self.tree.insert('', 'end',
                             values=(mark, t['label'], speed, cam, n_arch, n_ini, patched_str),
                             tags=(tag,))

    def _on_tree_click(self, event):
        # Clic sur un en-tête -> géré par le tri (heading command). On ignore ici.
        if self.tree.identify_region(event.x, event.y) != 'cell':
            return
        item = self.tree.identify_row(event.y)
        if not item:
            return
        cur = self.tree.set(item, 'select')
        self.tree.set(item, 'select', '☐' if cur == '☑' else '☑')
        self._update_header_check_state()

    def _on_double_click(self, event):
        item = self.tree.identify_row(event.y)
        if not item:
            return
        label = self.tree.set(item, 'mod')
        for t in self.targets:
            if t['label'] == label:
                self._preview_original_values(t)
                break

    def _update_header_check_state(self):
        items = self.tree.get_children()
        if not items:
            self.select_all_var.set(False)
            return
        self.select_all_var.set(all(self.tree.set(i, 'select') == '☑' for i in items))

    def _on_global_slider_change(self, value):
        val = int(round(float(value)))
        descriptions = {0: "Original (×1)", 1: "😎 Cool (≈×1.5)", 2: "😠 Énervé (≈×2)", 3: "🔥 Déchaîné (≈×3)"}
        self.global_slider_label.config(text=descriptions.get(val, ""))
        self._apply_slider_defaults(val)
        self._speed_choice = val
        if hasattr(self, 'simple_speed_buttons'):
            self._highlight_simple_speed()

    def _apply_slider_defaults(self, position):
        levels = {0: '1', 1: '1.5', 2: '2', 3: '3'}
        factor = levels.get(position, '2')
        for cat in CATS:
            self.cat_vars[cat].set('1' if cat == 'detection' else factor)

    def _apply_cam_preset(self, name):
        self.camera_config.reset()
        self.camera_config.apply_preset(name)
        for var in CAM_VARS:
            self.cam_vars[var].set(self.camera_config.get_value(var))
        self._log(f"Preset caméra '{name}' appliqué")

    def _reset_camera(self):
        self.camera_config.reset()
        for var in CAM_VARS:
            self.cam_vars[var].set('')
        self._log("Vue par défaut appliquée")

    def _selected_targets(self):
        selected = []
        for item in self.tree.get_children():
            if self.tree.set(item, 'select') == '☑':
                label = self.tree.set(item, 'mod')
                for t in self.targets:
                    if t['label'] == label:
                        selected.append(t)
                        break
        return selected

    def _check_all_mods(self):
        for item in self.tree.get_children():
            self.tree.set(item, 'select', '☑')

    def _uncheck_all_mods(self):
        for item in self.tree.get_children():
            self.tree.set(item, 'select', '☐')

    def _reset_defaults(self):
        self.global_slider_var.set(2)          # Énervé
        self._on_global_slider_change(2)       # met à jour cat_vars + _speed_choice + surlignage
        self.camera_config.reset()
        for var in CAM_VARS:
            self.cam_vars[var].set('')
        self._cam_choice = ""
        if hasattr(self, 'simple_cam_buttons'):
            self._highlight_simple_cam()
        self._log("Champs réinitialisés aux valeurs par défaut")

    def _load_last_settings(self):
        state = self.config_manager.load_state()
        last_factors = state.get('last_factors', {})
        last_cam = state.get('last_cam', {})

        if last_factors:
            # On charge directement les facteurs mémorisés (sans repasser par le
            # slider, qui écraserait des réglages "perso").
            for cat, val in last_factors.items():
                if cat in self.cat_vars:
                    self.cat_vars[cat].set(str(val))

        if last_cam:
            for var, val in last_cam.items():
                if var in self.cam_vars:
                    self.cam_vars[var].set(str(val))

        self._log("Derniers réglages chargés")

    def _export_config(self):
        config = {
            'global_slider': self.global_slider_var.get(),
            'cam': {v: self.cam_vars[v].get() for v in CAM_VARS},
            'detail_values': {c: self.cat_vars[c].get() for c in CATS},
            'slider_configs': self.slider_configs
        }
        self.config_manager.save_preset(config)
        self._log("Configuration exportée")
        messagebox.showinfo("GenSpeed", "Configuration exportée avec succès!")

    def _import_config(self):
        filename = filedialog.askopenfilename(
            title="Importer une configuration",
            filetypes=[("JSON files", "*.json"), ("All files", "*.*")]
        )
        if not filename:
            return
        try:
            with open(filename, 'r', encoding='utf-8') as f:
                config = json.load(f)
            slider_val = max(0, min(3, int(config.get('global_slider', 2))))
            self.global_slider_var.set(slider_val)
            self._on_global_slider_change(slider_val)
            cam = config.get('cam', {})
            for var in CAM_VARS:
                self.cam_vars[var].set(cam.get(var, ''))
            if config.get('detail_values'):
                for c in CATS:
                    if c in config['detail_values']:
                        self.cat_vars[c].set(config['detail_values'][c])
            if config.get('slider_configs'):
                self.slider_configs = config['slider_configs']
            self._log(f"Configuration importée depuis {filename}")
            messagebox.showinfo("GenSpeed", "Configuration importée avec succès!")
        except Exception as e:
            messagebox.showerror("GenSpeed", f"Erreur import: {e}")

    def _launch_genlauncher(self):
        import ctypes

        exe = os.path.join(self.game_dir, "GenLauncher.exe")
        if not os.path.exists(exe):
            messagebox.showerror("GenSpeed", "GenLauncher.exe introuvable ici.")
            return

        try:
            rc = ctypes.windll.shell32.ShellExecuteW(
                None, "runas", exe, None, self.game_dir, 1
            )
            if rc <= 32:
                self._log("Lancement de GenLauncher annulé ou refusé (UAC).")
            else:
                self._log("GenLauncher lancé (admin).")
        except Exception as e:
            messagebox.showerror("GenSpeed", f"Échec lancement : {e}")
