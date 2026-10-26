"""boottimes.py <録画フォルダ>...: 起動の時刻をまとめる(2026-10-26 起動ロゴの調査)。bootframes.py の後に使う
  ログ(端末の時計、logcat -v epoch): 起動の命令(START) / OS の起動画面 / Displayed / Unity 起動画面の前 / シーン読み込みの前 / 最初のフレーム / BootSplash
  録画(segments.json): 最初の表示の変化 / ロゴが見えている区間 / ホーム
  出力: 各フォルダの1行 + times.json
"""
import sys, os, json, re
def ev(D):
    L = open(os.path.join(D, 'logcat_all.txt'), encoding='utf-8', errors='replace').read().splitlines()
    def first(pat):
        for l in L:
            if re.search(pat, l):
                try: return float(l.split()[0])
                except ValueError: pass
        return None
    e = dict(start=first(r'ActivityTaskManager: START .*action2nd'), displayed=first(r'Displayed com\.zero0084'),
             splash=first(r'\[Boot\] before splash'), sceneload=first(r'\[Boot\] before scene load'), firstframe=first(r'\[Boot\] first frame'),
             bs_start=first(r'\[BootSplash\] start'), bs_vis=first(r'\[BootSplash\] logo visible'), bs_hid=first(r'\[BootSplash\] logo hidden'), bs_done=first(r'\[BootSplash\] done'))
    return e
out = []
for D in sys.argv[1:]:
    e = ev(D); s0 = e['start']
    rel = {k: (round(v - s0, 2) if (v is not None and s0) else None) for k, v in e.items()}
    seg = json.load(open(os.path.join(D, 'segments.json'))) if os.path.exists(os.path.join(D, 'segments.json')) else []
    # 録画の時刻は「ホストの LAUNCH」基準。ログの START との差(ホストとエミュの時計のずれ+am の遅れ)を足して START 基準にそろえる
    lau = float(open(os.path.join(D, 'launch.txt')).read().split()[1]); off = lau - s0 if s0 else 0.0
    logo = [s for s in seg if s['k'] == 'logo']; home = [s for s in seg if s['k'] == 'other' and s['t0'] > 0.5]
    vid = dict(logo_visible=round(sum(s['t1'] - s['t0'] for s in logo), 2), logo_from=round(logo[0]['t0'] + off, 2) if logo else None,
               logo_to=round(logo[-1]['t1'] + off, 2) if logo else None, home_from=round(home[0]['t0'] + off, 2) if home else None)
    interactive = rel['bs_done'] if rel['bs_done'] is not None else rel['firstframe']
    row = dict(run=os.path.basename(D), **rel, interactive=interactive, video=vid)
    out.append(row)
    print(f"{row['run']:22s} displayed {rel['displayed']} | unity splash {rel['splash']} -> sceneload {rel['sceneload']} | firstframe {rel['firstframe']} | "
          f"logo(log) {rel['bs_vis']}..{rel['bs_hid']} | interactive {interactive} | video logo {vid['logo_from']}..{vid['logo_to']} ({vid['logo_visible']}s) home {vid['home_from']}")
if len(sys.argv) > 1: json.dump(out, open(os.path.join(os.path.dirname(sys.argv[1].rstrip('/')), 'boottimes_last.json'), 'w'), indent=1)
