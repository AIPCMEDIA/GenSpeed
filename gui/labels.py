# Libellés dérivés pour l'affichage de l'état des mods.
from core import CATS

from gui.constants import SPEED_LABELS


def speed_label(factors):
    """Libellé de vitesse à partir des facteurs (Original/Cool/Énervé/Déchaîné/Perso)."""
    if not factors:
        return "Original"
    try:
        vals = {round(float(factors[c]), 3) for c in CATS if c != 'detection' and c in factors}
    except (TypeError, ValueError):
        return "Perso"
    if len(vals) == 1:
        return SPEED_LABELS.get(vals.pop(), "Perso")
    return "Perso"


def cam_label(cam, cam_presets):
    """Nom de la vue caméra à partir des valeurs (preset connu, Original, ou Perso)."""
    if not cam:
        return "Original"
    active = {k: v for k, v in cam.items() if v not in (None, '')}
    if not active:
        return "Original"

    def _eq(a, b):
        try:
            return abs(float(a) - float(b)) < 1e-6
        except (TypeError, ValueError):
            return str(a).strip().upper() == str(b).strip().upper()

    for name, preset in cam_presets.items():
        if name == 'Reset camera':
            continue
        pv = {k: v for k, v in preset.items() if v not in (None, '')}
        if pv and set(pv) == set(active) and all(_eq(active[k], pv[k]) for k in pv):
            return name
    return "Perso"
