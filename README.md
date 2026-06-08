<div align="center">

<img src="png/logo.png" width="120" alt="GenSpeed logo"/>

# GenSpeed 🚀⚡

**Accélère le gameplay de *Command & Conquer™ Generals – Zero Hour* (et de ses mods), même en LAN.**
*Speed up the gameplay of C&C Generals: Zero Hour (and its mods), even in LAN.*

### [⬇️ TÉLÉCHARGER GenSpeed.exe / DOWNLOAD GenSpeed.exe](https://github.com/AIPCMEDIA/GenSpeed/releases/latest/download/GenSpeed.exe)

[![Download](https://img.shields.io/badge/⬇️_Télécharger_/_Download-GenSpeed.exe_(direct)-5a9030?style=for-the-badge)](https://github.com/AIPCMEDIA/GenSpeed/releases/latest/download/GenSpeed.exe)
[![Platform](https://img.shields.io/badge/Windows-10_/_11-blue?style=flat)](#)
[![License](https://img.shields.io/badge/license-MIT-lightgrey?style=flat)](LICENSE)

> 👉 Clique le bouton vert ci-dessus = tu télécharges **directement `GenSpeed.exe`**.
> Ne télécharge **pas** le « *Source code (zip)* » : c'est le code source, il **ne contient pas** l'application.
> *Click the green button above to download **`GenSpeed.exe`** directly. Do **not** download "Source code (zip)" — that's the source code, it does **not** contain the app.*

</div>

> 🇫🇷 GenSpeed modifie les fichiers de données (`.ini`) du jeu/mod pour rendre les parties plus rapides : unités plus véloces, constructions/recharges plus courtes, presets de caméra. Plus un **outil de diagnostic de désync (mismatch) LAN** qui compare ton install à celle d'un ami et nomme exactement ce qui diffère.
>
> 🇬🇧 GenSpeed edits the game/mod `.ini` data to make matches faster: quicker units, shorter build/reload times, camera presets. Plus a **LAN mismatch (desync) diagnostic** that compares your install with a friend's and names exactly what differs.

---

## ✨ Nouveau dans la v2.0 / New in v2.0

🇫🇷
- 🖥️ **Application autonome (.exe)** — **plus besoin de Python**, double-clic et c'est parti.
- 🎨 **3 thèmes** (Terminal EVA, USA, Chine) + **interface bilingue FR/EN**.
- 🩺 **Diagnostic mismatch complet** : inventaire **nommé** de tout l'écosystème (jeu, mods + versions, addons, GenTool, redists…) et comparaison entre joueurs.
- ⚡ Redimensionnement fluide, presets vitesse/caméra modifiables (créer/renommer/supprimer), confirmation détaillée avant patch.

🇬🇧
- 🖥️ **Standalone .exe** — **no Python required** anymore, just double-click.
- 🎨 **3 themes** (EVA Terminal, USA, China) + **bilingual FR/EN UI**.
- 🩺 **Full mismatch diagnostic**: **named** inventory of the whole ecosystem (game, mods + versions, add-ons, GenTool, redists…) and player-to-player comparison.
- ⚡ Smooth resizing, editable speed/camera presets (create/rename/delete), detailed pre-patch confirmation.

---

## 📸 Aperçu / Screenshots

| Écran principal / Main window | Diagnostic mismatch |
|:---:|:---:|
| ![Main](docs/screenshots/01-main-eva.png) | ![Diagnostic](docs/screenshots/05-diagnostic.png) |
| **Confirmation avant patch / Apply confirmation** | **Thèmes / Themes** |
| ![Confirm](docs/screenshots/04-confirm.png) | ![USA theme](docs/screenshots/02-theme-usa.png) ![China theme](docs/screenshots/03-theme-china.png) |

---

## ⚙️ Configuration requise / Requirements

> ⚠️ **GenSpeed est conçu pour l'écosystème Steam + GenLauncher + GenPatcher.**
> *GenSpeed is designed for the Steam + GenLauncher + GenPatcher ecosystem.*

| | |
|---|---|
| **OS** | Windows 10 / 11 (64-bit) |
| **Jeu / Game** | C&C Generals – Zero Hour via **Steam** |
| **Mods** | gérés par **GenLauncher** (dossier `GLM`) / managed by **GenLauncher** (`GLM` folder) |
| **Patchs** | **GenPatcher** (patch communautaire, redists…) |
| **.NET** | ❌ rien à installer / nothing to install (runtime embarqué dans l'exe) |

**Ne fonctionne PAS avec / Does NOT work with :** versions CD/DVD, GOG, ou installations non-Steam.

---

## ⬇️ Téléchargement & lancement / Download & run

1. **Télécharge `GenSpeed.exe`** → **[clic ici pour le téléchargement direct](https://github.com/AIPCMEDIA/GenSpeed/releases/latest/download/GenSpeed.exe)** (un seul fichier, pas un zip).
   *Download `GenSpeed.exe` → **[click here for the direct download](https://github.com/AIPCMEDIA/GenSpeed/releases/latest/download/GenSpeed.exe)** (a single file, not a zip).*
2. **Double-clique dessus.** Aucune installation, rien à décompresser. / **Double-click it.** No install, nothing to unzip.
3. GenSpeed **détecte automatiquement** ton install Steam + tes mods GenLauncher. Sinon, il te demande le dossier une fois et le mémorise.
   *GenSpeed **auto-detects** your Steam install + GenLauncher mods. Otherwise it asks for the folder once and remembers it.*

> 💡 Au 1er lancement, Windows SmartScreen peut afficher « éditeur inconnu » (exe non signé) → **Informations complémentaires → Exécuter quand même**. C'est normal.
> *On first run, Windows SmartScreen may say "unknown publisher" (unsigned exe) → **More info → Run anyway**. This is expected.*

---

## 🎮 Fonctionnalités / Features

- ✅ **Jeu de base + mods** (détection auto via GenLauncher / auto-detect via GenLauncher)
- ⚡ Paliers de vitesse **Original / Cool / Énervé / Déchaîné** + presets personnalisables
- 🎛️ Réglage **détaillé par catégorie** (déplacement, tir, construction, vision…)
- 🎥 **Presets de caméra** (vue haute, max, satellite… + personnalisés)
- 💾 **Sauvegarde + restauration automatiques** (`.speedbak`)
- 🛡️ **Code LAN** (hash) pour vérifier que tous les joueurs ont les mêmes fichiers
- 🩺 **Diagnostic mismatch** : exporte ton empreinte, compare avec un ami, verdict détaillé
- 🔎 Aperçu des valeurs (clés / exhaustif / modifiées) + ouverture du dossier du mod
- 🎨 3 thèmes · 🌐 FR/EN · 🔒 **Aucune télémétrie, aucune connexion internet**

---

## 🕹️ Utilisation rapide / Quick start

1. **Coche un ou plusieurs mods** dans la liste (clic sur la ligne). / **Check one or more mods** (click the row).
2. **Règle la vitesse** : Original (×1) · Cool (≈×1.5) · Énervé (≈×2, recommandé) · Déchaîné (≈×3).
3. **Caméra** (optionnel) : un preset ou réglages manuels. / **Camera** (optional): a preset or manual values.
4. **Appliquer la config** → une fenêtre récapitule ce que ça change, tu valides (UAC). / **Apply** → a window summarizes the changes, you confirm (UAC).
5. **▶ Lancer GenLauncher** et joue ! Pour revenir à l'original : **Annuler**.

🖱️ **Clic droit sur un mod** : aperçus + ouvrir son dossier. / **Right-click a mod**: previews + open its folder.

---

## 🌐 Multijoueur LAN & diagnostic mismatch

🇫🇷 En réseau, **tous les joueurs doivent avoir exactement les mêmes fichiers** (même jeu, même mod/version, mêmes réglages), sinon « mismatch / désync ». GenSpeed aide :

1. **🛡 Calculer mon code LAN** — un hash de ton install ; comparez-le, il doit être **identique**.
2. **🩺 Diagnostic mismatch** — chacun **exporte son empreinte** (un fichier `.json`), l'un de vous **compare** celle de l'autre. GenSpeed liste, **par gravité et avec des noms clairs**, ce qui diffère :
   - ❌ **Critique** (cause le désync) : version du jeu, INI, **mods + version** (« Contra 10.0.2 ↔ 10.0.1 »)
   - ⚠️ **Attention** : maps
   - ℹ️ **Info** (contexte) : GenTool, GenLauncher, addons, VC++ redists, résolution…
3. **📜 Dernier replay** — version + map + CRC de ta dernière partie.

🇬🇧 In LAN, **all players must have exactly the same files** or you get a mismatch/desync. GenSpeed's **🩺 diagnostic** has each player export a fingerprint and compares them, listing what differs **by severity with clear names** (e.g. "Mod Contra 10.0.2 ↔ 10.0.1"). The shared report contains **no personal data** — only file/mod/add-on names, versions and truncated hashes.

---

## 🔧 Workflow conseillé / Recommended workflow

```
1. Steam + C&C Generals – Zero Hour (jeu de base / base game)
        ↓
2. GenPatcher (patch communautaire, redists…)
        ↓
3. GenLauncher + mods.  Dans les options / In the options:
   ✔ "Use default Camera height (recommended)"
   ✔ "Use modded exe files (recommended)"
   ✔ "Disable GenTool"
   ✘ "Check Mod files integrity"
   ✘ "Hide GenLauncher while the Game is running"
        ↓
4. GenSpeed → règle vitesse/caméra, Appliquer, Lancer GenLauncher → joue !
```

---

## 🧠 Comment ça marche / How it works

- Lit les archives `.big`/`.gib` du jeu/mod et modifie les variables `.ini` (vitesses ×N, durées ÷N).
- Crée un `.speedbak` **à côté de chaque fichier** avant toute modification → **dépatch** = restauration exacte.
- Config locale dans `%LOCALAPPDATA%\GenSpeed` (aucune télémétrie).
- Patch identique **octet-pour-octet** à la v1.0 Python → compatibilité LAN entre toutes les versions.

*Reads the mod's `.big`/`.gib` archives, scales `.ini` variables, backs up each file as `.speedbak` (exact restore). Local config in `%LOCALAPPDATA%\GenSpeed`. Patching is **byte-for-byte identical** to the Python v1.0 → LAN-compatible across versions.*

---

## 🛠️ Compiler depuis les sources / Build from source

Le code C# (.NET 8 / WPF) est dans [`dotnet/`](dotnet/). / The C# code (.NET 8 / WPF) is in [`dotnet/`](dotnet/).

```powershell
cd dotnet
dotnet run --project src/GenSpeed.App      # lancer en dev / run in dev
.\publish.ps1                              # générer l'exe autonome / build the standalone exe
```

---

## ⚠️ Avertissements / Disclaimers

- **Non affilié à Electronic Arts.** *Command & Conquer™, Generals™, Zero Hour™* appartiennent à **Electronic Arts Inc.** Projet **amateur, non officiel**, sans lien avec EA ni les auteurs des mods.
- **À tes propres risques.** Fourni « TEL QUEL », **sans garantie**. L'auteur n'est **pas responsable** d'éventuels dommages, pertes de données ou bannissements.
- **Pas pour la triche en ligne / compétitive.** Conçu pour le solo et le **LAN entre amis**.

*Not affiliated with EA. Provided "AS IS", no warranty, use at your own risk. Not for online/ranked cheating — made for solo and LAN-with-friends play.*

---

## 🤖 À propos du code / About the code

🇫🇷 Le code de GenSpeed est **écrit par des IA** (Claude Code, etc.). Moi, je ne code pas : mon rôle est plutôt celui d'un **architecte / chef d'orchestre** — je définis la vision et les fonctionnalités, je guide les choix, je teste et j'oriente. GenSpeed existe parce que je voulais retrouver la rapidité de l'Escarmouche… mais en LAN entre potes. Avis aux moddeurs : ce serait génial de le peaufiner, voire de l'intégrer à GenLauncher 😉. Un bug, une idée ? Ouvre une **issue** !

🇬🇧 GenSpeed's code is **written by AI** (Claude Code, etc.). I don't write the code myself — my role is closer to an **architect / director**: I set the vision and features, guide the decisions, test and steer. GenSpeed exists because I wanted Skirmish-like speed in LAN with friends. Modders welcome to improve it! Found a bug or have an idea? Open an **issue**.

---

## 📜 Licence / License

MIT — voir [`LICENSE`](LICENSE). Libre d'utilisation, modification et partage, sans garantie.

*Command & Conquer™, Generals™ and Zero Hour™ are property of Electronic Arts Inc. This project is neither endorsed nor supported by EA.*

---

## 📝 Changelog

### v2.0
- 🖥️ Réécriture complète en **C# / .NET 8 (WPF)** → **exe autonome**, plus de Python
- 🩺 **Diagnostic mismatch** avec inventaire nommé de l'écosystème (mods+versions, addons, GenTool, système)
- 🎨 **3 thèmes** + interface **bilingue FR/EN**
- 🛠️ Presets vitesse/caméra **modifiables** (CRUD), confirmation détaillée, clic droit + aperçus copiables
- ⚡ Redimensionnement fluide, persistance config, détection + sélection manuelle du dossier de jeu

### v1.0
- Système de patching, modes Simple/Avancé, presets caméra, vérification LAN, auto-détection Steam, sauvegarde/restauration, intégration GenLauncher & GenPatcher *(version Python — conservée dans l'historique git)*

---

<div align="center">

**Enjoy faster gameplay! 🚀⚡** · *Pour Steam + GenPatcher + GenLauncher.*

</div>
