import io, json, os
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', '..', 'Unity', '2ndAction', 'Assets', 'Resources', 'Localization'))
LANGS = "ar bn cs de en es fa fil fr hi id it km ko lo ms my pl pt-BR ro ru sw ta th tr uk vi zh-Hans zh-Hant".split()
REF = ["死神の乱入まで {0}秒", "{0}が切断しました", "HOSTが脱出に成功しました"]
keys = [l.rstrip('\n').split('\t') for l in io.open(os.path.join(HERE, '..', 'keys_extra51.tsv'), encoding='utf-8') if l.strip()]
rows = []
for c in LANGS:
    d = json.load(io.open(os.path.join(SRC, c + '.json'), encoding='utf-8'))
    t = dict(zip(d['k'], d['v']))
    rows.append(c + '\t' + '\t'.join(t.get(k, '-') for k in REF))
src = '\n'.join(f"{k[0]}\t{k[3]}\t({k[2]})" for k in keys)
text = """エステル、続けてもう3行お願いします(日本語はこのままです)。マルチの画面の新しい文です。

【原文(ID・日本語・使う場所)】
""" + src + """

【決まり】
・どれも走りながら読む短い表示。主語(私/あなた)は付けない。HOST は英字のまま。{0} はそのまま残す。
・「死神」「脱落」「復活なし」の言い方は、下の既存の訳(死神の言い方)とそろえてください。
・11125 と 11126 は「死神の攻撃で倒れたので、仲間の救助もカードの復活も効かない」ことが伝われば十分です。括弧の形は各言語で自然な形に。

【既存の訳(言い回しをそろえる参考)】順に: 死神の乱入まで {0}秒 / {0}が切断しました / HOSTが脱出に成功しました
""" + '\n'.join(rows) + """

【返してほしい形】1つのコードブロックに、タブ区切りで「言語コード<TAB>ID<TAB>訳」。29言語×3行=87行。
言語: """ + ', '.join(LANGS) + "\n"
io.open(os.path.join(HERE, 'request51.txt'), 'w', encoding='utf-8').write(text)
print(json.dumps(text, ensure_ascii=False))
