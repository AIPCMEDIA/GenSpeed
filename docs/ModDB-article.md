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

### ⬇️ Download

Grab `GenSpeed.exe` from the GitHub Releases page:
**https://github.com/AIPCMEDIA/GenSpeed/releases/latest**

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

### ⬇️ Téléchargement

`GenSpeed.exe` sur la page Releases GitHub :
**https://github.com/AIPCMEDIA/GenSpeed/releases/latest**

### ⚠️ Avertissement

Non affilié à EA. Fourni « TEL QUEL », sans garantie, à tes propres risques. Conçu pour le **solo et le LAN entre amis** — **pas** pour la triche en ligne. *C&C™, Generals™, Zero Hour™ © Electronic Arts Inc. Code **écrit par IA** ; conçu et dirigé par un passionné (rôle d'**architecte**). Sous licence MIT.*

---

**Enjoy faster gameplay! 🚀⚡**
