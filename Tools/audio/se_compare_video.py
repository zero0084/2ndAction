# SE の変更前後の聞き比べ動画(2026-11-26 依頼②)。ゲームと同じ素材・同じ倍率(空振り 0.72 倍 / 命中時の空振りの減衰 0.07 秒)を並べる。
#  使い方: python se_compare_video.py <出力フォルダ>   → se_compare.mp4(字幕の静止画 + 音)
import os, sys, wave, subprocess
import numpy as np
from PIL import Image, ImageDraw, ImageFont

SR = 48000
SE = r'C:/GameProject/2ndAction/Unity/2ndAction/Assets/Audio/Placeholder/SE'
FF = r'C:/Users/0084k/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.1-full_build/bin/ffmpeg.exe'
OUT = sys.argv[1]; os.makedirs(OUT, exist_ok=True)
SWING_VOL, FADE = 0.72, 0.07

def load(name):
    with wave.open(f'{SE}/{name}.wav', 'rb') as w:
        sr, ch, n = w.getframerate(), w.getnchannels(), w.getnframes()
        x = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float32) / 32768.0
    if ch > 1: x = x.reshape(-1, ch).mean(axis=1)
    if sr != SR: x = np.interp(np.linspace(0, len(x) - 1, int(len(x) * SR / sr)), np.arange(len(x)), x)
    return x

def place(buf, x, t, gain=1.0, fade_at=None):
    i = int(t * SR); y = x * gain
    if fade_at is not None:  # 命中の時刻から FADE 秒で 0 へ(ゲームの SoftenSwingOnHit と同じ直線)
        k = int((fade_at - t) * SR); f = int(FADE * SR)
        env = np.ones(len(y)); env[k:k + f] = np.linspace(1, 0, min(f, max(0, len(y) - k))) if k < len(y) else env[k:k + f]; env[k + f:] = 0
        y = y * env
    end = min(len(buf), i + len(y)); buf[i:end] += y[:end - i]

swings = [load('old_AttackSe1_n'), load('old_AttackSe2_n'), load('atk_sword_0')]
hits = [load('hit_0'), load('hit_1'), load('hit_2')]

def seg_swings(after):
    b = np.zeros(int(2.6 * SR))
    for k in range(6): place(b, swings[k % 3], 0.2 + k * 0.32, SWING_VOL if after else 1.0)
    return b
def seg_hits(after, targets=1):
    b = np.zeros(int(2.8 * SR))
    for k in range(5):
        t = 0.2 + k * 0.45
        place(b, swings[k % 3], t, SWING_VOL if after else 1.0, fade_at=(t + 0.08) if after else None)
        for j in range(targets): place(b, hits[(k + j) % 3], t + 0.08 + j * 0.02)
    return b
def seg_one(name, n=3, gap=0.9):
    x = load(name); b = np.zeros(int((0.3 + n * gap) * SR))
    for k in range(n): place(b, x, 0.2 + k * gap)
    return b

segs = [
    ('変更前: 連続攻撃(空振り)', seg_swings(False)),
    ('変更後: 連続攻撃(空振り) — 空振り音を 0.72 倍(約 -3dB)', seg_swings(True)),
    ('変更前: 攻撃が当たった時(空振り音と命中音が重なる)', seg_hits(False)),
    ('変更後: 当たった時は命中音を優先(空振り音を 0.07 秒で減衰)', seg_hits(True)),
    ('変更前: 複数体(3体)へ命中', seg_hits(False, 3)),
    ('変更後: 複数体(3体)へ命中', seg_hits(True, 3)),
    ('変更前: 被弾の音(ポヨーン)', seg_one('old_player_damage_n')),
    ('変更後: 被弾の音(体に響く重い打撃)', seg_one('player_damage_v4')),
    ('変更前: 敵の攻撃の予兆(ピュイーン)', seg_one('enemy_telegraph')),
    ('変更後: 敵の攻撃の予兆(力を溜める低いうなり)', seg_one('enemy_telegraph_v4')),
]

font = ImageFont.truetype(r'C:/Windows/Fonts/meiryo.ttc', 40)
small = ImageFont.truetype(r'C:/Windows/Fonts/meiryo.ttc', 26)
lst = []
audio = []
for i, (cap, a) in enumerate(segs):
    img = Image.new('RGB', (1280, 720), (16, 20, 36) if '変更前' in cap else (14, 34, 28))
    d = ImageDraw.Draw(img)
    d.text((60, 300), cap, font=font, fill=(240, 240, 250))
    d.text((60, 640), f'One More Mile  SE 聞き比べ  {i + 1}/{len(segs)}', font=small, fill=(160, 170, 190))
    p = os.path.join(OUT, f'cap{i:02d}.png'); img.save(p)
    dur = len(a) / SR
    lst.append(f"file '{p}'\nduration {dur:.3f}")
    audio.append(a)
lst.append(f"file '{os.path.join(OUT, f'cap{len(segs) - 1:02d}.png')}'")
open(os.path.join(OUT, 'list.txt'), 'w', encoding='utf-8').write('\n'.join(lst) + '\n')
mixd = np.concatenate(audio)
peak = np.abs(mixd).max()
if peak > 0.98: mixd *= 0.98 / peak   # 全体を同じ倍率で(前後の比べを崩さない)
with wave.open(os.path.join(OUT, 'se_compare.wav'), 'wb') as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR)
    w.writeframes((np.clip(mixd, -1, 1) * 32767).astype(np.int16).tobytes())
subprocess.run([FF, '-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', os.path.join(OUT, 'list.txt'), '-i', os.path.join(OUT, 'se_compare.wav'),
                '-c:v', 'libx264', '-r', '30', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-b:a', '192k', '-shortest', os.path.join(OUT, 'se_compare.mp4')], check=True)
print('ok', os.path.join(OUT, 'se_compare.mp4'), f'{len(mixd) / SR:.1f}s')
