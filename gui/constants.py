# Constantes et libellés de l'interface GenSpeed.
from core import MULT, DIV

VERSION = "1.0"

DIV_ONLY = set(DIV) - set(MULT)

SPEED_LABELS = {1.0: "Original", 1.5: "Cool", 2.0: "Énervé", 3.0: "Déchaîné"}

_SAFE = ('#2ecc71', "Sûr")
CAT_DANGER = {
    'detection': ('#e74c3c', "Peut casser l'équilibrage"),
    'merite': ('#e67e22', "Change un peu l'équilibre"),
    'economie_gain': ('#e67e22', "Change un peu l'équilibre"),
}


def cat_danger(cat):
    return CAT_DANGER.get(cat, _SAFE)


CAT_HELP = {
    'deplacement': "Vitesse, accélération, freinage et rotation des unités (×).",
    'projectiles': "Vitesse des projectiles et des armes (×).",
    'visee': "Vitesse de rotation/inclinaison des tourelles (×).",
    'construction': "Temps de construction des bâtiments/unités (÷ = plus rapide).",
    'tir': "Délai entre tirs et rechargement du chargeur (÷ = plus rapide).",
    'pouvoirs': "Recharge des pouvoirs spéciaux (÷ = plus rapide).",
    'deploiement': "Temps de déballage / préparation (÷ = plus rapide).",
    'economie_collecte': "Délai de collecte des ressources (÷ = plus rapide).",
    'economie_gain': "Valeur des caisses de ravitaillement (×).",
    'detection': "Portée de vision et de révélation (×). ⚠ change l'équilibre.",
    'soin': "Soin : quantité (×) ET fréquence (÷ = plus souvent).",
    'merite': "Expérience gagnée en tuant une unité (×) → promotions plus rapides.",
}
