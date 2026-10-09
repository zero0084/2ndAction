# -*- coding: utf-8 -*-
# 翻訳の取り込み(2026-10-07): Tools/loc/out/<code>.*.tsv → Assets/Resources/Localization/<code>.json
# キー: keys.tsv(id 0..) + keys_extra.tsv(id 2000.. キャラの説明文など)。TSV の中の \n は改行。
# 検査: 全IDがある / {n} の差し込みが元と同じ / 書式タグ(<color> <size> <b>)の数が同じ / 空でない / 仮名が残っていない。
# 検査に通らない行は入れない(実行時は英語へフォールバックし、開発版で「欠けている訳」として記録される)。
import io, json, os, re, glob

HERE = os.path.dirname(os.path.abspath(__file__))
DST = os.path.abspath(os.path.join(HERE, '..', '..', 'Unity', '2ndAction', 'Assets', 'Resources', 'Localization'))
os.makedirs(DST, exist_ok=True)
NL = chr(10)
BSN = chr(92) + 'n'

keys = {}
for name in ('keys.tsv', 'keys_extra.tsv', 'keys_extra2.tsv', 'keys_extra3.tsv', 'keys_extra4.tsv', 'keys_extra5.tsv', 'keys_extra6.tsv', 'keys_extra7.tsv'):
    path = os.path.join(HERE, name)
    if not os.path.exists(path): continue
    for line in io.open(path, encoding='utf-8'):
        line = line.rstrip('\r\n')
        if not line: continue
        p = line.split('\t', 3)
        if len(p) < 4: continue
        if p[3].startswith("'"): continue  # 抜き出しの誤り(キャラ説明の1行目だけ)。正しい物は keys_extra.tsv
        k = re.sub(r'\\x([0-9A-Fa-f]{2})', lambda m: chr(int(m.group(1), 16)), p[3])  # 抜き出しで残った \xNN を文字に
        keys[int(p[0])] = k.replace(BSN, NL)

PH = re.compile(r'\{\d+\}')
TAG = re.compile(r'</?(color|size|b|i)(=[^>]*)?>')
KANA = re.compile('[぀-ヺー-ヿ]')  # ・(30FB) は記号として残してよい

def tags(s): return sorted(m.group(0).split('=')[0] for m in TAG.finditer(s))

codes = sorted(set(re.sub(r'\.(\d|x|y|z|w|v|u|t|s)\.tsv$', '', os.path.basename(p)) for p in glob.glob(os.path.join(HERE, 'out', '*.tsv'))))
report = []
for code in codes:
    tr = {}
    for part in sorted(glob.glob(os.path.join(HERE, 'out', code + '.*.tsv'))):
        if not re.search(r'\.(\d|x|y|z|w|v|u|t|s)\.tsv$', part): continue
        for line in io.open(part, encoding='utf-8-sig'):
            line = line.rstrip('\r\n')
            if not line or '\t' not in line: continue
            i, v = line.split('\t', 1)
            if i.strip().isdigit(): tr[int(i)] = re.sub(r'\\x([0-9A-Fa-f]{2})', lambda m: chr(int(m.group(1), 16)), v.strip()).replace(BSN, NL)
    ok_k, ok_v, bad = [], [], []
    for i, k in sorted(keys.items()):
        v = tr.get(i)
        if v is None or v == '': bad.append((i, 'missing')); continue
        if sorted(PH.findall(k)) != sorted(PH.findall(v)): bad.append((i, 'placeholder')); continue
        if tags(k) != tags(v): bad.append((i, 'tags')); continue
        if code != 'ja' and KANA.search(v): bad.append((i, 'kana left')); continue
        if code == 'ja' and v == k and re.search('[぀-ヿ㐀-鿿]', k): continue  # 日本語の元の文はそのまま(表に入れない)。英語のまま残す物は表に入れる(欠けとして数えない)
        ok_k.append(k); ok_v.append(v)
    with io.open(os.path.join(DST, code + '.json'), 'w', encoding='utf-8') as f:
        json.dump({'k': ok_k, 'v': ok_v}, f, ensure_ascii=False)
    report.append(f'{code}: {len(tr)} lines, {len(ok_k)} accepted, {len(bad)} rejected ' + (str(bad[:10]) if bad else ''))
print(NL.join(report))
io.open(os.path.join(HERE, 'merge_report.txt'), 'w', encoding='utf-8').write(NL.join(report) + NL)
