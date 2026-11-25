# -*- coding: utf-8 -*-
# 2台のマルチの記録(TEAMLOG)を実時刻でそろえて比べる(2026-11-24)。使い方: python teamrun.py <dir with host.log/join.log> coop|versus
#  ・画面の距離(hud)が下がらないか / 0.5 秒で不自然に跳ばないか
#  ・協力: HOST と JOIN の画面の距離の差(同じ時刻の近い記録どうし)
#  ・対戦: 画面の距離 = 各自の距離(own)のままか
#  ・相手がカード選択/一時停止の間、こちらは進み続けたか(こちらがボスの闘技場で止まっている時を除く)
#  ・ボスを倒した後(bosses=0)に足止め(hold)が残っていないか / HOST の時間(ts)が 0 になっていないか
import io, os, re, sys
from datetime import datetime

D = sys.argv[1]; MODE = sys.argv[2] if len(sys.argv) > 2 else 'coop'
pat = re.compile(r'TEAMLOG (.*)')
def parse(path):
    rows = []
    if not os.path.exists(path): return rows
    for line in io.open(path, encoding='utf-8', errors='replace'):
        m = pat.search(line)
        if not m: continue
        kv = dict(p.split('=', 1) for p in m.group(1).split() if '=' in p)
        try: kv['T'] = datetime.strptime(kv['utc'], '%H:%M:%S.%f')
        except Exception: continue
        for k in ('x', 'raw', 'hud', 'own', 'front', 'haltF', 'ts', 't', 'en', 'spawned', 'hsL', 'hsS', 'snaps'):
            if k in kv: kv[k] = float(kv[k])
        for k in ('holding', 'choosing', 'paused', 'hold', 'phase', 'fightOver'):
            kv[k] = kv.get(k) == 'True'
        kv['bosses'] = int(kv.get('bosses', '0'))
        rows.append(kv)
    return rows

H = parse(os.path.join(D, 'host.log')); J = parse(os.path.join(D, 'join.log'))
res = []
def check(ok, msg): res.append(('OK  ' if ok else 'NG  ') + msg)

for name, rows in (('HOST', H), ('JOIN', J)):
    if not rows: check(False, f'{name}: no TEAMLOG'); continue
    # 2026-11-25: ランの終わりに HOST の値へそろえた時(snaps が増えた行)の 1m 以下の戻りは、確定のそろえ(仕様)として除く
    back = max((a['hud'] - b['hud'] for a, b in zip(rows, rows[1:]) if not (b.get('snaps', 0) > a.get('snaps', 0) and a['hud'] - b['hud'] <= 1.0)), default=0)
    snapback = max((a['hud'] - b['hud'] for a, b in zip(rows, rows[1:]) if b.get('snaps', 0) > a.get('snaps', 0)), default=0)
    if snapback > 0: res.append(f'INFO {name}: aligned to the HOST value at the end of the run (moved back {snapback:.2f} m)')
    jump = max((b['hud'] - a['hud'] for a, b in zip(rows, rows[1:])), default=0)
    check(back < 0.05, f'{name}: screen distance never goes back (largest drop {back:.2f} m)')
    check(jump < 30.0, f'{name}: no sudden jump in 0.5 s (largest step {jump:.1f} m)')
    if MODE == 'versus':
        diff = max(abs(r['hud'] - r['own']) for r in rows)
        check(diff < 0.05, f'{name}: VERSUS shows own distance (max |hud-own| {diff:.2f})')
    # 1回だけの ts=0 は命中の一瞬の停止(HitStop、約0.1秒)。0.5 秒以上続けて止まった所を数える
    zero = [r['ts'] < 0.05 and not r['paused'] for r in rows]
    runs = sum(1 for a2, b2, c2 in zip(zero, zero[1:], zero[2:]) if a2 and b2 and c2)
    check(runs == 0, f'{name}: the shared world never stopped for 1 s or more (3+ consecutive ts=0 samples {runs}, single hit-stop samples {sum(zero)})')

# 協力: 同じ時刻の画面の距離の差
if MODE == 'coop' and H and J:
    diffs = []
    # 2026-11-25: 記録の時刻は2台で最大0.3秒ずれるので、HOST の値は前後2行から同じ時刻へ直線で補って比べる(以前は近い行どうしで、走る速さ×ずれ = 数m が差に入っていた)
    raw_diffs = []
    for r in J:
        if r['t'] <= 3: continue
        h = min(H, key=lambda q: abs((q['T'] - r['T']).total_seconds()))
        if abs((h['T'] - r['T']).total_seconds()) <= 0.3: raw_diffs.append(abs(h['hud'] - r['hud']))
        for a2, b2 in zip(H, H[1:]):
            if a2['T'] <= r['T'] <= b2['T']:
                span = (b2['T'] - a2['T']).total_seconds()
                k = ((r['T'] - a2['T']).total_seconds() / span) if span > 0 else 0.0
                diffs.append(abs(a2['hud'] + (b2['hud'] - a2['hud']) * k - r['hud']))
                break
    if raw_diffs: res.append(f'INFO COOP: nearest-row comparison (old way) max {max(raw_diffs):.1f} m mean {sum(raw_diffs)/len(raw_diffs):.1f} m')
    if diffs:
        tail = diffs[-10:]
        check(max(diffs) < 40.0, f'COOP: HOST/JOIN screen distance differ at most {max(diffs):.1f} m (mean {sum(diffs)/len(diffs):.1f} m, last 10 max {max(tail):.1f} m)')
        check(max(tail) < 15.0, f'COOP: they agree at the end (last 10 samples max {max(tail):.1f} m)')

