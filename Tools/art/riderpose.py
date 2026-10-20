"""ゴブリンライダーなど「横長の騎乗ボス」のコマ(ChatGPT 生成・マゼンタ背景・横長 1536x1024、全コマ同じ構図と縮尺)を、
ゲームの待機の絵と同じ縮尺のコマ(BossAnim、ピボット = 下端中央)へ。2026-10-20
エステルさんに「全コマ同じ縮尺・同じ位置・地面の高さをそろえる」で描いてもらい、こちらは全コマ同じ変換をかける:
  - マゼンタを抜く(reaperpose.py と同じ)
  - 縮尺 --scale(待機の絵に合わせた倍率。overlay で決める)
  - 地面 --ground(元画像の y。ここが出力の下端 = 足元。空中のコマは浮いたまま)
  - 横の基準 --cx(元画像の x。ここが出力の横の中心 = ピボット)
  - 出力の幅 --width(全コマ同じ。はみ出す所は切る)
usage:
  riderpose.py place <in.png> <out.png> --scale S --ground G --cx X [--width W] [--top T]
  riderpose.py overlay <frame.png> <idle.png> <out.png> --scale S --ground G --cx X   待機の絵を半透明で重ねた確認用
"""
import os
import sys
import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from reaperpose import key  # noqa: E402  (マゼンタ抜き。reaperpose はモジュールとして読むだけなら main を実行しない作りにしてある)


def arg(name, default=None):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


def place(src, scale, ground, cx, width, top):
    im = key(Image.open(src))
    # 地面より下(マゼンタの残りや影)は切る。上は top まで(既定は画像の上端)
    im = im.crop((0, top, im.width, int(round(ground))))
    w2 = round(im.width * scale)
    h2 = round(im.height * scale)
    im = im.resize((w2, h2), Image.LANCZOS)
    can = Image.new('RGBA', (width, h2), (0, 0, 0, 0))
    can.paste(im, (int(round(width / 2 - cx * scale)), 0), im)
    bb = can.split()[-1].getbbox()
    if bb:  # 上の余白だけ詰める(下端 = 地面、横 = 中心は動かさない)
        can = can.crop((0, bb[1], width, h2))
    return can


if __name__ == '__main__':
    cmd = sys.argv[1]
    scale = float(arg('--scale'))
    ground = float(arg('--ground'))
    cx = float(arg('--cx'))
    width = int(arg('--width', '900'))
    top = int(arg('--top', '0'))
    if cmd == 'place':
        out = place(sys.argv[2], scale, ground, cx, width, top)
        out.save(sys.argv[3])
        print(sys.argv[3], out.size)
    elif cmd == 'overlay':
        fr = place(sys.argv[2], scale, ground, cx, width, top)
        idle = Image.open(sys.argv[3]).convert('RGBA')
        H = max(fr.height, idle.height) + 20
        W = max(width, idle.width) + 20
        can = Image.new('RGBA', (W, H), (90, 90, 90, 255))
        can.alpha_composite(fr, ((W - fr.width) // 2, H - fr.height))
        ghost = idle.copy()
        a = np.asarray(ghost).copy()
        a[..., 3] = (a[..., 3] * 0.45).astype(np.uint8)
        ghost = Image.fromarray(a, 'RGBA')
        can.alpha_composite(ghost, ((W - idle.width) // 2, H - idle.height))
        can.convert('RGB').save(sys.argv[4])
        print(sys.argv[4], can.size)
