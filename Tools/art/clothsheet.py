"""走りのコマを1枚のシート(マゼンタ背景)へ並べる/編集されたシートからコマへ戻す(2026-10-25 布表現の見直し)。
全コマを同じ1枚で編集してもらうと、布の色・厚み・長さがコマ間でそろいやすい。

pack:   clothsheet.py pack <コマのフォルダ> <出力シート.png> <配置.json> [--scale 1.5] [--cols 4] [--cell 384x512] [--canvas 1536x1024] [--foot 8]
        各コマを scale 倍して、セルの下端から foot px 上に足元(ピボット)・セルの中央にピボットの x が来るように置く。
unpack: clothsheet.py unpack <編集されたシート.png> <配置.json> <出力フォルダ>
        シートが別の大きさで返ってきた時は、キャンバスの比率で拡大縮小してから切る。
        各コマは「元のコマと同じ大きさのキャンバス」に戻す(位置合わせ・布以外の復元は clothrun.py で)。
"""
import sys, os, re, glob, json
from PIL import Image

def pivot(path):
    m = re.search(r'spritePivot: \{x: ([\d.]+), y: ([\d.]+)\}', open(path + '.meta', encoding='utf-8').read())
    return float(m.group(1)), float(m.group(2))

def opt(args, name, default):
    return args[args.index(name) + 1] if name in args else default

cmd = sys.argv[1]
if cmd == 'pack':
    src, out, js = sys.argv[2:5]; a = sys.argv[5:]
    scale = float(opt(a, '--scale', '1.5')); cols = int(opt(a, '--cols', '4'))
    cw, ch = map(int, opt(a, '--cell', '384x512').split('x')); W, H = map(int, opt(a, '--canvas', '1536x1024').split('x')); foot = int(opt(a, '--foot', '8'))
    files = sorted(glob.glob(os.path.join(src, 'run_*.png')))
    rows = (len(files) + cols - 1) // cols
    sheet = Image.new('RGB', (W, H), (255, 0, 255))
    info = dict(scale=scale, canvas=[W, H], frames=[])
    for k, f in enumerate(files):
        im = Image.open(f).convert('RGBA'); px, py = pivot(f)
        r, c = divmod(k, cols)
        ncol = min(cols, len(files) - r * cols)
        x_off = (W - ncol * cw) // 2  # 段ごとに中央寄せ
        cx = x_off + c * cw + cw / 2; cy = (H - rows * ch) // 2 + r * ch + ch - foot
        big = im.resize((round(im.width * scale), round(im.height * scale)), Image.LANCZOS)
        ox, oy = round(cx - px * big.width), round(cy - (1 - py) * big.height)
        bb = big.getbbox()
        sheet.paste(big, (ox, oy), big)
        info['frames'].append(dict(file=os.path.basename(f), size=list(im.size), pivot=[px, py], ox=ox, oy=oy, bbox=[ox + bb[0], oy + bb[1], ox + bb[2], oy + bb[3]]))
    sheet.save(out); json.dump(info, open(js, 'w'), indent=1)
    print('packed', len(files), 'frames; content boxes:', [fr['bbox'] for fr in info['frames']])
elif cmd == 'unpack':
    sh, js, outdir = sys.argv[2:5]
    info = json.load(open(js)); os.makedirs(outdir, exist_ok=True)
    S = Image.open(sh).convert('RGB')
    if list(S.size) != info['canvas']:
        S = S.resize(tuple(info['canvas']), Image.LANCZOS)
    s = info['scale']
    for fr in info['frames']:
        w, h = fr['size']
        crop = S.crop((fr['ox'], fr['oy'], fr['ox'] + round(w * s), fr['oy'] + round(h * s)))
        # 元の大きさのキャンバスへ(マゼンタのまま。透過にするのは clothrun.py)
        crop.resize((w, h), Image.LANCZOS).save(os.path.join(outdir, fr['file']))
    print('unpacked', len(info['frames']))
