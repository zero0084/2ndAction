# 読み込みの失敗の画面の3件(10060〜10062、2026-10-24 エステル)を集めて out/<code>.24.tsv と keys_extra24.tsv を作る。
#   python collect_load.py <batch files...>
import io, os, re, sys
HERE = os.path.dirname(os.path.abspath(__file__))
LOC = os.path.abspath(os.path.join(HERE, '..'))
JA = {10060: "読み込みに時間がかかっています", 10061: "シーンを読み込めませんでした", 10062: "もう一度読み込む"}
LANGS = "ar bn cs de en es fa fil fr hi id it km ko lo ms my pl pt-BR ro ru sw ta th tr uk vi zh-Hans zh-Hant".split()
ROW = re.compile(r'^([a-zA-Z-]+)\t(1006[0-2])\t(.+)$')
tr = {}
for f in sys.argv[1:]:
    for line in io.open(f, encoding='utf-8-sig'):
        m = ROW.match(line.rstrip('\r\n'))
        if m and m.group(1) in LANGS: tr[(m.group(1), int(m.group(2)))] = m.group(3).strip()
with io.open(os.path.join(LOC, 'keys_extra24.tsv'), 'w', encoding='utf-8', newline='\n') as w:
    for i, v in JA.items(): w.write(f"{i}\tui\tUI/LoadingScreen.cs レビュー 2026-10-24 (エステル)\t{v}\n")
missing = []
for lang in LANGS:
    rows = [(i, tr.get((lang, i))) for i in JA]
    if any(not v for _, v in rows): missing.append(lang)
    with io.open(os.path.join(LOC, 'out', f'{lang}.24.tsv'), 'w', encoding='utf-8', newline='\n') as w:
        for i, v in rows:
            if v: w.write(f"{i}\t{v}\n")
print('collected', len(tr), 'rows; missing', missing)
