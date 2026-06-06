#!/usr/bin/env python3
# =============================================================================
# core.py - Logique de bas niveau (BIG, patching, détection mods)
# =============================================================================
import struct
import os
import re
import shutil
import hashlib


# =============================================================================
# FICHIERS BIG
# =============================================================================
class BigFileError(Exception):
    """Exception personnalisée pour les erreurs de fichiers BIG"""
    pass


class BigFileCorruptedError(BigFileError):
    """Exception quand l'archive BIG est corrompue"""
    pass


def read_big(path):
    """Lit un fichier BIG et retourne (raw_data, files_list)."""
    if not os.path.exists(path):
        raise FileNotFoundError(f"Fichier BIG introuvable: {path}")
    
    try:
        with open(path, 'rb') as f:
            raw = f.read()
    except IOError as e:
        raise BigFileError(f"Erreur lecture fichier {path}: {e}")
    
    if len(raw) < 16:
        raise BigFileCorruptedError(f"Header BIG trop court: {path}")
    
    if raw[:4] != b'BIGF':
        raise BigFileCorruptedError(f"Signature BIG invalide: {path}")
    
    try:
        # NB : octets 4-7 = champ "taille" de l'en-tête BIG. Sa sémantique
        # (endianness, contenu exact) varie selon les variantes BIG et les
        # archives retraitées par GenLauncher : il ne correspond PAS toujours
        # à la taille réelle du fichier. Les lecteurs BIG éprouvés l'ignorent.
        # On ne s'en sert donc PAS pour valider. Les vraies protections sont
        # les bornes offset/size vérifiées par entrée (plus bas).
        num_files = struct.unpack('>I', raw[8:12])[0]
        header_size = struct.unpack('>I', raw[12:16])[0]
    except struct.error as e:
        raise BigFileCorruptedError(f"Erreur lecture header BIG: {e}")

    if num_files > 100000:
        raise BigFileCorruptedError(f"Nombre de fichiers suspect: {num_files}")

    if header_size > len(raw):
        raise BigFileCorruptedError(f"Taille header invalide: {header_size}")

    pos = 16
    files = []
    
    for i in range(num_files):
        if pos + 8 > len(raw):
            raise BigFileCorruptedError(f"Entrée {i}: header tronqué")
        
        try:
            offset = struct.unpack('>I', raw[pos:pos+4])[0]
            size = struct.unpack('>I', raw[pos+4:pos+8])[0]
        except struct.error as e:
            raise BigFileCorruptedError(f"Entrée {i}: erreur lecture offset/size: {e}")
        
        pos += 8
        
        try:
            end = raw.index(b'\x00', pos)
        except ValueError:
            raise BigFileCorruptedError(f"Entrée {i}: nom de fichier non terminé")
        
        if end - pos > 255:
            raise BigFileCorruptedError(f"Entrée {i}: nom de fichier trop long")
        
        name = raw[pos:end].decode('latin-1')
        pos = end + 1
        
        if offset > len(raw):
            raise BigFileCorruptedError(f"Fichier {name}: offset hors limite")
        
        if offset + size > len(raw):
            raise BigFileCorruptedError(f"Fichier {name}: taille hors limite")
        
        files.append({'name': name, 'off': offset, 'sz': size})
    
    return raw, files


def write_big(path, items):
    """Écrit un fichier BIG avec les items fournis."""
    header_size = 16
    for item in items:
        header_size += 8 + len(item['name'].encode('latin-1')) + 1
    
    current_offset = header_size
    for item in items:
        item['noff'] = current_offset
        current_offset += len(item['data'])
    
    try:
        with open(path, 'wb') as f:
            f.write(b'BIGF')
            f.write(struct.pack('>I', current_offset))
            f.write(struct.pack('>I', len(items)))
            f.write(struct.pack('>I', header_size))
            
            for item in items:
                f.write(struct.pack('>I', item['noff']))
                f.write(struct.pack('>I', len(item['data'])))
                f.write(item['name'].encode('latin-1') + b'\x00')
            
            for item in items:
                f.write(item['data'])
                
    except IOError as e:
        raise BigFileError(f"Erreur écriture fichier {path}: {e}")
    except struct.error as e:
        raise BigFileError(f"Erreur écriture structure BIG: {e}")


