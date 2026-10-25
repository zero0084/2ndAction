# -*- coding: utf-8 -*-
# 画像の幅・高さを 4 の倍数にそろえる(2026-10-25 v1.0 容量監査)。足すのは透明な画素だけ。
#  Windows の GPU 圧縮(BC7 / DXT)は 4x4 のブロック単位なので、4 の倍数でない画像は無圧縮(1画素 4バイト)のままビルドに入っていた。
#  使い方: python pad4.py <list.json(画像のパスの一覧)> <log.tsv> [--dry]
#  ・横: 左右に分けて足す(左 = 足す数 // 2)。縦: ピボットが下端(alignment 7)・独自(9)は上だけ、中央(0)は上下に分ける
#  ・独自のピボット(alignment 9)は、同じ画素を指すように .meta の spritePivot を直す(絵の位置は変わらない)
#    下端/中央のピボットは Unity が比率で決めるので、横の足し方が奇数の時だけ 0.5 画素ずれる(画面では見えない大きさ)
#  ・PNG の gAMA / sRGB / pHYs などの補助の情報は元のまま残す(色の扱いを変えない)
import io, json, re, struct, sys, zlib
from PIL import Image

def chunks(b):
    i = 8; out = []
    while i < len(b):
        n = struct.unpack('>I', b[i:i + 4])[0]; t = b[i + 4:i + 8]; out.append((t, b[i:i + 12 + n])); i += 12 + n
        if t == b'IEND': break
    return out

def save_keep_chunks(img, path, orig_bytes):
    buf = io.BytesIO(); img.save(buf, 'PNG', optimize=False, compress_level=9)
    new = chunks(buf.getvalue()); old = chunks(orig_bytes)
    keep = [raw for t, raw in old if t not in (b'IHDR', b'IDAT', b'IEND')]  # 元の補助の情報(gAMA/sRGB/pHYs など)をそのまま
    out = io.BytesIO(); out.write(buf.getvalue()[:8])
    for t, raw in new:
        if t == b'IHDR': out.write(raw); [out.write(k) for k in keep]
        elif t in (b'IDAT', b'IEND'): out.write(raw)
    open(path, 'wb').write(out.getvalue())

def main():
    paths = json.load(io.open(sys.argv[1], encoding='utf-8'))
    log = io.open(sys.argv[2], 'w', encoding='utf-8', newline='\n')
    dry = '--dry' in sys.argv
    log.write('path\told\tnew\tleft\tright\ttop\tbottom\talignment\toldPivot\tnewPivot\n')
    n = 0
    for p in paths:
        b = open(p, 'rb').read()
        im = Image.open(io.BytesIO(b)); im.load()
        if im.mode != 'RGBA': continue
        w, h = im.size
        px, py = (4 - w % 4) % 4, (4 - h % 4) % 4
        if px == 0 and py == 0: continue
        meta = io.open(p + '.meta', encoding='utf-8').read()
        a = re.search(r'\n  alignment: (\d+)', meta).group(1)
        m = re.search(r'\n  spritePivot: \{x: ([-\d.eE]+), y: ([-\d.eE]+)\}', meta)
        left = px // 2; right = px - left
        if a == '0': bottom = py // 2; top = py - bottom
        else: top = py; bottom = 0
        W, H = w + px, h + py
        oldp = newp = ''
        if a == '9' and m:
            ox, oy = float(m.group(1)), float(m.group(2))
            nx, ny = (ox * w + left) / W, (oy * h + bottom) / H
            oldp, newp = f'{ox},{oy}', f'{nx:.9g},{ny:.9g}'
            meta = meta[:m.start()] + f'\n  spritePivot: {{x: {nx:.9g}, y: {ny:.9g}}}' + meta[m.end():]
        out = Image.new('RGBA', (W, H), (0, 0, 0, 0)); out.paste(im, (left, top))
        log.write(f'{p}\t{w}x{h}\t{W}x{H}\t{left}\t{right}\t{top}\t{bottom}\t{a}\t{oldp}\t{newp}\n')
        n += 1
        if dry: continue
        save_keep_chunks(out, p, b)
        if newp: io.open(p + '.meta', 'w', encoding='utf-8', newline='').write(meta)
    log.close()
    print(('would pad ' if dry else 'padded ') + str(n))

if __name__ == '__main__':
    main()
