# -*- coding: utf-8 -*-
# 翻訳する文の抜き出し(2026-10-07)。プレイヤーが通常目にする文だけ(開発版の道具/ログ/テスト/調整用の説明は除く)。
#  ・スクリプト: UI の描画/お知らせに渡す文字列(日本語を含む物と、英語の UI ラベル)。$"..{x}.." は {0}{1}.. の型にする
#  ・シーン: SceneBuilder が uGUI の Text に焼き込む文
#  ・データ: カード(名前/説明)/キャラ(名前/肩書き/説明/役割)/マップ(名前/説明)
# 出力: Tools/loc/source.json  [{ "key": 元の文(日本語 or 英語), "ctx": 場所, "kind": ui|card|char|stage }]
import io, json, os, re, glob, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', 'Unity', '2ndAction', 'Assets'))
SCRIPTS = os.path.join(ROOT, 'Scripts')
JP = re.compile(r'[぀-ヿ㐀-鿿！-｠]')

EXCLUDE_FILE = re.compile(r'(QaSweep|Qa\.cs$|AutoTest|Tour|SelfTest|Demo|DebugPanel|CardTest|NetAutoTest|LanAutoTest|FreezeDiagnostics|DiagnosticsOverlay|StallProbe|Dev[/\\]|EndgameDebug|Tuning\.cs$|BossDeathTuning|FinalEvolutionTuning|AudioMeter|HitboxOverlay|GameFeelDebug|SaveKeys|SaveSystem|DebugRun|BossFinishDebug|FinishDebug|ArenaCatalog\.cs$|BossDiagnostics|EncounterTypes|EncounterDirector|HighSpeedAssist|BossManager\.|Scenery|Localization/|SaveProfile|NetStats|WorldRange|BossLeash)')
# UI に渡す呼び出し(この行の文字列だけを拾う)
UI_CALL = re.compile(r'(GUI\.Label|GUI\.Button|UiKit\.Button|UiKit\.Label|DrawStyledButton|DrawStatPanel|\.text\s*=|Row\(|Enqueue\(|Toast\(|title\s*=|body\s*=|hint\s*=|msg\s*=|label\s*=|Note\(|Head\(|RowLabel\(|ChoiceRow\(|SliderRow\(|Banner|Announce|ShowMessage|SetLabel|Status\s*=|status\s*=|note\s*=|LevelLine|Title\s*=|Description\s*=|=>\s*\$?"|return\s+\$?"|\?\s*\$?"|:\s*\$?")')
SKIP_LINE = re.compile(r'(Debug\.Log|LogWarning|LogError|DeathLog|FreezeDiagnostics|\[Tooltip|\[Header|\bL\(\$?"|Check\(|Warn\(|BlocksSave\(|PlayerPrefs|SaveStore\.|GetField\(|GetMethod\(|Resources\.Load|Find\("|\.name\s*=|new GameObject\(|AddComponent|Shader\.Find|CompareTag|tag\s*==|nameof\()')
STR = re.compile(r'(\$?)@?"((?:[^"\\]|\\.)*)"')
ENG_UI = re.compile(r'^[A-Z][A-Z0-9 \-!?:/%+.&\'()×]{1,40}$')

def template(s, interp):
    if not interp: return s
    out, n = [], 0
    i = 0
    while i < len(s):
        c = s[i]
        if c == '{' and i + 1 < len(s) and s[i + 1] == '{': out.append('{{'); i += 2; continue
        if c == '}' and i + 1 < len(s) and s[i + 1] == '}': out.append('}}'); i += 2; continue
        if c == '{':
            depth, j = 1, i + 1
            while j < len(s) and depth:
                if s[j] == '{': depth += 1
                elif s[j] == '}': depth -= 1
                j += 1
            out.append('{%d}' % n); n += 1; i = j; continue
        out.append(c); i += 1
    return ''.join(out)

def unescape(s):
    return s.replace('\\n', '\n').replace('\\"', '"').replace('\\t', '\t').replace('\\\\', '\\')

def want(text):
    if not text.strip(): return False
    if JP.search(text): return True
    t = text.strip()
    return bool(ENG_UI.match(t)) and len(t) >= 2

items = {}
PH = re.compile(r'\{(\d+)\}')
def add1(key, ctx, kind):
    if kind in ('ui', 'scene') and not want(key): return
    if not key.strip(): return
    if key in items: items[key]['n'] += 1; return
    items[key] = {'key': key, 'ctx': ctx, 'kind': kind, 'n': 1}
def add(key, ctx, kind):
    if kind == 'char' and chr(10) in key:
        add1(key, ctx, kind)  # キャラの説明文は文全体で1つ(途中で行を折っているため。実行時は全体一致を先に引く)
        return
    # 実行時は1行ずつ引く(Loc.Auto)。行に分けて、行の中の差し込みは {0} から振り直す
    for line in key.split(chr(10)):
        line = line.strip(' ')
        if not line: continue
        order = []
        def ren(m):
            if m.group(1) not in order: order.append(m.group(1))
            return '{%d}' % order.index(m.group(1))
        add1(PH.sub(ren, line), ctx, kind)

