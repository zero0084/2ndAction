import io, json, os
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', '..', 'Unity', '2ndAction', 'Assets', 'Resources', 'Localization'))
LANGS = "ar bn cs de en es fa fil fr hi id it km ko lo ms my pl pt-BR ro ru sw ta th tr uk vi zh-Hans zh-Hant".split()
REF = ["{0}が切断しました", "このまま走る", "ホームへ戻る"]
rows = []
for c in LANGS:
    d = json.load(io.open(os.path.join(SRC, c + '.json'), encoding='utf-8'))
    t = dict(zip(d['k'], d['v']))
    rows.append(c + '\t' + '\t'.join(t.get(k, '-') for k in REF))
text = """エステル、こんにちは。One More Mile の新しい文を 1 行だけ訳してください(日本語はマスターが確認済みで、このままです)。

【原文(ID・日本語・使う場所)】
11118	仲間に合流しました	(協力マルチのラン中、画面中央の短い知らせ(2.5秒)。仲間から大きく遅れた人を、前の仲間の近くへ自動で移した時に、移された本人の画面に出す。短く、主語なし)

【決まり】走りながら一瞬で読む短い知らせ。主語(私/あなた)は付けない。「チーム」「仲間」の言い方は、下の既存の訳(同じ協力マルチの画面の文)とそろえてください。

【既存の訳(言い回しをそろえる参考)】順に: {0}が切断しました / このまま走る / ホームへ戻る
""" + '\n'.join(rows) + """

【返してほしい形】1つのコードブロックに、タブ区切りで「言語コード<TAB>ID<TAB>訳」。29言語×1行=29行。
言語: """ + ', '.join(LANGS) + "\n"
io.open(os.path.join(HERE, 'request49.txt'), 'w', encoding='utf-8').write(text)
print(text[:1500])
