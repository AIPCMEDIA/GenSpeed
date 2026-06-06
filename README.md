# GenSpeed 🚀⚡

**Accélère le gameplay de *Command & Conquer™ Generals – Zero Hour* (et de ses mods), même en LAN.**

GenSpeed est un petit utilitaire Windows qui modifie les fichiers de données (`.ini`) du jeu/mod pour rendre les parties plus rapides : unités plus véloces, constructions et recharges plus courtes, etc. Réglages par paliers (Cool / Énervé / Déchaîné) ou détaillés, presets de caméra, et un outil de vérification de compatibilité pour le multijoueur LAN.

> 🇬🇧 *GenSpeed is a small Windows tool that speeds up C&C Generals: Zero Hour (and its mods) by editing the game's `.ini` data. Works in LAN as long as all players use the same mod version and identical GenSpeed settings.*

---

## ⚙️ Configuration requise

### Important : Plateforme Steam + GenLauncher + GenPatcher

⚠️ **GenSpeed fonctionne UNIQUEMENT avec :**
- **Command & Conquer: Generals - Zero Hour** installé via **Steam**
- **GenLauncher** (gestionnaire de mods/launcher)
- **GenPatcher** (application de patchs supplémentaires)

**Ne fonctionne PAS avec :**
- ❌ Versions CD/DVD (anciennes)
- ❌ GOG ou autres stores
- ❌ Installations non-Steam
- ❌ Sans GenLauncher/GenPatcher

