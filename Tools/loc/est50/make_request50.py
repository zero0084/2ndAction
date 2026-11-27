import io, json, os
# 2026-11-28: マルチの順位の行(11119〜11122)と HOST のクリアの知らせ(11123)をエステルへ頼む文を作る
HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.abspath(os.path.join(HERE, '..', '..', '..', 'Unity', '2ndAction', 'Assets', 'Resources', 'Localization'))
LANGS = "ar bn cs de en es fa fil fr hi id it km ko lo ms my pl pt-BR ro ru sw ta th tr uk vi zh-Hans zh-Hant".split()
REF = ["仲間に合流しました", "このまま走る", "脱出成功！"]
keys = [l.rstrip('\n').split('\t') for l in io.open(os.path.join(HERE, '..', 'keys_extra50.tsv'), encoding='utf-8') if l.strip()]
rows = []
for c in LANGS:
    d = json.load(io.open(os.path.join(SRC, c + '.json'), encoding='utf-8'))
    t = dict(zip(d['k'], d['v']))
    rows.append(c + '\t' + '\t'.join(t.get(k, '-') for k in REF))
src = '\n'.join(f"{k[0]}\t{k[3]}\t({k[2]})" for k in keys)
text = """エステル、こんにちは。One More Mile のマルチの画面に、新しい文を 5 行追加します。訳をお願いします(日本語はこのままです)。

【原文(ID・日本語・使う場所)】
""" + src + """

【決まり】
・11119〜11122 は走りながら一瞬で読む HUD の1行(左上の小さな欄の中、幅が狭い)。できるだけ短く。{0} {1} はそのまま残す(順番は言語に合わせて入れ替えてよい)。
・11119 は「何位 / 何人中」、11120 は「コース上で前から何番目 / 何人中」(順位ではなく並び順)。2つの違いが分かる言い方に。
・11121 と 11122 は同じ場所に入れ替わって出る(先頭でない時は差、先頭の時は一語)。11122 は 11121 の「先頭」と同じ言葉に。
・11123 の HOST は英字のまま。主語(私/あなた)は付けない。
・用語は下の既存の訳(同じマルチの画面の文)とそろえてください。

【既存の訳(言い回しをそろえる参考)】順に: 仲間に合流しました / このまま走る / 脱出成功！
""" + '\n'.join(rows) + """

【返してほしい形】1つのコードブロックに、タブ区切りで「言語コード<TAB>ID<TAB>訳」。29言語×5行=145行。長くて途中で切れそうなら、言語の順に分けて返してください(続きはこちらから頼みます)。
言語: """ + ', '.join(LANGS) + "\n"
io.open(os.path.join(HERE, 'request50.txt'), 'w', encoding='utf-8').write(text)
print(len(text)); print(text[:1800])
