"""ssexport.py <素材フォルダ> <選択.json> <出力フォルダ>: 選んだ時刻のコマを原寸(1920x1080)の PNG で書き出す。選択.json = [[出力名, ショット, 秒], ...]"""
import sys, json, subprocess, os, time
FF = os.environ.get('FFMPEG', 'ffmpeg')
root, sj, out = sys.argv[1:4]; os.makedirs(out, exist_ok=True)
for name, shot, t in json.load(open(sj, encoding='utf-8')):
    p = os.path.join(out, name + '.png'); os.makedirs(os.path.dirname(p), exist_ok=True)
    # Windows で書き込み先が一時的に開けないことがある(Invalid argument)→ 少し待ってやり直す
    for k in range(5):
        if subprocess.run([FF, '-v', 'error', '-y', '-ss', f'{t:.3f}', '-i', os.path.join(root, shot + '.mkv'), '-frames:v', '1', '-pix_fmt', 'rgb24', p]).returncode == 0: break
        time.sleep(2)
    else: raise SystemExit('failed: ' + p)
    print(p)