for path in glob.glob(os.path.join(SCRIPTS, '**', '*.cs'), recursive=True):
    rel = os.path.relpath(path, SCRIPTS).replace('\\', '/')
    if EXCLUDE_FILE.search(rel): continue
    src = io.open(path, encoding='utf-8-sig').read()
    in_dev = False
    for ln, line in enumerate(src.split('\n'), 1):
        code = line.split('//')[0] if '"' not in line.split('//')[0] or line.count('"') % 2 == 0 else line
        st = line.strip()
        if st.startswith('//') or st.startswith('*') or st.startswith('/*'): continue
        if SKIP_LINE.search(line): continue
        if not UI_CALL.search(line) and not JP.search(line) and not re.search(r'"[A-Z][A-Z ]{3,}"', line): continue
        for m in STR.finditer(code):
            interp, raw = m.group(1) == '$', m.group(2)
            text = unescape(template(raw, interp))
            if want(text): add(text, f'{rel}:{ln}', 'ui')

# シーンに焼き込む文(SceneBuilder)
sb = os.path.join(ROOT, 'Editor', 'SceneBuilder.cs')
for ln, line in enumerate(io.open(sb, encoding='utf-8-sig').read().split('\n'), 1):
    if SKIP_LINE.search(line) or line.strip().startswith('//'): continue
    if not re.search(r'(\.text\s*=|CreateText|Label|Button|Header|Title)', line): continue
    for m in STR.finditer(line.split('//')[0]):
        text = unescape(template(m.group(2), m.group(1) == '$'))
        if want(text): add(text, f'Editor/SceneBuilder.cs:{ln}', 'scene')

# データ(ScriptableObject の YAML。日本語は \uXXXX)
def yaml_str(v):
    v = v.strip()
    if v.startswith('"') and v.endswith('"'):
        v = v[1:-1]
        v = re.sub(r'\\u([0-9a-fA-F]{4})', lambda m: chr(int(m.group(1), 16)), v)
        v = v.replace('\\n', '\n').replace('\\"', '"')
    return v

def yaml_fields(path, names):
    out = {}
    lines = io.open(path, encoding='utf-8').read().split(chr(10))
    i = 0
    while i < len(lines):
        m = re.match(r'^  (\w+): (.*)$', lines[i])
        if m and m.group(1) in names:
            v = m.group(2)
            if v.startswith("'"):
                # 単一引用符: 閉じる ' まで続く。空行=改行、それ以外の改行=空白、'' = '
                body = [v[1:]]
                while not (body[-1].rstrip().endswith("'") and not body[-1].rstrip().endswith("''")) or (len(body) == 1 and body[0].rstrip() == ''):
                    i += 1
                    if i >= len(lines): break
                    body.append(lines[i].strip())
                body[-1] = body[-1].rstrip()[:-1]
                text, pend = '', ''
                for b in body:
                    if b == '': pend = chr(10); continue
                    text += (pend if text else '') + b if pend else ((' ' if text else '') + b)
                    pend = ''
                out[m.group(1)] = text.replace("''", "'")
            else:
                while v.startswith('"') and not re.search(r'(?<!\\)"\s*$', v[1:]) and i + 1 < len(lines):
                    i += 1; v += ' ' + lines[i].strip()
                out[m.group(1)] = yaml_str(v)
        i += 1
    return out

RES = os.path.join(ROOT, 'Resources')
for p in glob.glob(os.path.join(RES, 'Cards', '*.asset')):
    f = yaml_fields(p, ['cardId', 'cardName', 'description'])
    for k in ('cardName', 'description'):
        if f.get(k): add(f[k], 'card:' + f.get('cardId', ''), 'card')
for p in glob.glob(os.path.join(RES, 'Characters', '*.asset')):
    f = yaml_fields(p, ['characterId', 'displayName', 'subtitle', 'flavorText', 'role'])
    for k in ('displayName', 'subtitle', 'flavorText', 'role'):
        if f.get(k): add(f[k], 'char:' + f.get('characterId', ''), 'char')
for p in glob.glob(os.path.join(RES, 'Stages', '*.asset')):
    f = yaml_fields(p, ['stageId', 'displayName', 'description', 'featureText', 'enemyText', 'obstacleText', 'routeText'])
    for k, v in f.items():
        if k != 'stageId' and v: add(v, 'stage:' + f.get('stageId', ''), 'stage')

for k in ['{0} のキャラカード']: add(k, 'manual', 'ui')
out = sorted(items.values(), key=lambda x: (x['kind'], x['ctx']))
dst = os.path.join(os.path.dirname(__file__), 'source.json')
io.open(dst, 'w', encoding='utf-8').write(json.dumps(out, ensure_ascii=False, indent=1))
from collections import Counter
print(len(out), Counter(x['kind'] for x in out))
print(Counter(x['ctx'].split(':')[0] for x in out).most_common(40))
