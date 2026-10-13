# -*- coding: utf-8 -*-
# 翻訳の追加取り込み(2026-10-13): keys_extra9.tsv(id 9000..)の訳 out/<code>.3.tsv を、今の表(Resources/Localization/<code>.json)へ足す。
# merge.py は全部の表を作り直すが、こちらは今の表を壊さずに足す/置き換えるだけ(検査は merge.py と同じ)。
# 使い方: python merge_add.py [suffix=3] [keys=keys_extra9.tsv]
import io, json, os, re, glob, sys

HERE = os.path.dirname(os.path.abspath(__file__))
DST = os.path.abspath(os.path.join(HERE, '..', '..', 'Unity', '2ndAction', 'Assets', 'Resources', 'Localization'))
NL = chr(10)
BSN = chr(92) + 'n'
suffix = sys.argv[1] if len(sys.argv) > 1 else '3'
keyfile = sys.argv[2] if len(sys.argv) > 2 else 'keys_extra9.tsv'
PH = re.compile(r'\{\d+\}')
TAG = re.compile(r'</?(color|size|b|i)(=[^>]*)?>')
KANA = re.compile('[぀-ヺー-ヿ]')
JP = re.compile('[぀-ヿ㐀-鿿]')
def tags(s): return sorted(m.group(0).split('=')[0] for m in TAG.finditer(s))

keys = {}
for line in io.open(os.path.join(HERE, keyfile), encoding='utf-8'):
    p = line.rstrip('\r\n').split('\t', 3)
    if len(p) == 4 and p[0].isdigit(): keys[int(p[0])] = p[3].replace(BSN, NL)

report = []
for part in sorted(glob.glob(os.path.join(HERE, 'out', '*.' + suffix + '.tsv'))):
    code = os.path.basename(part)[:-len('.' + suffix + '.tsv')]
    tr = {}
    for line in io.open(part, encoding='utf-8-sig'):
        line = line.rstrip('\r\n')
        if not line or '\t' not in line: continue
        i, v = line.split('\t', 1)
        if i.strip().isdigit(): tr[int(i)] = v.strip().replace(BSN, NL)
    path = os.path.join(DST, code + '.json')
    t = json.load(io.open(path, encoding='utf-8')) if os.path.exists(path) else {'k': [], 'v': []}
    idx = {k: n for n, k in enumerate(t['k'])}
    added = replaced = 0; bad = []
    for i, k in sorted(keys.items()):
        v = tr.get(i)
        if not v: bad.append((i, 'missing')); continue
        if sorted(PH.findall(k)) != sorted(PH.findall(v)): bad.append((i, 'placeholder')); continue
        if tags(k) != tags(v): bad.append((i, 'tags')); continue
        if k.count(NL) != v.count(NL): bad.append((i, 'lines')); continue
        if code != 'ja' and KANA.search(v): bad.append((i, 'kana left')); continue
        if code == 'ja' and v == k and JP.search(k): continue
        if k in idx: t['v'][idx[k]] = v; replaced += 1
        else: idx[k] = len(t['k']); t['k'].append(k); t['v'].append(v); added += 1
    with io.open(path, 'w', encoding='utf-8') as f:
        json.dump(t, f, ensure_ascii=False)
    report.append(f'{code}: +{added} ~{replaced} rejected {len(bad)} ' + (str(bad[:8]) if bad else ''))
print(NL.join(report))
