# 2026-11-14 新しい文言(ガチャの分類 / お知らせ / 更新の案内)を keys_extra42.tsv へ書き出す。
#   python make_keys42.py   (Tools/loc で実行。C# の定数 = 日本語の原文と ID をそのまま拾う)
import io, re

BASE = "C:/GameProject/2ndAction/Unity/2ndAction/Assets/Scripts/"
PAT = re.compile(r'public const string (\w+) = "((?:[^"\\]|\\.)*)";\s*//[^\n]*?ID (\d+)')
CTX = {
    "LocGacha.WillDraw": "ガチャで選んでいる分類の案内。{0}=分類名(共通/荒野/洞窟/天空/ラスダン)。その分類のカードだけが出る",
    "LocGacha.NoCards": "選んだ分類で引けるカードが無い時(引けない)",
    "LocGacha.Locked": "未解禁の分類を押した時",
    "LocGacha.Pick": "縦画面の分類の帯の見出し(短く)",
    "LocNotes.Title": "タイトル画面のボタンと画面の見出し(更新内容のお知らせ)",
    "LocNotes.VersionLine": "お知らせの版の見出し。{0}=版の番号 {1}=日付(2026/11/14)。全角括弧はその言語の括弧に",
    "LocNotes.Changes": "お知らせの小見出し(主な変更点)",
    "LocNotes.Known": "お知らせの小見出し(既知の不具合)",
    "LocUpdate.Title": "更新の案内の見出し",
    "LocUpdate.Body": "更新の案内の本文(\\n で改行。強制ではない)",
    "LocUpdate.Update": "ボタン: ストアのこのアプリのページを開く",
    "LocUpdate.Later": "ボタン: 閉じて遊び続ける",
}
rows = []
for path, cls in (("Gacha/GameManager.GachaCategory.cs", "LocGacha"), ("UI/PatchNotesUi.cs", "LocNotes"), ("Platform/UpdateCheck.cs", "LocUpdate")):
    s = io.open(BASE + path, encoding="utf-8-sig").read()
    for m in PAT.finditer(s):
        name, src, i = m.group(1), m.group(2), int(m.group(3))
        key = f"{cls}.{name}"
        ctx = CTX.get(key, "お知らせの変更点(利用者向けの1項目)" if name.startswith("N4") else "")
        rows.append((i, "ui", f"{key} {ctx}", src))
rows.sort()
with io.open("keys_extra42.tsv", "w", encoding="utf-8", newline="\n") as f:
    for r in rows:
        f.write(f"{r[0]}\t{r[1]}\t{r[2]}\t{r[3]}\n")
print(len(rows), "rows")
for r in rows:
    print(r[0], r[3][:50])
