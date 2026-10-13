# 試験の前に控えた通常データ(test: 以外)と比べる。Unity が自分で書き換える値(起動ごとの識別番号・ウィンドウの大きさ)以外が変わっていれば NG。
# ウィンドウの大きさ(Screenmanager*)だけは元へ戻す
import winreg, sys, io, ast
before = {}
for line in io.open(sys.argv[1], encoding='utf-8'):
    line = line.rstrip('\n')
    if not line: continue
    n, t, v = line.split('\t', 2)
    before[n] = (int(t), ast.literal_eval(v))
k = winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\DefaultCompany\One More Mile', 0, winreg.KEY_READ | winreg.KEY_SET_VALUE)
now = {}
i = 0
while True:
    try: n, v, t = winreg.EnumValue(k, i)
    except OSError: break
    i += 1
    if not n.startswith('test:'): now[n] = (t, v)
unity = ('unity.player_session', 'unity_connect.')
window = ('Screenmanager',)
migr = ('SaveProfile.TestKeys_',)  # 2026-10-14: テスト用データの管理用の一覧を test: の中へ移した(1回だけ)
changed, restored, migrated = [], [], []
for n in sorted(set(before) | set(now)):
    if n.startswith(unity): continue
    if before.get(n) == now.get(n): continue
    if n.startswith(window) and n in before:
        winreg.SetValueEx(k, n, 0, before[n][0], before[n][1]); restored.append(n); continue
    if n.startswith(migr) and n in before and n not in now: migrated.append(n); continue
    changed.append(n + (' (removed)' if n not in now else ' (added)' if n not in before else ' (changed)'))
print('Unity window size put back:', restored)
print('one-time move of the test-data key list into test::', migrated)
print('NORMAL DATA CHANGED BY THE TEST:' if changed else 'normal data unchanged (except Unity session ids / window size)', changed)
