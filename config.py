#!/usr/bin/env python3
# =============================================================================
# config.py - Gestion de la configuration (state + camera)
# =============================================================================
import os
import json
import re


# =============================================================================
# EMPLACEMENT DE LA CONFIG (dossier utilisateur, toujours inscriptible)
# =============================================================================
def default_config_dir():
    """Dossier de config utilisateur : %LOCALAPPDATA%\\GenSpeed (fallback ~)."""
    base = os.environ.get('LOCALAPPDATA') or os.path.expanduser('~')
    return os.path.join(base, 'GenSpeed')


def default_config_file():
    return os.path.join(default_config_dir(), 'genspeed-config.json')


# =============================================================================
# DÉTECTION AUTOMATIQUE DE L'INSTALLATION (Steam)
# =============================================================================
_ZH_FOLDER_NAMES = [
    'Command & Conquer Generals - Zero Hour',
    'Command and Conquer Generals Zero Hour',
    'CnC Generals Zero Hour',
]


def _steam_path():
    """Chemin d'installation de Steam via le registre Windows (ou None)."""
    try:
        import winreg
    except ImportError:
        return None
    candidates = [
        (winreg.HKEY_CURRENT_USER, r'Software\Valve\Steam', 'SteamPath'),
        (winreg.HKEY_LOCAL_MACHINE, r'SOFTWARE\WOW6432Node\Valve\Steam', 'InstallPath'),
        (winreg.HKEY_LOCAL_MACHINE, r'SOFTWARE\Valve\Steam', 'InstallPath'),
    ]
    for hive, key, value_name in candidates:
        try:
            with winreg.OpenKey(hive, key) as k:
                val, _ = winreg.QueryValueEx(k, value_name)
                if val:
                    return val.replace('/', '\\')
        except OSError:
            continue
    return None


def _steam_libraries():
    """Liste des dossiers de bibliothèque Steam (où sont installés les jeux)."""
    libs = []
    sp = _steam_path()
    if sp and os.path.isdir(sp):
        libs.append(sp)
        vdf = os.path.join(sp, 'steamapps', 'libraryfolders.vdf')
        if os.path.isfile(vdf):
            try:
                with open(vdf, encoding='utf-8', errors='ignore') as f:
                    txt = f.read()
                for m in re.finditer(r'"path"\s*"([^"]+)"', txt):
                    libs.append(m.group(1).replace('\\\\', '\\'))
            except OSError:
                pass
    return libs


def _looks_like_zh(path):
    """Vrai si le dossier ressemble à une install ZH valide."""
    if not os.path.isdir(path):
        return False
    markers = [
        os.path.join(path, 'GLM'),
        os.path.join(path, 'Data', 'INI'),
        os.path.join(path, 'generals.exe'),
        os.path.join(path, 'GeneralsZH.exe'),
    ]
    return any(os.path.exists(m) for m in markers)


def detect_zh_install():
    """Tente de localiser automatiquement l'installation de Zero Hour.

    Retourne un chemin valide ou None.
    """
    seen = set()
    search_roots = list(_steam_libraries())
    # Emplacements par défaut courants en complément
    search_roots += [
        r'C:\Program Files (x86)\Steam',
        r'C:\Program Files\Steam',
    ]

    for root in search_roots:
        common = os.path.join(root, 'steamapps', 'common')
        for name in _ZH_FOLDER_NAMES:
            p = os.path.join(common, name)
            if p in seen:
                continue
            seen.add(p)
            if _looks_like_zh(p):
                return p
    return None


# =============================================================================
# VARIABLES CAMÉRA
# =============================================================================
CAM_VARS = ['CameraPitch', 'CameraYaw', 'CameraHeight', 'MaxCameraHeight', 'MinCameraHeight', 'DrawEntireTerrain']

DEFAULT_CAM = {
    'CameraPitch': '',
    'CameraYaw': '',
    'CameraHeight': '',
    'MaxCameraHeight': '',
    'MinCameraHeight': '',
    'DrawEntireTerrain': ''
}

DEFAULT_CAM_PRESETS = {
    "Cam haute": {
        "CameraPitch": "31",
        "CameraYaw": "",
        "CameraHeight": "600",
        "MaxCameraHeight": "800",
        "MinCameraHeight": "120",
        "DrawEntireTerrain": "Yes"
    },
    "Cam max": {
        "CameraPitch": "30",
        "CameraYaw": "",
        "CameraHeight": "800",
        "MaxCameraHeight": "1200",
        "MinCameraHeight": "120",
        "DrawEntireTerrain": "Yes"
    },
    "Cam eloignee": {
        "CameraPitch": "29",
        "CameraYaw": "",
        "CameraHeight": "1000",
        "MaxCameraHeight": "1500",
        "MinCameraHeight": "120",
        "DrawEntireTerrain": "Yes"
    },
    "Vue satellite": {
        "CameraPitch": "28",
        "CameraYaw": "",
        "CameraHeight": "1200",
        "MaxCameraHeight": "2000",
        "MinCameraHeight": "120",
        "DrawEntireTerrain": "Yes"
    },
    "Reset camera": {}
}

