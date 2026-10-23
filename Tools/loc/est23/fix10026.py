# 10026(削除の画面: 広告なしパスの箇条書き)の引用のボタン名を、各言語の既存のボタン「購入を復元」(8133)の訳にそろえる。
#   python fix10026.py   … out/<code>.23.tsv の 10026 を書き換え、前後を fix10026.log へ書く(collect.py の後に毎回)
import io, json, os, re, glob
HERE = os.path.dirname(os.path.abspath(__file__))
LOC = os.path.abspath(os.path.join(HERE, '..'))
DST = os.path.abspath(os.path.join(LOC, '..', '..', 'Unity', '2ndAction', 'Assets', 'Resources', 'Localization'))
# 引用の形ごとに順に探す(' は略記の dell' などと間違えるので最後、英字の直後は除く)
QUOTES = [re.compile(p) for p in (r'«\s?([^»]+?)\s?»', r'„([^“”]+?)[“”]', r'“([^”]+?)”', r'"([^"]+?)"', r'「([^」]+?)」',
                                  r'『([^』]+?)』', r'‘([^’]+?)’', r"(?<![A-Za-z])'([^']+?)'")]


def find_quote(v):
    for q in QUOTES:
        m = q.search(v)
        if m: return m
    return None


out = io.open(os.path.join(HERE, 'fix10026.log'), 'w', encoding='utf-8')
for part in sorted(glob.glob(os.path.join(LOC, 'out', '*.23.tsv'))):
    code = os.path.basename(part)[:-len('.23.tsv')]
    t = json.load(io.open(os.path.join(DST, code + '.json'), encoding='utf-8'))
    name = dict(zip(t['k'], t['v'])).get('購入を復元')
    lines = io.open(part, encoding='utf-8').read().split('\n')
    for n, line in enumerate(lines):
        if not line.startswith('10026\t'): continue
        v = line.split('\t', 1)[1]
        m = find_quote(v)
        if not name or not m: out.write(f"{code}\tNO MATCH\t{name}\t{v}\n"); continue
        nv = v[:m.start(1)] + name + v[m.end(1):]
        lines[n] = '10026\t' + nv
        out.write(f"{code}\t{m.group(1)} -> {name}\t{nv}\n")
    io.open(part, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines))
out.close()
print(io.open(os.path.join(HERE, 'fix10026.log'), encoding='utf-8').read().count('\n'), 'langs; see fix10026.log')
