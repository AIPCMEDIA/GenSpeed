# GenSpeed v2.0 — speed up Zero Hour (and its mods), even in LAN

> 📋 **How to use this file:** copy/paste into a ModDB *Article / News* post (or the mod page summary).
> Add screenshots where indicated. Two versions below: **English** then **Français**.

---

## 🏷️ Suggested summary (ModDB "summary" field)

> **GenSpeed** is a free Windows tool that speeds up the gameplay of *C&C Generals: Zero Hour* and its mods (Contra, ROTR, ShockWave, NProject…) — quicker units, shorter build/reload times, camera presets. v2.0 is a standalone .exe (no Python) with a built-in **LAN mismatch diagnostic** that tells you exactly why you desync with a friend.

---

## 🇬🇧 English

### Tired of slow LAN games? Want Skirmish-like speed with your friends?

**GenSpeed** edits the `.ini` game data of Zero Hour (and any GenLauncher mod) to make matches faster: units move quicker, buildings and reloads are shorter, powers recharge faster. Pick a preset (Cool / Angry / Unleashed) or fine-tune every category. Every change is backed up (`.speedbak`) and **fully reversible** in one click.

### 🆕 What's new in v2.0

- 🖥️ **Standalone `.exe`** — no Python, no install. Just download and double-click.
- 🩺 **Mismatch (desync) diagnostic** — the big one. Each player exports a small fingerprint file; GenSpeed compares two installs and lists, **by severity and with clear names**, exactly what differs:
  - ❌ **Critical** (causes the desync): game version, INI overrides, **mods + version** — e.g. *"Mod Contra: 10.0.2 ↔ 10.0.1"*
  - ⚠️ **Warning**: maps
  - ℹ️ **Info**: GenTool, GenLauncher, add-ons, VC++ redistributables, resolution…
  
  No more guessing why you mismatch. The shared report contains **no personal data** — only file/mod/add-on names, versions and short hashes.
- 🎨 **3 themes** (EVA Terminal, USA, China) + a fully **bilingual FR/EN** interface.
- 🛠️ Editable speed & camera **presets** (create / rename / delete), a detailed **confirmation screen** before patching (it tells you, in plain words, what each change does in-game), right-click previews, and "open mod folder".
- 📁 **Change the game folder — and the mods (GLM) folder — from the app** (⚙ Config menu): a wrong folder picked on first launch, or a GenLauncher installed elsewhere, is no longer a problem (with on-screen hints to locate them).
- ❔ **Built-in help** (❔ button): a bilingual help window covering quick start, speed/camera, folders, LAN & mismatch, backup/restore and an About section.

### 🎮 Features

- Base game **+ mods**, auto-detected via GenLauncher (GLM)
- Speed presets + per-category tuning (movement, firing, construction, vision…)
- Camera presets (high / max / satellite + custom)
- Automatic backup & restore (`.speedbak`)
- LAN "code" (hash) to confirm everyone has identical files
- Last-replay reader (version + map + CRC)
- No telemetry, no internet connection

### ⚙️ Requirements

- Windows 10 / 11 (64-bit)
- *C&C Generals: Zero Hour* via **Steam**
- **GenPatcher** + **GenLauncher** (the usual community setup)
- Nothing else — the .NET runtime is bundled in the exe.

### 🧭 Recommended setup & the LAN "golden rule"

**Install order (do the SAME on every PC for LAN):**
1. **Steam:** launch *Generals* once, then *Zero Hour* once (initializes the game).
2. **GenPatcher:** Win10/11 fixes + redists. **GenTool is optional** — for GenSpeed + LAN you can skip it (or disable it in GenLauncher). Other extras (Control Bar Pro, map packs, hotkeys, World Builder) are optional too; hotkeys & World Builder are purely local.
3. **GenLauncher options:** ✔ *Use default Camera height* (let GenSpeed handle the camera), ✔ *Use modded .exe*, ✘ *Check Mod files integrity* (OFF — it would revert GenSpeed). The rest to taste.
4. Launch **Zero Hour once**; if it offers a "recommended configuration", **decline it**.
5. Install your **mods** (+ patches/add-ons) and **launch each once**.
6. Run **GenSpeed last**.

**The LAN golden rule — avoid mismatches.** On **both** PCs you need: ① the same game + same mods (same versions), ② the same GenSpeed settings, ③ only play maps you **both** have.
- A map you **don't play** never causes a desync — even if only one of you has it. Only the **map actually played** matters (its `.map` + `map.ini` must be identical).
- **Mod maps are bundled inside the mod** → identical if you share the same mod version. Nothing to share by hand.
- "Missing a map" is just **availability** (the game can transfer it), **not** a desync.

### ⬇️ Download

**Direct download (single file, not a zip):**
**https://github.com/AIPCMEDIA/GenSpeed/releases/latest**