# Ordre des presets de caméra (pour affichage cohérent dans les modes simple et avancé)
DEFAULT_CAM_PRESET_ORDER = ["Cam haute", "Cam max", "Cam eloignee", "Vue satellite"]


# =============================================================================
# CONFIGURATION UNIFIÉE
# =============================================================================
class ConfigManager:
    """Gère toute la configuration de l'application dans un seul fichier JSON"""
    
    def __init__(self, game_dir=None, config_path=None):
        # game_dir n'est plus utilisé pour localiser la config : il reste
        # disponible comme info (lan_hash, etc.) mais la config vit dans
        # un dossier utilisateur toujours inscriptible (pas de droits admin).
        self.game_dir = game_dir
        self.config_file = config_path or default_config_file()
        try:
            os.makedirs(os.path.dirname(self.config_file), exist_ok=True)
        except OSError:
            pass

    def _load_all(self):
        """Charge toute la configuration depuis le fichier JSON."""
        default_config = {
            'game_dir': None,
            'state': {},
            'slider_configs': {},
            'preset': {},
            'camera_presets': DEFAULT_CAM_PRESETS.copy()
        }
        
        try:
            if os.path.exists(self.config_file):
                with open(self.config_file, 'r', encoding='utf-8') as f:
                    loaded = json.load(f)
                    default_config.update(loaded)
        except FileNotFoundError:
            pass
        except json.JSONDecodeError as e:
            print(f"Erreur JSON: {e}")
        except IOError as e:
            print(f"Erreur lecture config: {e}")
        
        return default_config
    
    def _save_all(self, config):
        """Sauvegarde toute la configuration dans le fichier JSON (atomique)."""
        try:
            # Écriture atomique : fichier temporaire + rename
            tmp_file = self.config_file + '.tmp'
            with open(tmp_file, 'w', encoding='utf-8') as f:
                json.dump(config, f, indent=2, ensure_ascii=False)
            os.replace(tmp_file, self.config_file)
        except IOError as e:
            print(f"Erreur sauvegarde config: {e}")
    
    # Mode d'interface (simple / advanced)
    def load_ui_mode(self):
        return self._load_all().get('ui_mode', 'simple')

    def save_ui_mode(self, mode):
        config = self._load_all()
        config['ui_mode'] = mode
        self._save_all(config)

    # Chemin du jeu mémorisé
    def load_game_dir(self):
        """Retourne le dossier du jeu mémorisé (ou None)."""
        return self._load_all().get('game_dir')

    def save_game_dir(self, path):
        """Mémorise le dossier du jeu."""
        config = self._load_all()
        config['game_dir'] = path
        self._save_all(config)
        self.game_dir = path

    # State
    def load_state(self):
        """Charge l'état depuis la configuration."""
        config = self._load_all()
        return config.get('state', {})
    
    def save_state(self, state):
        """Sauvegarde l'état dans la configuration."""
        config = self._load_all()
        config['state'] = state
        self._save_all(config)
    
    # Slider configs
    def load_slider_configs(self):
        """Charge les configurations du slider."""
        config = self._load_all()
        return config.get('slider_configs', {})
    
    def save_slider_config(self, position, config_dict):
        """Sauvegarde une configuration de slider."""
        config = self._load_all()
        if 'slider_configs' not in config:
            config['slider_configs'] = {}
        config['slider_configs'][str(position)] = config_dict
        self._save_all(config)
    
    # Preset
    def load_preset(self):
        """Charge un preset."""
        config = self._load_all()
        return config.get('preset', {})
    
    def save_preset(self, preset):
        """Sauvegarde un preset."""
        config = self._load_all()
        config['preset'] = preset
        self._save_all(config)
    
    # Camera presets
    def load_camera_presets(self):
        """Charge les presets caméra."""
        config = self._load_all()
        return config.get('camera_presets', DEFAULT_CAM_PRESETS.copy())
    
    def save_camera_presets(self, presets):
        """Sauvegarde les presets caméra."""
        config = self._load_all()
        config['camera_presets'] = presets
        self._save_all(config)
    
    # Target status
    def get_target_status(self, target_label, state=None):
        """Retourne le statut d'une cible."""
        if state is None:
            state = self.load_state()
        
        info = state.get(target_label)
        if info:
            factors = info.get('factors', {})
            vals = set(factors.values())
            is_neutral = (len(vals) == 1 and list(vals)[0] == 1.0)
            
            if is_neutral:
                speed = "x1"
            elif len(vals) == 1:
                speed = f"x{list(vals)[0]}"
            else:
                speed = "perso"
            
            status = "Original 📦" if is_neutral else f"Patché ✅ {speed}"
            
            cam = info.get('cam', {}) or {}
            if cam:
                abbr = {
                    'CameraPitch': 'P', 'CameraYaw': 'Y', 'CameraHeight': 'H',
                    'MaxCameraHeight': 'Max', 'MinCameraHeight': 'Min',
                    'DrawEntireTerrain': 'DET'
                }
                cam_parts = []
                for k, v in cam.items():
                    if k == 'DrawEntireTerrain':
                        disp = v
                    else:
                        # 600.0 -> 600, 37.5 -> 37.5
                        try:
                            disp = '%g' % float(v)
                        except (TypeError, ValueError):
                            disp = v
                    cam_parts.append(f"{abbr.get(k, k)}{disp}")
                status += f" | Cam {' '.join(cam_parts)}"
            
            return status
        
        return "Original 📦"


