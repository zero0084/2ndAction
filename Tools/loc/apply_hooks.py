# -*- coding: utf-8 -*-
# 翻訳の入口を描画の所へ入れる(2026-10-07)。何度走らせても同じ(既に入っている所は触らない)。
#  ・プレイヤー向けのファイルの GUI.Label( → LocGUI.Label(
#  ・uGUI の文を書き換える所(.text = 式;) → .text = Loc.Auto(式);
import io, os, re, glob
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', 'Unity', '2ndAction', 'Assets', 'Scripts'))
EXCLUDE = re.compile(r'(QaSweep|AutoTest|Tour|SelfTest|Demo|DebugPanel|CardTest|NetAutoTest|LanAutoTest|FreezeDiagnostics|DiagnosticsOverlay|StallProbe|Dev[/\\]|EndgameDebug|BossDiagnostics|AudioMeter|HitboxOverlay|GameFeelDebug|Localization[/\\]|BossFinishDebug|FinishDebug|TestDataLabel|SaveProfile)')
TEXT_FILES = re.compile(r'(DeckEditUI|CardFusionUI|CharacterSelectUI|StageSelectUI|RewardCardUI|LevelUpChoiceRowUI|SprintDeparturePanel|ComboCounterUI|RewardCardSequence|ScreenTransitionManager|CardDetail|Ui[A-Z]\w*)\.cs$')
SET_TEXT = re.compile(r'^(.*?)([\w\.\[\]]+\.text\s*=(?!=)\s*)(?!Loc\.)([^;]+);(\s*(//.*)?)$')
changed = []
for path in glob.glob(os.path.join(ROOT, '**', '*.cs'), recursive=True):
    rel = os.path.relpath(path, ROOT).replace('\\', '/')
    if EXCLUDE.search(rel): continue
    raw = io.open(path, encoding='utf-8-sig', newline='').read()
    crlf = '\r\n' in raw
    s = raw.replace('\r\n', '\n')
    orig = s
    s = re.sub(r'(?<![\w\.])GUI\.Label\(', 'LocGUI.Label(', s)
    if TEXT_FILES.search(rel):
        out = []
        for line in s.split('\n'):
            m = SET_TEXT.match(line)
            if m and '"' not in m.group(3).split('?')[0][:0] and not m.group(3).strip() in ('""', 'null', 'string.Empty'):
                expr = m.group(3)
                line = f'{m.group(1)}{m.group(2)}Loc.Auto({expr});{m.group(4)}'
            out.append(line)
        s = '\n'.join(out)
    if s != orig:
        if crlf: s = s.replace('\n', '\r\n')
        io.open(path, 'w', encoding='utf-8', newline='').write(s)
        changed.append(rel)
print(len(changed), 'files changed')
for c in changed: print('  ', c)