# =============================================================================
# PATCHING
# =============================================================================
class PatcherError(Exception):
    """Exception personnalisée pour les erreurs de patch"""
    pass


class InvalidFactorError(PatcherError):
    """Exception pour un facteur invalide"""
    pass


# NOTE: noms de variables validés par l'analyse comparative des 6 mods
# (Contra, Giant Robot Edition, NProject, Rise of the Reds, Shockwave Chaos, Zero Hour Reborn).
# Tous les mods partagent le même socle SAGE -> noms identiques partout.
MULT = {
    'deplacement': ['Speed','SpeedDamaged','MinSpeed','Acceleration','AccelerationDamaged',
                    'Braking','TurnRate','TurnRateDamaged','MinTurnSpeed'],
    'projectiles': ['InitialVelocity','WeaponSpeed'],
    'visee': ['TurretTurnRate','TurretPitchRate'],
    'economie_gain': ['ValuePerSupplyBox'],
    'detection': ['VisionRange','ShroudClearingRange'],
    'soin': ['HealingAmount'],
    'merite': ['ExperienceValue'],  # XP donnée en tuant l'unité -> promotions + rapides
}

DIV = {
    'construction': ['BuildTime'],
    'tir': ['DelayBetweenShots','ClipReloadTime'],
    'pouvoirs': ['ReloadTime'],
    'deploiement': ['UnpackTime','PreparationTime'],
    'economie_collecte': ['SupplyWarehouseActionDelay'],
    'soin': ['HealingDelay'],
}

CATS = [
    'deplacement', 'projectiles', 'visee', 'construction', 'tir', 'pouvoirs',
    'deploiement', 'economie_collecte', 'economie_gain', 'detection', 'soin', 'merite'
]

CAT_NAMES = {
    'deplacement': 'Déplacement',
    'projectiles': 'Projectiles',
    'visee': 'Visée (tourelles)',
    'construction': 'Construction',
    'tir': 'Tir',
    'pouvoirs': 'Pouvoirs (recharge)',
    'deploiement': 'Déploiement',
    'economie_collecte': 'Collecte',
    'economie_gain': 'Gain caisse',
    'detection': 'Vision',
    'soin': 'Soin',
    'merite': 'Mérite (XP)',
}

# Variables affichées dans "Aperçu original" — uniquement celles réellement
# présentes dans les mods (vérifiées par l'analyse comparative).
PREVIEW_VARS = {
    'Speed': '🏃 Vitesse déplacement',
    'TurnRate': '🔄 Vitesse de rotation',
    'InitialVelocity': '💥 Vitesse projectile',
    'WeaponSpeed': '💥 Vitesse arme',
    'TurretTurnRate': '🎯 Rotation tourelle',
    'BuildTime': '🏗️ Temps construction',
    'DelayBetweenShots': '🔫 Délai entre tirs',
    'ClipReloadTime': '🔫 Rechargement chargeur',
    'ReloadTime': '⚡ Recharge pouvoir',
    'UnpackTime': '📦 Temps déploiement',
    'SupplyWarehouseActionDelay': '💰 Délai collecte',
    'ValuePerSupplyBox': '📦 Valeur caisse',
    'VisionRange': '👁️ Portée de vision',
    'ShroudClearingRange': '👁️ Portée révélation',
    'HealingAmount': '➕ Soin (quantité)',
    'HealingDelay': '➕ Soin (fréquence)',
    'ExperienceValue': '⭐ Valeur XP (mérite)',
    'CameraHeight': '📷 Hauteur caméra',
    'MaxCameraHeight': '📷 Zoom max',
    'MinCameraHeight': '📷 Zoom min',
    'CameraPitch': '📷 Inclinaison',
    'DrawEntireTerrain': '📷 Terrain complet'
}


def validate_factor(value):
    """Valide un facteur utilisateur."""
    try:
        factor = float(value)
    except (ValueError, TypeError):
        raise InvalidFactorError(f"Facteur invalide: '{value}' n'est pas un nombre")
    
    if factor == 0:
        raise InvalidFactorError("Le facteur ne peut pas être 0 (division par zéro)")
    
    if factor < 0:
        raise InvalidFactorError(f"Le facteur ne peut pas être négatif: {factor}")
    
    if factor > 100:
        raise InvalidFactorError(f"Le facteur est trop élevé: {factor} (maximum: 100)")
    
    if factor < 0.01:
        raise InvalidFactorError(f"Le facteur est trop faible: {factor} (minimum: 0.01)")
    
    if factor != factor:
        raise InvalidFactorError("Le facteur ne peut pas être NaN")
    
    if factor == float('inf') or factor == float('-inf'):
        raise InvalidFactorError("Le facteur ne peut pas être infini")
    
    return factor


