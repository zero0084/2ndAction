"""キャラの in_erased を動作ごとにまとめて並べた絵を作る(2026-10-25 布表現の見直し、全12人)。
同じ動作のコマは同じ絵へ(布の形がそろう)、1コマだけの動作はまとめて1枚、1枚は6コマまで。
usage: clothsheetsall.py <id>   → chars/<id>/sheets/s01.png ... と sheets/list.txt
"""
import sys, os, subprocess, collections
id = sys.argv[1]; W = f'Docs/ClothRevision_2026-10-25/chars/{id}'; E = W + '/in_erased'
os.makedirs(W + '/sheets', exist_ok=True)
groups = collections.OrderedDict()
for f in sorted(os.listdir(E)):
    if f.endswith('_x4.png'): groups.setdefault(f.split('__')[0], []).append(f[:-7])
sheets, cur = [], []
for g, fs in sorted(groups.items(), key=lambda kv: (len(kv[1]) == 1, kv[0])):
    for i in range(0, len(fs), 6 if len(fs) <= 6 else 4):
        part = fs[i:i + (6 if len(fs) <= 6 else 4)]
        if cur and len(cur) + len(part) > 6: sheets.append(cur); cur = []
        cur += part
        if len(fs) > 6: sheets.append(cur); cur = []
if cur: sheets.append(cur)
with open(W + '/sheets/list.txt', 'w') as L:
    for k, fs in enumerate(sheets, 1):
        nm = f's{k:02d}'; cols = len(fs) if len(fs) <= 4 else 3
        subprocess.run([sys.executable, 'Tools/art/clothsheet2.py', 'make', f'{W}/sheets/{nm}.png', str(cols)] + [f'{E}/{x}_x4.png' for x in fs], check=True)
        L.write(nm + '\t' + ' '.join(fs) + '\n')
