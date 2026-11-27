import json, os, csv
OUT = os.path.dirname(os.path.abspath(__file__))
M = {m["id"]: m for m in json.load(open(OUT + "/measure.json"))}
g = M["goblin"]["per"]["run"]
rows = list(csv.DictReader(open(OUT + "/enemy_size_audit.csv", encoding="utf-8")))
for r in rows:
    m = M[r["enemyId"]]; per = m["per"]; v = per.get("run", per["sprite"]); q = v / g
    r["whole_silhouette_ratio"] = round(q, 3)
    r["alt_scale_whole_silhouette"] = round(m["s"] / q, 2) if q < 0.97 else m["s"]
with open(OUT + "/enemy_size_audit.csv", "w", newline="", encoding="utf-8") as f:
    w = csv.DictWriter(f, list(rows[0].keys())); w.writeheader(); w.writerows(rows)
with open(OUT + "/enemy_size_audit.md", "a", encoding="utf-8") as f:
    f.write("\nAlternative (softer) rule: whole-silhouette sqrt(area) incl. wings vs goblin run frames:\n\n| enemy | whole ratio | alt scale | body-rule scale (applied) |\n|---|---|---|---|\n")
    for r in rows:
        if r["proposed_visualScale"] != r["visualScale_now"] or float(r["whole_silhouette_ratio"]) < 0.97:
            f.write(f"| {r['enemyId']} | {r['whole_silhouette_ratio']} | {r['alt_scale_whole_silhouette']} | {r['proposed_visualScale']} |\n")
            print(r["enemyId"], r["whole_silhouette_ratio"], r["alt_scale_whole_silhouette"], r["proposed_visualScale"])