def validate_factors(factors):
    """Valide un dictionnaire de facteurs."""
    validated = {}
    for cat, val in factors.items():
        validated[cat] = validate_factor(val)
    return validated


def scale(txt, varnames, factor):
    """Applique un facteur multiplicatif aux variables spécifiées.

    Le nom de variable doit être un token COMPLET en début de ligne
    (précédé uniquement d'espaces). Cela évite qu'un nom court comme
    'TurnRate' matche la fin d'un nom plus long ('TurretTurnRate') ou
    'Speed' matche 'WeaponSpeed'. Les lignes en commentaire (;) sont
    ignorées car ';' n'est pas un espace.
    """
    count = [0]

    def repl(m):
        count[0] += 1
        v = float(m.group(2))
        nv = v * factor

        if '.' not in m.group(2):
            s = str(int(round(nv)))
        else:
            s = ("%.4f" % nv).rstrip('0').rstrip('.')

        return m.group(1) + s + m.group(3)

    for var in varnames:
        # ^\s* : seuls des espaces avant le nom -> token entier garanti
        pattern = r'^(\s*%s\s*=\s*)(-?\d+\.?\d*)(.*)$' % re.escape(var)
        txt = re.sub(pattern, repl, txt, flags=re.M|re.I)
    
    return txt, count[0]


def set_camera(txt, cam):
    """Applique les réglages de caméra."""
    for var, val in cam.items():
        if val is None or val == '':
            continue

        if var == 'DrawEntireTerrain':
            repl = str(val)
        else:
            # Accepte float OU chaîne numérique ('800' comme 800.0)
            repl = '%g' % float(val)
        txt = re.sub(r'^(\s*%s\s*=\s*)\S+' % re.escape(var),
                     lambda m, r=repl: m.group(1) + r, txt, flags=re.M|re.I)
    
    if 'MaxCameraHeight' in cam and cam['MaxCameraHeight']:
        txt = re.sub(r'^(\s*EnforceMaxCameraHeight\s*=\s*)\S+', r'\g<1>No', txt, flags=re.M|re.I)
    
    return txt


def apply_text(txt, factors, cam=None):
    """Applique les facteurs et les réglages de caméra au texte."""
    report = {}
    
    for cat, vars_list in MULT.items():
        fac = factors.get(cat, 1.0)
        if fac != 1.0:
            txt, c = scale(txt, vars_list, fac)
            report[cat] = report.get(cat, 0) + c
    
    for cat, vars_list in DIV.items():
        fac = factors.get(cat, 1.0)
        if fac != 1.0:
            txt, c = scale(txt, vars_list, 1.0 / fac)
            report[cat] = report.get(cat, 0) + c
    
    if cam:
        txt = set_camera(txt, cam)
    
    return txt, report


