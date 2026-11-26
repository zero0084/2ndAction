# 2026-11-26 エステルの訳 est48_all.tsv(言語コード<TAB>ID<TAB>訳)を out/<code>.43.tsv(ID<TAB>訳)へ分ける。
#   python collect48.py <est48_all.tsv>   → その後 python merge_add.py 43 keys_extra48.tsv
import io, os, sys, collections

HERE = os.path.dirname(os.path.abspath(__file__))
LANGS = "ar bn cs de en es fa fil fr hi id it km ko lo ms my pl pt-BR ro ru sw ta th tr uk vi zh-Hans zh-Hant".split()
ids = [l.split("\t")[0] for l in io.open(os.path.join(HERE, "keys_extra48.tsv"), encoding="utf-8") if l.strip()]
got = collections.defaultdict(dict)
bad = []
for n, line in enumerate(io.open(sys.argv[1], encoding="utf-8-sig"), 1):
    line = line.rstrip("\r\n")
    if not line.strip(): continue
    p = line.split("\t")
    if len(p) != 3 or p[0] not in LANGS or p[1] not in ids: bad.append((n, line[:80])); continue
    got[p[0]][p[1]] = p[2].strip()
missing = [(c, i) for c in LANGS for i in ids if i not in got[c]]
for c in LANGS:
    if not got[c]: continue
    with io.open(os.path.join(HERE, "out", c + ".48.tsv"), "w", encoding="utf-8", newline="\n") as f:
        for i in ids:
            if i in got[c]: f.write(f"{i}\t{got[c][i]}\n")
print(f"languages {sum(1 for c in LANGS if got[c])}/29, rows {sum(len(v) for v in got.values())}, bad lines {len(bad)}, missing {len(missing)}")
for b in bad[:10]: print("bad", b)
for m in missing[:20]: print("missing", m)
