# 2026-11-15 新しい文言(ガチャの 共通30%/マップ70%・提供割合)を keys_extra43.tsv へ書き出す。
#   python make_keys43.py   (Tools/loc で実行。LocGacha の ID 11040〜 だけ)
import io, re

BASE = "C:/GameProject/2ndAction/Unity/2ndAction/Assets/Scripts/"
PAT = re.compile(r'public const string (\w+) = "((?:[^"\\]|\\.)*)";\s*//[^\n]*?ID (\d+)')
CTX = {
    "MixLine": "ガチャの札の2行目。{0}=マップの分類名(荒野/洞窟/天空/ラスダン) {1}=70 {2}=30。1回ごとにマップ70%・共通30%で分類が決まる",
    "NoCardsMap": "最後に出発したマップの分類に、まだ引けるカードが無い時(引けない。MILEも広告の回数も使わない)",
    "Condition": "引けるようになる条件の前置き。{0}=条件の文(例: 自然洞窟で1,000m到達して帰還)",
    "Odds": "ボタンと画面の見出し: ガチャの提供割合(排出率)。短く",
    "OddsHow": "提供割合の画面の説明",
    "OddsMapIs": "提供割合の画面。{0}=マップの分類名",
    "OddsShare": "提供割合の表の見出し(左の列: 分類とその割合)。短く",
    "OddsTotal": "提供割合の表の合計の行。短く",
    "OddsCards": "提供割合の表: その分類の対象のカードの枚数。{0}=枚数。短く",
    "OddsPerPull": "提供割合の表の下の注記",
}
rows = []
s = io.open(BASE + "Gacha/GameManager.GachaCategory.cs", encoding="utf-8-sig").read()
for m in PAT.finditer(s):
    name, src, i = m.group(1), m.group(2), int(m.group(3))
    if i < 11040: continue
    rows.append((i, "ui", f"LocGacha.{name} {CTX.get(name, '')}", src))
s2 = io.open(BASE + "UI/PatchNotesUi.cs", encoding="utf-8-sig").read()
for m in PAT.finditer(s2):
    name, src, i = m.group(1), m.group(2), int(m.group(3))
    if i >= 11050: rows.append((i, "ui", f"LocNotes.{name} お知らせの変更点(利用者向けの1項目。前の N451a の差し替え)", src))
rows.sort()
with io.open("keys_extra43.tsv", "w", encoding="utf-8", newline="\n") as f:
    for r in rows: f.write(f"{r[0]}\t{r[1]}\t{r[2]}\t{r[3]}\n")
print(len(rows), "rows")
for r in rows: print(r[0], r[3])