# =============================================================================
# DÉTECTION DES MODS
# =============================================================================
class ModDetector:
    """Détecte les mods et fichiers INI dans le dossier du jeu"""
    
    def __init__(self, game_dir):
        self.game_dir = game_dir
        self.glm_dir = os.path.join(game_dir, "GLM")
        self.ini_dir = os.path.join(game_dir, "Data", "INI")
    
    def _gib_names(self, path):
        """Noms des fichiers d'un .gib (source unique : _archive_names, latin-1).

        Équivalent vérifié à l'ancienne version ascii/replace sur l'ensemble des
        archives réelles ; latin-1 aligne la lecture rapide sur read_big.
        """
        return _archive_names(path)

    def _gib_has_ini(self, path):
        """Vérifie si un .gib contient des .ini"""
        return any(n.lower().endswith('.ini') for n in self._gib_names(path))
    
    def _gibs_in(self, directory):
        """Liste les fichiers .gib dans un dossier"""
        try:
            return [os.path.join(directory, f) for f in os.listdir(directory) 
                    if f.lower().endswith('.gib')]
        except Exception:
            return []
    
    def collect_stat_archives(self, mod_path):
        """Toutes les archives de STATS d'un mod (base + patches), hors Addons.

        Pourquoi tout plutôt qu'un seul dossier 'actif' :
        certains mods sont en COUCHES (dossier de base + patch communautaire
        qui ÉCRASE la base en jeu via l'ordre de chargement, ex. RotR +
        Hanpatch). Deviner lequel 'gagne' à partir de la date de modif est
        fragile. En patchant toutes les archives de stats (hors Addons
        cosmétiques), le facteur ×N s'applique quel que soit le fichier qui
        l'emporte au chargement -> effet garanti, robuste à toute réinstall.
        """
        out = []
        for root, _, _files in os.walk(mod_path):
            rel = os.path.relpath(root, mod_path)
            if any(part.lower() == 'addons' for part in rel.split(os.sep)):
                continue
            for g in self._gibs_in(root):
                if self._gib_has_ini(g):
                    out.append(g)
        return sorted(out)
    
    def detect_targets(self):
        """Détecte toutes les cibles disponibles (Vanilla + mods)."""
        targets = []
        
        if os.path.isdir(self.ini_dir):
            try:
                inis = [os.path.join(self.ini_dir, f) 
                        for f in os.listdir(self.ini_dir) 
                        if f.lower().endswith('.ini')]
                if inis:
                    targets.append({
                        'label': 'VANILLA (Data/INI)',
                        'type': 'ini',
                        'files': inis
                    })
            except Exception:
                pass
        
        if os.path.isdir(self.glm_dir):
            try:
                for mod in sorted(os.listdir(self.glm_dir)):
                    mod_path = os.path.join(self.glm_dir, mod)
                    if not os.path.isdir(mod_path):
                        continue
                    if mod in ['Addons', 'Patches', 'Tools']:
                        continue
                    
                    arch = self.collect_stat_archives(mod_path)

                    if arch:
                        targets.append({
                            'label': mod,
                            'type': 'gib',
                            'files': arch
                        })
            except Exception:
                pass
        
        return targets


def detect_targets(game_dir):
    """Fonction de commodité pour détecter les cibles."""
    detector = ModDetector(game_dir)
    return detector.detect_targets()


def _ensure_pristine_backup(fp, prev_hash):
    """Garantit que .speedbak = version 'pristine' (avant patch) ET que `fp`
    contient cette version pristine, prêt à être patché.

    Gère le cas où le fichier a été remplacé hors GenSpeed depuis le dernier
    patch (MAJ mod/addon) : on ne restaure alors PAS l'ancien backup (qui
    écraserait la nouvelle version), on rafraîchit le backup à la place.
    """
    bak = fp + ".speedbak"
    if not os.path.exists(bak):
        # Premier patch : le fichier actuel EST le pristine
        shutil.copy2(fp, bak)
        return
    cur = file_sha256(fp)
    if prev_hash is None or cur == prev_hash:
        # Backup de confiance (fp = notre ancien patch, ou inconnu) :
        # on restaure le pristine pour repatcher proprement.
        shutil.copy2(bak, fp)
    else:
        # fp a changé hors GenSpeed -> nouvelle version = nouveau pristine.
        shutil.copy2(fp, bak)


def patch_target(target, factors, cam, prev_hashes=None, log=None):
    """Patche une cible (LOGIQUE CANONIQUE, source unique de vérité).

    - crée/rafraîchit les .speedbak (pristine), gère les MAJ externes via
      `prev_hashes` ({fp: sha256} du dernier patch) ;
    - ne réécrit un fichier/une archive QUE si son contenu change réellement
      (évite de toucher l'audio, l'IA, ou tout fichier sans variable utile) ;
    - calcule l'empreinte des fichiers réellement patchés.

    Returns:
        (patched_files, skipped) où patched_files = {fp: sha256}.
    """
    prev_hashes = prev_hashes or {}
    patched_files = {}
    skipped = 0

    for fp in target['files']:
        _ensure_pristine_backup(fp, prev_hashes.get(fp))

        if target['type'] == 'ini':
            with open(fp, 'r', encoding='latin-1') as fh:
                original = fh.read()
            new_txt, _ = apply_text(original, factors, cam)
            changed = (new_txt != original)
            if changed:
                with open(fp, 'w', encoding='latin-1') as fh:
                    fh.write(new_txt)
        else:
            raw, files = read_big(fp)
            items = []
            changed = False
            for fo in files:
                data = raw[fo['off']:fo['off'] + fo['sz']]
                if fo['name'].lower().endswith('.ini'):
                    original = data.decode('latin-1')
                    new_txt, _ = apply_text(original, factors, cam)
                    if new_txt != original:
                        changed = True
                    data = new_txt.encode('latin-1')
                items.append({'name': fo['name'], 'data': data})
            if changed:
                write_big(fp, items)

        if changed:
            patched_files[fp] = file_sha256(fp)
        else:
            # Rien à modifier -> pas de backup inutile
            bak = fp + ".speedbak"
            if os.path.exists(bak):
                os.remove(bak)
            skipped += 1
            if log:
                log(f"    (ignoré, aucune variable : {os.path.basename(fp)})")

    return patched_files, skipped


