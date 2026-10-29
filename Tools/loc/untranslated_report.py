# -*- coding: utf-8 -*-
# 訳が無いために元の文で代わりに出した物の一覧(2026-10-29)。
# 開発版は persistentDataPath/loc_untranslated.tsv に書く(Android: /sdcard/Android/data/com.zero0084.action2nd/files/、
# Windows の試験版: %USERPROFILE%/AppData/LocalLow/DefaultCompany/One More Mile Test/)。取り出したファイルをまとめて、
# 元の文ごとに「どの言語で・どの種類で出たか」と、表に型(キー)があるかを書き出す。訳を足す時の一覧にする。
# 使い方: python untranslated_report.py <loc_untranslated.tsv> [...] > report.tsv
import io, json, os, sys, glob
HERE = os.path.dirname(os.path.abspath(__file__))
TABLES = os.path.abspath(os.path.join(HERE, '..', '..', 'Unity', '2ndAction', 'Assets', 'Resources', 'Localization'))
keys = set()
for p in glob.glob(os.path.join(TABLES, 'en.json')):
    keys |= set(json.load(io.open(p, encoding='utf-8'))['k'])
rows = {}
for f in sys.argv[1:]:
    for line in io.open(f, encoding='utf-8', errors='replace'):
        p = line.rstrip('\n').split('\t')
        if len(p) < 4: continue
        _, lang, kind, src = p[:4]
        r = rows.setdefault(src, {'langs': set(), 'kinds': set()})
        r['langs'].add(lang); r['kinds'].add(kind)
out = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
out.write('source\tkinds\tlangs\tkey_in_table\n')
for src, r in sorted(rows.items(), key=lambda x: (-len(x[1]['langs']), x[0])):
    out.write(f"{src}\t{','.join(sorted(r['kinds']))}\t{','.join(sorted(r['langs']))}\t{src.replace(chr(92) + 'n', chr(10)) in keys}\n")