# 相手が選択中/一時停止の間に、こちらが進み続けたか
def moved_while(other, me, label):
    wins = [r for r in other if r['choosing'] or r['paused']]
    if not wins: return
    t0, t1 = wins[0]['T'], wins[-1]['T']
    mine = [r for r in me if t0 <= r['T'] <= t1]
    free = [r for r in mine if not r['holding'] and not r['choosing'] and not r['paused']]
    if len(free) < 2: check(True, f'{label}: other side choosing/paused {(t1-t0).total_seconds():.1f}s (this side was in the boss arena or choosing - not measured)'); return
    dx = free[-1]['x'] - free[0]['x']
    sec = (t1 - t0).total_seconds()
    check(dx > min(3.0, 1.5 * sec), f'{label}: kept running while the other side chose/paused ({(t1-t0).total_seconds():.1f}s, moved {dx:.1f} m outside the arena)')
moved_while(H, J, 'JOIN'); moved_while(J, H, 'HOST')

for name, rows in (('HOST', H), ('JOIN', J)):
    if not rows: continue
    halted = sum(1 for r in rows if r['holding'])
    after = 0; seen_kill = False
    for a, b in zip(rows, rows[1:]):
        if a['bosses'] > 0 and b['bosses'] == 0: seen_kill = True
        if seen_kill and b['holding'] and b['bosses'] == 0: after += 1
    res.append(f'INFO {name}: arena-halt samples {halted}, halted after the boss died {after}, hud {rows[0]["hud"]:.0f}->{rows[-1]["hud"]:.0f} own {rows[-1]["own"]:.0f} dupOffers {rows[-1].get("dup")}')
    if seen_kill: check(after <= 5, f'{name}: not held after the boss died beyond the ~2 s defeat animation ({after} samples of 0.5 s)')

# 2026-11-25: HOST が選択中/一時停止の間も、雑魚の出現(HOST が全員の分を出す)が続いたか。第3引数 legacy = 変更前の決まり(判定しない)
LEGACY = len(sys.argv) > 3 and sys.argv[3] == 'legacy'
# HOST の記録を順に読み、HOST(P1)の選択中/一時停止中に立った雑魚の出現の予定([ENCOUNTER] #n、休みの波も含む)と、
# 「ボス戦で止めている(paused (boss phase))」を数える(ログに時刻が無いので、行の順番で区切る)
if os.path.exists(os.path.join(D, 'host.log')):
    # 判定: ボスに致命の一撃が入った後の HOST の選択中に「ボス戦なので雑魚を止める(paused (boss phase))」が出ないこと。
    # 立った予定の数は情報だけ(雑魚は最前の人の前に出る = HOST が最前で止まっている間は、HOST より前の予定は増えない。
    # ほかの人が HOST を追い越すと、その人の前に出る。出現の波の「休み」も予定に数える)
    inwin = False; lethal = False; plans = enemies = pauses_after = 0; windows = 0; inphase = False; early = late = 0
    for line in io.open(os.path.join(D, 'host.log'), encoding='utf-8', errors='replace'):
        if '[Encounter] BossStart' in line: lethal = False; inphase = True
        if '[Encounter] BossEnd' in line: inphase = False
        if 'resumed after boss' in line:
            if inphase: early += 1
            else: late += 1
        elif 'every boss has taken its lethal hit' in line or '[Encounter] BossEnd' in line: lethal = True
        if 'P1 IsChoosingCard=True' in line: inwin = True; windows += 1
        elif 'P1 IsChoosingCard=False' in line: inwin = False
        elif inwin and '[ENCOUNTER] #' in line:
            plans += 1
            m = re.search(r'members=(\d+)', line)
            if m: enemies += int(m.group(1))
        elif inwin and lethal and 'paused (boss phase)' in line: pauses_after += 1
    if windows and early + late > 0:
        msg = f'zako spawns resumed when the boss died, before the reward choice ended (resumed during the boss phase {early}, only after it {late}; plans while HOST chose {plans}, enemies {enemies})'
        if LEGACY: res.append('INFO ' + msg)
        else: check(early > 0 and late == 0, msg)
# 2026-11-25: ヒットストップ(マルチは攻撃した本人だけ)
for name, rows in (('HOST', H), ('JOIN', J)):
    if rows and 'hsL' in rows[-1]:
        res.append(f'INFO {name}: hit-stop on this player only {rows[-1]["hsL"]:.0f}, not stopped (someone else hit / boss) {rows[-1]["hsS"]:.0f}, team-distance snaps {rows[-1]["snaps"]:.0f}')
print('\n'.join(res))
ng = sum(1 for r in res if r.startswith('NG'))
print(f'RESULT {"PASS" if ng == 0 else "FAIL"} ng={ng}')