def file_sha256(path):
    """Empreinte SHA-256 d'un fichier (ou None s'il est illisible)."""
    try:
        h = hashlib.sha256()
        with open(path, 'rb') as f:
            for ch in iter(lambda: f.read(65536), b''):
                h.update(ch)
        return h.hexdigest()
    except OSError:
        return None


def list_patched_files(game_dir):
    """Liste les fichiers actuellement patchés (= ceux qui ont un .speedbak)."""
    files = []
    for base in [os.path.join(game_dir, "GLM"), os.path.join(game_dir, "Data", "INI")]:
        if not os.path.isdir(base):
            continue
        for root, _, fs in os.walk(base):
            for f in fs:
                if f.endswith(".speedbak"):
                    orig = os.path.join(root, f[:-9])
                    if os.path.exists(orig):
                        files.append(orig)
    return sorted(files)


def lan_hash(game_dir):
    """Calcule le hash LAN des fichiers patchés."""
    files = list_patched_files(game_dir)

    if not files:
        return None, 0
    h = hashlib.sha256()
    for fp in files:
        h.update(os.path.relpath(fp, game_dir).replace("\\", "/").encode())
        with open(fp, 'rb') as f:
            for ch in iter(lambda: f.read(65536), b''):
                h.update(ch)
    
    return h.hexdigest()[:8].upper(), len(files)


def _archive_names(path):
    """Noms des fichiers d'une archive BIGF (.big/.gib), lecture de la table seule."""
    try:
        with open(path, 'rb') as f:
            if f.read(4) != b'BIGF':
                return []
            f.read(4)
            num = struct.unpack('>I', f.read(4))[0]
            f.read(4)
            names = []
            for _ in range(num):
                f.read(8)
                nm = b''
                b = f.read(1)
                while b not in (b'\x00', b''):
                    nm += b
                    b = f.read(1)
                names.append(nm.decode('latin-1'))
            return names
    except Exception:
        return []


def archive_has_ini(path):
    return any(n.lower().endswith('.ini') for n in _archive_names(path))


def base_install_files(game_dir):
    """Fichiers de l'installation de BASE pertinents pour la synchro LAN :
    les archives racine (.big/.gib contenant des .ini, ex. INIZH.big) + l'exe.

    Ce sont les fichiers du jeu vanilla qui se chargent SOUS le mod et
    définissent la version de base — souvent en cause dans les désyncs.
    """
    out = []
    try:
        for f in os.listdir(game_dir):
            fp = os.path.join(game_dir, f)
            if not os.path.isfile(fp):
                continue
            low = f.lower()
            if low.endswith(('.big', '.gib')) and archive_has_ini(fp):
                out.append(fp)
            elif low in ('generals.exe', 'generalszh.exe'):
                out.append(fp)
    except OSError:
        pass
    return sorted(out)


def compat_report(game_dir, targets):
    """Rapport de compatibilité DÉTAILLÉ (empreinte par fichier) pour comparer
    deux installations entre joueurs et localiser précisément ce qui diffère.

    Structure : {'base': {relpath: [sha12, size]}, 'mods': {label: {relpath: [sha12, size]}}}
    """
    def entries(files):
        out = {}
        for fp in files:
            rel = os.path.relpath(fp, game_dir).replace("\\", "/")
            h = file_sha256(fp)
            out[rel] = [h[:12] if h else None, os.path.getsize(fp) if os.path.exists(fp) else 0]
        return out

    report = {'base': entries(base_install_files(game_dir)), 'mods': {}}
    for t in targets:
        report['mods'][t['label']] = entries(t['files'])
    return report


