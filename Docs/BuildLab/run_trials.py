# -*- coding: utf-8 -*-
# ビルド検証(依頼F)の試行を並列に回す。trials.csv(trial,build,stage,growth,seed,depart,dest,extra)を読み、
# 結果の無い試行だけを最大 N 本同時に起動する(各試行は別プロセス・メモリ保存なので干渉しない)。
# usage: python run_trials.py <trials.csv> <resdir> <parallel> <runner.sh>
import csv, os, subprocess, sys, time

trials_csv, resdir, par, runner = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
BASH = r'C:\Program Files\Git\bin\bash.exe'
rows = list(csv.DictReader(open(trials_csv, encoding='utf-8')))
def busy_or_done(r):
    d = os.path.join(resdir, r['trial'])
    if os.path.exists(os.path.join(d, 'result.json')): return True
    log = os.path.join(d, 'player.log')  # 別の実行が走らせている途中(10分以内に書かれている)
    return os.path.exists(log) and time.time() - os.path.getmtime(log) < 600
todo = [r for r in rows if not busy_or_done(r)]
print(f'{len(rows)} trials, {len(todo)} to run, parallel {par}', flush=True)
running = []
t0 = time.time()
GLOBAL_MAX = int(os.environ.get('BL_GLOBAL_MAX', '0'))  # >0: この機械で同時に動くゲームの数の上限(別の実行の分も数える)
MIN_FREE = float(os.environ.get('BL_MIN_FREE_GB', '0'))  # >0: 空きメモリがこれ未満なら新しい試行を始めない
import ctypes
class _MS(ctypes.Structure):
    _fields_ = [('dwLength', ctypes.c_ulong), ('dwMemoryLoad', ctypes.c_ulong), ('ullTotalPhys', ctypes.c_ulonglong), ('ullAvailPhys', ctypes.c_ulonglong),
                ('a', ctypes.c_ulonglong), ('b', ctypes.c_ulonglong), ('c', ctypes.c_ulonglong), ('d', ctypes.c_ulonglong), ('e', ctypes.c_ulonglong)]
def free_gb():
    m = _MS(); m.dwLength = ctypes.sizeof(_MS); ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m)); return m.ullAvailPhys / 2**30
def game_count():
    try:
        out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq OneMoreMile.exe', '/NH'], capture_output=True, text=True).stdout
        return sum(1 for l in out.splitlines() if 'OneMoreMile.exe' in l)
    except Exception: return 0
while todo or running:
    while todo and len(running) < par and (GLOBAL_MAX <= 0 or game_count() < GLOBAL_MAX) and (MIN_FREE <= 0 or free_gb() >= MIN_FREE):
        r = todo.pop(0)
        args = [BASH, runner, resdir, r['trial'], r['build'], r['stage'], r['growth'], r['seed']]
        if r.get('depart') == 'sprint': args += ['-blDepart', 'sprint', '-blSprintDest', r.get('dest') or '10000']
        if r.get('extra'): args += r['extra'].split()
        env = dict(os.environ)
        if r.get('timeout'): env['BL_TIMEOUT'] = r['timeout']
        p = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env)
        running.append((r, p, time.time()))
        time.sleep(20)  # 起動を少しずらす(メモリの測り直しのため)
    for item in list(running):
        r, p, ts = item
        if p.poll() is not None:
            out = p.stdout.read().decode('utf-8', 'replace').strip()
            print(f'[{(time.time() - t0) / 60:6.1f}m] done {r["trial"]} ({(time.time() - ts) / 60:.1f}m) {out[-160:]}', flush=True)
            running.remove(item)
    time.sleep(5)
print('all done', flush=True)