> Just download `GenSpeed.exe` and double-click — no install, nothing to unzip. Do **not** download "Source code (zip)" on GitHub; that's only the source, not the app.
> First run: Windows SmartScreen may warn "unknown publisher" (the exe isn't code-signed) → *More info → Run anyway*.

### ⚠️ Disclaimer

Not affiliated with Electronic Arts. Provided "AS IS", no warranty, use at your own risk. Made for **solo and LAN-with-friends** play — **not** for online/ranked cheating. *C&C™, Generals™, Zero Hour™ © Electronic Arts Inc.*

*(GenSpeed's code is **written by AI** — I act as the **architect / director** (vision, features, testing), not the coder. Open-source under the MIT license; modders welcome to improve it, or integrate it into GenLauncher 😉.)*

> 📸 **Screenshots** (à uploader sur ModDB depuis `docs/screenshots/` / upload these to ModDB):
> - `01-main-eva.png` — main window (EVA theme)
> - `05-diagnostic.png` — the mismatch diagnostic verdict (the headline feature)
> - `04-confirm.png` — the apply confirmation (plain-language effects)
> - `02-theme-usa.png` / `03-theme-china.png` — the USA & China themes

---

## 🇫🇷 Français

### Marre des parties LAN trop lentes ? Tu veux la rapidité de l'Escarmouche, mais entre potes ?

**GenSpeed** modifie les données `.ini` de Zero Hour (et de n'importe quel mod GenLauncher) pour accélérer les parties : unités plus rapides, constructions et recharges plus courtes, pouvoirs plus prompts. Choisis un palier (Cool / Énervé / Déchaîné) ou règle chaque catégorie. Chaque modification est sauvegardée (`.speedbak`) et **réversible en un clic**.

### 🆕 Nouveautés v2.0

- 🖥️ **Exe autonome** — plus de Python, aucune installation. Télécharge, double-clique.
- 🩺 **Diagnostic de désync (mismatch)** — la grande nouveauté. Chaque joueur exporte une petite empreinte ; GenSpeed compare deux installations et liste, **par gravité et avec des noms clairs**, ce qui diffère :
  - ❌ **Critique** (cause le désync) : version du jeu, INI, **mods + version** — ex. *« Mod Contra : 10.0.2 ↔ 10.0.1 »*
  - ⚠️ **Attention** : maps
  - ℹ️ **Info** : GenTool, GenLauncher, addons, redists VC++, résolution…
  
  Fini de deviner pourquoi ça désync. Le rapport partagé **ne contient aucune info perso** — juste des noms de fichiers/mods/addons, versions et hash courts.
- 🎨 **3 thèmes** (Terminal EVA, USA, Chine) + interface **bilingue FR/EN**.
- 🛠️ Presets vitesse & caméra **modifiables**, **écran de confirmation détaillé** avant patch (il explique en clair ce que chaque réglage change en jeu), aperçus au clic droit, et « ouvrir le dossier du mod ».
- 📁 **Changer le dossier du jeu — et celui des mods (GLM) — depuis l'appli** (menu ⚙ Config) : un mauvais dossier choisi au 1er lancement, ou un GenLauncher installé ailleurs, n'est plus un souci (avec conseils d'emplacement à l'écran).
- ❔ **Aide intégrée** (bouton ❔) : une fenêtre d'aide bilingue couvrant le démarrage rapide, la vitesse/caméra, les dossiers, le LAN & mismatch, la sauvegarde/restauration et une section « À propos ».

### 🧭 Installation conseillée & la « règle d'or » du LAN

**Ordre d'installation (identique sur chaque PC pour le LAN) :**
1. **Steam :** lance *Generals* une fois, puis *Zero Hour* une fois (initialise le jeu).
2. **GenPatcher :** correctifs Win10/11 + redists. **GenTool est optionnel** — pour GenSpeed + LAN tu peux le sauter (ou le désactiver dans GenLauncher). Les autres extras (Control Bar Pro, packs de maps, hotkeys, World Builder) sont aussi optionnels ; hotkeys & World Builder sont purement locaux.
3. **Options GenLauncher :** ✔ *Use default Camera height* (laisse GenSpeed gérer la caméra), ✔ *Use modded .exe*, ✘ *Check Mod files integrity* (OFF — sinon il annule GenSpeed). Le reste selon ton goût.
4. Lance **Zero Hour une fois** ; s'il propose une « recommended configuration », **refuse-la**.
5. Installe tes **mods** (+ patchs/addons) et **lance chacun une fois**.
6. Utilise **GenSpeed en dernier**.

**La règle d'or du LAN — éviter les mismatch.** Sur les **deux** PC il faut : ① même jeu + mêmes mods (mêmes versions), ② mêmes réglages GenSpeed, ③ ne jouer que des cartes que vous avez **tous les deux**.
- Une carte que vous **ne jouez pas** ne cause jamais de désync — même si un seul l'a. Seule la **carte jouée** compte (son `.map` + `map.ini` doivent être identiques).
- **Les maps des mods sont embarquées dans le mod** → identiques si même version de mod. Rien à partager à la main.
- « Il me manque une carte » = simple **disponibilité** (le jeu peut la transférer), **pas** un désync.

### ⬇️ Téléchargement

**Téléchargement direct (un seul fichier, pas un zip) :**
**https://github.com/AIPCMEDIA/GenSpeed/releases/latest**

> Télécharge `GenSpeed.exe` et double-clique — aucune installation, rien à décompresser. Ne télécharge **pas** le « Source code (zip) » de GitHub : c'est le code source, pas l'application.

### ⚠️ Avertissement

Non affilié à EA. Fourni « TEL QUEL », sans garantie, à tes propres risques. Conçu pour le **solo et le LAN entre amis** — **pas** pour la triche en ligne. *C&C™, Generals™, Zero Hour™ © Electronic Arts Inc. Code **écrit par IA** ; conçu et dirigé par un passionné (rôle d'**architecte**). Sous licence MIT.*

---

**Enjoy faster gameplay! 🚀⚡**