def diff_compat_reports(mine, other):
    """Compare deux rapports compat. Retourne une liste de différences :
    (section, fichier, statut) où statut ∈ {différent, absent_chez_moi, absent_chez_lui}.
    """
    diffs = []

    def cmp_section(name, a, b):
        a = a or {}
        b = b or {}
        for rel in sorted(set(a) | set(b)):
            if rel not in a:
                diffs.append((name, rel, "absent chez moi"))
            elif rel not in b:
                diffs.append((name, rel, "absent chez l'autre"))
            elif a[rel] != b[rel]:
                diffs.append((name, rel, "DIFFÉRENT"))

    cmp_section("Base Steam", mine.get('base'), other.get('base'))
    mmods, omods = mine.get('mods', {}), other.get('mods', {})
    for label in sorted(set(mmods) | set(omods)):
        if label not in mmods:
            diffs.append(("Mod " + label, "(tout le mod)", "absent chez moi"))
        elif label not in omods:
            diffs.append(("Mod " + label, "(tout le mod)", "absent chez l'autre"))
        else:
            cmp_section("Mod " + label, mmods[label], omods[label])
    return diffs


def find_latest_replay():
    """Chemin du replay ZH le plus récent (dossier de données utilisateur), ou None."""
    import glob
    up = os.environ.get('USERPROFILE') or os.path.expanduser('~')
    bases = [
        os.path.join(up, 'Documents', 'Command and Conquer Generals Zero Hour Data', 'Replays'),
        os.path.join(up, 'OneDrive', 'Documents', 'Command and Conquer Generals Zero Hour Data', 'Replays'),
    ]
    reps = []
    for b in bases:
        if os.path.isdir(b):
            reps += glob.glob(os.path.join(b, '*.rep'))
    return max(reps, key=os.path.getmtime) if reps else None


def read_replay_fingerprint(path):
    """Lit l'en-tête d'un replay ZH (.rep) et en extrait l'empreinte de partie.

    Returns: dict {version, map, map_crc, players, mtime} ou None.
    """
    try:
        data = open(path, 'rb').read(8192)
    except OSError:
        return None
    if data[:6] != b'GENREP':
        return None
    u = data.decode('utf-16-le', 'ignore')
    uruns = re.findall(r'[\x20-\x7e]{3,}', u)
    version = next((r.strip() for r in uruns if re.search(r'V\d', r)), '')
    a = data.decode('latin-1', 'ignore')
    aruns = re.findall(r'[\x20-\x7e]{5,}', a)
    info = next((r for r in aruns if 'M=' in r and 'MC=' in r), '')
    out = {'version': version, 'map': '', 'map_crc': '', 'players': [], 'mtime': os.path.getmtime(path)}
    for part in info.split(';'):
        if part.startswith('M='):
            out['map'] = part[2:]
        elif part.startswith('MC='):
            out['map_crc'] = part[3:]
        elif part.startswith('S='):
            for pl in part[2:].split(':'):
                if pl and pl != 'X':
                    out['players'].append(pl.split(',')[0])
    return out


def install_hash(game_dir, files):
    """Empreinte de l'état ACTUEL d'un ensemble de fichiers (patchés ou non).

    Sert au contrôle de compatibilité LAN : deux joueurs doivent obtenir le
    même hash d'installation sur le même mod pour être sûrs d'avoir des
    données identiques (au-delà du seul patch GenSpeed).

    Returns: (hash8, nb_fichiers, taille_totale_octets)
    """
    files = sorted(files)
    h = hashlib.sha256()
    total = 0
    for fp in files:
        h.update(os.path.relpath(fp, game_dir).replace("\\", "/").encode())
        try:
            with open(fp, 'rb') as f:
                for ch in iter(lambda: f.read(1 << 20), b''):
                    h.update(ch)
                    total += len(ch)
        except OSError:
            pass
    return h.hexdigest()[:8].upper(), len(files), total