### Configuration minimale
- **Windows 10 / 11** (64-bit recommandé)
- **Python 3.7+** ([python.org](https://www.python.org/))
- **Steam** avec **C&C Generals ZH** installé
- **GenLauncher + GenPatcher** configurés

---

## Fonctionnalités / Features

- ✅ Vanilla **+ mods** (détection automatique via GenLauncher)
- ⚡ Paliers de vitesse **Cool / Énervé / Déchaîné** (+ Original)
- 🎛️ Réglage **détaillé par catégorie** (mode avancé)
- 🎥 **Presets de caméra**
- 💾 **Sauvegarde + restauration automatiques** (`.speedbak`)
- 🌐 **Vérificateur de compatibilité LAN** (code + comparaison de fichiers)
- 🔎 Détection **Steam** automatique
- 🔒 **Aucune télémétrie, aucune connexion internet**

---

## 📋 Compatibilité

### Mods testés & supportés
- ✅ **Vanilla (C&C Generals - Zero Hour de base)**
- ✅ **Contra**
- ✅ **NProject**
- ✅ **ShockWave** / **ShockWave Chaos**
- ✅ **Rise of the Reds** (RotR)
- ✅ **Autres mods SAGE** (probablement compatibles)

### Plateforme
- **Windows 10 / 11** (64-bit recommandé)
- **Steam Edition** (obligatoire)
- **Python 3.7+**
- **GenLauncher** (pour la gestion des mods)
- **GenPatcher** (pour les patchs supplémentaires)

> **Remarque LAN :** la vitesse de **simulation réseau** de Zero Hour est plafonnée par le moteur (~2×). GenSpeed accélère le **gameplay** (données), ce qui marche en LAN tant que tous les joueurs ont les mêmes fichiers — c'est l'approche la plus fiable.

---

## ⚠️ Avertissements importants (à lire)

- **Non affilié à Electronic Arts (EA).** *Command & Conquer*, *Generals* et *Zero Hour* sont des marques et œuvres déposées d'**Electronic Arts Inc.** GenSpeed est un **outil amateur, non officiel**, sans aucun lien avec EA ni les auteurs des mods.
- **Utilisation à vos propres risques.** Le logiciel est fourni « TEL QUEL », **sans aucune garantie**. L'auteur **ne peut être tenu responsable** d'un quelconque dommage, perte de données, dysfonctionnement, bannissement ou autre conséquence liée à son utilisation.
- **GenSpeed modifie des fichiers du jeu.** Une **sauvegarde automatique** (`.speedbak`) est créée et tu peux **revenir à l'original** à tout moment, mais fais tes propres sauvegardes si tu y tiens.
- **Pas pour la triche en ligne.** Destiné au **jeu solo / entre amis en LAN**. En réseau, **tous les joueurs doivent avoir exactement les mêmes fichiers** (même mod, même version, mêmes réglages GenSpeed), sinon « mismatch / désync ». Un outil de vérification est inclus.
- **N'utilise pas ça en partie classée / compétitive.** Ce n'est pas son but.

---

## Installation

### Prérequis
1. **Windows 10/11**
2. **Steam** avec **C&C Generals - Zero Hour** installé
3. **GenLauncher** et **GenPatcher** configurés
4. **Python 3.7+** ([python.org](https://www.python.org/) — cocher « Add Python to PATH » à l'installation)

### Étapes

1. **Vérifier** que GenLauncher et GenPatcher sont correctement installés et configurés

2. **Télécharger** ce dépôt (bouton vert **Code → Download ZIP**), puis dézipper où tu veux.

3. **(Optionnel, recommandé)** Installer les dépendances pour le thème sombre :
   ```bash
   python -m pip install -r requirements.txt
   ```
   Sinon GenSpeed démarre quand même avec un thème clair de secours.

4. **Démarrer GenSpeed** :
   - Double-cliquer **`GenSpeed.bat`** (le plus simple)
   - Ou lancer `python main.py` en ligne de commande

5. **(Optionnel)** `Creer-raccourci-bureau.bat` crée un raccourci sur le Bureau pour accès rapide.

### Auto-détection
GenSpeed **détecte automatiquement** l'installation Steam de Zero Hour. Si ce n'est pas trouvé, il te demande le dossier une fois et le mémorise.

---

## Utilisation rapide

### Mode Simple (Recommandé)

1. **Sélectionne tes mods** dans la liste (ou rien = jeu vanilla)
2. **Règle la vitesse** : 
   - 🟢 **Original** (×1) – sans changement
   - 😎 **Cool** (≈×1.5) – léger boost
   - 😠 **Énervé** (≈×2) – recommandé (bon équilibre)
   - 🔥 **Déchaîné** (≈×3) – très rapide
3. **Règle la caméra** (optionnel) – vue par défaut ou un des presets
4. **Appliquer** (demande les droits administrateur)
5. **Lance GenLauncher** et joue !

Pour revenir au jeu d'origine : bouton **Annuler**.

### Mode Avancé

Règle **chaque catégorie individuellement** :
- Déplacement, Projectiles, Visée, Construction, Tir, Pouvoirs, Déploiement, Économie, Vision, Soin, Mérite (XP)

---

## 🌐 Multijoueur LAN

### Configuration
1. Chaque joueur **sélectionne le même mod** et applique **les mêmes réglages GenSpeed**
2. Utilise **🌐 Vérification multijoueur** pour comparer votre « code »
3. Les codes **doivent être IDENTIQUES** pour jouer sans désync

### Outils de diagnostic
- **🛡 Vérifier mon code** – calcule ton hash personnel (base Steam + mods cochés)
- **📜 Versions de ma dernière partie** – affiche jeu/mod/patch + map du dernier replay
- **📤 Exporter rapport** – crée un fichier détaillé (empreinte par fichier)
- **🔍 Comparer rapport ami** – charge le rapport d'un copain, localise les différences exactes

---

## 🔧 Workflow complet : GenSpeed + GenLauncher + GenPatcher

GenSpeed s'intègre dans un **écosystème complet** :

```
1. GenSpeed (configuration vitesse/caméra du mod)
       ↓
2. GenPatcher (application de patchs additionnels)
       ↓
3. GenLauncher (lancement du jeu avec tout appliqué)
       ↓
4. Zero Hour + mods + speed + patchs
```

**Exemple concret :**
- Ouvre **GenSpeed**
- Sélectionne "Contra"
- Configure vitesse : "Énervé (×2)"
- Configure caméra : "Cam haute"
- Clique "Appliquer"
- Dans GenSpeed, clique "🚀 Lancer GenLauncher"
- GenLauncher démarre avec tes réglages appliqués
- GenPatcher applique d'autres patchs si nécessaire
- **Joue !**

---

## Comment ça marche (technique)

- **Lecture d'archives** : lit les fichiers `.big`/`.gib` du mod (via Steam)
- **Modification .ini** : applique les facteurs de vitesse aux variables concernées
  - Vitesses ×N (déplacement, projectiles, etc.)
  - Durées ÷N (construction, recharge, etc.)
- **Sauvegarde sécurisée** : crée un `.speedbak` avant toute modification
- **Dépatch** : restaure l'original via le backup
- **Configuration locale** : stockée dans `%LOCALAPPDATA%\GenSpeed` (aucune télémétrie)
- **Intégration GenLauncher** : fonctionne en tandem avec le launcher Steam

---

## À propos du code

⚠️ **Note importante** : Ce projet a été **généré et développé par une IA** (GitHub Copilot).
Je ne suis **pas développeur professionnel**. Le code fonctionne selon les spécifications, mais il peut contenir des améliorations possibles ou des cas limites non gérés.

Si tu trouves un bug ou une amélioration, n'hésite pas à ouvrir une **issue** ! 🤖

---

## Licence

Distribué sous licence **MIT** (voir [`LICENSE`](LICENSE)). Tu peux l'utiliser, le modifier et le partager librement. Aucune garantie.

*Command & Conquer™, Generals™ et Zero Hour™ appartiennent à Electronic Arts Inc.*
*Ce projet n'est ni approuvé ni soutenu par EA.*

---

## Ressources communauté

- 🌍 [ModDB - C&C Generals Mods](https://www.moddb.com/games/c-c-generals)
- 💬 [Reddit r/commandandconquer](https://www.reddit.com/r/commandandconquer/)
- 🎮 [CNC Online](https://cnc-online.net/)
- 📺 [ZeroHour.net](https://www.zerohour.net/)

---

## Changelog

### v1.0 (Initial Release)
- ✅ Système de patching complet (multiplicatif/division par catégorie)
- ✅ Mode Simple (4 paliers) + Mode Avancé (détaillé)
- ✅ Presets caméra intégrés
- ✅ Vérification LAN (hash + comparaison de fichiers)
- ✅ Auto-détection Steam
- ✅ Sauvegarde/restauration automatiques
- ✅ Intégration GenLauncher & GenPatcher

---

**Enjoy faster gameplay! 🚀⚡**

*Pour Steam, GenLauncher, et GenPatcher uniquement.*
