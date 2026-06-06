# Thème visuel, assets et icône de fenêtre.
import os

import tkinter as tk
from tkinter import ttk

_PKG_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def asset_dir():
    """Dossier png/ à la racine du projet GenSpeed."""
    return os.path.join(_PKG_ROOT, 'png')


class ThemeMixin:
    """Palette de couleurs et chargement logo/icône."""

    def _init_palette(self):
        """Définit la palette (sombre C&C si ttkbootstrap, sinon clair = ancien look)."""
        if self.dark:
            try:
                col = self.root.style.colors
                bg, fg, inputbg = col.bg, col.fg, col.inputbg
            except Exception:
                bg, fg, inputbg = '#222222', '#e6e6e6', '#2b2b2b'
            self.c_bg, self.c_fg, self.c_inputbg = bg, fg, inputbg
            self.c_card_bg = '#2b2b2b'
            self.c_dim = '#9aa0a6'
            self.c_primary, self.c_primary_h, self.c_primary_fg = '#E8A317', '#cf9214', '#1a1a1a'
            self.c_go, self.c_go_h, self.c_go_fg = '#5a8f3d', '#4e7d35', '#ffffff'
            self.c_inactive, self.c_inactive_fg = '#3a3f44', fg
            self.tag_orig = {'background': bg, 'foreground': fg}
            self.tag_patched = {'background': '#3d3320', 'foreground': '#E8A317'}
            self.tag_mod = {'background': '#26402a', 'foreground': '#8fe08f'}
            self.c_hashlbl = '#E8A317'
            self.c_base_fg = '#8fb3d6'
        else:
            self.c_bg, self.c_fg, self.c_inputbg = 'SystemButtonFace', 'black', 'white'
            self.c_card_bg = '#f4f4f4'
            self.c_dim = '#777777'
            self.c_primary, self.c_primary_h, self.c_primary_fg = '#2ecc71', '#27ae60', 'white'
            self.c_go, self.c_go_h, self.c_go_fg = '#3498db', '#2980b9', 'white'
            self.c_inactive, self.c_inactive_fg = 'SystemButtonFace', 'black'
            self.tag_orig = {'background': '#ffffff', 'foreground': '#333333'}
            self.tag_patched = {'background': '#d6f5d6', 'foreground': '#0a5d22'}
            self.tag_mod = {'background': '#d6f5d6', 'foreground': '#0a5d22'}
            self.c_hashlbl = '#0a0'
            self.c_base_fg = '#2c3e50'

    def _place_logo(self, parent):
        """Affiche le logo (png/logo.png) à gauche de la barre de titre, si présent."""
        self._logo_img = None
        candidate = None
        for name in ('logo.png', 'genspeed.png', 'icon.png'):
            p = os.path.join(asset_dir(), name)
            if os.path.isfile(p):
                candidate = p
                break
        if not candidate:
            return False
        try:
            from PIL import Image, ImageTk
            im = Image.open(candidate).convert('RGBA')
            h = 96
            w = max(1, int(im.width * h / im.height))
            im = im.resize((w, h), Image.LANCZOS)
            self._logo_img = ImageTk.PhotoImage(im)
        except Exception:
            try:
                self._logo_img = tk.PhotoImage(file=candidate)
            except Exception:
                self._logo_img = None
        if self._logo_img:
            ttk.Label(parent, image=self._logo_img).pack(side='left')
            return True
        return False

    def _set_window_icon(self):
        ico = os.path.join(asset_dir(), 'genspeed.ico')
        if os.path.isfile(ico):
            try:
                self.root.iconbitmap(ico)
            except Exception:
                pass