def _run_elevated(mode, params):
    """Lance une instance élevée (UAC) en mode `--<mode>` avec un fichier de
    paramètres JSON unique. Retourne un handle de processus (int) à attendre,
    ou None si l'UAC est annulé / l'élévation échoue.

    Partagé par le patch et le dépatch.
    """
    import ctypes
    from ctypes import wintypes
    import sys
    import json
    import tempfile

    fd, params_file = tempfile.mkstemp(prefix="genspeed_%s_" % mode, suffix=".json")
    os.close(fd)
    with open(params_file, 'w', encoding='utf-8') as f:
        json.dump(params, f)

    args = '"%s" --%s "%s"' % (os.path.abspath(sys.argv[0]), mode, params_file)

    class SHELLEXECUTEINFOW(ctypes.Structure):
        _fields_ = [
            ("cbSize", wintypes.DWORD),
            ("fMask", ctypes.c_ulong),
            ("hwnd", wintypes.HWND),
            ("lpVerb", wintypes.LPCWSTR),
            ("lpFile", wintypes.LPCWSTR),
            ("lpParameters", wintypes.LPCWSTR),
            ("lpDirectory", wintypes.LPCWSTR),
            ("nShow", ctypes.c_int),
            ("hInstApp", wintypes.HINSTANCE),
            ("lpIDList", ctypes.c_void_p),
            ("lpClass", wintypes.LPCWSTR),
            ("hkeyClass", wintypes.HKEY),
            ("dwHotKey", wintypes.DWORD),
            ("hIconOrMonitor", wintypes.HANDLE),
            ("hProcess", wintypes.HANDLE),
        ]

    SEE_MASK_NOCLOSEPROCESS = 0x00000040
    SW_HIDE = 0

    sei = SHELLEXECUTEINFOW()
    sei.cbSize = ctypes.sizeof(sei)
    sei.fMask = SEE_MASK_NOCLOSEPROCESS
    sei.lpVerb = "runas"
    sei.lpFile = sys.executable
    sei.lpParameters = args
    sei.lpDirectory = os.getcwd()
    sei.nShow = SW_HIDE

    ok = ctypes.windll.shell32.ShellExecuteExW(ctypes.byref(sei))
    if not ok or not sei.hProcess:
        return None
    return int(sei.hProcess)


def classify_restore(target, expected=None):
    """Sans rien modifier sur le disque, classe les fichiers d'une cible.

    Retourne (to_restore, stale) :
      - to_restore : fichiers avec backup, restaurables normalement
      - stale      : fichiers dont le backup est périmé (fichier modifié hors
                     GenSpeed depuis le patch) — à ne pas écraser aveuglément.
    """
    to_restore = []
    stale = []
    for fp in target['files']:
        bak = fp + ".speedbak"
        if not os.path.exists(bak):
            continue
        if expected:
            exp = expected.get(fp)
            if exp and file_sha256(fp) != exp:
                stale.append(fp)
                continue
        to_restore.append(fp)
    return to_restore, stale


def elevate_and_restore(restore_files, delbak_files):
    """Élève (UAC) pour effectuer les opérations fichier du dépatch.

    Seules les opérations dans le dossier du jeu (protégé) sont faites ici ;
    la GUI gère détection et état (config utilisateur, sans admin).

    Args:
        restore_files: fichiers à restaurer (copier .speedbak -> fichier, puis
                       supprimer le .speedbak).
        delbak_files:  fichiers dont on supprime juste le .speedbak périmé
                       (on garde la version actuelle).
    """
    return _run_elevated('restore', {
        'restore': list(restore_files),
        'delbak': list(delbak_files),
    })


def elevate_and_patch(game_dir, factors, cam, targets, config_path=None):
    """
    Lance une instance avec élévation admin pour effectuer le patch.

    Les paramètres sont écrits dans un fichier temporaire au nom UNIQUE
    (évite toute collision entre instances/utilisateurs simultanés), dont
    le chemin est transmis au processus élevé en argument de ligne de
    commande après --patch.

    Args:
        game_dir: Dossier du jeu
        factors: Facteurs de vitesse
        cam: Configuration caméra
        targets: Liste des cibles à patcher

    Returns:
        Un handle de processus (int) du process admin lancé, pour pouvoir
        l'attendre et rafraîchir l'UI à la fin. None si l'utilisateur a
        annulé l'UAC ou si l'élévation a échoué.
    """
    return _run_elevated('patch', {
        'game_dir': game_dir,
        'factors': factors,
        'cam': cam,
        'targets': [t['label'] for t in targets],
        'config_path': config_path,  # pour écrire l'état dans la même config
    })
