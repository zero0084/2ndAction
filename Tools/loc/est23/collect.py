# エステルの訳(2026-10-23、42件 × 29言語)を集めて out/<code>.23.tsv と keys_extra23.tsv を作る。
#   python collect.py <batch files...>
# 行の形: 言語コード<TAB>ID<TAB>訳(同じ言語・IDが複数あれば後の物)。英語は en_review.tsv(エステルの見直しを反映した最終版)
import io, os, re, sys, glob

HERE = os.path.dirname(os.path.abspath(__file__))
LOC = os.path.abspath(os.path.join(HERE, '..'))
IDS = [10010, 10011] + list(range(10012, 10048)) + [10056, 10057, 10058, 10059]
LANGS = "ar bn cs de en es fa fil fr hi id it km ko lo ms my pl pt-BR ro ru sw ta th tr uk vi zh-Hans zh-Hant".split()
ROW = re.compile(r'^([a-zA-Z-]+)\t(10\d{3})\t(.+)$')

tr = {}
for f in sys.argv[1:]:
    for line in io.open(f, encoding='utf-8-sig'):
        m = ROW.match(line.rstrip('\r\n'))
        if m and m.group(1) in LANGS and int(m.group(2)) in IDS:
            tr[(m.group(1), int(m.group(2)))] = m.group(3).strip()

# 日本語の元の文(keys_extra19 の 10012..10047 と、LocPendingReview の定数)
ja = {}
for line in io.open(os.path.join(LOC, 'keys_extra19.tsv'), encoding='utf-8'):
    p = line.rstrip('\r\n').split('\t', 3)
    if len(p) == 4 and p[0].isdigit(): ja[int(p[0])] = p[3]
ja.update({
    10010: "ランは終了せず、直前のチェックポイント({0}m)からCONTINUEできます。",
    10011: "それより後に獲得したMILE・EXP・カードは取り消されます。",
    10056: "ライセンス", 10057: "表示する",
    10058: "このゲームで使っている素材の著作権表示とライセンスです(原文のまま掲載しています)。",
    10059: "脱出成功！",
})
with io.open(os.path.join(LOC, 'keys_extra23.tsv'), 'w', encoding='utf-8', newline='\n') as w:
    for i in IDS:
        w.write(f"{i}\tui\tレビュー 2026-10-23 (エステル)\t{ja[i]}\n")

os.makedirs(os.path.join(LOC, 'out'), exist_ok=True)
missing = []
for lang in LANGS:
    rows = [(i, tr.get((lang, i))) for i in IDS]
    miss = [i for i, v in rows if not v]
    if miss: missing.append((lang, miss))
    with io.open(os.path.join(LOC, 'out', f'{lang}.23.tsv'), 'w', encoding='utf-8', newline='\n') as w:
        for i, v in rows:
            if v: w.write(f"{i}\t{v}\n")
print('collected', len(tr), 'rows')
for lang, miss in missing: print('missing', lang, len(miss), miss[:6])
