import re, glob
BS = chr(92)  # backslash
loc = open('src/GenSpeed.App/Localization.cs', encoding='utf-8').read()

def parse_entries(s):
    out = {}
    for m in re.finditer(r'\["([^"]+)"\]\s*=\s*\[', s):
        key = m.group(1); k = m.end()
        strings = 0; depth = 1
        while k < len(s) and depth > 0:
            c = s[k]
            if c == '"':
                strings += 1; k += 1
                while k < len(s):
                    if s[k] == BS: k += 2; continue
                    if s[k] == '"': k += 1; break
                    k += 1
                continue
            if c == '[': depth += 1
            elif c == ']': depth -= 1
            k += 1
        out[key] = strings
    return out

defs = parse_entries(loc)
print(f"Entrees definies : {len(defs)}")
bad = {k: n for k, n in defs.items() if n != 2}
if bad:
    print("PROBLEME - entrees pas a 2 langues :")
    for k, n in bad.items(): print(f"   {k} -> {n} chaine(s)")
else:
    print("OK - toutes les entrees ont exactement FR + EN")

refs = set()
files = (glob.glob('src/GenSpeed.App/**/*.cs', recursive=True)
         + glob.glob('src/GenSpeed.Core/**/*.cs', recursive=True)
         + glob.glob('src/GenSpeed.App/**/*.xaml', recursive=True))
pat = re.compile(r'"((?:clean|tip|fx|help|cam|campreset|col|card|tb|crud|dlg|msg|log|speed|confirm|apply|subtitle|inst|pick|cfg)\.[A-Za-z0-9_.]+)"')
for f in files:
    if 'Localization.cs' in f: continue
    for m in pat.finditer(open(f, encoding='utf-8', errors='replace').read()):
        refs.add(m.group(1))

missing = sorted(r for r in refs if r not in defs)
print(f"\nCles litterales referencees : {len(refs)}")
if missing:
    print("PROBLEME - referencees mais NON definies :")
    for k in missing: print(f"   {k}")
else:
    print("OK - toutes les cles litterales referencees sont definies")
