# Widgets Tkinter réutilisables.
import tkinter as tk


class ToolTip:
    """Infobulle simple pour n'importe quel widget Tkinter."""

    def __init__(self, widget, text):
        self.widget = widget
        self.text = text
        self.tip = None
        widget.bind('<Enter>', self._show)
        widget.bind('<Leave>', self._hide)

    def _show(self, _event):
        if self.tip or not self.text:
            return
        x = self.widget.winfo_rootx() + 20
        y = self.widget.winfo_rooty() + self.widget.winfo_height() + 2
        self.tip = tk.Toplevel(self.widget)
        self.tip.wm_overrideredirect(True)
        self.tip.wm_geometry(f"+{x}+{y}")
        tk.Label(self.tip, text=self.text, background='#ffffe0', relief='solid',
                 borderwidth=1, font=('Segoe UI', 8), justify='left',
                 wraplength=320).pack()

    def _hide(self, _event):
        if self.tip:
            self.tip.destroy()
            self.tip = None
