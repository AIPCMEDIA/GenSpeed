# GenSpeed

**Accélère le gameplay de *Command & Conquer™ Generals – Zero Hour* (et de ses mods), même en LAN.**

GenSpeed est un petit utilitaire Windows qui modifie les fichiers de données (`.ini`) du
jeu/mod pour rendre les parties plus rapides : unités plus véloces, constructions et
recharges plus courtes, etc. Réglages par paliers (Cool / Énervé / Déchaîné) ou détaillés,
presets de caméra, et un outil de vérification de compatibilité pour le multijoueur LAN.

> 🇬🇧 *GenSpeed is a small Windows tool that speeds up C&C Generals: Zero Hour (and its mods)
> by editing the game's `.ini` data. Works in LAN as long as all players apply the same settings.*

---

## ⚠️ Avertissements importants (à lire)

- **Non affilié à Electronic Arts (EA).** *Command & Conquer*, *Generals* et *Zero Hour* sont
  des marques et œuvres déposées d'**Electronic Arts Inc.** GenSpeed est un **outil amateur,
  non officiel**, sans aucun lien avec EA ni les auteurs des mods.
- **Utilisation à vos propres risques.** Le logiciel est fourni « TEL QUEL », **sans aucune
  garantie**. L'auteur **ne peut être tenu responsable** d'un quelconque dommage, perte de
  données, dysfonctionnement, bannissement ou autre conséquence liée à son utilisation.
- **GenSpeed modifie des fichiers du jeu.** Une **sauvegarde automatique** (`.speedbak`) est
  créée et tu peux **revenir à l'original** à tout moment, mais fais tes propres sauvegardes
  si tu y tiens.
- **Pas pour la triche en ligne.** Destiné au **jeu solo / entre amis en LAN**. En réseau,
  **tous les joueurs doivent avoir exactement les mêmes fichiers** (même mod, même version,
  mêmes réglages GenSpeed), sinon « mismatch / désync ». Un outil de vérification est inclus.
- **N'utilise pas ça en partie classée / compétitive.** Ce n'est pas son but.

---

## Installation

1. Avoir **Windows** + **Python 3** ([python.org](https://www.python.org/), cocher
   « Add Python to PATH » à l'installation).
2. Télécharger ce dépôt (bouton vert **Code → Download ZIP**), puis dézipper où tu veux.
3. (Optionnel, recommandé) Installer le thème sombre et le logo :
   ```
   python -m pip install -r requirements.txt
   ```
   Sinon GenSpeed démarre quand même avec un thème clair de secours.
4. Double-cliquer **`GenSpeed.bat`** (ou lancer `python main.py`).
   - `Creer-raccourci-bureau.bat` crée un raccourci sur le Bureau.

GenSpeed **détecte automatiquement** l'installation Steam de Zero Hour ; sinon il te
demande le dossier une fois et le mémorise.

---

## Utilisation rapide

1. **Choisis** un ou plusieurs mods dans la liste (ou rien = jeu vanilla).
2. **Règle la vitesse** : Original / Cool / Énervé / Déchaîné.
3. **Règle la caméra** (optionnel) : vue par défaut ou un preset.
4. **Applique** (demande les droits administrateur pour écrire les fichiers du jeu).
5. **Lance GenLauncher** et joue. Pour revenir au jeu d'origine : bouton **Annuler**.

**Multijoueur (LAN) :** chaque joueur applique **le même réglage sur le même mod**, puis
utilise **🌐 Vérification multijoueur** pour comparer son « code » (il doit être identique
chez tous). En cas de mismatch, l'outil **📜 Versions de la dernière partie** et la
**comparaison de rapports** aident à trouver la différence.

---

## Compatibilité

- **Windows 10 / 11**, jeu Steam de Zero Hour, lancé via **GenPatcher + GenLauncher**.
- Testé sur les mods **Contra**, **NProject**, **ShockWave**, **Rise of the Reds**
  (RotR peut être sensible au désync en LAN entre machines différentes — voir l'outil de
  vérification).

> Astuce LAN : la vitesse de **simulation réseau** de Zero Hour est plafonnée par le moteur
> (~2×). GenSpeed accélère le **gameplay** (données), ce qui marche en LAN tant que tous les
> joueurs ont les mêmes fichiers — c'est l'approche la plus fiable.

---

## Comment ça marche (en bref)

- Lit les archives `.big`/`.gib` du mod, met à l'échelle les valeurs concernées dans les
  `.ini` (vitesses ×N, durées ÷N), et réécrit l'archive.
- Crée un `.speedbak` (sauvegarde) avant toute modification ; le « dépatch » restaure.
- La configuration utilisateur est rangée dans `%LOCALAPPDATA%\GenSpeed` :
  **aucune connexion réseau, aucune télémétrie**, rien n'est envoyé nulle part.

---

## Licence

Distribué sous licence **MIT** (voir [`LICENSE`](LICENSE)). Tu peux l'utiliser, le modifier
et le partager librement. Aucune garantie.

*Command & Conquer™, Generals™ et Zero Hour™ appartiennent à Electronic Arts Inc.
Ce projet n'est ni approuvé ni soutenu par EA.*
