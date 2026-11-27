# Apply plan.json to Resources/Enemies/*.asset (text edits only). Logs before/after to apply_log.txt.
import json, os, re
OUT = os.path.dirname(os.path.abspath(__file__))
plan = json.load(open(OUT + "/plan.json"))
log = []
def fmt(v): return ("%.4f" % v).rstrip("0").rstrip(".") if abs(v) > 1e-9 else "0"
for eid, p in plan.items():
    path = p["asset"]
    t = open(path, encoding="utf-8", newline="").read(); orig = t
    nl = "\r\n" if "\r\n" in t else "\n"
    def sub(key, new, append=False):
        global t
        m = re.search(r"(\n  " + key + r": )([^\r\n]*)", t)
        if m:
            log.append(f"{eid}: {key}: {m.group(2)}  ->  {new}")
            t = t[:m.start(2)] + new + t[m.end(2):]
        else:
            assert append, (eid, key)
            log.append(f"{eid}: {key}: (missing, appended)  ->  {new}")
            t = t.rstrip("\r\n") + nl + f"  {key}: {new}" + nl
    sub("visualScaleMultiplier", fmt(p["newScale"]))
    sub("bodyColliderScale", "{x: %s, y: %s}" % (fmt(p["newColScale"][0]), fmt(p["newColScale"][1])), append=True)
    sub("bodyColliderOffset", "{x: %s, y: %s}" % (fmt(p["newColOffset"][0]), fmt(p["newColOffset"][1])), append=True)
    if eid == "flying_wyvern":
        m = re.search(r"\n  runFrames:\r?\n  - (\{[^\r\n]*\})", t)
        sub("sprite", m.group(1))
    assert t != orig
    open(path, "w", encoding="utf-8", newline="").write(t)
open(OUT + "/apply_log.txt", "w", encoding="utf-8").write("\n".join(log) + "\n")
print("\n".join(log))
