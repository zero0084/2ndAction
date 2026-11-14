# 製品版(Android、DEVELOPMENT_BUILD/UNITY_EDITOR なし)で有効になるコードだけを残して、試験用の入口の候補を探す(2026-11-14)。
#   python active_scan.py [extra defines...]   例: python active_scan.py OMM_ADS_PROD
# #if/#elif/#else/#endif を定義の集合で評価する(式は ! && || () と記号だけ)。読むだけ
import io, os, re, sys
ROOT = "C:/GameProject/2ndAction/Unity/2ndAction/Assets/Scripts"
DEF = {"UNITY_ANDROID", "OMM_ADMOB", "OMM_UNITY_IAP", "UNITY_6000_0_OR_NEWER", "UNITY_2021_3_OR_NEWER", "UNITY_2022_3_OR_NEWER", "UNITY_2023_1_OR_NEWER", "ENABLE_IL2CPP", "UNITY_ANDROID_API", "PLATFORM_ANDROID"} | set(sys.argv[1:])
def ev(expr):
    e = expr.split("//")[0].strip()
    e = re.sub(r"\b([A-Za-z_][A-Za-z0-9_]*)\b", lambda m: "True" if m.group(1) in DEF or m.group(1) == "true" else ("False" if m.group(1) not in ("True", "False") else m.group(1)), e)
    e = e.replace("&&", " and ").replace("||", " or ").replace("!", " not ")
    return bool(eval(e))
def active_lines(path):
    out = []; stack = []  # (parent_active, this_active, taken)
    act = True
    for i, line in enumerate(io.open(path, encoding="utf-8-sig", errors="replace")):
        t = line.strip()
        if t.startswith("#if "):
            v = ev(t[4:]); stack.append((act, v, v)); act = act and v; continue
        if t.startswith("#elif "):
            pa, _, taken = stack[-1]; v = (not taken) and ev(t[6:]); stack[-1] = (pa, v, taken or v); act = pa and v; continue
        if t.startswith("#else"):
            pa, _, taken = stack[-1]; v = not taken; stack[-1] = (pa, v, True); act = pa and v; continue
        if t.startswith("#endif"):
            pa, _, _ = stack.pop(); act = pa; continue
        if act: out.append((i + 1, line.rstrip("\n")))
    return out
PATS = [("command line", re.compile(r"GetCommandLineArgs|QaArgs\.")), ("qa_args file", re.compile(r"qa_args")),
        ("dev arg string", re.compile(r'"-(qa|mem|file|ugs|update|title|net|dev|debug|mon|ump|bal|lc|ld|ad|ex|no)[A-Za-z]*"')),
        ("debug flag", re.compile(r"\bDev[A-Z][A-Za-z]*\s*=\s*true|DevUnlockAll\s*=\s*true|QaOverride\s*=\s*true"))]
hits = {}
for dp, _, fs in os.walk(ROOT):
    for f in fs:
        if not f.endswith(".cs"): continue
        p = os.path.join(dp, f).replace("\\", "/")
        for n, l in active_lines(p):
            for name, rx in PATS:
                if rx.search(l): hits.setdefault(p[len(ROOT) + 1:], []).append((n, name, l.strip()[:150]))
for p in sorted(hits):
    print(f"== {p}")
    for n, name, l in hits[p]: print(f"  {n:5} [{name}] {l}")
print("files:", len(hits))
