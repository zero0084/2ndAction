# -*- coding: utf-8 -*-
# ビルド検証(依頼F)の試行を並列に回す。trials.csv(trial,build,stage,growth,seed,depart,dest,extra)を読み、
# 結果の無い試行だけを最大 N 本同時に起動する(各試行は別プロセス・メモリ保存なので干渉しない)。
# usage: python run_trials.py <trials.csv> <resdir> <parallel> <runner.sh>
import csv, os, subprocess, sys, time

trials_csv, resdir, par, runner = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
BASH = r'C:\Program Files\Git\bin\bash.exe'
rows = list(csv.DictReader(open(trials_csv, encoding='utf-8')))
todo = [r for r in rows if not os.path.exists(os.path.join(resdir, r['trial'], 'result.json'))]
print(f'{len(rows)} trials, {len(todo)} to run, parallel {par}', flush=True)
running = []
t0 = time.time()
while todo or running:
    while todo and len(running) < par:
        r = todo.pop(0)
        args = [BASH, runner, resdir, r['trial'], r['build'], r['stage'], r['growth'], r['seed']]
        if r.get('depart') == 'sprint': args += ['-blDepart', 'sprint', '-blSprintDest', r.get('dest') or '10000']
        if r.get('extra'): args += r['extra'].split()
        env = dict(os.environ)
        if r.get('timeout'): env['BL_TIMEOUT'] = r['timeout']
        p = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env)
        running.append((r, p, time.time()))
        time.sleep(3)  # 起動を少しずらす
    for item in list(running):
        r, p, ts = item
        if p.poll() is not None:
            out = p.stdout.read().decode('utf-8', 'replace').strip()
            print(f'[{(time.time() - t0) / 60:6.1f}m] done {r["trial"]} ({(time.time() - ts) / 60:.1f}m) {out[-160:]}', flush=True)
            running.remove(item)
    time.sleep(5)
print('all done', flush=True)
