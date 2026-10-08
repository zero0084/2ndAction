# -*- coding: utf-8 -*-
# 検証の実行中、機械の空きメモリが下限を切ったら、いちばん進んでいない試行を止める(後で run_trials.py がやり直す)。
# usage: python memguard.py <resdir1,resdir2,...> <min_free_gb>
import ctypes, os, subprocess, sys, time, re

class MS(ctypes.Structure):
    _fields_ = [('dwLength', ctypes.c_ulong), ('dwMemoryLoad', ctypes.c_ulong), ('ullTotalPhys', ctypes.c_ulonglong), ('ullAvailPhys', ctypes.c_ulonglong),
                ('ullTotalPageFile', ctypes.c_ulonglong), ('ullAvailPageFile', ctypes.c_ulonglong), ('ullTotalVirtual', ctypes.c_ulonglong),
                ('ullAvailVirtual', ctypes.c_ulonglong), ('ullAvailExtendedVirtual', ctypes.c_ulonglong)]
def free_gb():
    m = MS(); m.dwLength = ctypes.sizeof(MS); ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m)); return m.ullAvailPhys / 2**30

dirs, floor = sys.argv[1].split(','), float(sys.argv[2])
while True:
    f = free_gb()
    if f < floor:
        # 走っている試行(result.json が無く、progress.txt がある)のうち、ゲーム内の経過がいちばん短い物
        cand = []
        for d in dirs:
            for t in os.listdir(d):
                p = os.path.join(d, t)
                if os.path.exists(os.path.join(p, 'result.json')) or not os.path.exists(os.path.join(p, 'progress.txt')): continue
                if time.time() - os.path.getmtime(os.path.join(p, 'player.log')) > 300: continue
                m = re.match(r'([\d.]+)min', open(os.path.join(p, 'progress.txt'), encoding='utf-8', errors='replace').read())
                cand.append((float(m.group(1)) if m else 0.0, t))
        if cand:
            cand.sort(); mins, trial = cand[0]
            ps = subprocess.run(['powershell', '-NoProfile', '-Command', f"Get-CimInstance Win32_Process -Filter \"Name='OneMoreMile.exe'\" | Where-Object {{ $_.CommandLine -like '*-blTrial {trial} *' }} | ForEach-Object {{ Stop-Process -Id $_.ProcessId -Force; $_.ProcessId }}"], capture_output=True, text=True)
            print(time.strftime('%H:%M:%S'), f'free {f:.1f}GB < {floor} -> stopped {trial} ({mins}min) pid {ps.stdout.strip()}', flush=True)
            time.sleep(60)
    time.sleep(20)