# =============================================================================
# CAMERA CONFIG
# =============================================================================
class CameraConfig:
    """Gère la configuration de la caméra"""
    
    def __init__(self, game_dir=None):
        self.game_dir = game_dir
        self.config = DEFAULT_CAM.copy()
        self.presets = DEFAULT_CAM_PRESETS.copy()
        
        if game_dir:
            config_mgr = ConfigManager(game_dir)
            self.presets = config_mgr.load_camera_presets()
    
    def apply_preset(self, preset_name):
        """Applique un preset de caméra."""
        if preset_name in self.presets:
            preset = self.presets[preset_name]
            for var in CAM_VARS:
                if var in preset:
                    self.config[var] = preset[var]
                else:
                    self.config[var] = ''
    
    def set_value(self, var, value):
        """Définit une valeur de caméra."""
        if var in CAM_VARS:
            self.config[var] = value
    
    def get_value(self, var):
        """Retourne une valeur de caméra."""
        return self.config.get(var, '')
    
    def get_config(self):
        """Retourne la configuration complète (valeurs non vides)."""
        return {k: v for k, v in self.config.items() if v and v != ''}
    
    def reset(self):
        """Réinitialise aux valeurs par défaut (vides)."""
        self.config = DEFAULT_CAM.copy()
    
    def validate(self):
        """Valide la configuration de la caméra."""
        for var, val in self.config.items():
            if not val or val == '':
                continue
            
            if var == 'DrawEntireTerrain':
                if val.upper() not in ['YES', 'NO']:
                    return False, f"DrawEntireTerrain doit être 'Yes' ou 'No' (reçu: {val})"
            else:
                try:
                    f = float(val)
                    if f != f:
                        return False, f"{var} ne peut pas être NaN"
                    if f == float('inf') or f == float('-inf'):
                        return False, f"{var} ne peut pas être infini"
                    
                    # Validation stricte des valeurs caméra
                    if var == 'CameraPitch':
                        if f < 0 or f > 90:
                            return False, f"CameraPitch doit être entre 0 et 90° (reçu: {f})"
                    elif var == 'CameraYaw':
                        if f < 0 or f >= 360:
                            return False, f"CameraYaw doit être entre 0 et 360° (reçu: {f})"
                    elif var == 'CameraHeight':
                        if f < 0 or f > 5000:
                            return False, f"CameraHeight doit être entre 0 et 5000 (reçu: {f})"
                    elif var == 'MaxCameraHeight':
                        if f < 0 or f > 5000:
                            return False, f"MaxCameraHeight doit être entre 0 et 5000 (reçu: {f})"
                    elif var == 'MinCameraHeight':
                        if f < 0 or f > 5000:
                            return False, f"MinCameraHeight doit être entre 0 et 5000 (reçu: {f})"
                    
                    # Vérifier la cohérence Min/Max
                    if var == 'MaxCameraHeight' and self.config.get('MinCameraHeight'):
                        min_h = float(self.config['MinCameraHeight'])
                        if f <= min_h:
                            return False, f"MaxCameraHeight doit être > MinCameraHeight (Max: {f}, Min: {min_h})"
                    elif var == 'MinCameraHeight' and self.config.get('MaxCameraHeight'):
                        max_h = float(self.config['MaxCameraHeight'])
                        if f >= max_h:
                            return False, f"MinCameraHeight doit être < MaxCameraHeight (Min: {f}, Max: {max_h})"
                    
                except ValueError:
                    return False, f"{var} doit être un nombre (reçu: {val})"
        
        return True, None
    
    def get_validated_config(self):
        """Valide la config caméra et retourne un dict typé prêt pour le patch.

        Point unique de validation (évite la duplication GUI/patch).
        - DrawEntireTerrain -> 'YES'/'NO' (str)
        - autres variables   -> float
        Les champs vides sont ignorés.

        Raises:
            ValueError: si une valeur est invalide (message explicite).
        """
        ok, err = self.validate()
        if not ok:
            raise ValueError(err)

        out = {}
        for var in CAM_VARS:
            val = self.config.get(var, '')
            if not val or val == '':
                continue
            if var == 'DrawEntireTerrain':
                out[var] = val.upper()
            else:
                out[var] = float(val)
        return out

    def to_dict(self):
        """Retourne la configuration complète."""
        return self.config.copy()
    
    def from_dict(self, config_dict):
        """Charge une configuration depuis un dict."""
        for var in CAM_VARS:
            self.config[var] = config_dict.get(var, '')
