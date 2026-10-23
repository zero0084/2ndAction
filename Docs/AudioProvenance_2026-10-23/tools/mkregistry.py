# 音の権利管理表(TSV)を作る: 監査の一覧(appendix.md)+ ファイルの md5 + 元ファイル + 制作過程の確かさ
import hashlib, os, re, csv

ROOT = r'C:/GameProject/2ndAction/Unity/2ndAction/Assets/Audio'
ORIG = r'C:/GameProject/2ndAction/Docs/AudioProvenance_2026-10-23/originals'
APPX = r'C:/Users/0084k/AppData/Local/Temp/claude/C--GameProject-1stRPG/972e48a6-cec8-40d5-8275-62f59fbcb3de/scratchpad/audit/appendix.md'
OUT = r'C:/GameProject/2ndAction/Docs/AudioProvenance_2026-10-23/audio_rights_registry.tsv'


def md5(p):
    return hashlib.md5(open(p, 'rb').read()).hexdigest()


# 元ファイル(Downloads から保存したもの)の md5 → 名前
orig = {}
for d, _, fs in os.walk(ORIG):
    for f in fs:
        if f.lower().endswith(('.wav', '.mid')):
            orig[md5(os.path.join(d, f))] = os.path.relpath(os.path.join(d, f), ORIG).replace('\\', '/')
# zip の中身
import zipfile
z = zipfile.ZipFile(os.path.join(ORIG, 'se', 'OneMoreMile_SE_Subtle_Pack.zip'))
for n in z.namelist():
    if n.lower().endswith('.wav'):
        orig[hashlib.md5(z.read(n)).hexdigest()] = 'se/OneMoreMile_SE_Subtle_Pack.zip!' + n

rows = {}
for line in open(APPX, encoding='utf-8'):
    if not line.startswith('| ') or line.startswith('| #') or line.startswith('|---'):
        continue
    c = [x.strip() for x in line.strip().strip('|').split('|')]
    if len(c) < 8:
        continue
    name = c[1].strip('`')
    rows[name] = c

OLDMAP = {'old_attack_hit_n.wav': '01_attack_hit.wav', 'old_player_damage_n.wav': '02_player_damage.wav', 'old_enemy_defeat_n.wav': '03_enemy_defeat.wav',
          'old_player_death_n.wav': '04_player_death.wav', 'old_landing_n.wav': '05_landing.wav', 'old_AttackSe1_n.wav': 'AttackSe1.wav', 'old_AttackSe2_n.wav': 'AttackSe2.wav',
          'old_AttackSe3_n.wav': 'AttackSe3.wav', 'old_CardConfirmSe_n.wav': 'CardConfirmSe.wav', 'old_CardDeckAppearSe_n.wav': 'CardDeckAppearSe.wav', 'old_CardDrawSe_n.wav': 'CardDrawSe.wav',
          'old_CardFlipSe_n.wav': 'CardFlipSe.wav', 'old_CardSelectSe_n.wav': 'CardSelectSe.wav', 'old_double_jump_n.wav': 'DoubleJumpSe.wav', 'old_jump_n.wav': 'JumpSe.wav'}
orig_of_se = {}
for f in os.listdir(os.path.join(ROOT, 'SE')):
    if f.lower().endswith('.wav'):
        orig_of_se[f] = orig.get(md5(os.path.join(ROOT, 'SE', f)), '?')
out = []
for d, _, fs in os.walk(ROOT):
    for f in sorted(fs):
        if not f.lower().endswith('.wav'):
            continue
        p = os.path.join(d, f)
        rel = os.path.relpath(p, ROOT).replace('\\', '/')
        key = rel.replace('Placeholder/', 'P/')
        if key.startswith('SE/') or key in ('GameplayBgm.wav', 'TitleBgm.wav'):
            key2 = key
        else:
            key2 = key
        c = rows.get(key2) or rows.get(rel) or rows.get(os.path.basename(rel))
        h = md5(p)
        made = c[2] if c else ''
        source = c[4] if c else ''
        use = c[5] if c else ''
        trailer = c[6] if c else ''
        verdict = c[7] if c else ''
        o = orig.get(h, '')
        # 制作過程の確かさ
        if key in ('TitleBgm.wav',):
            process = '確定: ChatGPT の MIDI(ゲームTOPBGM.mid)→ MuseScore 4.7.4 + MS Basic(保存データ audiosettings.json と、同じ MIDI の再書き出しで一致)'
            lic = 'OpenAI 利用規約(MIDI)+ MS Basic: MIT(謝辞と著作権表示の掲載が必要 → ゲームのクレジットに収録)'
        elif key in ('GameplayBgm.wav',):
            process = '確定(再現による): ChatGPT の MIDI(ゲームプレイBGM.mid)→ MuseScore 4 + MS Basic。2026-10-23 に同じ MIDI を MuseScore 4.7.5 + MS Basic で書き出し、元の WAV と相関 0.9999996・長さ/形式一致(タイトル曲の対照と同じ一致度)。保存データは残っていない'
            lic = 'OpenAI 利用規約(MIDI)+ MS Basic: MIT(謝辞と著作権表示の掲載が必要 → ゲームのクレジットに収録)'
        elif key.startswith('SE/') or '/old_' in key:
            process = '不明: ChatGPT の会話で作られた WAV(ダウンロード元 chatgpt.com)。ChatGPT の中でどう作ったか(合成か外部素材か)の記録は無い。WAV の中に作者・道具の情報なし'
            lic = 'OpenAI 利用規約(通常の生成出力は商用利用可、2026-10-23 マスター判断で継続使用)'
            if '/old_' in key:
                process = '音量調整のみ(gen_audio_v2.py)。元の音は ' + process
        elif 'gen_audio' in made:
            process = '確定: プロジェクトのスクリプトの数式だけで合成(外部の素材・音源なし)'
            lic = 'プロジェクトの自作'
        else:
            process = '(要確認)'
            lic = ''
        if not o and key == 'TitleBgm.wav':
            o = 'bgm/ゲームTOPBGM.mid(作曲の MIDI)+ bgm/musescore_project_TOPBGM/(MuseScore の保存データ)。WAV は Assets と同じ(md5 一致)'
        if not o and key == 'GameplayBgm.wav':
            o = 'bgm/ゲームプレイBGM.mid(作曲の MIDI。One_More_Mile_Celtic_Road_Loop_v1 (1).mid と同一)。WAV は Assets と同じ(md5 一致)'
        if not o and '/old_' in key:
            base = OLDMAP.get(os.path.basename(key))
            o = ('Assets/Audio/SE/%s を音量調整したもの → その元ファイル: %s' % (base, orig_of_se.get(base, '?'))) if base else '?'
        out.append([rel, h, made, source, process, lic, o, use, trailer, verdict])

hdr = ['ファイル(Assets/Audio/)', 'md5', '作ったもの', '出どころ', '制作過程(確かさ)', '利用条件', '保存した元ファイル(originals/)', 'ゲームでの使用', '紹介動画', '判定(2026-10-23 監査)']
with open(OUT, 'w', encoding='utf-8-sig', newline='') as f:
    w = csv.writer(f, delimiter='\t', lineterminator='\n')
    w.writerow(hdr)
    w.writerows(out)
print(len(out), 'rows;', sum(1 for r in out if r[6]), 'with original;', sum(1 for r in out if r[4] == '(要確認)'), 'unknown')
for r in out:
    if (r[0].startswith('SE/') or '/old_' in r[0] or r[0].endswith('Bgm.wav')) and not r[6]:
        print('no original:', r[0])
    if r[4] == '(要確認)':
        print('unmatched:', r[0])
